using System;
using UnityEngine;

namespace PenaltyShootout.Stage0
{
    public static class Stage0Constants
    {
        public const string EnvironmentId = "penalty-shootout-physics-v0";
        public const string BehaviorName = "Stage0ConnectionProbe";
        public const float GoalInsideWidth = 7.32f;
        public const float GoalHalfWidth = GoalInsideWidth * 0.5f;
        public const float CrossbarLowerEdge = 2.44f;
        public const float FrameThickness = 0.12f;
        public const float PenaltyMarkDistance = 11f;
        public const float BallRadius = 0.11f;
        public const float BallMass = 0.43f;
        public const float FixedTimestep = 0.02f;
        public const float CanonicalFlightTime = 0.55f;
        public const float AttemptTimeout = 2f;
        public const float TargetTolerance = 0.05f;

        public static readonly Vector3 CanonicalLaunch = new Vector3(0f, BallRadius, PenaltyMarkDistance);
        public static readonly Vector3 CanonicalTarget = new Vector3(0f, 1.2f, 0f);
    }

    public enum ShotOutcome
    {
        None = 0,
        Goal = 1,
        MissWide = 2,
        MissHigh = 3,
        Timeout = 4,
        Invalid = 5,
    }

    public static class BallisticShotSolver
    {
        public static Vector3 SolveInitialVelocity(
            Vector3 launch,
            Vector3 target,
            float flightTime,
            Vector3 gravity)
        {
            if (flightTime <= 0f || !IsFinite(flightTime))
            {
                throw new ArgumentOutOfRangeException(nameof(flightTime), "Flight time must be finite and positive.");
            }

            if (!IsFinite(launch) || !IsFinite(target) || !IsFinite(gravity))
            {
                throw new ArgumentException("Launch, target, and gravity must be finite.");
            }

            var velocity = (target - launch - 0.5f * gravity * flightTime * flightTime) / flightTime;
            if (!IsFinite(velocity))
            {
                throw new InvalidOperationException("Solved launch velocity is not finite.");
            }

            return velocity;
        }

        public static Vector3 SolvePhysXInitialVelocity(
            Vector3 launch,
            Vector3 target,
            float flightTime,
            Vector3 gravity,
            float fixedTimestep)
        {
            if (fixedTimestep <= 0f || !IsFinite(fixedTimestep))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fixedTimestep),
                    "Fixed timestep must be finite and positive.");
            }

            // PhysX applies gravity before integrating position each fixed
            // step (semi-implicit Euler). This half-step correction preserves
            // the requested continuous ballistic target at the declared step.
            return SolveInitialVelocity(launch, target, flightTime, gravity) -
                0.5f * gravity * fixedTimestep;
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }

    public static class GoalLineGeometry
    {
        public static bool TryIntersectPlane(
            Vector3 previous,
            Vector3 current,
            float planeZ,
            out Vector3 intersection)
        {
            intersection = default;
            if (!BallisticShotSolver.IsFinite(previous) || !BallisticShotSolver.IsFinite(current))
            {
                return false;
            }

            var previousDistance = previous.z - planeZ;
            var currentDistance = current.z - planeZ;
            if (previousDistance < 0f || currentDistance > 0f)
            {
                return false;
            }

            var delta = current.z - previous.z;
            if (Mathf.Abs(delta) < 1e-7f)
            {
                return false;
            }

            var t = (planeZ - previous.z) / delta;
            if (t < 0f || t > 1f)
            {
                return false;
            }

            intersection = Vector3.LerpUnclamped(previous, current, t);
            return BallisticShotSolver.IsFinite(intersection);
        }

        public static bool IsWholeBallInsideGoal(
            Vector3 ballCenter,
            float ballRadius,
            float goalHalfWidth,
            float crossbarLowerEdge)
        {
            if (!BallisticShotSolver.IsFinite(ballCenter) || ballRadius <= 0f)
            {
                return false;
            }

            var horizontallyInside = Mathf.Abs(ballCenter.x) + ballRadius <= goalHalfWidth + 1e-5f;
            var belowCrossbar = ballCenter.y + ballRadius <= crossbarLowerEdge + 1e-5f;
            var aboveGround = ballCenter.y - ballRadius >= -1e-5f;
            return horizontallyInside && belowCrossbar && aboveGround;
        }

        public static ShotOutcome ClassifyWholeBallCrossing(Vector3 ballCenter)
        {
            if (!BallisticShotSolver.IsFinite(ballCenter))
            {
                return ShotOutcome.Invalid;
            }

            if (IsWholeBallInsideGoal(
                    ballCenter,
                    Stage0Constants.BallRadius,
                    Stage0Constants.GoalHalfWidth,
                    Stage0Constants.CrossbarLowerEdge))
            {
                return ShotOutcome.Goal;
            }

            if (ballCenter.y + Stage0Constants.BallRadius > Stage0Constants.CrossbarLowerEdge)
            {
                return ShotOutcome.MissHigh;
            }

            return ShotOutcome.MissWide;
        }
    }

    [Serializable]
    public sealed class OutcomeLatch
    {
        [SerializeField]
        private ShotOutcome outcome;

        [SerializeField]
        private int duplicateTerminalEvents;

        public ShotOutcome Outcome => outcome;
        public int DuplicateTerminalEvents => duplicateTerminalEvents;
        public bool IsTerminal => outcome != ShotOutcome.None;

        public void Reset()
        {
            outcome = ShotOutcome.None;
            duplicateTerminalEvents = 0;
        }

        public bool TrySet(ShotOutcome terminalOutcome)
        {
            if (terminalOutcome == ShotOutcome.None)
            {
                return false;
            }

            if (IsTerminal)
            {
                duplicateTerminalEvents++;
                return false;
            }

            outcome = terminalOutcome;
            return true;
        }
    }
}
