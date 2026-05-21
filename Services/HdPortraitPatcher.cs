using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using HarmonyLib;

namespace LivingCompanionsValley.Services
{
    public static class HdPortraitPatcher
    {
        private static IMonitor _logger = null!;

        public static void ApplyPatches(Harmony harmony, IMonitor logger)
        {
            _logger = logger;

            try
            {
                harmony.Patch(
                    original: AccessTools.Method(typeof(DialogueBox), "drawPortrait"),
                    prefix: new HarmonyMethod(typeof(HdPortraitPatcher), nameof(DrawPortrait_Prefix))
                );
                
                _logger.Log("Harmony Patch: 'DialogueBox.drawPortrait' interceptado para soportar Retratos HD.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                _logger.Log($"Error aplicando parche HD Portrait: {ex.Message}", LogLevel.Error);
            }
        }

        public static bool DrawPortrait_Prefix(DialogueBox __instance, SpriteBatch b)
        {
            try
            {
                var dialogue = __instance.characterDialogue;
                if (dialogue == null || dialogue.speaker == null || dialogue.speaker.Portrait == null)
                    return true; // Dejar que el juego original se encargue

                Texture2D texture = dialogue.speaker.Portrait;

                // Si la textura es de tamaño normal (128 de ancho, 2 columnas de 64x64), no interferimos
                if (texture.Width <= 128)
                    return true;

                // ¡Es una textura HD! Tomamos el control para evitar que Stardew Valley la recorte a 64x64
                int frameSize = texture.Width / 2; // Asumiendo formato estandar de 2 columnas
                int currentEmotion = dialogue.getPortraitIndex();

                Rectangle sourceRect = Game1.getSourceRectForStandardTileSheet(texture, currentEmotion, frameSize, frameSize);
                
                if (!texture.Bounds.Contains(sourceRect))
                {
                    sourceRect = Game1.getSourceRectForStandardTileSheet(texture, 0, frameSize, frameSize);
                }

                // El cuadro base en pantalla para el retrato es de 256x256. 
                // Calculamos la escala necesaria para que nuestra imagen HD encaje perfectamente.
                float portraitScale = 256f / frameSize;

                // Las matemáticas del Portrait Plate nativo
                int portraitBoxX = __instance.xPositionOnScreen + __instance.width - 460 - 4;

                // Dibujamos el retrato HD
                b.Draw(texture, 
                    new Vector2(portraitBoxX + 104, __instance.yPositionOnScreen + 48), 
                    new Rectangle?(sourceRect), 
                    Color.White, 0f, Vector2.Zero, portraitScale, SpriteEffects.None, 0.88f);

                return false; // Cancelamos el dibujo nativo porque ya lo hicimos nosotros
            }
            catch (Exception ex)
            {
                _logger.Log($"Error dibujando retrato HD: {ex.Message}", LogLevel.Error);
                return true; // En caso de error, intentamos hacer fallback al original
            }
        }
    }
}
