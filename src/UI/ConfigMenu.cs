using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.BellsAndWhistles;
using StardewModdingAPI;
using StardewLivingValley.Configuration;
using System;
using System.Collections.Generic;

namespace StardewLivingValley.UI
{
    public class ConfigMenu : IClickableMenu
    {
        private ModConfig _config;
        private IModHelper _helper;

        // --- Sistema de Slots (copiado del OptionsPage nativo) ---
        private const int ItemsPerPage = 7;
        private List<ClickableComponent> _optionSlots = new List<ClickableComponent>();
        private int _currentItemIndex = 0;
        private ClickableTextureComponent _upArrow;
        private ClickableTextureComponent _downArrow;
        private ClickableTextureComponent _scrollBar;
        private Rectangle _scrollBarRunner;
        private bool _scrolling;
        private int _optionsSlotHeld = -1;

        // --- Nuestras opciones custom ---
        private List<ConfigOption> _options = new List<ConfigOption>();

        // --- Estado de escucha de teclas ---
        private int _listeningOptionIndex = -1;

        // --- Dropdown state ---
        private ConfigOption? _openDropdown;

        // --- Botón OK ---
        private ClickableTextureComponent _okButton;

        public ConfigMenu(ModConfig config, IModHelper helper)
            : base(
                Game1.uiViewport.Width / 2 - (800 + IClickableMenu.borderWidth * 2) / 2,
                Game1.uiViewport.Height / 2 - (600 + IClickableMenu.borderWidth * 2) / 2,
                800 + IClickableMenu.borderWidth * 2,
                600 + IClickableMenu.borderWidth * 2,
                showUpperRightCloseButton: true)
        {
            _config = config;
            _helper = helper;

            // Flechas de scroll (exactamente como OptionsPage)
            _upArrow = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width + 16, yPositionOnScreen + 64, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
            _downArrow = new ClickableTextureComponent(
                new Rectangle(xPositionOnScreen + width + 16, yPositionOnScreen + height - 64, 44, 48),
                Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
            _scrollBar = new ClickableTextureComponent(
                new Rectangle(_upArrow.bounds.X + 12, _upArrow.bounds.Y + _upArrow.bounds.Height + 4, 24, 40),
                Game1.mouseCursors, new Rectangle(435, 463, 6, 10), 4f);
            _scrollBarRunner = new Rectangle(_scrollBar.bounds.X, _upArrow.bounds.Y + _upArrow.bounds.Height + 4,
                _scrollBar.bounds.Width, height - 128 - _upArrow.bounds.Height - 8);

            // Crear slots (7 filas visibles, como el OptionsPage original)
            for (int i = 0; i < ItemsPerPage; i++)
            {
                _optionSlots.Add(new ClickableComponent(
                    new Rectangle(xPositionOnScreen + 16, yPositionOnScreen + 80 + 4 + i * ((height - 128) / ItemsPerPage) + 16,
                        width - 32, (height - 128) / ItemsPerPage + 4),
                    i.ToString()));
            }

            int slotWidth = _optionSlots.Count > 0 ? _optionSlots[0].bounds.Width : width - 32;

            // --- Agregar opciones ---
            _options.Add(new ConfigOption("Stardew Living Valley", ConfigOptionType.SectionTitle));

            _options.Add(new ConfigOption("Clave de Acceso", ConfigOptionType.ApiKeyEntry, slotWidth));
            var apiEditor = _options[_options.Count - 1].ApiEditor;
            if (apiEditor != null)
            {
                apiEditor.Text = _config.VeniceApiKey ?? "";
                apiEditor.CursorPosition = apiEditor.Text.Length;
            }

            _options.Add(new ConfigOption("Controles:", ConfigOptionType.SectionTitle));

            _options.Add(new ConfigOption("Hablar con Aldeanos", ConfigOptionType.KeyBind, slotWidth));
            _options[_options.Count - 1].KeyValue = _config.AIChatKey;

            _options.Add(new ConfigOption("Abrir Opciones del Mod", ConfigOptionType.KeyBind, slotWidth));
            _options[_options.Count - 1].KeyValue = _config.ConfigMenuKey;

            _options.Add(new ConfigOption("Experimental:", ConfigOptionType.SectionTitle));

            _options.Add(new ConfigOption("Intercepcion de dialogo nativo", ConfigOptionType.Checkbox));
            _options[_options.Count - 1].BoolValue = _config.InterceptVanillaDialogue;

            _options.Add(new ConfigOption("Idioma de Salida", ConfigOptionType.Dropdown, slotWidth));
            _options[_options.Count - 1].DropdownOptions = new List<string> { "Español", "English", "Português", "Français", "Deutsch", "Italiano", "日本語", "中文" };
            
            int langIdx = _options[_options.Count - 1].DropdownOptions.IndexOf(_config.OutputLanguage);
            if (langIdx == -1 && _config.OutputLanguage == "Spanish") langIdx = 0;
            _options[_options.Count - 1].DropdownSelectedIndex = Math.Max(0, langIdx);

            // Botón OK (posición inferior derecha, dentro de la caja)
            _okButton = new ClickableTextureComponent("OK",
                new Rectangle(xPositionOnScreen + width - 128 - IClickableMenu.borderWidth,
                    yPositionOnScreen + height - 20, 64, 64),
                null, null, Game1.mouseCursors, Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 46), 1f);
        }

        private void SetScrollBarToCurrentIndex()
        {
            if (_options.Count > 0)
            {
                _scrollBar.bounds.Y = _scrollBarRunner.Height / Math.Max(1, _options.Count - ItemsPerPage + 1) * _currentItemIndex + _upArrow.bounds.Bottom + 4;
                if (_scrollBar.bounds.Y > _downArrow.bounds.Y - _scrollBar.bounds.Height - 4)
                    _scrollBar.bounds.Y = _downArrow.bounds.Y - _scrollBar.bounds.Height - 4;
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (_listeningOptionIndex >= 0) return;

            base.receiveLeftClick(x, y, playSound);

            // Desmarcar todas las opciones de texto por defecto
            foreach (var opt in _options)
            {
                if (opt.ApiEditor != null && opt.ApiEditor.Selected)
                    opt.ApiEditor.Selected = false;
            }

            if (_openDropdown != null)
            {
                int optIdx = _options.IndexOf(_openDropdown);
                int visibleIdx = optIdx - _currentItemIndex;
                if (visibleIdx >= 0 && visibleIdx < ItemsPerPage)
                {
                    int slotX = _optionSlots[visibleIdx].bounds.X;
                    int slotY = _optionSlots[visibleIdx].bounds.Y;

                    int ddX = slotX + _openDropdown.Bounds.X;
                    int ddY = slotY + _openDropdown.Bounds.Y;
                    int ddW = _openDropdown.Bounds.Width;
                    int ddH = _openDropdown.Bounds.Height;

                    Rectangle expandedBounds = new Rectangle(ddX, ddY, ddW - 48, ddH * _openDropdown.DropdownOptions.Count);
                    if (expandedBounds.Contains(x, y))
                    {
                        int clickedIndex = (y - ddY) / ddH;
                        if (clickedIndex >= 0 && clickedIndex < _openDropdown.DropdownOptions.Count)
                        {
                            _openDropdown.DropdownSelectedIndex = clickedIndex;
                            Game1.playSound("drumkit6");
                        }
                    }
                }
                
                // Siempre cerrar el dropdown al hacer click, sea dentro o fuera
                _openDropdown = null;
                return;
            }

            if (_okButton.containsPoint(x, y))
            {
                SaveAndClose();
                return;
            }

            if (_downArrow.containsPoint(x, y) && _currentItemIndex < Math.Max(0, _options.Count - ItemsPerPage))
            {
                _currentItemIndex++;
                SetScrollBarToCurrentIndex();
                Game1.playSound("shwip");
                return;
            }
            if (_upArrow.containsPoint(x, y) && _currentItemIndex > 0)
            {
                _currentItemIndex--;
                SetScrollBarToCurrentIndex();
                Game1.playSound("shwip");
                return;
            }
            if (_scrollBar.containsPoint(x, y))
            {
                _scrolling = true;
                return;
            }

            for (int i = 0; i < _optionSlots.Count; i++)
            {
                int optIdx = _currentItemIndex + i;
                if (optIdx >= _options.Count) break;

                if (_optionSlots[i].bounds.Contains(x, y))
                {
                    var opt = _options[optIdx];
                    int localX = x - _optionSlots[i].bounds.X;
                    int localY = y - _optionSlots[i].bounds.Y;

                    if (opt.Type == ConfigOptionType.Checkbox)
                    {
                        if (localX >= opt.Bounds.X && localX <= opt.Bounds.X + 36
                            && localY >= opt.Bounds.Y && localY <= opt.Bounds.Y + 36)
                        {
                            opt.BoolValue = !opt.BoolValue;
                            Game1.playSound("drumkit6");
                        }
                    }
                    else if (opt.Type == ConfigOptionType.KeyBind)
                    {
                        if (localX >= opt.SetButtonBounds.X && localX <= opt.SetButtonBounds.X + opt.SetButtonBounds.Width
                            && localY >= opt.SetButtonBounds.Y && localY <= opt.SetButtonBounds.Y + opt.SetButtonBounds.Height)
                        {
                            _listeningOptionIndex = optIdx;
                            Game1.playSound("breathin");
                        }
                    }
                    else if (opt.Type == ConfigOptionType.ApiKeyEntry && opt.ApiEditor != null)
                    {
                        opt.ApiEditor.Selected = true;
                    }
                    else if (opt.Type == ConfigOptionType.TextEntry)
                    {
                        // Desactivar escritura en TextEntry
                        Game1.playSound("cancel");
                    }
                    else if (opt.Type == ConfigOptionType.Dropdown)
                    {
                        if (localX >= opt.Bounds.X && localX <= opt.Bounds.X + opt.Bounds.Width &&
                            localY >= opt.Bounds.Y && localY <= opt.Bounds.Y + opt.Bounds.Height)
                        {
                            _openDropdown = opt;
                            Game1.playSound("shwip");
                            return; // Importante para no activar _optionsSlotHeld ni otras cosas
                        }
                    }

                    _optionsSlotHeld = i;
                    break;
                }
            }
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            if (_scrolling)
            {
                int oldY = _scrollBar.bounds.Y;
                _scrollBar.bounds.Y = Math.Min(yPositionOnScreen + height - 64 - 12 - _scrollBar.bounds.Height,
                    Math.Max(y, yPositionOnScreen + _upArrow.bounds.Height + 20));
                float pct = (float)(y - _scrollBarRunner.Y) / (float)_scrollBarRunner.Height;
                _currentItemIndex = Math.Min(_options.Count - ItemsPerPage, Math.Max(0, (int)(_options.Count * pct)));
                SetScrollBarToCurrentIndex();
                if (oldY != _scrollBar.bounds.Y) Game1.playSound("shiny4");
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            _optionsSlotHeld = -1;
            _scrolling = false;
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (direction > 0 && _currentItemIndex > 0)
            {
                _currentItemIndex--;
                SetScrollBarToCurrentIndex();
                Game1.playSound("shiny4");
            }
            else if (direction < 0 && _currentItemIndex < Math.Max(0, _options.Count - ItemsPerPage))
            {
                _currentItemIndex++;
                SetScrollBarToCurrentIndex();
                Game1.playSound("shiny4");
            }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (_listeningOptionIndex >= 0)
            {
                if (key == Keys.Escape)
                {
                    Game1.playSound("bigDeSelect");
                }
                else
                {
                    if (Enum.TryParse<SButton>(key.ToString(), out SButton sbtn))
                    {
                        _options[_listeningOptionIndex].KeyValue = sbtn;
                        Game1.playSound("coin");
                    }
                }
                _listeningOptionIndex = -1;
                return;
            }

            foreach (var opt in _options)
            {
                if (opt.ApiEditor != null && Game1.keyboardDispatcher.Subscriber == opt.ApiEditor)
                {
                    if (key == Keys.Left && opt.ApiEditor.CursorPosition > 0)
                        opt.ApiEditor.CursorPosition--;
                    else if (key == Keys.Right && opt.ApiEditor.CursorPosition < opt.ApiEditor.Text.Length)
                        opt.ApiEditor.CursorPosition++;
                    return;
                }
                if (opt.TextBox != null && Game1.keyboardDispatcher.Subscriber == opt.TextBox) return;
            }

            if (key == Keys.Escape)
            {
                Game1.exitActiveMenu();
            }
        }

        protected override void cleanupBeforeExit()
        {
            base.cleanupBeforeExit();

            foreach (var opt in _options)
            {
                if (opt.Type == ConfigOptionType.TextEntry && opt.Label == "Clave de Acceso")
                {
                    _config.VeniceApiKey = opt.TextBox?.Text ?? _config.VeniceApiKey;
                }
                else if (opt.Type == ConfigOptionType.ApiKeyEntry && opt.Label == "Clave de Acceso")
                {
                    _config.VeniceApiKey = opt.ApiEditor?.Text ?? _config.VeniceApiKey;
                }
                else if (opt.Type == ConfigOptionType.KeyBind && opt.Label == "Hablar con Aldeanos")
                {
                    _config.AIChatKey = opt.KeyValue;
                }
                else if (opt.Type == ConfigOptionType.KeyBind && opt.Label == "Abrir Opciones del Mod")
                {
                    _config.ConfigMenuKey = opt.KeyValue;
                }
                else if (opt.Type == ConfigOptionType.Dropdown && opt.Label == "Idioma de Salida")
                {
                    if (opt.DropdownSelectedIndex >= 0 && opt.DropdownSelectedIndex < opt.DropdownOptions.Count)
                    {
                        _config.OutputLanguage = opt.DropdownOptions[opt.DropdownSelectedIndex];
                    }
                }
                else if (opt.Type == ConfigOptionType.Checkbox)
                {
                    _config.InterceptVanillaDialogue = opt.BoolValue;
                }
                else if (opt.Type == ConfigOptionType.TextEntry && opt.Label == "Idioma de Salida")
                {
                    _config.OutputLanguage = opt.TextBox?.Text ?? _config.OutputLanguage;
                }
            }

            _helper.WriteConfig(_config);
        }

        private void SaveAndClose()
        {
            Game1.playSound("money");
            Game1.exitActiveMenu();
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            _upArrow.tryHover(x, y);
            _downArrow.tryHover(x, y);
            _scrollBar.tryHover(x, y);
            _okButton.tryHover(x, y, 0.2f);
        }

        public override void draw(SpriteBatch b)
        {
            // 1. Fondo oscuro (como GameMenu.draw)
            if (!Game1.options.showMenuBackground && !Game1.options.showClearBackgrounds)
            {
                b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.4f);
            }

            // 2. Caja de diálogo nativa (exactamente como GameMenu.draw)
            Game1.drawDialogueBox(xPositionOnScreen, yPositionOnScreen, width, height, speaker: false, drawOnlyBox: true);

            // 3. Dibujar cada opción en su slot en el batch FrontToBack
            //    (excepto ApiKeyEntry, cuyo texto se dibuja en el batch Deferred de abajo)
            b.End();
            b.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);

            for (int i = 0; i < _optionSlots.Count; i++)
            {
                int optIdx = _currentItemIndex + i;
                if (optIdx < 0 || optIdx >= _options.Count) continue;

                DrawOption(b, _options[optIdx], _optionSlots[i].bounds.X, _optionSlots[i].bounds.Y);
            }

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            // 3b. Dibujar el texto de ApiKeyEntry en el batch Deferred para evitar conflictos de layerDepth
            for (int i = 0; i < _optionSlots.Count; i++)
            {
                int optIdx = _currentItemIndex + i;
                if (optIdx < 0 || optIdx >= _options.Count) continue;
                var opt = _options[optIdx];
                if (opt.Type == ConfigOptionType.ApiKeyEntry && opt.ApiEditor != null)
                {
                    DrawApiKeyText(b, opt, _optionSlots[i].bounds.X, _optionSlots[i].bounds.Y);
                }
            }

            // 4. Flechas y scrollbar
            _upArrow.draw(b);
            _downArrow.draw(b);
            if (_options.Count > ItemsPerPage)
            {
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 383, 6, 6),
                    _scrollBarRunner.X, _scrollBarRunner.Y, _scrollBarRunner.Width, _scrollBarRunner.Height,
                    Color.White, 4f, drawShadow: false);
                _scrollBar.draw(b);
            }

            // 5. Botón OK
            _okButton.draw(b);

            // 6. Overlay de escucha (como OptionsInputListener.draw)
            if (_listeningOptionIndex >= 0)
            {
                b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
                    new Rectangle(0, 0, 1, 1), Color.Black * 0.75f, 0f, Vector2.Zero, SpriteEffects.None, 0.999f);

                string msg = "Presiona una tecla...";
                Vector2 msgSize = Game1.dialogueFont.MeasureString(msg);
                b.DrawString(Game1.dialogueFont, msg,
                    Utility.getTopLeftPositionForCenteringOnScreen((int)msgSize.X, (int)msgSize.Y),
                    Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9999f);
            }

            // 6. Dropdown abierto (se dibuja encima de todo)
            if (_openDropdown != null)
            {
                int optIdx = _options.IndexOf(_openDropdown);
                int visibleIdx = optIdx - _currentItemIndex;
                if (visibleIdx >= 0 && visibleIdx < ItemsPerPage)
                {
                    int slotX = _optionSlots[visibleIdx].bounds.X;
                    int slotY = _optionSlots[visibleIdx].bounds.Y;

                    int ddX = slotX + _openDropdown.Bounds.X;
                    int ddY = slotY + _openDropdown.Bounds.Y;
                    int ddW = _openDropdown.Bounds.Width;
                    int ddH = _openDropdown.Bounds.Height;

                    // Dibujar caja grande del menú desplegado
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(433, 451, 3, 3), ddX, ddY, ddW - 48, ddH * _openDropdown.DropdownOptions.Count, Color.White, 4f, false);

                    for (int j = 0; j < _openDropdown.DropdownOptions.Count; j++)
                    {
                        int itemY = ddY + (j * ddH);
                        // Highlight si el ratón está encima
                        if (new Rectangle(ddX, itemY, ddW - 48, ddH).Contains(Game1.getMouseX(), Game1.getMouseY()))
                        {
                            b.Draw(Game1.staminaRect, new Rectangle(ddX + 4, itemY + 4, ddW - 48 - 8, ddH - 8), Color.Black * 0.3f);
                        }

                        string optText = _openDropdown.DropdownOptions[j];
                        Utility.drawTextWithShadow(b, optText, Game1.smallFont, new Vector2(ddX + 16, itemY + 8), Game1.textColor, 1f, 0.999f);
                    }
                }
            }

            // 7. Cruz de cerrar y mouse
            base.draw(b);
            if (!Game1.options.hardwareCursor)
            {
                drawMouse(b, ignore_transparency: true);
            }
        }

        /// <summary>
        /// Dibuja el texto y caret del ApiKeyEntry en el batch Deferred para evitar
        /// el parpadeo causado por conflictos de layerDepth en SpriteSortMode.FrontToBack.
        /// </summary>
        private void DrawApiKeyText(SpriteBatch b, ConfigOption opt, int slotX, int slotY)
        {
            if (opt.ApiEditor == null) return;

            int tbX = slotX + opt.Bounds.X - 8;
            int tbY = slotY + opt.Bounds.Y;
            int tbW = opt.Bounds.Width + 16;

            string fullText = opt.ApiEditor.Text ?? "";
            int cursorPosition = opt.ApiEditor.CursorPosition;
            if (cursorPosition < 0) cursorPosition = 0;
            if (cursorPosition > fullText.Length) cursorPosition = fullText.Length;

            int startIndex = 0;
            while (startIndex < cursorPosition && Game1.smallFont.MeasureString(fullText.Substring(startIndex, cursorPosition - startIndex)).X > tbW - 32)
            {
                startIndex++;
            }

            string visibleText = fullText.Substring(startIndex);
            while (visibleText.Length > 0 && Game1.smallFont.MeasureString(visibleText).X > tbW - 28)
            {
                visibleText = visibleText.Substring(0, visibleText.Length - 1);
            }

            Vector2 textPos = new Vector2(tbX + 12, tbY + 12);
            b.DrawString(Game1.smallFont, visibleText, textPos, Game1.textColor);

            bool showCaret = opt.ApiEditor.Selected && (DateTime.Now.Millisecond % 1000 < 500);
            if (showCaret)
            {
                string visibleTextBeforeCursor = fullText.Substring(startIndex, cursorPosition - startIndex);
                float cursorOffsetX = visibleTextBeforeCursor.Length > 0 ? Game1.smallFont.MeasureString(visibleTextBeforeCursor).X : 0;
                b.Draw(Game1.staminaRect, new Rectangle((int)(textPos.X + cursorOffsetX + 2), tbY + 8, 3, 32), Game1.textColor);
            }
        }

        private void DrawOption(SpriteBatch b, ConfigOption opt, int slotX, int slotY)
        {
            switch (opt.Type)
            {
                case ConfigOptionType.SectionTitle:
                    // Exactamente como OptionsElement con whichOption == -1
                    SpriteText.drawString(b, opt.Label,
                        slotX + opt.Bounds.X + (int)opt.LabelOffset.X,
                        slotY + opt.Bounds.Y + (int)opt.LabelOffset.Y + 56 - SpriteText.getHeightOfString(opt.Label),
                        999, -1, 999, 1f, 0.1f);
                    break;

                case ConfigOptionType.Checkbox:
                    // Exactamente como OptionsCheckbox.draw
                    b.Draw(Game1.mouseCursors,
                        new Vector2(slotX + opt.Bounds.X, slotY + opt.Bounds.Y),
                        opt.BoolValue ? new Rectangle(236, 425, 9, 9) : new Rectangle(227, 425, 9, 9),
                        Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.4f);
                    // Label a la derecha del checkbox (como OptionsElement base)
                    int labelStartX = slotX + opt.Bounds.X + opt.Bounds.Width + 8 + (int)opt.LabelOffset.X;
                    int labelStartY = slotY + opt.Bounds.Y + (int)opt.LabelOffset.Y;
                    Utility.drawTextWithShadow(b, opt.Label, Game1.dialogueFont,
                        new Vector2(labelStartX, labelStartY),
                        Game1.textColor, 1f, 0.1f);
                    break;

                case ConfigOptionType.KeyBind:
                    // Exactamente como OptionsInputListener.draw
                    string keyText = opt.Label + ": " + opt.KeyValue.ToString();
                    Utility.drawTextWithShadow(b, keyText, Game1.dialogueFont,
                        new Vector2(slotX + opt.Bounds.X, slotY + opt.Bounds.Y),
                        Game1.textColor, 1f, 0.15f);
                    // Botón "set" nativo
                    Utility.drawWithShadow(b, Game1.mouseCursors,
                        new Vector2(slotX + opt.SetButtonBounds.X, slotY + opt.SetButtonBounds.Y),
                        new Rectangle(294, 428, 21, 11),
                        Color.White, 0f, Vector2.Zero, 4f, flipped: false, 0.15f);
                    break;

                case ConfigOptionType.TextEntry:
                    if (opt.TextBox != null)
                    {
                        opt.TextBox.X = slotX + opt.Bounds.X - 8;
                        opt.TextBox.Y = slotY + opt.Bounds.Y;
                        opt.TextBox.Draw(b);
                        // Label a la derecha del TextBox
                        Utility.drawTextWithShadow(b, opt.Label, Game1.dialogueFont,
                            new Vector2(slotX + opt.Bounds.X + opt.Bounds.Width + 8,
                                        slotY + opt.Bounds.Y),
                            Game1.textColor, 1f, 0.1f);
                    }
                    break;

                case ConfigOptionType.ApiKeyEntry:
                    if (opt.ApiEditor != null)
                    {
                        // Dibujar sólo la textura de la caja en el batch FrontToBack.
                        // El texto y caret se dibujan en DrawApiKeyText() dentro del batch Deferred.
                        Texture2D tex = Game1.content.Load<Texture2D>("LooseSprites\\textBox");
                        int tbX = slotX + opt.Bounds.X - 8;
                        int tbY = slotY + opt.Bounds.Y;
                        int tbW = opt.Bounds.Width + 16;
                        int tbH = 48;

                        b.Draw(tex, new Rectangle(tbX, tbY, 16, tbH), new Rectangle(0, 0, 16, tex.Height), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.1f);
                        b.Draw(tex, new Rectangle(tbX + 16, tbY, tbW - 32, tbH), new Rectangle(16, 0, 4, tex.Height), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.1f);
                        b.Draw(tex, new Rectangle(tbX + tbW - 16, tbY, 16, tbH), new Rectangle(tex.Bounds.Width - 16, 0, 16, tex.Height), Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.1f);

                        // Etiqueta a la derecha de la caja (estilo nativo de Stardew, sin los dos puntos)
                        Utility.drawTextWithShadow(b, opt.Label, Game1.dialogueFont,
                            new Vector2(tbX + tbW + 12, tbY + 4),
                            Game1.textColor, 1f, 0.1f);
                    }
                    break;
                    
                case ConfigOptionType.Dropdown:
                    int ddX = slotX + opt.Bounds.X;
                    int ddY = slotY + opt.Bounds.Y;
                    int ddW = opt.Bounds.Width;
                    int ddH = opt.Bounds.Height;

                    // Fondo del dropdown, hasta antes del botón
                    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(433, 451, 3, 3), ddX, ddY, ddW - 48, ddH, Color.White, 4f, false);

                    // Texto seleccionado usando smallFont con profundidad correcta
                    if (opt.DropdownOptions.Count > 0 && opt.DropdownSelectedIndex >= 0 && opt.DropdownSelectedIndex < opt.DropdownOptions.Count)
                    {
                        Utility.drawTextWithShadow(b, opt.DropdownOptions[opt.DropdownSelectedIndex], Game1.smallFont, new Vector2(ddX + 16, ddY + 8), Game1.textColor, 1f, 0.991f);
                    }

                    // Botón de flecha justo al final del fondo
                    b.Draw(Game1.mouseCursors, new Vector2(ddX + ddW - 48, ddY), new Rectangle(437, 450, 10, 11), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.9f);

                    // Etiqueta a la derecha
                    Utility.drawTextWithShadow(b, opt.Label, Game1.dialogueFont, new Vector2(ddX + ddW + 16, ddY + 4), Game1.textColor, 1f, 0.1f);
                    break;
            }
        }
    }

    // --- Tipos y clases auxiliares ---
    public enum ConfigOptionType
    {
        SectionTitle,
        Checkbox,
        KeyBind,
        TextEntry,
        ApiKeyEntry,   // Full-width text box for long keys
        Dropdown
    }

    public class ConfigOption
    {
        public string Label;
        public ConfigOptionType Type;
        public Rectangle Bounds;
        public Rectangle SetButtonBounds;
        public Vector2 LabelOffset = Vector2.Zero;

        public bool BoolValue;
        public SButton KeyValue;
        public string TextValue = "";
        public TextBox? TextBox;
        public ApiKeyEditor? ApiEditor;
        public List<string> DropdownOptions = new List<string>();
        public int DropdownSelectedIndex = 0;

        public ConfigOption(string label, ConfigOptionType type, int slotWidth = 0)
        {
            Label = label;
            Type = type;

            switch (type)
            {
                case ConfigOptionType.SectionTitle:
                    Bounds = new Rectangle(32, 16, 36, 36);
                    break;

                case ConfigOptionType.Checkbox:
                    Bounds = new Rectangle(32, 16, 36, 36);
                    break;

                case ConfigOptionType.KeyBind:
                    Bounds = new Rectangle(32, 16, slotWidth - 32, 44);
                    SetButtonBounds = new Rectangle(slotWidth - 112, 28, 84, 44);
                    break;

                case ConfigOptionType.TextEntry:
                    int textWidth = (int)Game1.smallFont.MeasureString("Windowed Borderless Mode   ").X + 48;
                    Bounds = new Rectangle(32, 16, textWidth, 44);
                    TextBox = new TextBox(
                        Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
                        null, Game1.smallFont, Color.Black);
                    TextBox.Width = textWidth;
                    break;

                case ConfigOptionType.ApiKeyEntry:
                    // Mitad del ancho disponible, etiqueta a la derecha
                    int apiWidth = (slotWidth - 64) / 2;
                    Bounds = new Rectangle(32, 16, apiWidth, 44);
                    ApiEditor = new ApiKeyEditor();
                    break;

                case ConfigOptionType.Dropdown:
                    int dropWidth = (slotWidth - 64) / 2;
                    Bounds = new Rectangle(32, 16, dropWidth, 44);
                    break;
            }
        }
    }

    public class ApiKeyEditor : IKeyboardSubscriber
    {
        public string Text { get; set; } = "";
        public int CursorPosition { get; set; } = 0;
        
        private bool _selected;
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return; // Rompe el ciclo infinito (StackOverflow)
                _selected = value;
                if (value) Game1.keyboardDispatcher.Subscriber = this;
                else if (Game1.keyboardDispatcher.Subscriber == this) Game1.keyboardDispatcher.Subscriber = null;
            }
        }

        public void RecieveTextInput(char inputChar)
        {
            if (CursorPosition < 0) CursorPosition = 0;
            if (CursorPosition > Text.Length) CursorPosition = Text.Length;
            Text = Text.Insert(CursorPosition, inputChar.ToString());
            CursorPosition++;
        }

        public void RecieveTextInput(string text)
        {
            if (CursorPosition < 0) CursorPosition = 0;
            if (CursorPosition > Text.Length) CursorPosition = Text.Length;
            Text = Text.Insert(CursorPosition, text);
            CursorPosition += text.Length;
        }

        public void RecieveCommandInput(char command)
        {
            if (command == '\b' && CursorPosition > 0)
            {
                Text = Text.Remove(CursorPosition - 1, 1);
                CursorPosition--;
            }
        }

        public void RecieveSpecialInput(Keys key)
        {
            // Opcional: manejar Home, End, Delete, etc. si se requiere.
        }
    }
}
