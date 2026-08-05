using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GoalkeeperGloveCalibrationProfileV2
    {
        Conservative = 0,
        Balanced = 1,
        Permissive = 2,
    }

    public readonly struct GoalkeeperGloveThresholdsV2
    {
        public readonly GoalkeeperGloveCalibrationProfileV2 Profile;
        public readonly float CatchAlignment;
        public readonly float OneHandCatchMaximumSpeed;
        public readonly float TwoHandCatchMaximumSpeed;
        public readonly float CentralExtent;
        public readonly float PunchAlignment;
        public readonly float PunchForwardSpeed;

        public GoalkeeperGloveThresholdsV2(
            GoalkeeperGloveCalibrationProfileV2 profile,
            float catchAlignment,
            float oneHandCatchMaximumSpeed,
            float twoHandCatchMaximumSpeed,
            float centralExtent,
            float punchAlignment,
            float punchForwardSpeed)
        {
            Profile = profile;
            CatchAlignment = catchAlignment;
            OneHandCatchMaximumSpeed = oneHandCatchMaximumSpeed;
            TwoHandCatchMaximumSpeed = twoHandCatchMaximumSpeed;
            CentralExtent = centralExtent;
            PunchAlignment = punchAlignment;
            PunchForwardSpeed = punchForwardSpeed;
        }

        public string Id => Profile.ToString().ToLowerInvariant();
    }

    public static class GoalkeeperGloveCalibrationProfilesV2
    {
        public static GoalkeeperGloveThresholdsV2 Get(
            GoalkeeperGloveCalibrationProfileV2 profile)
        {
            switch (profile)
            {
                case GoalkeeperGloveCalibrationProfileV2.Conservative:
                    return new GoalkeeperGloveThresholdsV2(
                        profile, 0.70f, 8f, 16f, 0.76f, 0.50f, 1.00f);
                case GoalkeeperGloveCalibrationProfileV2.Permissive:
                    return new GoalkeeperGloveThresholdsV2(
                        profile, 0.56f, 10f, 20f, 0.88f, 0.30f, 0.65f);
                default:
                    return new GoalkeeperGloveThresholdsV2(
                        GoalkeeperGloveCalibrationProfileV2.Balanced,
                        0.62f, 9f, 18f, 0.82f, 0.40f, 0.80f);
            }
        }

        public static bool IsDefined(int profile)
        {
            return profile >= (int)GoalkeeperGloveCalibrationProfileV2.Conservative &&
                profile <= (int)GoalkeeperGloveCalibrationProfileV2.Permissive;
        }
    }

    [CreateAssetMenu(
        fileName = "GoalkeeperGloveHandlingV2",
        menuName = "Penalty Shootout/Stage 6/Goalkeeper Glove Handling V2")]
    public sealed class GoalkeeperGloveHandlingConfigV2 : ScriptableObject
    {
        public string ContractId = KernelConstants.GoalkeeperGloveHandlingV2ContractId;
        public string GeometryId = KernelConstants.GoalkeeperPalmGeometryId;

        [Header("Catch and possession")]
        public float MaximumCaptureDistance = 0.16f;
        public float TwoHandCaptureRadius = 0.19f;
        public float TwoHandMaximumSeparation = 0.34f;
        public float CaptureBlendDuration = 0.08f;
        public float CatchPossessionDuration = 0.18f;

        [Header("Redirect response")]
        [Range(0f, 1f)] public float MinimumParryAlignment = 0.12f;
        [Range(0f, 1f)] public float ParryRestitution = 0.18f;
        [Range(0f, 1f)] public float ParryTangentialRetention = 0.50f;
        [Range(0f, 1f)] public float ParryGloveVelocityTransfer = 0.35f;
        public float PunchMinimumImpactSpeed = 12f;
        [Range(0f, 1f)] public float PunchRestitution = 0.30f;
        [Range(0f, 1f)] public float PunchTangentialRetention = 0.38f;
        [Range(0f, 1f)] public float PunchGloveVelocityTransfer = 0.55f;
        [Range(0f, 1f)] public float MaximumOutgoingEnergyRatio = 0.95f;

        public bool Validate(out string error)
        {
            if (ContractId != KernelConstants.GoalkeeperGloveHandlingV2ContractId ||
                GeometryId != KernelConstants.GoalkeeperPalmGeometryId)
            {
                error = "Glove handling v2 contract or geometry ID is invalid.";
                return false;
            }

            if (!Positive(MaximumCaptureDistance) ||
                !Positive(TwoHandCaptureRadius) ||
                !Positive(TwoHandMaximumSeparation) ||
                !Positive(CaptureBlendDuration) ||
                !Positive(CatchPossessionDuration) ||
                CaptureBlendDuration > CatchPossessionDuration ||
                !Unit(MinimumParryAlignment) ||
                !Unit(ParryRestitution) ||
                !Unit(ParryTangentialRetention) ||
                !Unit(ParryGloveVelocityTransfer) ||
                !Positive(PunchMinimumImpactSpeed) ||
                !Unit(PunchRestitution) ||
                !Unit(PunchTangentialRetention) ||
                !Unit(PunchGloveVelocityTransfer) ||
                !Positive(MaximumOutgoingEnergyRatio) ||
                MaximumOutgoingEnergyRatio > 1f)
            {
                error = "keeper-glove-handling-v2 configuration is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool Positive(float value)
        {
            return KernelMath.IsFinite(value) && value > 0f;
        }

        private static bool Unit(float value)
        {
            return KernelMath.IsFinite(value) && value >= 0f && value <= 1f;
        }
    }
}
