using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "GoalkeeperGloveHandlingV1",
        menuName = "Penalty Shootout/Stage 6/Goalkeeper Glove Handling V1")]
    public sealed class GoalkeeperGloveHandlingConfigV1 : ScriptableObject
    {
        public string ContractId = KernelConstants.GoalkeeperGloveHandlingContractId;
        public string GeometryId = KernelConstants.GoalkeeperPalmGeometryId;

        [Header("Compound glove geometry")]
        public Vector3 PalmSize = new Vector3(0.15f, 0.13f, 0.055f);
        public Vector3 FingerSize = new Vector3(0.11f, 0.05f, 0.05f);
        public float FingerOffsetY = 0.07f;
        public float MaximumRadialExtent = 0.11f;

        [Header("Contact classification")]
        [Range(0f, 1f)] public float CentralPalmExtent = 0.78f;
        [Range(0f, 1f)] public float MinimumCatchAlignment = 0.68f;
        public float OneHandCatchMaximumSpeed = 10f;
        public float TwoHandCatchMaximumSpeed = 22f;
        public float TwoHandCaptureRadius = 0.19f;
        public float TwoHandMaximumSeparation = 0.34f;
        public float CatchPossessionDuration = 0.12f;

        [Header("Redirect response")]
        [Range(0f, 1f)] public float MinimumParryAlignment = 0.12f;
        [Range(0f, 1f)] public float ParryRestitution = 0.18f;
        [Range(0f, 1f)] public float TangentialRetention = 0.50f;
        [Range(0f, 1f)] public float GloveVelocityTransfer = 0.35f;
        public float PunchMinimumForwardSpeed = 2f;
        [Range(0f, 1f)] public float MaximumOutgoingEnergyRatio = 0.95f;

        public float CompoundRadialExtent
        {
            get
            {
                var palm = new Vector2(PalmSize.x * 0.5f, PalmSize.y * 0.5f)
                    .magnitude;
                var fingers = new Vector2(
                    FingerSize.x * 0.5f,
                    FingerOffsetY + FingerSize.y * 0.5f).magnitude;
                return Mathf.Max(palm, fingers);
            }
        }

        public bool Validate(out string error)
        {
            if (ContractId != KernelConstants.GoalkeeperGloveHandlingContractId ||
                GeometryId != KernelConstants.GoalkeeperPalmGeometryId)
            {
                error = "Glove handling contract or geometry ID is invalid.";
                return false;
            }

            if (!KernelMath.IsFinite(PalmSize) ||
                PalmSize.x <= 0f || PalmSize.y <= 0f || PalmSize.z <= 0f ||
                !KernelMath.IsFinite(FingerSize) ||
                FingerSize.x <= 0f || FingerSize.y <= 0f || FingerSize.z <= 0f ||
                !KernelMath.IsFinite(FingerOffsetY) || FingerOffsetY < 0f ||
                !KernelMath.IsFinite(MaximumRadialExtent) ||
                MaximumRadialExtent <= 0f ||
                CompoundRadialExtent > MaximumRadialExtent + 1e-5f ||
                !KernelMath.IsFinite(CentralPalmExtent) ||
                CentralPalmExtent <= 0f || CentralPalmExtent > 1f ||
                !KernelMath.IsFinite(MinimumCatchAlignment) ||
                MinimumCatchAlignment < 0f || MinimumCatchAlignment > 1f ||
                !KernelMath.IsFinite(OneHandCatchMaximumSpeed) ||
                OneHandCatchMaximumSpeed <= 0f ||
                !KernelMath.IsFinite(TwoHandCatchMaximumSpeed) ||
                TwoHandCatchMaximumSpeed < OneHandCatchMaximumSpeed ||
                !KernelMath.IsFinite(TwoHandCaptureRadius) ||
                TwoHandCaptureRadius <= 0f ||
                !KernelMath.IsFinite(TwoHandMaximumSeparation) ||
                TwoHandMaximumSeparation <= 0f ||
                !KernelMath.IsFinite(CatchPossessionDuration) ||
                CatchPossessionDuration <= 0f ||
                !KernelMath.IsFinite(MinimumParryAlignment) ||
                MinimumParryAlignment < 0f || MinimumParryAlignment > 1f ||
                !KernelMath.IsFinite(ParryRestitution) ||
                !KernelMath.IsFinite(TangentialRetention) ||
                !KernelMath.IsFinite(GloveVelocityTransfer) ||
                !KernelMath.IsFinite(PunchMinimumForwardSpeed) ||
                PunchMinimumForwardSpeed < 0f ||
                !KernelMath.IsFinite(MaximumOutgoingEnergyRatio) ||
                MaximumOutgoingEnergyRatio <= 0f ||
                MaximumOutgoingEnergyRatio > 1f)
            {
                error = "keeper-glove-handling-v1 configuration is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
