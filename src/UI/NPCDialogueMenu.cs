using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;
using StardewLivingValley.Services;

namespace StardewLivingValley.UI
{
    /// <summary>
    /// Menú de diálogo AI personalizado que replica la apariencia nativa de Stardew Valley.
    /// Renderizado Pixel-Perfect nativo.
    /// </summary>
    public class NPCDialogueMenu : IClickableMenu
    {
        private NPC _npc;
        private EmotionService _emotionService;
        private TextBox _textBox; // Oculto: usado solo para capturar teclado nativo
        private Action<string> _onSubmit;
        private string _aiResponseText = "";
        private bool _isThinking = false;
        
        public int CurrentEmotion { get; set; } = 0; // 0 = Neutral
        
        // Efecto Typewriter
        private int _typewriterIndex = 0;
        private double _typewriterTimer = 0;
        private const double TypewriterSpeedMs = 35.0; // 15% más lento que antes (30.0 -> 35.0)

        // Auto-cierre para misiones [go_to]
        private bool _autoCloseAfterTyping = false;
        private double _autoCloseTimer = 0;
        private const double AutoCloseDelayMs = 2000.0; // 2 segundos después de terminar

        // --- Variables de Paginación e Historial ---
        private List<string> _historyPages = new List<string>();
        private List<int> _historyEmotions = new List<int>();
        private int _historyIndex = 0;
        private int _maxHistoryIndexReached = 0;
        private Dictionary<int, int> _emotionKeyframes = new Dictionary<int, int>();
        private int _globalTextIndex = 0;

        // --- Variables del Auto-Scroll Chat ---
        private string _fullChatText = "";
        private List<string> _wrappedLines = new List<string>();
        private int _maxVisibleLines = 3;
        private int _currentScrollIndex = 0;
        private int _chatBoxBaseY; 
        private int _chatBoxX;
        private int _chatBoxWidth;
        private int _chatBoxHeight;

        public NPCDialogueMenu(NPC npc, EmotionService emotionService, Action<string> onSubmit)
            : base(0, 0, 1200, 384) // Tamaños base del DialogueBox nativo
        {
            _npc = npc;
            _emotionService = emotionService;
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

        public void SetAutoClose()
        {
            _autoCloseAfterTyping = true;
            _autoCloseTimer = 0;
        }

        private void CalculateLayout()
        {
            // Coordenadas nativas exactas extraídas del descompilado
            this.width = Math.Min(1200, Game1.uiViewport.Width - 64);
            this.height = Game1.tileSize * 6; // 384px

            this.xPositionOnScreen = (int)Utility.getTopLeftPositionForCenteringOnScreen(this.width, this.height).X;
            // Subimos toda la interfaz maestra restando más margen inferior (~5% de la pantalla)
            this.yPositionOnScreen = Game1.uiViewport.Height - this.height - 124; 

            // Base para el chat box dinámico
            _chatBoxWidth = this.width - 484; // Igual al ancho del texto
            _chatBoxX = this.xPositionOnScreen - 4; // Ajuste microscópico: 1% más a la izquierda
            _chatBoxBaseY = this.yPositionOnScreen + this.height + 104; // Ajuste microscópico: 2% más abajo
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

        public void ReceiveAiResponse(string rawResponse)
        {
            _isThinking = false;
            ProcessAiText(rawResponse.Trim());
            _textBox.Selected = true; 
        }

        private void ProcessAiText(string rawText)
        {
            _emotionKeyframes.Clear();
            _typewriterIndex = 0;
            _globalTextIndex = 0;

            int startIndex = _historyPages.Count;

            string cleanText = "";
            int cleanIndex = 0;
            
            // 1. Encontrar todas las etiquetas [X] en el texto
            var matches = System.Text.RegularExpressions.Regex.Matches(rawText, @"\[(\d+)\]");
            int lastPos = 0;

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                // Unir el texto limpio antes de esta etiqueta
                string textPart = rawText.Substring(lastPos, match.Index - lastPos);
                cleanText += textPart;
                cleanIndex += textPart.Length;

                // Guardar el cambio de emoción en el índice exacto de la letra donde debe ocurrir
                if (int.TryParse(match.Groups[1].Value, out int emotion))
                {
                    _emotionKeyframes[cleanIndex] = emotion;
                }
                lastPos = match.Index + match.Length;
            }
            // Añadir el resto del texto final
            cleanText += rawText.Substring(lastPos);

            // Si la IA empezó con una emoción en el índice 0, la activamos de inmediato
            if (_emotionKeyframes.ContainsKey(0)) CurrentEmotion = _emotionKeyframes[0];

            // 2. Paginación nativa automática
            // Ajustamos el width restando para no superponer con el Portrait Plate
            string parsedString = Game1.parseText(cleanText.Trim(), Game1.dialogueFont, this.width - 500);
            string[] lines = parsedString.Split('\n');
            
            string currentPage = "";
            int lineCount = 0;

            foreach (string line in lines)
            {
                if (lineCount >= 4) // Límite de 4 líneas por caja de diálogo (Página)
                {
                    _historyPages.Add(currentPage.TrimEnd());
                    _historyEmotions.Add(CurrentEmotion);
                    currentPage = "";
                    lineCount = 0;
                }
                currentPage += line + "\n";
                lineCount++;
            }
            if (!string.IsNullOrWhiteSpace(currentPage))
            {
                _historyPages.Add(currentPage.TrimEnd());
                _historyEmotions.Add(CurrentEmotion);
            }
            if (_historyPages.Count == startIndex)
            {
                _historyPages.Add("...");
                _historyEmotions.Add(CurrentEmotion);
            }
            
            _historyIndex = startIndex;
            _maxHistoryIndexReached = startIndex;
            _aiResponseText = _historyPages[_historyIndex];
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
            
            // Si el mouse está en el chat box (input del jugador)
            if (new Rectangle(_chatBoxX, _chatBoxBaseY - _chatBoxHeight, _chatBoxWidth, _chatBoxHeight).Contains(Game1.getMouseX(), Game1.getMouseY()))
            {
                if (_wrappedLines.Count > _maxVisibleLines)
                {
                    if (direction > 0)
                        _currentScrollIndex = Math.Max(0, _currentScrollIndex - 1);
                    else if (direction < 0)
                        _currentScrollIndex = Math.Min(_wrappedLines.Count - _maxVisibleLines, _currentScrollIndex + 1);
                }
            }
            // Si el mouse está en la caja de diálogo superior
            else if (new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height).Contains(Game1.getMouseX(), Game1.getMouseY()))
            {
                if (!_isThinking && _historyPages.Count > 0)
                {
                    if (direction > 0 && _historyIndex > 0) // Scroll Up (Retroceder)
                    {
                        _historyIndex--;
                        _aiResponseText = _historyPages[_historyIndex];
                        _typewriterIndex = _aiResponseText.Length;
                        CurrentEmotion = _historyEmotions[_historyIndex];
                        Game1.playSound("shwip");
                    }
                    else if (direction < 0 && _historyIndex < _maxHistoryIndexReached) // Scroll Down (Avanzar por historial)
                    {
                        _historyIndex++;
                        _aiResponseText = _historyPages[_historyIndex];
                        _typewriterIndex = _aiResponseText.Length;
                        CurrentEmotion = _historyEmotions[_historyIndex];
                        Game1.playSound("shwip");
                    }
                }
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

            if (!_isThinking && _historyPages.Count > 0 && _historyIndex < _historyPages.Count)
            {
                if (_typewriterIndex < _aiResponseText.Length)
                {
                    _typewriterTimer += time.ElapsedGameTime.TotalMilliseconds;
                    if (_typewriterTimer >= TypewriterSpeedMs)
                    {
                        _typewriterIndex++;
                        _globalTextIndex++;
                        _typewriterTimer = 0;
                        
                        if (_emotionKeyframes.ContainsKey(_globalTextIndex))
                        {
                            CurrentEmotion = _emotionKeyframes[_globalTextIndex];
                            _historyEmotions[_historyIndex] = CurrentEmotion;
                        }

                        if (_typewriterIndex % 3 == 0) 
                            Game1.playSound("dialogueCharacter");
                    }
                }
                else if (_autoCloseAfterTyping)
                {
                    // Auto-avanzar páginas sin clic del jugador
                    if (_historyIndex < _historyPages.Count - 1)
                    {
                        _autoCloseTimer += time.ElapsedGameTime.TotalMilliseconds;
                        if (_autoCloseTimer >= 1500.0) // 1.5 segundos entre páginas
                        {
                            _historyIndex++;
                            _aiResponseText = _historyPages[_historyIndex];
                            _typewriterIndex = 0;
                            _autoCloseTimer = 0;
                            Game1.playSound("smallSelect");
                        }
                    }
                    else
                    {
                        // Última página terminada, esperar 2 segundos y cerrar
                        _autoCloseTimer += time.ElapsedGameTime.TotalMilliseconds;
                        if (_autoCloseTimer >= AutoCloseDelayMs)
                        {
                            Game1.exitActiveMenu();
                            Game1.playSound("dialogueCharacterClose");
                        }
                    }
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
            DrawNativeBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height);

            // Renderizado del Retrato, Nombre y Joya
            if (_npc != null && _npc.Portrait != null)
            {
                // El retrato nativo va ADENTRO del DialogueBox, a la derecha. (Alineado al interior del borde)
                int portraitBoxX = this.xPositionOnScreen + this.width - 460 - 4; 
                
                // Caja de fondo del retrato (Portrait Plate)
                b.Draw(Game1.mouseCursors, 
                    new Vector2(portraitBoxX, this.yPositionOnScreen + 4), 
                    new Rectangle(583, 411, 115, 97), 
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
                
                Texture2D textureToDraw = _npc.Portrait;
                
                int frameSize = textureToDraw.Width <= 128 ? 64 : textureToDraw.Width / 2;
                int actualFrame = _emotionService != null ? _emotionService.GetFrameForEmotion(_npc.Name, this.CurrentEmotion) : this.CurrentEmotion;
                Rectangle sourceRect = Game1.getSourceRectForStandardTileSheet(textureToDraw, actualFrame, frameSize, frameSize);
                
                // --- PROTECCIÓN MATEMÁTICA CONTRA PANTALLAS ROSAS ---
                // Si la IA pide una emoción (como la 5) y la imagen no es lo suficientemente grande,
                // usamos la cara neutral (0) para evitar cuadros rosas o crashes.
                if (!textureToDraw.Bounds.Contains(sourceRect))
                {
                    sourceRect = Game1.getSourceRectForStandardTileSheet(textureToDraw, 0, frameSize, frameSize);
                }

                float portraitScale = 256f / frameSize;

                // Dibujo exacto del Retrato 
                b.Draw(textureToDraw, 
                    new Vector2(portraitBoxX + 104, this.yPositionOnScreen + 42), // Offset ligeramente subido
                    new Rectangle?(sourceRect), 
                    Color.White, 0f, Vector2.Zero, portraitScale, SpriteEffects.None, 0.88f);
                
                // Renderizado del Nameplate (bajado ~4% del original)
                StardewValley.BellsAndWhistles.SpriteText.drawStringHorizontallyCenteredAt(
                    b, _npc.getName(), portraitBoxX + 230, this.yPositionOnScreen + 324);
                    
                // Joya de Amistad Animada
                Rectangle jewelSource;
                if (Game1.player.getFriendshipHeartLevelForNPC(_npc.Name) >= 10) {
                    jewelSource = new Rectangle(269, 494, 11, 11); // Gema de 10 corazones estática
                } else {
                    int frameOffset = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000.0 / 250.0) * 11;
                    int heartRow = (Game1.player.getFriendshipHeartLevelForNPC(_npc.Name) / 2) * 11;
                    jewelSource = new Rectangle(140 + frameOffset, 532 + heartRow, 11, 11);
                }
                
                // Dibujar joya de amistad
                b.Draw(Game1.mouseCursors, 
                    new Vector2(portraitBoxX + 24, this.yPositionOnScreen + 24), 
                    new Rectangle?(jewelSource), 
                    Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.89f);
            }

            // ============================================================
            // 2. TEXTO DEL DIÁLOGO (MÁQUINA DE ESCRIBIR)
            // ============================================================
            if (_isThinking)
            {
                string dots = new string('.', 1 + (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 500) % 3);
                Utility.drawTextWithShadow(b, dots, Game1.dialogueFont, 
                    new Vector2(this.xPositionOnScreen + 16, this.yPositionOnScreen + 32), 
                    Color.Gray, 1f, -1f, -1, -1, 1f, 3);
            }
            else if (!string.IsNullOrEmpty(_aiResponseText))
            {
                string visibleText = _aiResponseText.Substring(0, Math.Min(_typewriterIndex, _aiResponseText.Length));
                
                // El texto ya fue parseado y paginado en ProcessAiText, lo dibujamos directamente
                Utility.drawTextWithShadow(b, visibleText, Game1.dialogueFont, 
                    new Vector2(this.xPositionOnScreen + 16, this.yPositionOnScreen + 32), 
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
            DrawNativeBox(b, _chatBoxX, currentTopY, _chatBoxWidth, _chatBoxHeight);

            // Placeholder Text / Empty State
            if (string.IsNullOrWhiteSpace(_fullChatText))
            {
                string placeholderText = _isThinking ? "Esperando respuesta..." : "Escribe un mensaje...";
                Vector2 placeholderPos = new Vector2(_chatBoxX + 16, currentTopY + 12);
                b.DrawString(font, placeholderText, placeholderPos, Color.Gray * 0.8f, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);

                // Dibujar el cursor parpadeante si no está pensando
                if (!_isThinking && Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 1000 < 500)
                {
                    b.DrawString(font, "|", new Vector2(placeholderPos.X - 2, placeholderPos.Y), Game1.textColor, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9f);
                }
            }

            for (int i = 0; i < visibleLinesCount; i++)
            {
                int lineIndex = _currentScrollIndex + i; 
                if (lineIndex < _wrappedLines.Count && !string.IsNullOrEmpty(_wrappedLines[lineIndex]))
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

        public void DrawNativeBox(SpriteBatch b, int xPos, int yPos, int boxWidth, int boxHeight)
        {
            // 1. Centro (Textura de madera)
            b.Draw(Game1.mouseCursors, new Rectangle(xPos, yPos, boxWidth, boxHeight), new Rectangle(306, 320, 16, 16), Color.White);
            
            // 2. Bordes (Se dibujan con offsets negativos para sobresalir de la caja central)
            b.Draw(Game1.mouseCursors, new Rectangle(xPos, yPos - 20, boxWidth, 24), new Rectangle(275, 313, 1, 6), Color.White); // Arriba
            b.Draw(Game1.mouseCursors, new Rectangle(xPos + 12, yPos + boxHeight, boxWidth - 20, 32), new Rectangle(275, 328, 1, 8), Color.White); // Abajo
            b.Draw(Game1.mouseCursors, new Rectangle(xPos - 32, yPos + 24, 32, boxHeight - 28), new Rectangle(264, 325, 8, 1), Color.White); // Izquierda
            b.Draw(Game1.mouseCursors, new Rectangle(xPos + boxWidth, yPos, 28, boxHeight), new Rectangle(293, 324, 7, 1), Color.White); // Derecha
            
            // 3. Esquinas (Piezas únicas con escala de 4f)
            b.Draw(Game1.mouseCursors, new Vector2(xPos - 44, yPos - 28), new Rectangle(261, 311, 14, 13), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.87f); // Superior Izq
            b.Draw(Game1.mouseCursors, new Vector2(xPos + boxWidth - 8, yPos - 28), new Rectangle(291, 311, 12, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.87f); // Superior Der
            b.Draw(Game1.mouseCursors, new Vector2(xPos + boxWidth - 8, yPos + boxHeight - 8), new Rectangle(291, 326, 12, 12), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.87f); // Inferior Der
            b.Draw(Game1.mouseCursors, new Vector2(xPos - 44, yPos + boxHeight - 4), new Rectangle(261, 327, 14, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.87f); // Inferior Izq
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);
            _textBox.Selected = true; 
            
            // Si hacen click dentro del area de la caja de diálogo superior
            if (new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height).Contains(x, y))
            {
                if (!_isThinking && _historyPages.Count > 0)
                {
                    // Mitad izquierda para retroceder
                    if (x < this.xPositionOnScreen + this.width / 2 && _historyIndex > 0)
                    {
                        _historyIndex--;
                        _aiResponseText = _historyPages[_historyIndex];
                        _typewriterIndex = _aiResponseText.Length;
                        CurrentEmotion = _historyEmotions[_historyIndex];
                        Game1.playSound("smallSelect");
                        return;
                    }

                    // Mitad derecha (o si no se pudo retroceder) para avanzar/autocompletar
                    if (_historyIndex < _historyPages.Count)
                    {
                        if (_typewriterIndex < _aiResponseText.Length)
                        {
                            // Autocompletar la página actual
                            int remaining = _aiResponseText.Length - _typewriterIndex;
                            for (int i = 0; i <= remaining; i++)
                            {
                                if (_emotionKeyframes.ContainsKey(_globalTextIndex + i))
                                    CurrentEmotion = _emotionKeyframes[_globalTextIndex + i];
                            }
                            
                            _globalTextIndex += remaining;
                            _typewriterIndex = _aiResponseText.Length;
                            _historyEmotions[_historyIndex] = CurrentEmotion;
                            Game1.playSound("dialogueCharacter");
                        }
                        else if (_historyIndex < _historyPages.Count - 1)
                        {
                            // Siguiente página
                            _historyIndex++;
                            _aiResponseText = _historyPages[_historyIndex];
                            
                            if (_historyIndex > _maxHistoryIndexReached)
                            {
                                _maxHistoryIndexReached = _historyIndex;
                                _typewriterIndex = 0; // Si es nueva, efecto de máquina
                            }
                            else
                            {
                                _typewriterIndex = _aiResponseText.Length; // Si es historia, mostrar completo
                                CurrentEmotion = _historyEmotions[_historyIndex];
                            }
                            Game1.playSound("smallSelect");
                        }
                        else
                        {
                            // Cerrar menú si ya no hay más páginas (como en Vanilla)
                            Game1.exitActiveMenu();
                            Game1.playSound("dialogueCharacterClose");
                        }
                    }
                }
            }
        }
    }
}
