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
        public WorkerFakeFarmer Dummy { get; set; }
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
            for (int i = 0; i < 2; i++)
            {
                WorkerState state = WorkerGenerator.GenerateApplicant();
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

                // Título: Nombre Completo
                string nameStr = $"{app.State.Name} {app.State.Surname}";
                Utility.drawTextWithShadow(b, nameStr, Game1.dialogueFont, new Vector2(startX + 256 - Game1.dialogueFont.MeasureString(nameStr).X / 2f, headerY), Game1.textColor, 1f, -1f, -1, -1, 0.5f);

                // Texto introductorio
                string intro = "\"¡Busco empleo estable en tu granja!\"";
                Utility.drawTextWithShadow(b, intro, Game1.smallFont, new Vector2(startX + 256 - Game1.smallFont.MeasureString(intro).X / 2f, headerY + 45), Color.DarkSlateGray);

                // Sprite animado del trabajador
                int cardY = headerY + 90;
                app.Dummy.FarmerRenderer.drawMiniPortrat(b, new Vector2(startX + 30, cardY - 20), 0.00011f, 4f, 2, app.Dummy);

                // Traducir rasgo
                string traitStr = app.State.Trait switch
                {
                    WorkerTrait.Workaholic => "Adicto al Trabajo",
                    WorkerTrait.GreenThumb => "Mano Verde",
                    WorkerTrait.Clumsy => "Torpe",
                    WorkerTrait.EarlyBird => "Madrugador",
                    WorkerTrait.NightOwl => "Búho Nocturno",
                    WorkerTrait.CitySlicker => "Citadino",
                    _ => "Ninguno"
                };

                // Información Personal
                string genderStr = app.State.Gender == GenderArchetype.Male ? "Masculino" : "Femenino";
                Utility.drawTextWithShadow(b, $"Género: {genderStr}", Game1.smallFont, new Vector2(startX + 130, cardY + 5), Game1.textColor);
                Utility.drawTextWithShadow(b, $"Rasgo: {traitStr}", Game1.smallFont, new Vector2(startX + 130, cardY + 35), Game1.textColor);

                // Cuadrícula de Habilidades
                int skillsY = cardY + 90;
                Utility.drawTextWithShadow(b, "Habilidades:", Game1.dialogueFont, new Vector2(startX + 10, skillsY), Game1.textColor, 1f);
                
                skillsY += 45;
                DrawSkill(b, "Agricultura", app.State.FarmingLevel, new Rectangle(10, 428, 10, 10), startX + 10, skillsY, 175);
                DrawSkill(b, "Minería", app.State.MiningLevel, new Rectangle(30, 428, 10, 10), startX + 10, skillsY + 40, 175);
                DrawSkill(b, "Recolección", app.State.ForagingLevel, new Rectangle(60, 428, 10, 10), startX + 10, skillsY + 80, 175);
                
                DrawSkill(b, "Pesca", app.State.FishingLevel, new Rectangle(20, 428, 10, 10), startX + 240, skillsY, 130);
                DrawSkill(b, "Combate", app.State.CombatLevel, new Rectangle(120, 428, 10, 10), startX + 240, skillsY + 40, 130);

                // Economía y Costos
                int econY = skillsY + 130;
                Utility.drawTextWithShadow(b, "Salario:", Game1.dialogueFont, new Vector2(startX + 10, econY), Game1.textColor, 1f);
                
                econY += 45;
                Utility.drawTextWithShadow(b, $"Diario: {app.State.Wage}g", Game1.smallFont, new Vector2(startX + 10, econY + 4), Game1.textColor);
                Utility.drawTextWithShadow(b, $"Depósito Inicial: {app.HireCost}g (Alojamiento Requerido)", Game1.smallFont, new Vector2(startX + 10, econY + 44), Game1.textColor);

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

        private void DrawSkill(SpriteBatch b, string name, int level, Rectangle sourceRect, int x, int y, int levelOffset)
        {
            // Icono nativo de la habilidad
            b.Draw(Game1.mouseCursors, new Vector2(x, y), sourceRect, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 1f);
            
            // Nombre de la habilidad
            Utility.drawTextWithShadow(b, name, Game1.smallFont, new Vector2(x + 36, y + 2), Game1.textColor);
            
            // Nivel (Resaltado)
            Utility.drawTextWithShadow(b, $"Nv.{level}", Game1.smallFont, new Vector2(x + levelOffset, y + 2), Color.DarkRed);
        }
    }
}
