using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class FixedGoalkeeperActionSource : GoalkeeperActionSourceBehaviour
    {
        [SerializeField]
        private GoalkeeperAction action = GoalkeeperAction.Hold;

        public GoalkeeperAction Action
        {
            get => action;
            set => action = value;
        }

        public override GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            if (context.DecisionIndex > 0 &&
                action >= GoalkeeperAction.DiveLeftLow)
            {
                return GoalkeeperAction.Hold;
            }

            return actionMask.IsAllowed(action)
                ? action
                : GoalkeeperAction.Hold;
        }
    }
}
