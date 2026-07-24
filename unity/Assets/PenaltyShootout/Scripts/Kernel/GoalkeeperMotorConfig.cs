using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "GoalkeeperMotorProfile",
        menuName = "Penalty Shootout/Stage 1/Goalkeeper Motor Profile")]
    public sealed class GoalkeeperMotorConfig : ScriptableObject
    {
        public string MotorProfileId = KernelConstants.MotorProfileId;
        public float StandingZ = 0.30f;
        public float LateralLimit = 3.10f;
        public float MaximumShuffleSpeed = 3.5f;
        public float ShuffleAcceleration = 18f;
        public float ShuffleDeceleration = 22f;
        public float DiveDuration = 0.70f;
        public float RecoveryDuration = 0.35f;
        public float LowDiveReach = 2.10f;
        public float MiddleDiveReach = 1.90f;
        public float HighDiveReach = 1.70f;
        public float LowDiveHeight = 0.12f;
        public float MiddleDiveHeight = 0.45f;
        public float HighDiveHeight = 0.80f;
        public float MaximumBodyRollDegrees = 80f;

        [Header("Action-conditioned hand reach")]
        [Range(0f, 1f)]
        public float ReachStartNormalized = 0.08f;

        [Range(0f, 1f)]
        public float FullExtensionNormalized = 0.55f;

        public float LeadingLowLateralReach = 0.55f;
        public float TrailingLowLateralReach = 0.28f;
        public float LeadingMiddleLateralReach = 0.65f;
        public float TrailingMiddleLateralReach = 0.36f;
        public float LeadingHighLateralReach = 0.76f;
        public float TrailingHighLateralReach = 0.46f;
        public float LeadingLowHeight = 0.22f;
        public float TrailingLowHeight = 0.34f;
        public float LeadingMiddleHeight = 0.58f;
        public float TrailingMiddleHeight = 0.48f;
        public float LeadingHighHeight = 0.92f;
        public float TrailingHighHeight = 0.74f;
        public float LeadingForwardReach = 0.18f;
        public float TrailingForwardReach = 0.12f;

        [Header("Authoritative hand geometry")]
        public float GloveRadius = 0.125f;
        public float ArmRadius = 0.09f;
        public float MaximumArmLength = 0.95f;
        public float ReadyGloveLateral = 0.58f;
        public float ReadyGloveHeight = 0.92f;
        public float ReadyGloveForward = 0f;
        public float ShoulderLateral = 0.25f;
        public float ShoulderHeight = 1.30f;
        public float ShoulderForward = 0f;

        public bool Validate(out string error)
        {
            if (MotorProfileId != KernelConstants.MotorProfileId)
            {
                error = $"Motor profile ID must be {KernelConstants.MotorProfileId}.";
                return false;
            }

            if (LateralLimit <= 0f ||
                MaximumShuffleSpeed <= 0f ||
                ShuffleAcceleration <= 0f ||
                ShuffleDeceleration <= 0f ||
                DiveDuration <= 0f ||
                RecoveryDuration <= 0f)
            {
                error = "Motor speeds, limits, and durations must be positive.";
                return false;
            }

            if (LowDiveReach <= 0f ||
                MiddleDiveReach <= 0f ||
                HighDiveReach <= 0f ||
                MaximumBodyRollDegrees <= 0f ||
                MaximumBodyRollDegrees > 90f)
            {
                error = "Dive reach or body-roll configuration is invalid.";
                return false;
            }

            if (ReachStartNormalized < 0f ||
                FullExtensionNormalized <= ReachStartNormalized ||
                FullExtensionNormalized > 1f)
            {
                error = "Hand-reach timing must satisfy 0 <= start < full extension <= 1.";
                return false;
            }

            if (GloveRadius <= 0f ||
                ArmRadius <= 0f ||
                MaximumArmLength <= GloveRadius ||
                ReadyGloveLateral <= 0f ||
                ReadyGloveHeight <= 0f ||
                ShoulderLateral <= 0f ||
                ShoulderHeight <= 0f ||
                LeadingForwardReach < 0f ||
                TrailingForwardReach < 0f)
            {
                error = "Hand and arm geometry must use positive dimensions and non-negative reach.";
                return false;
            }

            if (!ValidReachPair(LeadingLowLateralReach, TrailingLowLateralReach) ||
                !ValidReachPair(LeadingMiddleLateralReach, TrailingMiddleLateralReach) ||
                !ValidReachPair(LeadingHighLateralReach, TrailingHighLateralReach))
            {
                error = "Every leading lateral reach must be positive and at least the trailing reach.";
                return false;
            }

            if (LeadingLowHeight <= 0f ||
                LeadingMiddleHeight <= LeadingLowHeight ||
                LeadingHighHeight <= LeadingMiddleHeight ||
                TrailingLowHeight <= 0f ||
                TrailingMiddleHeight <= TrailingLowHeight ||
                TrailingHighHeight <= TrailingMiddleHeight)
            {
                error = "Low, middle, and high hand targets must be strictly height ordered.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool ValidReachPair(float leading, float trailing)
        {
            return leading > 0f && trailing > 0f && leading >= trailing;
        }
    }
}
