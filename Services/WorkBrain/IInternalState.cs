namespace LivingCompanionsValley.Services.WorkBrain
{
    public class InternalStats
    {
        public float Energy { get; set; } = 100f; // 0-100%
        public float Morale { get; set; } = 100f; // 0-100%
        public float RestNeed { get; set; } = 0f; // Aumenta con el tiempo
    }

    /// <summary>
    /// Motor de necesidades que simula las variables orgánicas del trabajador.
    /// </summary>
    public interface IInternalState
    {
        InternalStats GetCurrentStats();

        /// <summary>
        /// Se llama cada ciclo para desgastar energía o aumentar fatiga según la actividad actual.
        /// </summary>
        void UpdateNeeds(float deltaTime, string currentActionName);

        /// <summary>
        /// Determina si el trabajador ha cruzado un umbral de necesidad urgente (ej. Energía < 20%).
        /// </summary>
        bool RequiresRest();
    }
}
