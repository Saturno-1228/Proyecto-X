using System;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public class InternalState : IInternalState
    {
        private InternalStats _stats;

        public InternalState()
        {
            _stats = new InternalStats();
        }

        public InternalStats GetCurrentStats()
        {
            return _stats;
        }

        public bool RequiresRest()
        {
            // Solo requieren descanso urgente si la energía está en niveles críticos (<= 5%)
            return _stats.Energy <= 5f;
        }

        public void UpdateNeeds(float deltaTime, string currentActionName)
        {
            if (currentActionName == "Sleep")
            {
                // Recupera energía rápido durante el sueño
                _stats.Energy += deltaTime * 4.0f;
                _stats.RestNeed -= deltaTime * 4.0f;
            }
            else if (currentActionName == "ClearDebris")
            {
                // Drena energía rápido si trabaja
                _stats.Energy -= deltaTime * 1.5f;
                _stats.RestNeed += deltaTime * 2.0f;
            }
            else
            {
                // Drena energía muy lentamente al caminar (Wander, LeaveCabin, etc.)
                _stats.Energy -= deltaTime * 0.1f;
                _stats.RestNeed += deltaTime * 0.2f;
            }

            // Clamp a límites
            _stats.Energy = Math.Clamp(_stats.Energy, 0f, 100f);
            _stats.RestNeed = Math.Clamp(_stats.RestNeed, 0f, 100f);
            _stats.Morale = Math.Clamp(_stats.Morale, 0f, 100f);
        }
    }
}
