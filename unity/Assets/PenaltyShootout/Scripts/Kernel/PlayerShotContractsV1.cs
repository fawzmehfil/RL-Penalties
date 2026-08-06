using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum PlayerShotStyleV1
    {
        Placed = 0,
        Power = 1,
        Curled = 2,
    }

    public enum ExpectedShotTargetClassV1
    {
        OnTarget = 0,
        Frame = 1,
        MissWide = 2,
        MissHigh = 3,
    }

    [Serializable]
    public struct PlayerShotCommandV1
    {
        public float AimX;
        public float AimY;
        public float Power;
        public float SideSpin;
        public float VerticalSpin;
        public float ContactErrorXMeters;
        public float ContactErrorYMeters;

        public PlayerShotCommandV1(
            float aimX,
            float aimY,
            float power,
            float sideSpin,
            float verticalSpin,
            float contactErrorXMeters,
            float contactErrorYMeters)
        {
            AimX = aimX;
            AimY = aimY;
            Power = power;
            SideSpin = sideSpin;
            VerticalSpin = verticalSpin;
            ContactErrorXMeters = contactErrorXMeters;
            ContactErrorYMeters = contactErrorYMeters;
            if (!Validate(out var error))
            {
                throw new ArgumentOutOfRangeException(nameof(aimX), error);
            }
        }

        public bool Validate(out string error)
        {
            if (!Finite(AimX) || !Finite(AimY) || !Finite(Power) ||
                !Finite(SideSpin) || !Finite(VerticalSpin) ||
                !Finite(ContactErrorXMeters) || !Finite(ContactErrorYMeters))
            {
                error = "Player shot command contains non-finite values.";
                return false;
            }

            if (AimX < -1f || AimX > 1f || AimY < -1f || AimY > 1f ||
                Power < 0f || Power > 1f || SideSpin < -1f || SideSpin > 1f ||
                VerticalSpin < -1f || VerticalSpin > 1f ||
                Mathf.Abs(ContactErrorXMeters) > 0.75f ||
                Mathf.Abs(ContactErrorYMeters) > 0.75f)
            {
                error = "Player shot command is outside player-shot-v1 bounds.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool Finite(float value) => KernelMath.IsFinite(value);
    }

    [Serializable]
    public struct ResolvedPlayerShotV1
    {
        public string ShotContractId;
        public string ShotPhysicsId;
        public PlayerShotStyleV1 ShotStyle;
        public string MixtureComponentId;
        public PlayerShotCommandV1 Command;
        public Vector3 IdealTargetLocal;
        public Vector3 ContactAdjustedTargetLocal;
        public Vector3 LaunchVelocityLocal;
        public Vector3 AngularVelocityLocal;
        public float SpinSaturationScale;
        public float NominalFlightTime;
        public float LaunchSpeed;
        public Vector3 PredictedUnopposedCrossingLocal;
        public Vector2 PredictedCurveDisplacement;
        public ExpectedShotTargetClassV1 ExpectedTargetClass;
        public int SolverIterations;
        public float SolverCrossingError;
        public bool RareTail;
    }

    public static class PlayerShotFlightModelV1
    {
        public static Vector3 MagnusAcceleration(
            Vector3 spin,
            Vector3 velocity,
            PlayerShotPhysicsConfigV1 configuration)
        {
            var acceleration =
                configuration.MagnusCoefficient * Vector3.Cross(spin, velocity);
            return Vector3.ClampMagnitude(
                acceleration,
                configuration.MaximumMagnusAcceleration);
        }

        public static void Step(
            ref Vector3 position,
            ref Vector3 velocity,
            ref Vector3 spin,
            Vector3 gravity,
            float deltaTime,
            PlayerShotPhysicsConfigV1 configuration)
        {
            velocity +=
                (gravity + MagnusAcceleration(spin, velocity, configuration)) *
                deltaTime;
            position += velocity * deltaTime;
            spin *= Mathf.Exp(-configuration.SpinDecay * deltaTime);
        }

        public static bool TryPredictCrossing(
            Vector3 position,
            Vector3 velocity,
            Vector3 spin,
            Vector3 gravity,
            float deltaTime,
            PlayerShotPhysicsConfigV1 configuration,
            out Vector3 crossing)
        {
            crossing = default;
            var previous = position;
            const int MaximumSteps = 256;
            for (var index = 0; index < MaximumSteps; index++)
            {
                Step(
                    ref position,
                    ref velocity,
                    ref spin,
                    gravity,
                    deltaTime,
                    configuration);
                if (KernelGoalGeometry.TryIntersectPlane(
                        previous,
                        position,
                        0f,
                        out crossing))
                {
                    return true;
                }

                if (!KernelMath.IsFinite(position) || position.z < -2f)
                {
                    return false;
                }

                previous = position;
            }

            return false;
        }
    }

    public static class PlayerShotResolverV1
    {
        public const float MinimumAimHeight = KernelConstants.BallRadius;
        public const float MaximumAimHeight = 2.90f;
        public const float MaximumAimX = 4.15f;

        public static float FlightTimeForPower(
            float power,
            PlayerShotPhysicsConfigV1 configuration)
        {
            if (!KernelMath.IsFinite(power) || power < 0f || power > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(power));
            }

            var smooth = power * power * (3f - 2f * power);
            return Mathf.Lerp(
                configuration.MaximumFlightTime,
                configuration.MinimumFlightTime,
                smooth);
        }

        public static ResolvedPlayerShotV1 Resolve(
            PlayerShotCommandV1 command,
            PlayerShotStyleV1 style,
            string mixtureComponentId,
            bool rareTail,
            Vector3 gravity,
            float fixedTimestep,
            PlayerShotPhysicsConfigV1 configuration)
        {
            if (!command.Validate(out var commandError))
            {
                throw new ArgumentException(commandError, nameof(command));
            }
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            if (!configuration.Validate(out var configError))
            {
                throw new ArgumentException(configError, nameof(configuration));
            }
            if (!KernelMath.IsFinite(gravity) ||
                !KernelMath.IsFinite(fixedTimestep) || fixedTimestep <= 0f)
            {
                throw new ArgumentException("Resolver physics inputs are invalid.");
            }
            if (Mathf.Abs(fixedTimestep - configuration.FixedTimestep) > 1e-6f)
            {
                throw new ArgumentException(
                    "Resolver timestep does not match football-flight-v1.",
                    nameof(fixedTimestep));
            }

            var idealTarget = new Vector3(
                command.AimX * MaximumAimX,
                Mathf.Lerp(MinimumAimHeight, MaximumAimHeight, (command.AimY + 1f) * 0.5f),
                0f);
            var adjustedTarget = idealTarget + new Vector3(
                command.ContactErrorXMeters,
                command.ContactErrorYMeters,
                0f);
            var flightTime = FlightTimeForPower(command.Power, configuration);
            var requestedSpin = new Vector3(
                -command.VerticalSpin * configuration.MaximumVerticalSpin,
                -command.SideSpin * configuration.MaximumSideSpin,
                0f);
            var launchVelocity = Vector3.zero;
            var crossing = Vector3.zero;
            var iterations = 0;
            var crossingError = float.PositiveInfinity;
            var spinScale = 1f;
            var spin = requestedSpin;
            var curve = Vector2.zero;
            const int MaximumSaturationPasses = 8;
            for (var pass = 0; pass < MaximumSaturationPasses; pass++)
            {
                spin = requestedSpin * spinScale;
                SolveLaunch(
                    adjustedTarget,
                    flightTime,
                    spin,
                    gravity,
                    fixedTimestep,
                    configuration,
                    out launchVelocity,
                    out crossing,
                    out iterations,
                    out crossingError);
                if (!PlayerShotFlightModelV1.TryPredictCrossing(
                        KernelConstants.CanonicalLaunch,
                        launchVelocity,
                        Vector3.zero,
                        gravity,
                        fixedTimestep,
                        configuration,
                        out var zeroSpinCrossing))
                {
                    zeroSpinCrossing = crossing;
                }
                curve = new Vector2(
                    crossing.x - zeroSpinCrossing.x,
                    crossing.y - zeroSpinCrossing.y);
                if (curve.magnitude <=
                    configuration.MaximumCurveDisplacement + 1e-4f)
                {
                    break;
                }
                spinScale *= Mathf.Clamp01(
                    configuration.MaximumCurveDisplacement /
                    Mathf.Max(curve.magnitude, 1e-6f) * 0.95f);
            }

            if (curve.magnitude > configuration.MaximumCurveDisplacement + 1e-4f)
            {
                throw new InvalidOperationException(
                    $"Predicted curve {curve.magnitude:F3} m exceeds contract.");
            }

            return new ResolvedPlayerShotV1
            {
                ShotContractId = KernelConstants.PlayerShotContractId,
                ShotPhysicsId = configuration.PhysicsId,
                ShotStyle = style,
                MixtureComponentId = mixtureComponentId ?? style.ToString(),
                Command = command,
                IdealTargetLocal = idealTarget,
                ContactAdjustedTargetLocal = adjustedTarget,
                LaunchVelocityLocal = launchVelocity,
                AngularVelocityLocal = spin,
                SpinSaturationScale = spinScale,
                NominalFlightTime = flightTime,
                LaunchSpeed = launchVelocity.magnitude,
                PredictedUnopposedCrossingLocal = crossing,
                PredictedCurveDisplacement = curve,
                ExpectedTargetClass = ClassifyExpectedCrossing(crossing),
                SolverIterations = iterations,
                SolverCrossingError = crossingError,
                RareTail = rareTail,
            };
        }

        private static void SolveLaunch(
            Vector3 adjustedTarget,
            float flightTime,
            Vector3 spin,
            Vector3 gravity,
            float fixedTimestep,
            PlayerShotPhysicsConfigV1 configuration,
            out Vector3 launchVelocity,
            out Vector3 crossing,
            out int iterations,
            out float crossingError)
        {
            var virtualTarget = adjustedTarget;
            launchVelocity = Vector3.zero;
            crossing = Vector3.zero;
            iterations = 0;
            crossingError = float.PositiveInfinity;
            for (var iteration = 1;
                iteration <= configuration.SolverIterations;
                iteration++)
            {
                iterations = iteration;
                launchVelocity = KernelBallisticShotSolver.SolvePhysXInitialVelocity(
                    KernelConstants.CanonicalLaunch,
                    virtualTarget,
                    flightTime,
                    gravity,
                    fixedTimestep);
                if (!PlayerShotFlightModelV1.TryPredictCrossing(
                        KernelConstants.CanonicalLaunch,
                        launchVelocity,
                        spin,
                        gravity,
                        fixedTimestep,
                        configuration,
                        out crossing))
                {
                    throw new InvalidOperationException(
                        "football-flight-v1 did not reach the goal plane.");
                }

                var error = adjustedTarget - crossing;
                error.z = 0f;
                crossingError = error.magnitude;
                if (crossingError <= configuration.SolverTargetTolerance)
                {
                    break;
                }
                virtualTarget += error;
            }

            if (crossingError > configuration.MaximumAcceptedSolverError)
            {
                throw new InvalidOperationException(
                    $"Shot solver error {crossingError:F4} m exceeds contract.");
            }
        }

        public static ExpectedShotTargetClassV1 ClassifyExpectedCrossing(
            Vector3 crossing)
        {
            if (KernelGoalGeometry.IsWholeBallInsideGoal(crossing))
            {
                return ExpectedShotTargetClassV1.OnTarget;
            }

            var postDistance = Mathf.Abs(Mathf.Abs(crossing.x) - KernelConstants.GoalHalfWidth);
            var crossbarDistance = Mathf.Abs(crossing.y - KernelConstants.CrossbarLowerEdge);
            var withinFrameHeight =
                crossing.y >= -KernelConstants.BallRadius &&
                crossing.y <= KernelConstants.CrossbarLowerEdge + KernelConstants.BallRadius;
            var withinFrameWidth =
                Mathf.Abs(crossing.x) <= KernelConstants.GoalHalfWidth + KernelConstants.BallRadius;
            if ((postDistance <= KernelConstants.BallRadius && withinFrameHeight) ||
                (crossbarDistance <= KernelConstants.BallRadius && withinFrameWidth))
            {
                return ExpectedShotTargetClassV1.Frame;
            }

            return crossing.y + KernelConstants.BallRadius >
                KernelConstants.CrossbarLowerEdge
                ? ExpectedShotTargetClassV1.MissHigh
                : ExpectedShotTargetClassV1.MissWide;
        }
    }
}
