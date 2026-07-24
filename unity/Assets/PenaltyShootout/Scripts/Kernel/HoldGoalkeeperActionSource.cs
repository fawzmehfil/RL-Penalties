namespace PenaltyShootout.Kernel
{
    public sealed class HoldGoalkeeperActionSource : GoalkeeperActionSourceBehaviour
    {
        public override GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            return GoalkeeperAction.Hold;
        }
    }
}
