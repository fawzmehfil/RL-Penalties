namespace PenaltyShootout.Kernel
{
    public sealed class RandomLegalGoalkeeperActionSource : GoalkeeperActionSourceBehaviour
    {
        private Pcg32 random;

        public override void OnAttemptStarted(long attemptId)
        {
            random = new Pcg32(Pcg32.DeriveSeed(20260724UL, 0, attemptId));
        }

        public override GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            var allowedCount = 0;
            for (var action = 0; action <= (int)GoalkeeperAction.DiveRightHigh; action++)
            {
                if (actionMask.IsAllowed((GoalkeeperAction)action))
                {
                    allowedCount++;
                }
            }

            if (allowedCount == 0)
            {
                return GoalkeeperAction.Hold;
            }

            var selected = (int)(random.NextUInt() % (uint)allowedCount);
            for (var action = 0; action <= (int)GoalkeeperAction.DiveRightHigh; action++)
            {
                if (!actionMask.IsAllowed((GoalkeeperAction)action))
                {
                    continue;
                }

                if (selected == 0)
                {
                    return (GoalkeeperAction)action;
                }

                selected--;
            }

            return GoalkeeperAction.Hold;
        }
    }
}
