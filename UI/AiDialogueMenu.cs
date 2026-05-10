using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;

namespace LivingCompanionsValley.UI
{
    /// <summary>
    /// Menú de diálogo AI personalizado que replica la apariencia nativa de Stardew Valley.
    /// Renderizado Pixel-Perfect nativo.
    /// </summary>
    public class AiDialogueMenu : IClickableMenu
    {
        private NPC _npc;
        private TextBox _textBox; // Oculto: usado solo para capturar teclado nativo
        private Action<string> _onSubmit;
        private string _aiResponseText = "";
        private bool _isThinking = false;
        
        public int CurrentEmotion { get; set; } = 0; // 0 = Neutral
        
        // Efecto Typewriter
        private int _typewriterIndex = 0;
        private double _typewriterTimer = 0;
        private const double TypewriterSpeedMs = 30.0;

        // --- Variables del Auto-Scroll Chat ---
        private string _fullChatText = "";
        private List<string> _wrappedLines = new List<string>();
        private int _maxVisibleLines = 3;
        private int _currentScrollIndex = 0;
        private int _chatBoxBaseY; 
        private int _chatBoxX;
        private int _chatBoxWidth;
        private int _chatBoxHeight;

        public AiDialogueMenu(NPC npc, Action<string> onSubmit)
            : base(0, 0, 1200, 384) // Tamaños base del DialogueBox nativo
        {
            _npc = npc;
            _onSubmit = onSubmit;

            CalculateLayout();
            _wrappedLines.Add(""); 

            // TextBox oculto para capturar el input del jugador limpiamente
            _textBox = new TextBox(
                Game1.content.Load<Texture2D>("LooseSprites\\textBox"), 
                null, 
                Game1.smallFont,
                Game1.textColor)
            {
                X = -10000, 
                Y = -10000, 
                Width = 10000, 
                Height = 48,
                Selected = true
            };
            
            Game1.keyboardDispatcher.Subscriber = _textBox;
            _textBox.OnEnterPressed += HandleEnterPressed;
            
            Game1.playSound("dialogueCharacter");
        }

        private void CalculateLayout()
        {
            // Coordenadas nativas de Stardew Valley DialogueBox
            this.width = Math.Min(1200, Game1.uiViewport.Width - 64);
            this.height = Game1.tileSize * 6; // 384px

            this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
            this.yPositionOnScreen = Game1.uiViewport.Height - this.height - 120; // 120px de margen inferior

            // Base para el chat box dinámico, flotando debajo de la caja principal
            _chatBoxWidth = this.width - 296 - 16; // Restamos el ancho del retrato para que no lo pise
            _chatBoxX = this.xPositionOnScreen + 32;
            _chatBoxBaseY = this.yPositionOnScreen + this.height + 64; // Creciendo desde un poco más abajo
        }

        private void HandleEnterPressed(TextBox sender)
        {
            if (string.IsNullOrWhiteSpace(sender.Text) || _isThinking) return;
            
            _isThinking = true;
            _aiResponseText = "";
            _typewriterIndex = 0;
            
            _onSubmit?.Invoke(sender.Text);
            
            sender.Text = "";
            _fullChatText = "";
            _wrappedLines.Clear();
            _wrappedLines.Add("");
            _currentScrollIndex = 0;
            sender.Selected = false;
        }

        public void ReceiveAiResponse(string response)
        {
            _isThinking = false;
            _aiResponseText = response;
            _typewriterIndex = 0;
            _textBox.Selected = true; 
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape)
            {
                Game1.exitActiveMenu();
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (_wrappedLines.Count > _maxVisibleLines)
            {
                if (direction > 0)
                    _currentScrollIndex = Math.Max(0, _currentScrollIndex - 1);
                else if (direction < 0)
                    _currentScrollIndex = Math.Min(_wrappedLines.Count - _maxVisibleLines, _currentScrollIndex + 1);
            }
        }

        public override void update(GameTime time)
        {
            base.update(time);

            if (_textBox.Text != _fullChatText)
            {
                _fullChatText = _textBox.Text;
                _wrappedLines = ParseTextIntoLines(_fullChatText, Game1.smallFont, _chatBoxWidth - 32);
                
                if (_wrappedLines.Count > _maxVisibleLines)
                    _currentScrollIndex = _wrappedLines.Count - _maxVisibleLines;
                else
                    _currentScrollIndex = 0;
            }

            if (!_isThinking && !string.IsNullOrEmpty(_aiResponseText) && _typewriterIndex < _aiResponseText.Length)
            {
                _typewriterTimer += time.ElapsedGameTime.TotalMilliseconds;
                if (_typewriterTimer >= TypewriterSpeedMs)
                {
                    _typewriterIndex++;
                    _typewriterTimer = 0;
                    
                    if (_typewriterIndex % 3 == 0) 
                        Game1.playSound("dialogueCharacter");
                }
            }
        }

        private List<string> ParseTextIntoLines(string text, SpriteFont font, int maxWidth)
        {
            List<string> lines = new List<string>();
            string[] words = text.Split(' ');
            string currentLine = "";

            foreach (string word in words)
            {
                string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                if (font.MeasureString(testLine).X > maxWidth)
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine);

            if (lines.Count == 0) lines.Add(""); 
            
            return lines;
        }

        public override void draw(SpriteBatch b)
        {
            // ============================================================
            // 1. DIBUJO DE LA INTERFAZ NATIVA DE STARDEW VALLEY
            // ============================================================
            
            // Caja de Diálogo Principal
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), 
                this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, 
                Color.White, 1f, true, 0.8f);

            // Renderizado del Retrato y Nombre
            if (_npc != null && _npc.Portrait != null)
            {
                int portraitBoxX = this.xPositionOnScreen + this.width + 8;
                
                // Caja de fondo del retrato
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), 
                    portraitBoxX, this.yPositionOnScreen, 296, 296, Color.White, 1f, true, 0.8f);
                
                // Dibujo exacto del Retrato usando la emoción
                b.Draw(_npc.Portrait, 
                    new Vector2(portraitBoxX + 20, this.yPositionOnScreen + 20), 
                    new Rectangle?(Game1.getSourceRectForStandardTileSheet(_npc.Portrait, this.CurrentEmotion, 64, 64)), 
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                
                // Renderizado del Nameplate
                SpriteFont nameFont = Game1.dialogueFont;
                string npcName = _npc.getName();
                Vector2 nameSize = nameFont.MeasureString(npcName);
                
                IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), 
                    this.xPositionOnScreen - 8, this.yPositionOnScreen - 56, (int)nameSize.X + 48, 72, 
                    Color.White, 1f, true, 0.8f);
                    
                Utility.drawTextWithShadow(b, npcName, nameFont, 
                    new Vector2(this.xPositionOnScreen + 16, this.yPositionOnScreen - 40), 
                    Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
            }

            // ============================================================
            // 2. TEXTO DEL DIÁLOGO (MÁQUINA DE ESCRIBIR)
            // ============================================================
            if (_isThinking)
            {
                string dots = new string('.', 1 + (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 500) % 3);
                Utility.drawTextWithShadow(b, dots, Game1.dialogueFont, 
                    new Vector2(this.xPositionOnScreen + 32, this.yPositionOnScreen + 32), 
                    Color.Gray, 1f, -1f, -1, -1, 1f, 3);
            }
            else if (!string.IsNullOrEmpty(_aiResponseText))
            {
                string visibleText = _aiResponseText.Substring(0, Math.Min(_typewriterIndex, _aiResponseText.Length));
                
                // Usar el ancho completo menos un poco de margen para el texto nativo
                string parsedText = Game1.parseText(visibleText, Game1.dialogueFont, this.width - 64);
                
                Utility.drawTextWithShadow(b, parsedText, Game1.dialogueFont, 
                    new Vector2(this.xPositionOnScreen + 32, this.yPositionOnScreen + 32), 
                    Game1.textColor, 1f, -1f, -1, -1, 1f, 3);

                // El Indicador de "Siguiente" (El Triángulo Parpadeante)
                if (_typewriterIndex >= _aiResponseText.Length)
                {
                    float yOffset = (float)Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 150.0) * 4f;
                    b.Draw(Game1.mouseCursors, 
                        new Vector2(this.xPositionOnScreen + this.width - 40, this.yPositionOnScreen + this.height - 40 + yOffset), 
                        new Rectangle?(new Rectangle(289, 342, 11, 12)), 
                        Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.88f);
                }
            }

            // ============================================================
            // 3. CAJA DE TEXTO DINÁMICA DEL JUGADOR (Auto-Scroll)
            // ============================================================
            SpriteFont font = Game1.smallFont;
            int lineHeight = (int)font.MeasureString("A").Y + 8; 
            int visibleLinesCount = Math.Min(_wrappedLines.Count, _maxVisibleLines);
            
            _chatBoxHeight = (visibleLinesCount * lineHeight) + 24; 
            int currentTopY = _chatBoxBaseY - _chatBoxHeight;

            // Renderizado visual del chatbox
            Texture2D tbTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
            IClickableMenu.drawTextureBox(b, tbTexture, new Rectangle(0, 0, tbTexture.Width, tbTexture.Height),
                _chatBoxX, currentTopY, _chatBoxWidth, _chatBoxHeight, Color.White, 1f, true, 0.89f);

            for (int i = 0; i < visibleLinesCount; i++)
            {
                int lineIndex = _currentScrollIndex + i; 
                if (lineIndex < _wrappedLines.Count)
                {
                    Vector2 position = new Vector2(_chatBoxX + 16, currentTopY + 12 + (i * lineHeight));
                    b.DrawString(font, _wrappedLines[lineIndex], position, Game1.textColor, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);

                    if (i == visibleLinesCount - 1 && lineIndex == _wrappedLines.Count - 1)
                    {
                        if (Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 500)
                        {
                            Vector2 textSize = font.MeasureString(_wrappedLines[lineIndex]);
                            b.DrawString(font, "|", new Vector2(position.X + textSize.X + 2, position.Y), Game1.textColor, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);
                        }
                    }
                }
            }

            if (_wrappedLines.Count > _maxVisibleLines)
            {
                if (_currentScrollIndex > 0)
                    b.DrawString(font, "^", new Vector2(_chatBoxX + _chatBoxWidth - 24, currentTopY + 12), Color.Gray, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);
                
                if (_currentScrollIndex < _wrappedLines.Count - _maxVisibleLines)
                    b.DrawString(font, "v", new Vector2(_chatBoxX + _chatBoxWidth - 24, currentTopY + _chatBoxHeight - 32), Color.Gray, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);
            }

            drawMouse(b);
        }
        
        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            _textBox.Selected = true; 
        }
    }
}
