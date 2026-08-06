using System;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [CreateAssetMenu(
        fileName = "PlayerPenaltyInputV1",
        menuName = "Penalty Shootout/Stage 7/Player Penalty Input V1")]
    public sealed class PlayerPenaltyInputConfigV1 : ScriptableObject
    {
        public string ContractId = KernelConstants.PlayerPenaltyInputContractId;
        [Range(0.5f, 2f)] public float PointerSensitivity = 1f;
        [Range(0.5f, 2f)] public float KeyboardAimSpeed = 1.1f;
        public float MaximumChargeSeconds = 1.2f;
        public float MinimumPower = 0.2f;
        public float ComposurePeriodSeconds = 1.15f;
        public float ReticleFadeSeconds = 0.18f;
        public float CurveRatePerSecond = 1.5f;
        public float MinimumVerticalSpin = 0.05f;
        public float MaximumVerticalSpin = 0.25f;
        public float PerfectTimingErrorMultiplier = 0.6f;
        public float PoorTimingErrorMultiplier = 1.8f;
        public float GaussianTruncation = 2.5f;
        public float ResultHoldSeconds = 1.4f;
        public float FadeSeconds = 0.2f;

        public bool Validate(out string error)
        {
            if (ContractId != KernelConstants.PlayerPenaltyInputContractId ||
                !InRange(PointerSensitivity, 0.5f, 2f) ||
                !InRange(KeyboardAimSpeed, 0.5f, 2f) ||
                !Positive(MaximumChargeSeconds) ||
                !InRange(MinimumPower, 0f, 1f) ||
                !Positive(ComposurePeriodSeconds) ||
                !Positive(ReticleFadeSeconds) ||
                !Positive(CurveRatePerSecond) ||
                !InRange(MinimumVerticalSpin, -1f, 1f) ||
                !InRange(MaximumVerticalSpin, -1f, 1f) ||
                MaximumVerticalSpin < MinimumVerticalSpin ||
                !Positive(PerfectTimingErrorMultiplier) ||
                PoorTimingErrorMultiplier < PerfectTimingErrorMultiplier ||
                !Positive(GaussianTruncation) ||
                !Positive(ResultHoldSeconds) ||
                !Positive(FadeSeconds))
            {
                error = "Player penalty input configuration is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool Positive(float value) =>
            KernelMath.IsFinite(value) && value > 0f;

        private static bool InRange(float value, float minimum, float maximum) =>
            KernelMath.IsFinite(value) && value >= minimum && value <= maximum;
    }

    public enum Stage7GameplayStateV1
    {
        Boot = 0,
        Preparing = 1,
        Aiming = 2,
        Charging = 3,
        RunUp = 4,
        BallInFlight = 5,
        Result = 6,
        SetComplete = 7,
        Paused = 8,
        TechnicalRetry = 9,
    }

    public static class PlayerPenaltyInputMathV1
    {
        public const int ShotsPerSet = 5;
        public const float CurledSpinThreshold = 0.25f;
        public const float PowerStyleThreshold = 0.72f;

        public static Vector2 CommandAimBounds
        {
            get
            {
                var x =
                    (KernelConstants.GoalHalfWidth - KernelConstants.BallRadius) /
                    PlayerShotResolverV1.MaximumAimX;
                var maximumHeight =
                    KernelConstants.CrossbarLowerEdge - KernelConstants.BallRadius;
                var y = Mathf.Lerp(
                    -1f,
                    1f,
                    Mathf.InverseLerp(
                        PlayerShotResolverV1.MinimumAimHeight,
                        PlayerShotResolverV1.MaximumAimHeight,
                        maximumHeight));
                return new Vector2(x, y);
            }
        }

        public static Vector2 ClampAim(Vector2 aim)
        {
            var bounds = CommandAimBounds;
            return new Vector2(
                Mathf.Clamp(aim.x, -bounds.x, bounds.x),
                Mathf.Clamp(aim.y, -1f, bounds.y));
        }

        public static float PowerForHold(
            float holdSeconds,
            PlayerPenaltyInputConfigV1 configuration)
        {
            return Mathf.Lerp(
                configuration.MinimumPower,
                1f,
                Mathf.Clamp01(holdSeconds / configuration.MaximumChargeSeconds));
        }

        public static float ComposureQuality(
            float elapsedSeconds,
            PlayerPenaltyInputConfigV1 configuration)
        {
            var phase = Mathf.Repeat(
                elapsedSeconds,
                configuration.ComposurePeriodSeconds) /
                configuration.ComposurePeriodSeconds;
            var triangle = 1f - Mathf.Clamp01(Mathf.Abs(phase - 0.5f) * 2f);
            return triangle * triangle * (3f - 2f * triangle);
        }

        public static PlayerShotStyleV1 InferStyle(float power, float sideSpin)
        {
            if (Mathf.Abs(sideSpin) >= CurledSpinThreshold)
            {
                return PlayerShotStyleV1.Curled;
            }
            return power >= PowerStyleThreshold
                ? PlayerShotStyleV1.Power
                : PlayerShotStyleV1.Placed;
        }

        public static Vector2 ContactError(
            ulong sessionSeed,
            int shotIndex,
            PlayerShotStyleV1 style,
            float timingQuality,
            PlayerPenaltyInputConfigV1 configuration)
        {
            if (sessionSeed == 0UL || shotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sessionSeed));
            }

            BaseSigma(style, out var sigmaX, out var sigmaY);
            var multiplier = Mathf.Lerp(
                configuration.PoorTimingErrorMultiplier,
                configuration.PerfectTimingErrorMultiplier,
                Mathf.Clamp01(timingQuality));
            var random = new Pcg32(Pcg32.DeriveSeed(sessionSeed, 0, shotIndex + 1));
            var u1 = Mathf.Max(1e-7f, random.NextFloat());
            var u2 = random.NextFloat();
            var radius = Mathf.Sqrt(-2f * Mathf.Log(u1));
            var z0 = Mathf.Clamp(
                radius * Mathf.Cos(2f * Mathf.PI * u2),
                -configuration.GaussianTruncation,
                configuration.GaussianTruncation);
            var z1 = Mathf.Clamp(
                radius * Mathf.Sin(2f * Mathf.PI * u2),
                -configuration.GaussianTruncation,
                configuration.GaussianTruncation);
            return new Vector2(
                Mathf.Clamp(sigmaX * multiplier * z0, -0.75f, 0.75f),
                Mathf.Clamp(sigmaY * multiplier * z1, -0.75f, 0.75f));
        }

        public static PlayerPenaltyShotRequestV1 BuildRequest(
            Vector2 lockedAim,
            float power,
            float sideSpin,
            float timingQuality,
            float chargeDuration,
            ulong sessionSeed,
            int shotIndex,
            PlayerShotInputDeviceV1 inputDevice,
            PlayerPenaltyInputConfigV1 configuration)
        {
            var style = InferStyle(power, sideSpin);
            var error = ContactError(
                sessionSeed,
                shotIndex,
                style,
                timingQuality,
                configuration);
            var aim = ClampAim(lockedAim);
            var command = new PlayerShotCommandV1(
                aim.x,
                aim.y,
                Mathf.Clamp01(power),
                Mathf.Clamp(sideSpin, -1f, 1f),
                Mathf.Lerp(
                    configuration.MinimumVerticalSpin,
                    configuration.MaximumVerticalSpin,
                    Mathf.Clamp01(power)),
                error.x,
                error.y);
            return new PlayerPenaltyShotRequestV1
            {
                Command = command,
                Style = style,
                InputSeed = Pcg32.DeriveSeed(sessionSeed, 1, shotIndex + 1),
                TimingQuality = Mathf.Clamp01(timingQuality),
                ChargeDuration = Mathf.Max(0f, chargeDuration),
                InputDevice = inputDevice,
            };
        }

        private static void BaseSigma(
            PlayerShotStyleV1 style,
            out float sigmaX,
            out float sigmaY)
        {
            switch (style)
            {
                case PlayerShotStyleV1.Power:
                    sigmaX = 0.24f;
                    sigmaY = 0.18f;
                    return;
                case PlayerShotStyleV1.Curled:
                    sigmaX = 0.18f;
                    sigmaY = 0.14f;
                    return;
                default:
                    sigmaX = 0.12f;
                    sigmaY = 0.10f;
                    return;
            }
        }
    }

    [Serializable]
    public sealed class PenaltySetScoreV1
    {
        public int ValidShots;
        public int Goals;
        public int Saves;
        public int Misses;

        public bool Complete => ValidShots >= PlayerPenaltyInputMathV1.ShotsPerSet;

        public bool Record(AttemptOutcome outcome)
        {
            switch (outcome)
            {
                case AttemptOutcome.Goal:
                    Goals++;
                    break;
                case AttemptOutcome.Saved:
                case AttemptOutcome.BlockedThenOut:
                    Saves++;
                    break;
                case AttemptOutcome.MissWide:
                case AttemptOutcome.MissHigh:
                case AttemptOutcome.PostOrCrossbarOut:
                    Misses++;
                    break;
                default:
                    return false;
            }

            ValidShots++;
            return true;
        }
    }
}
