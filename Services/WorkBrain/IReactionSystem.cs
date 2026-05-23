namespace LivingCompanionsValley.Services.WorkBrain
{
    public interface IInterruptReaction
    {
        bool Update(float deltaTime); // Retorna true si la reacción ha terminado
        void Start();
    }

    /// <summary>
    /// Sistema guiado por eventos para manejar interrupciones emergentes (ej. ataque de slime).
    /// </summary>
    public interface IReactionSystem
    {
        bool HasActiveInterrupt { get; }

        /// <summary>
        /// Se llama cuando ocurre un estímulo externo urgente. 
        /// Esto debe causar que el LivingBrain pause su acción actual de GOAP.
        /// </summary>
        void TriggerInterrupt(IInterruptReaction reaction);

        /// <summary>
        /// Procesa la reacción actual.
        /// Retorna true si la interrupción acaba de terminar (indicando al cerebro que haga 'Resume' a GOAP).
        /// </summary>
        bool UpdateInterrupt(float deltaTime);
    }
}
