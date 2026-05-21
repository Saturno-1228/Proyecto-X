using System;
using System.Collections.Generic;

namespace LivingCompanionsValley.Models
{
    /// <summary>
    /// Representa un objeto serializable de forma segura para guardar el inventario del trabajador.
    /// Evita serializar directamente clases complejas de Stardew Valley que contienen NetFields.
    /// </summary>
    public class SavedItem
    {
        public string ItemId { get; set; } = "";
        public int Stack { get; set; } = 1;
        public string Name { get; set; } = "";
        public bool IsTool { get; set; } = false;
        public int ToolUpgradeLevel { get; set; } = 0;
    }

    /// <summary>
    /// Modelo de datos persistente para guardar y reconstruir un trabajador procedural.
    /// </summary>
    public class WorkerState
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Wage { get; set; } = 50; // Salario en oro
        public int FarmingLevel { get; set; } = 1;
        public int ForagingLevel { get; set; } = 1;
        
        // Cabaña asignada
        public string CabinName { get; set; } = "Cabin";

        // Bitácora de la jornada para que la IA la comente
        public List<string> DailyLog { get; set; } = new List<string>();

        // Inventario mochila
        public List<SavedItem> Inventory { get; set; } = new List<SavedItem>();

        // Apariencia procedural (Índices nativos para el Granger Dummy)
        public int SkinColor { get; set; } = 0;
        public int HairStyle { get; set; } = 0;
        public int HairColorR { get; set; } = 255;
        public int HairColorG { get; set; } = 255;
        public int HairColorB { get; set; } = 255;
        public int Shirt { get; set; } = 0;
        public int Pants { get; set; } = 0;
        public int PantsColorR { get; set; } = 255;
        public int PantsColorG { get; set; } = 255;
        public int PantsColorB { get; set; } = 255;
    }

    /// <summary>
    /// Contenedor para guardar todos los trabajadores contratados de la partida.
    /// </summary>
    public class SaveData
    {
        public List<WorkerState> HiredWorkers { get; set; } = new List<WorkerState>();
    }
}
