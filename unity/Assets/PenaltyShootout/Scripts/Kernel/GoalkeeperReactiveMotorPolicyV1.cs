using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperReactiveMotorPolicyV1
    {
        public const float CommitMarginSeconds = 0.08f;

        public static GoalkeeperControlCommand DecideFromVisiblePrediction(
            float timeToPlane,
            Vector2 aim,
            float goalkeeperRootX,
            GoalkeeperControlMotorConfig configuration,
            GoalkeeperControlActionMask actionMask)
        {
            var estimate = GoalkeeperMotorTimingV1.Estimate(
                aim,
                new Vector3(
                    goalkeeperRootX,
                    0f,
                    configuration.StandingZ),
                configuration);
            var moveError = estimate.RootTargetLocal.x - goalkeeperRootX;
            return new GoalkeeperControlCommand
            {
                MoveX = Mathf.Clamp(
                    moveError /
                    GoalkeeperReactiveControlPolicyV1.MoveErrorForFullCommand,
                    -1f,
                    1f),
                AimX = aim.x,
                AimY = aim.y,
                Reach = 1f,
                Commit = actionMask.CanCommit &&
                    timeToPlane >= 0f &&
                    timeToPlane <= estimate.FullReachTime + CommitMarginSeconds,
            };
        }
    }
}
