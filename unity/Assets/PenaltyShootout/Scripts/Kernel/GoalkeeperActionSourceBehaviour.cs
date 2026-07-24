using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public abstract class GoalkeeperActionSourceBehaviour :
        MonoBehaviour,
        IGoalkeeperActionSource
    {
        public abstract GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask);

        public virtual void OnAttemptStarted(long attemptId)
        {
        }

        public virtual void OnAttemptEnded(AttemptResult result)
        {
        }
    }
}
