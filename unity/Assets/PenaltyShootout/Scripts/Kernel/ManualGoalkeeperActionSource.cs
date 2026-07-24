using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public sealed class ManualGoalkeeperActionSource : GoalkeeperActionSourceBehaviour
    {
        public override GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            var requested = GoalkeeperAction.Hold;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.A))
            {
                requested = GoalkeeperAction.ShuffleLeft;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                requested = GoalkeeperAction.ShuffleRight;
            }
            else if (Input.GetKeyDown(KeyCode.Q))
            {
                requested = GoalkeeperAction.DiveLeftLow;
            }
            else if (Input.GetKeyDown(KeyCode.W))
            {
                requested = GoalkeeperAction.DiveLeftMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                requested = GoalkeeperAction.DiveLeftHigh;
            }
            else if (Input.GetKeyDown(KeyCode.U))
            {
                requested = GoalkeeperAction.DiveRightLow;
            }
            else if (Input.GetKeyDown(KeyCode.I))
            {
                requested = GoalkeeperAction.DiveRightMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.O))
            {
                requested = GoalkeeperAction.DiveRightHigh;
            }
#endif
            return actionMask.IsAllowed(requested)
                ? requested
                : GoalkeeperAction.Hold;
        }
    }
}
