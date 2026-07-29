using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "GoalkeeperControlMotorProfile",
        menuName = "Penalty Shootout/Stage 5/Goalkeeper Control Motor Profile")]
    public sealed class GoalkeeperControlMotorConfig : ScriptableObject
    {
        public string MotorProfileId = KernelConstants.GoalkeeperControlMotorProfileId;

        [Header("Ground movement")]
        public float StandingZ = 0.30f;
        public float LateralLimit = 3.10f;
        public float MaximumMoveSpeed = 4.25f;
        public float MoveAcceleration = 20f;
        public float MoveDeceleration = 24f;
        public float MaximumGroundLeanDegrees = 9f;

        [Header("Save commitment")]
        public float PlantDuration = 0.12f;
        public float MinimumDiveDuration = 0.48f;
        public float MaximumDiveDuration = 0.78f;
        public float RecoveryDuration = 0.42f;
        public float MaximumDiveLateralDisplacement = 2.55f;
        public float MaximumDiveRootHeight = 0.72f;
        public float DiveArcHeight = 0.38f;
        public float MaximumBodyRollDegrees = 82f;
        public float CentralBlockThreshold = 0.28f;
        public float ArmAllowanceForBodyTarget = 0.68f;

        [Header("Mid-air reach")]
        [Range(0f, 1f)]
        public float ReachStartNormalized = 0.05f;

        [Range(0f, 1f)]
        public float FullReachNormalized = 0.42f;

        public float MaximumAimCorrection = 0.35f;
        public float MaximumGloveTargetSpeed = 8f;
        public float GloveSeparation = 0.16f;
        public float TrailingGloveDrop = 0.05f;

        [Header("Articulated arm geometry")]
        public float ShoulderLateral = 0.25f;
        public float ShoulderHeight = 1.30f;
        public float ShoulderForward = 0f;
        public float UpperArmLength = 0.40f;
        public float ForearmLength = 0.43f;
        public float ArmRadius = 0.085f;
        public float GloveRadius = 0.11f;
        public float ReadyGloveLateral = 0.43f;
        public float ReadyGloveHeight = 1.02f;
        public float ReadyGloveForward = 0.28f;
        public float ElbowPoleForward = 0.15f;
        public float ElbowPoleDown = 0.04f;
        public float ElbowPoleOutward = 0.35f;

        public bool Validate(out string error)
        {
            if (MotorProfileId != KernelConstants.GoalkeeperControlMotorProfileId)
            {
                error =
                    $"Motor profile ID must be {KernelConstants.GoalkeeperControlMotorProfileId}.";
                return false;
            }

            if (LateralLimit <= 0f ||
                MaximumMoveSpeed <= 0f ||
                MoveAcceleration <= 0f ||
                MoveDeceleration <= 0f ||
                PlantDuration <= 0f ||
                MinimumDiveDuration <= 0f ||
                MaximumDiveDuration < MinimumDiveDuration ||
                RecoveryDuration <= 0f)
            {
                error = "Stage 5 movement speeds and durations must be positive and ordered.";
                return false;
            }

            if (MaximumDiveLateralDisplacement <= 0f ||
                MaximumDiveRootHeight < 0f ||
                DiveArcHeight < 0f ||
                MaximumBodyRollDegrees <= 0f ||
                MaximumBodyRollDegrees > 90f ||
                CentralBlockThreshold < 0f ||
                ArmAllowanceForBodyTarget < 0f)
            {
                error = "Stage 5 dive geometry is invalid.";
                return false;
            }

            if (ReachStartNormalized < 0f ||
                FullReachNormalized <= ReachStartNormalized ||
                FullReachNormalized > 1f ||
                MaximumAimCorrection < 0f ||
                MaximumGloveTargetSpeed <= 0f ||
                GloveSeparation < 0f ||
                TrailingGloveDrop < 0f)
            {
                error = "Stage 5 reach timing or steering configuration is invalid.";
                return false;
            }

            if (ShoulderLateral <= 0f ||
                ShoulderHeight <= 0f ||
                UpperArmLength <= 0f ||
                ForearmLength <= 0f ||
                ArmRadius <= 0f ||
                GloveRadius <= 0f ||
                ReadyGloveLateral <= 0f ||
                ReadyGloveHeight <= 0f ||
                ElbowPoleForward <= 0f)
            {
                error = "Stage 5 articulated arm geometry must use positive dimensions.";
                return false;
            }

            var readyDistance = Vector3.Distance(
                new Vector3(
                    ShoulderLateral,
                    ShoulderHeight,
                    ShoulderForward),
                new Vector3(
                    ReadyGloveLateral,
                    ReadyGloveHeight,
                    ReadyGloveForward));
            if (readyDistance >= UpperArmLength + ForearmLength)
            {
                error = "Ready glove pose exceeds the articulated arm reach.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
