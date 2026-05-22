using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Buildings;
using LivingCompanionsValley.Models;
using LivingCompanionsValley.Services;

namespace LivingCompanionsValley.Menus
{
    public class Applicant
    {
        public WorkerState State { get; set; }
        public Farmer Dummy { get; set; }
        public int HireCost { get; set; }

        public Applicant(WorkerState state, int hireCost)
        {
            this.State = state;
            this.HireCost = hireCost;
            this.Dummy = WorkerTextureBaker.CreateDummyFarmer(state);
        }
    }

    public class HiringLedgerMenu : IClickableMenu
    {
        private readonly List<Applicant> _applicants = new List<Applicant>();
        private readonly List<ClickableComponent> _hireButtons = new List<ClickableComponent>();
        private string _statusMessage = "";
        private int _statusTimer = 0;
        private Texture2D _billboardTexture;

        public HiringLedgerMenu() : base(0, 0, 1352, 792, true)
        {
            _billboardTexture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\SpecialOrdersBoard");

            // Centrar el menú en la pantalla
            Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(width, height);
            this.xPositionOnScreen = (int)center.X;
            this.yPositionOnScreen = (int)center.Y;

            // Generar dos aplicantes procedurales
            GenerateApplicants();

            // Configurar botón de cerrar nativo (esquina superior derecha)
            this.upperRightCloseButton = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width - 20, this.yPositionOnScreen, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f
            );

            // Configurar botones de contratación imitando AcceptQuest
            int btnWidth = (int)Game1.dialogueFont.MeasureString("Contratar").X + 24;
            int btnHeight = (int)Game1.dialogueFont.MeasureString("Contratar").Y + 24;

            _hireButtons.Add(new ClickableComponent(
                new Rectangle(this.xPositionOnScreen + this.width / 4 - 128, this.yPositionOnScreen + this.height - 128, btnWidth, btnHeight),
                "0"
            ));

            _hireButtons.Add(new ClickableComponent(
                new Rectangle(this.xPositionOnScreen + this.width * 3 / 4 - 128, this.yPositionOnScreen + this.height - 128, btnWidth, btnHeight),
                "1"
            ));
        }

        private void GenerateApplicants()
        {
            var rand = new Random();
            string[] names = { "Pedro", "Juan", "Salome", "Mateo", "Lucas", "Maria", "Laura", "Carmen", "Sofia" };
            
            for (int i = 0; i < 2; i++)
            {
                string name = names[rand.Next(names.Length)] + $" {rand.Next(10, 99)}";
                var state = new WorkerState
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Wage = rand.Next(40, 120),
                    FarmingLevel = rand.Next(1, 6),
                    ForagingLevel = rand.Next(1, 6),
                    SkinColor = rand.Next(0, 6),
                    HairStyle = rand.Next(0, 36),
                    HairColorR = rand.Next(50, 255),
                    HairColorG = rand.Next(50, 255),
                    HairColorB = rand.Next(50, 255),
                    Shirt = rand.Next(0, 100),
                    Pants = rand.Next(0, 4),
                    PantsColorR = rand.Next(50, 255),
                    PantsColorG = rand.Next(50, 255),
                    PantsColorB = rand.Next(50, 255)
                };

                int hireCost = state.Wage * 3; // Depósito inicial de 3 días de salario
                _applicants.Add(new Applicant(state, hireCost));
            }
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            for (int i = 0; i < _hireButtons.Count; i++)
            {
                if (_hireButtons[i].containsPoint(x, y))
                {
                    TryHireWorker(_applicants[i]);
                    return;
                }
            }
        }

        private void TryHireWorker(Applicant applicant)
        {
            // 1. Validar fondos
            if (Game1.player.Money < applicant.HireCost)
            {
                ShowStatus("¡No tienes suficiente oro para el depósito!");
                Game1.playSound("cancel");
                return;
            }

            // 2. Buscar cabaña libre
            var farm = Game1.getFarm();
            var allCabins = farm.buildings
                .Where(b => b.buildingType.Value == "Cabin")
                .ToList();

            // Encontrar cabañas ya asignadas a trabajadores actuales
            var activeWorkers = ModEntry.Instance?.GetHiredWorkers() ?? new List<WorkerNPC>();
            var occupiedCabinNames = activeWorkers.Select(w => w.State.CabinName).ToHashSet();

            // Buscar la primera cabaña libre
            var freeCabin = allCabins.FirstOrDefault(c => c.indoors.Value != null && !occupiedCabinNames.Contains(c.indoors.Value.NameOrUniqueName));

            if (freeCabin == null)
            {
                ShowStatus("No hay cabañas libres. Construye una con Robin.");
                Game1.playSound("cancel");
                return;
            }

            // 3. Contratar
            Game1.player.Money -= applicant.HireCost;
            applicant.State.CabinName = freeCabin.indoors.Value.NameOrUniqueName;

            // Registrar en el ModEntry y spawnear
            ModEntry.Instance?.RegisterAndSpawnWorker(applicant.State);

            Game1.playSound("purchase");
            ShowStatus($"¡Contrataste a {applicant.State.Name}!");
            
            // Cerrar menú
            this.exitThisMenu();
        }

        private void ShowStatus(string msg)
        {
            _statusMessage = msg;
            _statusTimer = 180; // 3 segundos a 60 FPS
        }

        public override void update(GameTime time)
        {
            base.update(time);
            if (_statusTimer > 0)
            {
                _statusTimer--;
            }
        }

        public override void draw(SpriteBatch b)
        {
            if (!Game1.options.showClearBackgrounds)
            {
                b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);
            }

            // Fondo del Tablero de Órdenes Especiales original
            b.Draw(_billboardTexture, new Vector2(xPositionOnScreen, yPositionOnScreen), new Rectangle(0, 0, 338, 198), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);

            // Dibujar perfiles de aplicantes (Left y Right)
            for (int i = 0; i < _applicants.Count; i++)
            {
                var app = _applicants[i];
                int startX = xPositionOnScreen + (i == 0 ? 96 : 736);
                int headerY = yPositionOnScreen + 128;

                // Título: Nombre
                string nameStr = $"Se ofrece: {app.State.Name}";
                Utility.drawTextWithShadow(b, nameStr, Game1.dialogueFont, new Vector2(startX + 256 - Game1.dialogueFont.MeasureString(nameStr).X / 2f, headerY), Game1.textColor, 1f, -1f, -1, -1, 0.5f);

                // Sprite animado del trabajador
                app.Dummy.FarmerRenderer.drawMiniPortrat(b, new Vector2(startX, headerY - 10), 0.00011f, 3f, 2, app.Dummy);

                // Descripción simulando un anuncio
                string description = $"¡Hola! Busco empleo estable en tu granja. \n" +
                                     $"Cobro {app.State.Wage}g al día.\n" +
                                     $"Habilidades:\n" +
                                     $"Agricultura Nv.{app.State.FarmingLevel}\n" +
                                     $"Recolección Nv.{app.State.ForagingLevel}\n\n" +
                                     $"Depósito Inicial: {app.HireCost}g (Alojamiento Requerido)";
                
                string parsedDesc = Game1.parseText(description, Game1.dialogueFont, 480);
                Utility.drawTextWithShadow(b, parsedDesc, Game1.dialogueFont, new Vector2(startX, yPositionOnScreen + 220), Game1.textColor, 0.8f, -1f, -1, -1, 0.5f);

                // Botón de Contratar
                var btn = _hireButtons[i];
                bool isHovered = btn.containsPoint(Game1.getMouseX(), Game1.getMouseY());
                float scale = isHovered ? 1.2f : 1f;

                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 373, 9, 9), btn.bounds.X, btn.bounds.Y, btn.bounds.Width, btn.bounds.Height, scale > 1f ? Color.LightPink : Color.White, 4f * scale);
                Utility.drawTextWithShadow(b, "Contratar", Game1.dialogueFont, new Vector2(btn.bounds.X + 12, btn.bounds.Y + 16), Game1.textColor);
            }

            // Mensajes de estado (errores, éxito)
            if (_statusTimer > 0)
            {
                int textWidth = (int)Game1.dialogueFont.MeasureString(_statusMessage).X;
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), 
                    this.xPositionOnScreen + this.width / 2 - textWidth / 2 - 30, 
                    this.yPositionOnScreen + this.height - 80, 
                    textWidth + 60, 80, Color.White, 4f);
                Utility.drawTextWithShadow(b, _statusMessage, Game1.dialogueFont, 
                    new Vector2(this.xPositionOnScreen + this.width / 2 - textWidth / 2, this.yPositionOnScreen + this.height - 60), 
                    Color.DarkRed);
            }

            base.draw(b); // Dibuja upperRightCloseButton automáticamente
            Game1.mouseCursorTransparency = 1f;
            this.drawMouse(b);
        }
    }
}
