using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public interface ILivingBrain
    {
        void Initialize(NPC owner);
        void UpdateTicked(GameTime time, GameLocation location);
        
        /// <summary>
        /// El 10% del Player Override. 
        /// Fuerza una acción específica que sobreescribe la autonomía.
        /// </summary>
        void InjectPlayerCommand(IGoapAction command);
    }

    /// <summary>
    /// El Cerebro Central (Orchestrator).
    /// Controla la interacción entre Percepción, Necesidades y Reacciones sin bloquear el hilo principal.
    /// </summary>
    public class LivingBrain : ILivingBrain
    {
        private NPC? _owner;
        private readonly ISensorySystem _sensorySystem;
        private readonly IInternalState _internalState;
        private readonly IReactionSystem _reactionSystem;
        private readonly IGoapPlanner _planner;

        private IGoapAction? _currentAction;
        private IGoapAction? _playerOverrideAction;

        private float _timeSinceLastScan = 0f;
        private const float SCAN_INTERVAL = 2.0f; // Escanear el entorno cada 2 segundos para no laggear

        public LivingBrain(ISensorySystem sensorySystem, IInternalState internalState, IReactionSystem reactionSystem, IGoapPlanner planner)
        {
            _sensorySystem = sensorySystem;
            _internalState = internalState;
            _reactionSystem = reactionSystem;
            _planner = planner;
        }

        public void Initialize(NPC owner)
        {
            _owner = owner;
        }

        public void InjectPlayerCommand(IGoapAction command)
        {
            // Detenemos lo que sea que estuviera haciendo (autónomo)
            if (_currentAction != null)
            {
                _currentAction.End();
                _currentAction = null;
            }
            
            _playerOverrideAction = command;
            _playerOverrideAction.Start();
        }

        public void UpdateTicked(GameTime time, GameLocation location)
        {
            if (_owner == null) return;

            float deltaTime = (float)time.ElapsedGameTime.TotalSeconds;

            // 1. Escáner Sensorial Ligero (Solo corre cada SCAN_INTERVAL)
            bool sensoryScanOccurred = false;
            _timeSinceLastScan += deltaTime;
            if (_timeSinceLastScan >= SCAN_INTERVAL)
            {
                _sensorySystem.ScanEnvironment(location, _owner.Tile, 15);
                _sensorySystem.PruneCache();
                _timeSinceLastScan = 0f;
                sensoryScanOccurred = true;
            }

            // 2. Motor de Necesidades
            string currentActionName = _currentAction?.Name ?? "None";
            _internalState.UpdateNeeds(deltaTime, currentActionName);

            // 3. Sistema de Reacciones e Interrupciones (Emergent Behavior)
            if (_reactionSystem.HasActiveInterrupt)
            {
                // Si el GOAP estaba corriendo, lo pausamos
                if (_currentAction != null) _currentAction.Pause();
                
                // Ejecutamos la reacción (ej. Huir del Slime)
                bool interruptFinished = _reactionSystem.UpdateInterrupt(deltaTime);
                
                if (interruptFinished)
                {
                    // Volvemos a la tarea original
                    if (_currentAction != null) _currentAction.Resume();
                }
                
                return; // Bloquea el procesamiento GOAP mientras la interrupción esté activa
            }

            // 4. El Override del Jugador (The 10%)
            if (_playerOverrideAction != null)
            {
                bool isFinished = _playerOverrideAction.Update(deltaTime);
                if (isFinished)
                {
                    _playerOverrideAction.End();
                    _playerOverrideAction = null; // Volvemos a la autonomía
                }
                return; // Bloquea GOAP autónomo
            }

            // 5. Motor GOAP (El 90% Autónomo)
            if (_currentAction == null || _internalState.RequiresRest() || sensoryScanOccurred)
            {
                // Reevaluar planes si terminamos, no hay acción, si estamos agotados, o tras un escaneo sensorial
                var newAction = _planner.PlanNextAction(_sensorySystem.GetSensoryCache(), _internalState.GetCurrentStats());
                
                if (newAction != _currentAction)
                {
                    _currentAction?.End();
                    _currentAction = newAction;
                    _currentAction?.Start();
                }
            }

            // Ejecutar la acción actual
            if (_currentAction != null)
            {
                bool actionDone = _currentAction.Update(deltaTime);
                if (actionDone)
                {
                    _currentAction.End();
                    _currentAction = null;
                }
            }
        }
    }
}
