using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public static class KernelBallisticShotSolver
    {
        public static Vector3 SolveInitialVelocity(
            Vector3 launch,
            Vector3 target,
            float flightTime,
            Vector3 gravity)
        {
            if (!KernelMath.IsFinite(flightTime) || flightTime <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(flightTime));
            }

            if (!KernelMath.IsFinite(launch) ||
                !KernelMath.IsFinite(target) ||
                !KernelMath.IsFinite(gravity))
            {
                throw new ArgumentException("Ballistic solver inputs must be finite.");
            }

            var velocity =
                (target - launch - 0.5f * gravity * flightTime * flightTime) / flightTime;
            if (!KernelMath.IsFinite(velocity))
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
            if (!KernelMath.IsFinite(fixedTimestep) || fixedTimestep <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedTimestep));
            }

            return SolveInitialVelocity(launch, target, flightTime, gravity) -
                0.5f * gravity * fixedTimestep;
        }
    }

    public static class KernelGoalGeometry
    {
        public static bool TryIntersectPlane(
            Vector3 previous,
            Vector3 current,
            float planeZ,
            out Vector3 intersection)
        {
            intersection = default;
            if (!KernelMath.IsFinite(previous) || !KernelMath.IsFinite(current))
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

            var interpolation = (planeZ - previous.z) / delta;
            if (interpolation < 0f || interpolation > 1f)
            {
                return false;
            }

            intersection = Vector3.LerpUnclamped(previous, current, interpolation);
            return KernelMath.IsFinite(intersection);
        }

        public static bool IsWholeBallInsideGoal(Vector3 ballCentre)
        {
            if (!KernelMath.IsFinite(ballCentre))
            {
                return false;
            }

            var horizontallyInside =
                Mathf.Abs(ballCentre.x) + KernelConstants.BallRadius <=
                KernelConstants.GoalHalfWidth + 1e-5f;
            var belowCrossbar =
                ballCentre.y + KernelConstants.BallRadius <=
                KernelConstants.CrossbarLowerEdge + 1e-5f;
            var aboveGround = ballCentre.y - KernelConstants.BallRadius >= -1e-5f;
            return horizontallyInside && belowCrossbar && aboveGround;
        }

        public static AttemptOutcome ClassifyOutsideCrossing(Vector3 ballCentre)
        {
            if (!KernelMath.IsFinite(ballCentre))
            {
                return AttemptOutcome.Invalid;
            }

            if (IsWholeBallInsideGoal(ballCentre))
            {
                return AttemptOutcome.Goal;
            }

            return ballCentre.y + KernelConstants.BallRadius >
                KernelConstants.CrossbarLowerEdge
                ? AttemptOutcome.MissHigh
                : AttemptOutcome.MissWide;
        }
    }

    public static class ProceduralShotGenerator
    {
        public static ScenarioInstance Sample(
            ShotDistributionConfig configuration,
            ulong seed,
            Vector3 gravity,
            float fixedTimestep)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (!configuration.Validate(out var error))
            {
                throw new InvalidOperationException(error);
            }

            var random = new Pcg32(seed);
            float targetXNormalized;
            float targetYNormalized;
            var reachFocusSample =
                configuration.ReachFocusProbability > 0f &&
                random.NextFloat() < configuration.ReachFocusProbability;
            if (reachFocusSample)
            {
                var side = random.NextFloat() < 0.5f ? -1f : 1f;
                targetXNormalized =
                    side *
                    random.Range(
                        configuration.ReachFocusMinimumAbsoluteXNormalized,
                        configuration.ReachFocusMaximumAbsoluteXNormalized);
                if (configuration.ReachFocusBalancedHeightBands)
                {
                    var heightBand = Mathf.Min(
                        2,
                        Mathf.FloorToInt(random.NextFloat() * 3f));
                    var heightSpan =
                        configuration.ReachFocusMaximumYNormalized -
                        configuration.ReachFocusMinimumYNormalized;
                    var bandMinimum =
                        configuration.ReachFocusMinimumYNormalized +
                        heightSpan * heightBand / 3f;
                    var bandMaximum =
                        configuration.ReachFocusMinimumYNormalized +
                        heightSpan * (heightBand + 1) / 3f;
                    targetYNormalized = random.Range(
                        bandMinimum,
                        bandMaximum);
                }
                else
                {
                    targetYNormalized = random.Range(
                        configuration.ReachFocusMinimumYNormalized,
                        configuration.ReachFocusMaximumYNormalized);
                }
            }
            else
            {
                targetXNormalized = random.Range(
                    configuration.MinimumTargetXNormalized,
                    configuration.MaximumTargetXNormalized);
                targetYNormalized = random.Range(
                    configuration.MinimumTargetYNormalized,
                    configuration.MaximumTargetYNormalized);
            }
            var horizontalExtent =
                KernelConstants.GoalHalfWidth -
                KernelConstants.BallRadius -
                configuration.AdditionalFrameClearance;
            var minimumHeight =
                KernelConstants.BallRadius + configuration.AdditionalFrameClearance;
            var maximumHeight =
                KernelConstants.CrossbarLowerEdge -
                KernelConstants.BallRadius -
                configuration.AdditionalFrameClearance;
            var target = new Vector3(
                targetXNormalized * horizontalExtent,
                Mathf.Lerp(minimumHeight, maximumHeight, targetYNormalized),
                0f);
            var flightTime = random.Range(
                configuration.MinimumFlightTime,
                configuration.MaximumFlightTime);
            var launchDelay = random.Range(
                configuration.MinimumLaunchDelay,
                configuration.MaximumLaunchDelay);

            return new ScenarioInstance
            {
                ScenarioSuiteId = configuration.ScenarioSuiteId,
                Seed = seed,
                TargetXNormalized = targetXNormalized,
                TargetYNormalized = targetYNormalized,
                ReachFocusSample = reachFocusSample,
                TargetLocal = target,
                FlightTime = flightTime,
                LaunchDelay = launchDelay,
                Spin = Vector3.zero,
                LaunchVelocityLocal = KernelBallisticShotSolver.SolvePhysXInitialVelocity(
                    KernelConstants.CanonicalLaunch,
                    target,
                    flightTime,
                    gravity,
                    fixedTimestep),
            };
        }

        public static bool ValidateOnTarget(
            ScenarioInstance scenario,
            ShotDistributionConfig configuration,
            out string error)
        {
            if (!KernelMath.IsFinite(scenario.TargetLocal) ||
                !KernelMath.IsFinite(scenario.LaunchVelocityLocal) ||
                !KernelMath.IsFinite(scenario.FlightTime) ||
                !KernelMath.IsFinite(scenario.LaunchDelay))
            {
                error = "Scenario contains non-finite values.";
                return false;
            }

            var horizontalExtent =
                KernelConstants.GoalHalfWidth -
                KernelConstants.BallRadius -
                configuration.AdditionalFrameClearance;
            var minimumHeight =
                KernelConstants.BallRadius + configuration.AdditionalFrameClearance;
            var maximumHeight =
                KernelConstants.CrossbarLowerEdge -
                KernelConstants.BallRadius -
                configuration.AdditionalFrameClearance;

            if (Mathf.Abs(scenario.TargetLocal.x) > horizontalExtent + 1e-5f ||
                scenario.TargetLocal.y < minimumHeight - 1e-5f ||
                scenario.TargetLocal.y > maximumHeight + 1e-5f ||
                Mathf.Abs(scenario.TargetLocal.z) > 1e-5f)
            {
                error = "Scenario target is outside the declared on-target region.";
                return false;
            }

            var inConfiguredRange =
                scenario.ReachFocusSample
                    ? Mathf.Abs(scenario.TargetXNormalized) >=
                        configuration.ReachFocusMinimumAbsoluteXNormalized - 1e-5f &&
                      Mathf.Abs(scenario.TargetXNormalized) <=
                        configuration.ReachFocusMaximumAbsoluteXNormalized + 1e-5f &&
                      scenario.TargetYNormalized >=
                        configuration.ReachFocusMinimumYNormalized - 1e-5f &&
                      scenario.TargetYNormalized <=
                        configuration.ReachFocusMaximumYNormalized + 1e-5f
                    : scenario.TargetXNormalized >=
                        configuration.MinimumTargetXNormalized - 1e-5f &&
                      scenario.TargetXNormalized <=
                        configuration.MaximumTargetXNormalized + 1e-5f &&
                      scenario.TargetYNormalized >=
                        configuration.MinimumTargetYNormalized - 1e-5f &&
                      scenario.TargetYNormalized <=
                        configuration.MaximumTargetYNormalized + 1e-5f;
            if (!inConfiguredRange)
            {
                error = "Scenario target is outside the configured curriculum range.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
