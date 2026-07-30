using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GoalkeeperControlMode
    {
        DiscreteV0 = 0,
        HybridV1 = 1,
    }

    public enum GoalkeeperControlMotorState
    {
        Ready = 0,
        Moving = 1,
        Planting = 2,
        Diving = 3,
        Recovering = 4,
    }

    [Serializable]
    public struct GoalkeeperControlCommand
    {
        public float MoveX;
        public float AimX;
        public float AimY;
        public float Reach;
        public bool Commit;

        public static GoalkeeperControlCommand Neutral =>
            new GoalkeeperControlCommand
            {
                MoveX = 0f,
                AimX = 0f,
                AimY = 0f,
                Reach = -1f,
                Commit = false,
            };

        public float Reach01 => Mathf.Clamp01((Reach + 1f) * 0.5f);

        public GoalkeeperControlCommand Sanitized(out bool clamped)
        {
            var output = this;
            clamped = false;
            output.MoveX = ClampAction(MoveX, ref clamped);
            output.AimX = ClampAction(AimX, ref clamped);
            output.AimY = ClampAction(AimY, ref clamped);
            output.Reach = ClampAction(Reach, ref clamped);
            return output;
        }

        private static float ClampAction(float value, ref bool clamped)
        {
            if (!KernelMath.IsFinite(value))
            {
                clamped = true;
                return 0f;
            }

            var output = Mathf.Clamp(value, -1f, 1f);
            clamped |= !Mathf.Approximately(output, value);
            return output;
        }
    }

    public readonly struct GoalkeeperControlActionMask
    {
        public readonly bool CanCommit;

        public GoalkeeperControlActionMask(bool canCommit)
        {
            CanCommit = canCommit;
        }
    }

    public readonly struct GoalkeeperControlDecisionContext
    {
        public readonly long AttemptId;
        public readonly int DecisionIndex;
        public readonly int PhysicsTick;
        public readonly float BallFlightTime;
        public readonly float VisibleTimeToGoalPlane;

        public GoalkeeperControlDecisionContext(
            long attemptId,
            int decisionIndex,
            int physicsTick,
            float ballFlightTime)
            : this(
                attemptId,
                decisionIndex,
                physicsTick,
                ballFlightTime,
                -1f)
        {
        }

        public GoalkeeperControlDecisionContext(
            long attemptId,
            int decisionIndex,
            int physicsTick,
            float ballFlightTime,
            float visibleTimeToGoalPlane)
        {
            AttemptId = attemptId;
            DecisionIndex = decisionIndex;
            PhysicsTick = physicsTick;
            BallFlightTime = ballFlightTime;
            VisibleTimeToGoalPlane = visibleTimeToGoalPlane;
        }
    }

    public interface IGoalkeeperControlSourceV1
    {
        GoalkeeperControlCommand DecideControl(
            GoalkeeperControlDecisionContext context,
            GoalkeeperControlActionMask actionMask);

        void OnAttemptStarted(long attemptId);
        void OnAttemptEnded(AttemptResult result);
    }

    public static class GoalkeeperControlSpace
    {
        public const int ContinuousActionCount = 4;
        public const int CommitBranchSize = 2;

        public static Vector2 AimToLocal(float aimX, float aimY)
        {
            var targetXExtent = KernelConstants.GoalHalfWidth - KernelConstants.BallRadius;
            var minimumY = KernelConstants.BallRadius;
            var maximumY =
                KernelConstants.CrossbarLowerEdge - KernelConstants.BallRadius;
            return new Vector2(
                Mathf.Clamp(aimX, -1f, 1f) * targetXExtent,
                Mathf.Lerp(minimumY, maximumY, Mathf.Clamp01((aimY + 1f) * 0.5f)));
        }

        public static Vector2 LocalToAim(Vector2 local)
        {
            var targetXExtent = KernelConstants.GoalHalfWidth - KernelConstants.BallRadius;
            var minimumY = KernelConstants.BallRadius;
            var maximumY =
                KernelConstants.CrossbarLowerEdge - KernelConstants.BallRadius;
            return new Vector2(
                Mathf.Clamp(local.x / targetXExtent, -1f, 1f),
                Mathf.Lerp(
                    -1f,
                    1f,
                    Mathf.InverseLerp(minimumY, maximumY, local.y)));
        }
    }
}
