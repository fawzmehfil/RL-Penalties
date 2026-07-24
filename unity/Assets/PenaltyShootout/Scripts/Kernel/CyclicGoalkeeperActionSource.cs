namespace PenaltyShootout.Kernel
{
    public sealed class CyclicGoalkeeperActionSource : GoalkeeperActionSourceBehaviour
    {
        private GoalkeeperAction attemptAction;

        public override void OnAttemptStarted(long attemptId)
        {
            attemptAction = (GoalkeeperAction)(attemptId % 9);
        }

        public override GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            if (context.DecisionIndex > 0 &&
                attemptAction >= GoalkeeperAction.DiveLeftLow)
            {
                return GoalkeeperAction.Hold;
            }

            return actionMask.IsAllowed(attemptAction)
                ? attemptAction
                : GoalkeeperAction.Hold;
        }
    }
}
