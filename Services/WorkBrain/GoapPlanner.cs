using System.Collections.Generic;

namespace LivingCompanionsValley.Services.WorkBrain
{
    public class GoapPlanner : IGoapPlanner
    {
        private List<IGoapAction> _availableActions = new List<IGoapAction>();

        public void RegisterAction(IGoapAction action)
        {
            if (!_availableActions.Contains(action))
            {
                _availableActions.Add(action);
            }
        }

        public IGoapAction? PlanNextAction(IEnumerable<PerceivedEntity> sensoryCache, InternalStats currentStats)
        {
            IGoapAction? bestAction = null;
            float highestUtility = -1f;

            foreach (var action in _availableActions)
            {
                float utility = action.CalculateUtility(sensoryCache, currentStats);
                if (utility > highestUtility)
                {
                    highestUtility = utility;
                    bestAction = action;
                }
            }

            return bestAction;
        }
    }
}
