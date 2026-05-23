using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public class ReactionSystem : IReactionSystem
    {
        private Stack<IInterruptReaction> _interruptStack = new Stack<IInterruptReaction>();

        public bool HasActiveInterrupt => _interruptStack.Count > 0;

        public void TriggerInterrupt(IInterruptReaction reaction)
        {
            _interruptStack.Push(reaction);
            reaction.Start();
        }

        public bool UpdateInterrupt(float deltaTime)
        {
            if (_interruptStack.Count == 0) return false;

            var currentReaction = _interruptStack.Peek();
            bool isFinished = currentReaction.Update(deltaTime);

            if (isFinished)
            {
                _interruptStack.Pop();
                return true; // Acaba de terminar una reacción, notificar al cerebro
            }

            return false;
        }
    }
}
