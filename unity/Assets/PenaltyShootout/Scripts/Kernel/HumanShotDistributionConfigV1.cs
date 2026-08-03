using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "HumanShotDistributionV1",
        menuName = "Penalty Shootout/Stage 6/Human Shot Distribution V1")]
    public sealed class HumanShotDistributionConfigV1 : ScriptableObject
    {
        public string ScenarioSuiteId = KernelConstants.HumanShotScenarioSuiteId;
        public float MinimumLaunchDelay = 0.15f;
        public float MaximumLaunchDelay = 0.45f;
        [Range(0f, 1f)] public float PlacedWeight = 0.45f;
        [Range(0f, 1f)] public float PowerWeight = 0.35f;
        [Range(0f, 1f)] public float CurledWeight = 0.20f;
        [Range(0f, 1f)] public float OnTargetWeight = 0.92f;
        [Range(0f, 1f)] public float FrameWeight = 0.04f;
        [Range(0f, 1f)] public float MissWideWeight = 0.025f;
        [Range(0f, 1f)] public float MissHighWeight = 0.015f;
        [Range(0f, 1f)] public float RareTailProbability = 0.03f;
        [Range(-0.95f, 0.95f)] public float ContactErrorCorrelation = 0.25f;
        public float ContactErrorTruncationSigma = 2.5f;
        public int MaximumCategoryAttempts = 32;

        public bool Validate(out string error)
        {
            if (ScenarioSuiteId != KernelConstants.HumanShotScenarioSuiteId)
            {
                error = $"Scenario suite must be {KernelConstants.HumanShotScenarioSuiteId}.";
                return false;
            }

            var styleSum = PlacedWeight + PowerWeight + CurledWeight;
            var targetSum = OnTargetWeight + FrameWeight + MissWideWeight + MissHighWeight;
            if (!Finite(MinimumLaunchDelay) || !Finite(MaximumLaunchDelay) ||
                MinimumLaunchDelay < 0f || MinimumLaunchDelay > MaximumLaunchDelay ||
                !Finite(styleSum) || Mathf.Abs(styleSum - 1f) > 1e-4f ||
                !Finite(targetSum) || Mathf.Abs(targetSum - 1f) > 1e-4f ||
                !Finite(RareTailProbability) || RareTailProbability < 0f ||
                RareTailProbability > 1f ||
                !Finite(ContactErrorCorrelation) ||
                Mathf.Abs(ContactErrorCorrelation) >= 1f ||
                !Finite(ContactErrorTruncationSigma) ||
                ContactErrorTruncationSigma <= 0f || MaximumCategoryAttempts <= 0)
            {
                error = "human-shot-v1 distribution is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool Finite(float value) => KernelMath.IsFinite(value);
    }

    public static class HumanShotGeneratorV1
    {
        public static ScenarioInstance Sample(
            HumanShotDistributionConfigV1 distribution,
            PlayerShotPhysicsConfigV1 physics,
            ulong seed,
            Vector3 gravity,
            float fixedTimestep,
            float forcedHorizontalSide = 0f)
        {
            if (distribution == null)
            {
                throw new ArgumentNullException(nameof(distribution));
            }
            if (!distribution.Validate(out var distributionError))
            {
                throw new ArgumentException(distributionError, nameof(distribution));
            }
            if (physics == null)
            {
                throw new ArgumentNullException(nameof(physics));
            }
            if (!physics.Validate(out var physicsError))
            {
                throw new ArgumentException(physicsError, nameof(physics));
            }

            if (!KernelMath.IsFinite(forcedHorizontalSide) ||
                (Mathf.Abs(forcedHorizontalSide) > 1e-5f &&
                 Mathf.Abs(Mathf.Abs(forcedHorizontalSide) - 1f) > 1e-5f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forcedHorizontalSide),
                    "Forced horizontal side must be -1, 0, or 1.");
            }

            var random = new Pcg32(seed);
            var style = SampleStyle(ref random, distribution);
            var targetClass = SampleTargetClass(ref random, distribution);
            var rareTail = random.NextFloat() < distribution.RareTailProbability;
            ResolvedPlayerShotV1 resolved = default;
            var found = false;
            for (var candidate = 0;
                 candidate < distribution.MaximumCategoryAttempts;
                 candidate++)
            {
                var command = SampleCommand(
                    ref random,
                    distribution,
                    style,
                    targetClass,
                    rareTail);
                command = ApplyHorizontalSide(command, forcedHorizontalSide);
                try
                {
                    resolved = PlayerShotResolverV1.Resolve(
                        command,
                        style,
                        $"{style.ToString().ToLowerInvariant()}-{targetClass.ToString().ToLowerInvariant()}",
                        rareTail,
                        gravity,
                        fixedTimestep,
                        physics);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (resolved.LaunchSpeed >= 14f &&
                    resolved.LaunchSpeed <= 30f &&
                    resolved.ExpectedTargetClass == targetClass &&
                    (!rareTail ||
                     (resolved.Command.Power >= 0.95f &&
                      Mathf.Abs(resolved.Command.AimX) >= 0.90f)))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"human-shot-v1 exhausted deterministic candidates for " +
                    $"{style}/{targetClass} (rare_tail={rareTail}).");
            }

            return new ScenarioInstance
            {
                ScenarioSuiteId = distribution.ScenarioSuiteId,
                Seed = seed,
                TargetXNormalized = resolved.Command.AimX,
                TargetYNormalized = (resolved.Command.AimY + 1f) * 0.5f,
                ReachFocusSample = false,
                TargetLocal = resolved.ContactAdjustedTargetLocal,
                FlightTime = resolved.NominalFlightTime,
                LaunchDelay = random.Range(
                    distribution.MinimumLaunchDelay,
                    distribution.MaximumLaunchDelay),
                Spin = resolved.AngularVelocityLocal,
                LaunchVelocityLocal = resolved.LaunchVelocityLocal,
                PlayerShot = resolved,
            };
        }

        public static bool Validate(
            ScenarioInstance scenario,
            HumanShotDistributionConfigV1 distribution,
            PlayerShotPhysicsConfigV1 physics,
            out string error)
        {
            error = string.Empty;
            if (distribution == null)
            {
                error = "Human shot distribution is missing.";
                return false;
            }
            if (!distribution.Validate(out error))
            {
                return false;
            }
            if (physics == null)
            {
                error = "Player shot physics configuration is missing.";
                return false;
            }
            if (!physics.Validate(out error))
            {
                return false;
            }
            if (scenario.ScenarioSuiteId != KernelConstants.HumanShotScenarioSuiteId ||
                scenario.PlayerShot.ShotContractId != KernelConstants.PlayerShotContractId ||
                scenario.PlayerShot.ShotPhysicsId != KernelConstants.PlayerShotPhysicsId ||
                !scenario.PlayerShot.Command.Validate(out error) ||
                !KernelMath.IsFinite(scenario.LaunchVelocityLocal) ||
                !KernelMath.IsFinite(scenario.Spin) ||
                !KernelMath.IsFinite(scenario.PlayerShot.PredictedUnopposedCrossingLocal) ||
                scenario.PlayerShot.SolverCrossingError >
                    physics.MaximumAcceptedSolverError + 1e-5f ||
                scenario.PlayerShot.PredictedCurveDisplacement.magnitude >
                    physics.MaximumCurveDisplacement + 1e-5f)
            {
                if (string.IsNullOrEmpty(error))
                {
                    error = "Resolved human-shot-v1 scenario violates its contract.";
                }
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static PlayerShotStyleV1 SampleStyle(
            ref Pcg32 random,
            HumanShotDistributionConfigV1 configuration)
        {
            var sample = random.NextFloat();
            if (sample < configuration.PlacedWeight)
            {
                return PlayerShotStyleV1.Placed;
            }
            return sample < configuration.PlacedWeight + configuration.PowerWeight
                ? PlayerShotStyleV1.Power
                : PlayerShotStyleV1.Curled;
        }

        private static PlayerShotCommandV1 ApplyHorizontalSide(
            PlayerShotCommandV1 command,
            float forcedHorizontalSide)
        {
            if (Mathf.Abs(forcedHorizontalSide) <= 1e-5f)
            {
                return command;
            }

            return new PlayerShotCommandV1(
                Mathf.Abs(command.AimX) * forcedHorizontalSide,
                command.AimY,
                command.Power,
                command.SideSpin,
                command.VerticalSpin,
                command.ContactErrorXMeters,
                command.ContactErrorYMeters);
        }

        private static ExpectedShotTargetClassV1 SampleTargetClass(
            ref Pcg32 random,
            HumanShotDistributionConfigV1 configuration)
        {
            var sample = random.NextFloat();
            if (sample < configuration.OnTargetWeight)
            {
                return ExpectedShotTargetClassV1.OnTarget;
            }
            sample -= configuration.OnTargetWeight;
            if (sample < configuration.FrameWeight)
            {
                return ExpectedShotTargetClassV1.Frame;
            }
            sample -= configuration.FrameWeight;
            return sample < configuration.MissWideWeight
                ? ExpectedShotTargetClassV1.MissWide
                : ExpectedShotTargetClassV1.MissHigh;
        }

        private static PlayerShotCommandV1 SampleCommand(
            ref Pcg32 random,
            HumanShotDistributionConfigV1 configuration,
            PlayerShotStyleV1 style,
            ExpectedShotTargetClassV1 targetClass,
            bool rareTail)
        {
            SampleStyleParameters(
                ref random,
                style,
                out var power,
                out var sideSpin,
                out var verticalSpin,
                out var sigmaX,
                out var sigmaY);
            var aim = SampleAim(ref random, targetClass);
            if (rareTail)
            {
                power = random.Range(0.95f, 0.955f);
                var side = random.NextFloat() < 0.5f ? -1f : 1f;
                aim.x = side * random.Range(0.90f, 0.92f);
                if (targetClass == ExpectedShotTargetClassV1.MissHigh)
                {
                    aim.y = HeightToAim(random.Range(2.58f, 2.64f));
                    verticalSpin = -1f;
                }
                sideSpin = side * random.Range(0.65f, 0.78f);
            }

            var powerFactor = Mathf.InverseLerp(0.35f, 1f, power);
            sigmaX *= Mathf.Lerp(0.85f, 1.25f, powerFactor);
            sigmaY *= Mathf.Lerp(0.85f, 1.25f, powerFactor);
            SampleCorrelatedGaussian(
                ref random,
                sigmaX,
                sigmaY,
                configuration.ContactErrorCorrelation,
                configuration.ContactErrorTruncationSigma,
                out var errorX,
                out var errorY);
            if (rareTail)
            {
                var errorMagnitude =
                    targetClass == ExpectedShotTargetClassV1.MissWide
                        ? -0.15f
                        : targetClass == ExpectedShotTargetClassV1.Frame
                            ? 0.10f
                            : 0.22f;
                errorX = -Mathf.Sign(aim.x) * errorMagnitude;
                errorY = 0f;
            }
            return new PlayerShotCommandV1(
                aim.x,
                aim.y,
                power,
                sideSpin,
                verticalSpin,
                Mathf.Clamp(errorX, -0.75f, 0.75f),
                Mathf.Clamp(errorY, -0.75f, 0.75f));
        }

        private static void SampleStyleParameters(
            ref Pcg32 random,
            PlayerShotStyleV1 style,
            out float power,
            out float sideSpin,
            out float verticalSpin,
            out float sigmaX,
            out float sigmaY)
        {
            switch (style)
            {
                case PlayerShotStyleV1.Power:
                    power = random.Range(0.72f, 1f);
                    sideSpin = random.Range(-0.12f, 0.12f);
                    verticalSpin = random.Range(0.05f, 0.35f);
                    sigmaX = 0.24f;
                    sigmaY = 0.18f;
                    return;
                case PlayerShotStyleV1.Curled:
                    power = random.Range(0.50f, 0.82f);
                    sideSpin =
                        (random.NextFloat() < 0.5f ? -1f : 1f) *
                        random.Range(0.45f, 0.95f);
                    verticalSpin = random.Range(0.05f, 0.35f);
                    sigmaX = 0.18f;
                    sigmaY = 0.14f;
                    return;
                default:
                    power = random.Range(0.35f, 0.68f);
                    sideSpin = random.Range(-0.20f, 0.20f);
                    verticalSpin = random.Range(-0.10f, 0.18f);
                    sigmaX = 0.12f;
                    sigmaY = 0.10f;
                    return;
            }
        }

        private static Vector2 SampleAim(
            ref Pcg32 random,
            ExpectedShotTargetClassV1 targetClass)
        {
            if (targetClass == ExpectedShotTargetClassV1.Frame)
            {
                if (random.NextFloat() < 0.65f)
                {
                    var side = random.NextFloat() < 0.5f ? -1f : 1f;
                    return new Vector2(
                        side * random.Range(0.84f, 0.92f),
                        HeightToAim(random.Range(0.30f, 2.20f)));
                }
                return new Vector2(
                    random.Range(-0.80f, 0.80f),
                    HeightToAim(random.Range(2.31f, 2.52f)));
            }
            if (targetClass == ExpectedShotTargetClassV1.MissWide)
            {
                var side = random.NextFloat() < 0.5f ? -1f : 1f;
                return new Vector2(
                    side * random.Range(0.91f, 1f),
                    HeightToAim(random.Range(0.25f, 2.15f)));
            }
            if (targetClass == ExpectedShotTargetClassV1.MissHigh)
            {
                return new Vector2(
                    random.Range(-0.78f, 0.78f),
                    HeightToAim(random.Range(2.58f, 2.90f)));
            }

            return new Vector2(SampleHorizontalAim(ref random), SampleVerticalAim(ref random));
        }

        private static float SampleHorizontalAim(ref Pcg32 random)
        {
            var sample = random.NextFloat();
            if (sample < 0.25f)
            {
                return random.Range(-0.84f, -0.56f);
            }
            if (sample < 0.45f)
            {
                return random.Range(-0.56f, -0.16f);
            }
            if (sample < 0.55f)
            {
                return random.Range(-0.16f, 0.16f);
            }
            if (sample < 0.75f)
            {
                return random.Range(0.16f, 0.56f);
            }
            return random.Range(0.56f, 0.84f);
        }

        private static float SampleVerticalAim(ref Pcg32 random)
        {
            var sample = random.NextFloat();
            if (sample < 0.45f)
            {
                return HeightToAim(random.Range(0.18f, 0.82f));
            }
            if (sample < 0.80f)
            {
                return HeightToAim(random.Range(0.82f, 1.65f));
            }
            return HeightToAim(random.Range(1.65f, 2.25f));
        }

        private static float HeightToAim(float height)
        {
            return Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(
                PlayerShotResolverV1.MinimumAimHeight,
                PlayerShotResolverV1.MaximumAimHeight,
                height));
        }

        private static void SampleCorrelatedGaussian(
            ref Pcg32 random,
            float sigmaX,
            float sigmaY,
            float correlation,
            float truncation,
            out float x,
            out float y)
        {
            var u1 = Mathf.Max(1e-7f, random.NextFloat());
            var u2 = random.NextFloat();
            var radius = Mathf.Sqrt(-2f * Mathf.Log(u1));
            var z0 = Mathf.Clamp(
                radius * Mathf.Cos(2f * Mathf.PI * u2),
                -truncation,
                truncation);
            var z1 = Mathf.Clamp(
                radius * Mathf.Sin(2f * Mathf.PI * u2),
                -truncation,
                truncation);
            x = sigmaX * z0;
            y = sigmaY *
                (correlation * z0 +
                 Mathf.Sqrt(1f - correlation * correlation) * z1);
        }
    }
}
