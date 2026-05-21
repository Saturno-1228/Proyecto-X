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
        private ClickableTextureComponent _closeButton = null!;
        private string _statusMessage = "";
        private int _statusTimer = 0;

        public HiringLedgerMenu() : base(0, 0, 800, 600, true)
        {
            // Centrar el menú en la pantalla
            this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
            this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;

            // Generar dos aplicantes procedurales
            GenerateApplicants();

            // Configurar botón de cerrar nativo
            _closeButton = new ClickableTextureComponent(
                new Rectangle(this.xPositionOnScreen + this.width - 36, this.yPositionOnScreen - 8, 48, 48),
                Game1.mouseCursors,
                new Rectangle(337, 494, 12, 12),
                4f
            );

            // Configurar botones de contratación
            for (int i = 0; i < _applicants.Count; i++)
            {
                int btnX = this.xPositionOnScreen + this.width - 220;
                int btnY = this.yPositionOnScreen + 150 + (i * 180);
                
                _hireButtons.Add(new ClickableComponent(
                    new Rectangle(btnX, btnY, 180, 60),
                    i.ToString(),
                    "Contratar"
                ));
            }
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
            if (_closeButton.containsPoint(x, y))
            {
                Game1.playSound("bigDeSelect");
                this.exitThisMenu();
                return;
            }

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
            // Fondo oscuro
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

            // Panel de madera principal
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 4f);

            // Botón de cerrar
            _closeButton.draw(b);

            // Título
            string title = "Tablero de Contratación";
            Utility.drawTextWithShadow(b, title, Game1.dialogueFont, new Vector2(xPositionOnScreen + 60, yPositionOnScreen + 40), Game1.textColor, 1.2f);
            
            string desc = "Contrata trabajadores locales para la granja. Requiere una cabaña libre por trabajador.";
            Utility.drawTextWithShadow(b, desc, Game1.smallFont, new Vector2(xPositionOnScreen + 60, yPositionOnScreen + 95), Color.DarkGray);

            // Dibujar perfiles de aplicantes
            for (int i = 0; i < _applicants.Count; i++)
            {
                var app = _applicants[i];
                int cardY = yPositionOnScreen + 140 + (i * 180);
                int cardX = xPositionOnScreen + 50;

                // Dibujar recuadro de perfil
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 396, 15, 15), cardX, cardY, width - 100, 160, Color.White, 4f, false);

                // Dibujar al granjero dummy (animación de pie)
                app.Dummy.Position = new Vector2(cardX + 40, cardY + 40);
                app.Dummy.FacingDirection = 2; // Hacia el frente
                app.Dummy.draw(b);

                // Nombre e información
                Utility.drawTextWithShadow(b, app.State.Name, Game1.dialogueFont, new Vector2(cardX + 130, cardY + 25), Game1.textColor);
                string stats = $"Agricultura: Nv. {app.State.FarmingLevel} | Recolección: Nv. {app.State.ForagingLevel}";
                Utility.drawTextWithShadow(b, stats, Game1.smallFont, new Vector2(cardX + 130, cardY + 70), Color.DimGray);
                
                string costText = $"Costo Diario: {app.State.Wage}g | Depósito Inicial: {app.HireCost}g";
                Utility.drawTextWithShadow(b, costText, Game1.smallFont, new Vector2(cardX + 130, cardY + 105), Color.DarkGoldenrod);

                // Botón de Contratar
                var btn = _hireButtons[i];
                IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(384, 373, 18, 18), btn.bounds.X, btn.bounds.Y, btn.bounds.Width, btn.bounds.Height, Color.White, 4f);
                
                Vector2 labelSize = Game1.dialogueFont.MeasureString(btn.label);
                Vector2 labelPos = new Vector2(
                    btn.bounds.X + (btn.bounds.Width - labelSize.X) / 2,
                    btn.bounds.Y + (btn.bounds.Height - labelSize.Y) / 2 - 4
                );
                Utility.drawTextWithShadow(b, btn.label, Game1.dialogueFont, labelPos, Game1.textColor);
            }

            // Dibujar mensaje de estado
            if (_statusTimer > 0 && !string.IsNullOrEmpty(_statusMessage))
            {
                Vector2 msgSize = Game1.dialogueFont.MeasureString(_statusMessage);
                Vector2 msgPos = new Vector2(
                    xPositionOnScreen + (width - msgSize.X) / 2,
                    yPositionOnScreen + height - 80
                );
                Utility.drawTextWithShadow(b, _statusMessage, Game1.dialogueFont, msgPos, Color.Red);
            }

            drawMouse(b);
        }
    }
}
