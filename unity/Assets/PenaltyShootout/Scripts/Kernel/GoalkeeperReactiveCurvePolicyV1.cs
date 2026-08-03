using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class GoalkeeperReactiveCurvePolicyV1
    {
        public static GoalkeeperControlCommand Decide(
            GoalkeeperControlVisibleStateSnapshot visible,
            Vector3 gravity,
            float fixedTimestep,
            PlayerShotPhysicsConfigV1 physics,
            GoalkeeperControlActionMask actionMask,
            float commitHorizon = GoalkeeperReactiveControlPolicyV1.DefaultCommitHorizon)
        {
            if (!GoalkeeperControlTrainingContracts
                    .TryEstimateCurveAwareVisibleGoalPlaneAim(
                        visible.BallLocalPosition,
                        visible.BallLocalVelocity,
                        visible.BallAngularVelocity,
                        gravity,
                        fixedTimestep,
                        physics,
                        out var timeToPlane,
                        out var aim))
            {
                return GoalkeeperControlCommand.Neutral;
            }

            return DecideFromVisiblePrediction(
                timeToPlane,
                aim,
                visible.GoalkeeperRootLocalPosition.x,
                actionMask,
                commitHorizon);
        }

        public static GoalkeeperControlCommand DecideFromVisiblePrediction(
            float timeToPlane,
            Vector2 aim,
            float goalkeeperRootX,
            GoalkeeperControlActionMask actionMask,
            float commitHorizon = GoalkeeperReactiveControlPolicyV1.DefaultCommitHorizon)
        {
            var target = GoalkeeperControlSpace.AimToLocal(aim.x, aim.y);
            return new GoalkeeperControlCommand
            {
                MoveX = Mathf.Clamp(
                    (target.x - goalkeeperRootX) /
                    GoalkeeperReactiveControlPolicyV1.MoveErrorForFullCommand,
                    -1f,
                    1f),
                AimX = aim.x,
                AimY = aim.y,
                Reach = 1f,
                Commit = actionMask.CanCommit &&
                    timeToPlane >= 0f &&
                    timeToPlane <= Mathf.Max(0f, commitHorizon),
            };
        }
    }
}
