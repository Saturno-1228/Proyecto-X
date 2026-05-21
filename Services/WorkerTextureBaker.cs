using System;
using Microsoft.Xna.Framework;
using StardewValley;
using LivingCompanionsValley.Models;

namespace LivingCompanionsValley.Services
{
    public static class WorkerTextureBaker
    {
        /// <summary>
        /// Crea una instancia "Dummy" de Farmer configurada con la apariencia del trabajador.
        /// Esta instancia solo existe en memoria y se utiliza para renderizar el sprite.
        /// </summary>
        public static Farmer CreateDummyFarmer(WorkerState state)
        {
            var dummy = new Farmer();
            dummy.Name = state.Name;

            // Configurar Apariencia Procedural
            try
            {
                // En SV 1.6 many of these fields are NetFields on Farmer
                dummy.skin.Value = state.SkinColor;
                dummy.hair.Value = state.HairStyle;
                dummy.hairstyleColor.Value = new Color(state.HairColorR, state.HairColorG, state.HairColorB);
                
                // En SV 1.6, shirt y pants son strings IDs
                dummy.shirt.Value = state.Shirt.ToString();
                dummy.pants.Value = state.Pants.ToString();
                dummy.pantsColor.Value = new Color(state.PantsColorR, state.PantsColorG, state.PantsColorB);
            }
            catch (Exception ex)
            {
                ModEntry.Logger?.Log($"Error configurando apariencia de Farmer Dummy para {state.Name}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
            }

            return dummy;
        }
    }
}
