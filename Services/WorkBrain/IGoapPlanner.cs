using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public interface IGoapAction
    {
        string Name { get; }
        
        /// <summary>
        /// Evalúa qué tan prioritaria es esta acción basado en el entorno y las necesidades internas.
        /// Un puntaje más alto significa mayor prioridad.
        /// </summary>
        float CalculateUtility(IEnumerable<PerceivedEntity> sensoryCache, InternalStats stats);

        void Start();
        bool Update(float deltaTime);
        void End();
        void Pause(); // Usado por el ReactionSystem
        void Resume();
    }

    /// <summary>
    /// Planificador de acciones basado en GOAP / Utility AI.
    /// Decide qué hacer basándose en lo que ve (SensoryCache) y lo que siente (InternalState).
    /// </summary>
    public interface IGoapPlanner
    {
        void RegisterAction(IGoapAction action);

        /// <summary>
        /// Evalúa todas las acciones registradas y retorna la de mayor puntaje.
        /// </summary>
        IGoapAction? PlanNextAction(IEnumerable<PerceivedEntity> sensoryCache, InternalStats currentStats);
    }
}
