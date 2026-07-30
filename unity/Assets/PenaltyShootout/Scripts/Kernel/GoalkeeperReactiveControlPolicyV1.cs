using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GoalkeeperControlHeuristicMode
    {
        Manual = 0,
        ReactiveTeacher = 1,
    }

    public static class GoalkeeperReactiveControlPolicyV1
    {
        public const float DefaultCommitHorizon = 0.62f;
        public const float MoveErrorForFullCommand = 1.25f;

        public static GoalkeeperControlCommand Decide(
            Vector3 ballLocalPosition,
            Vector3 ballLocalVelocity,
            Vector3 localGravity,
            float goalkeeperLocalX,
            GoalkeeperControlActionMask actionMask,
            float commitHorizon = DefaultCommitHorizon)
        {
            if (!KernelMath.IsFinite(ballLocalPosition) ||
                !KernelMath.IsFinite(ballLocalVelocity) ||
                !KernelMath.IsFinite(localGravity) ||
                !KernelMath.IsFinite(goalkeeperLocalX))
            {
                return GoalkeeperControlCommand.Neutral;
            }

            var timeToPlane =
                GoalkeeperControlTrainingContracts
                    .EstimateVisibleTimeToGoalPlane(
                        ballLocalPosition,
                        ballLocalVelocity);
            var target = new Vector2(
                ballLocalPosition.x,
                ballLocalPosition.y);
            if (timeToPlane >= 0f)
            {
                target.x +=
                    ballLocalVelocity.x * timeToPlane +
                    0.5f * localGravity.x * timeToPlane * timeToPlane;
                target.y +=
                    ballLocalVelocity.y * timeToPlane +
                    0.5f * localGravity.y * timeToPlane * timeToPlane;
            }

            var aim = GoalkeeperControlSpace.LocalToAim(target);
            return new GoalkeeperControlCommand
            {
                MoveX = Mathf.Clamp(
                    (target.x - goalkeeperLocalX) /
                    MoveErrorForFullCommand,
                    -1f,
                    1f),
                AimX = aim.x,
                AimY = aim.y,
                Reach = 1f,
                Commit =
                    actionMask.CanCommit &&
                    timeToPlane >= 0f &&
                    timeToPlane <= Mathf.Max(0f, commitHorizon),
            };
        }
    }
}
