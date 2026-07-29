using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public abstract class GoalkeeperControlSourceBehaviourV1 :
        MonoBehaviour,
        IGoalkeeperControlSourceV1
    {
        public abstract GoalkeeperControlCommand DecideControl(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask);

        public virtual void OnAttemptStarted(long attemptId)
        {
        }

        public virtual void OnAttemptEnded(AttemptResult result)
        {
        }
    }
}
