using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "OnTargetShotDistribution",
        menuName = "Penalty Shootout/Stage 1/Shot Distribution")]
    public sealed class ShotDistributionConfig : ScriptableObject
    {
        public string ScenarioSuiteId = KernelConstants.ScenarioSuiteId;
        public float MinimumFlightTime = 0.38f;
        public float MaximumFlightTime = 0.85f;
        public float MinimumLaunchDelay = 0.15f;
        public float MaximumLaunchDelay = 0.45f;
        public float MinimumTargetXNormalized = -1f;
        public float MaximumTargetXNormalized = 1f;
        public float MinimumTargetYNormalized = 0f;
        public float MaximumTargetYNormalized = 1f;
        [Range(0f, 1f)]
        public float ReachFocusProbability;
        [Range(0f, 1f)]
        public float ReachFocusMinimumAbsoluteXNormalized = 0.45f;
        [Range(0f, 1f)]
        public float ReachFocusMaximumAbsoluteXNormalized = 0.95f;
        [Range(0f, 1f)]
        public float ReachFocusMinimumYNormalized = 0.55f;
        [Range(0f, 1f)]
        public float ReachFocusMaximumYNormalized = 0.98f;
        public bool ReachFocusBalancedHeightBands;
        public float AdditionalFrameClearance = 0.08f;
        public bool SpinEnabled;
        public bool CurveEnabled;
        public bool AimNoiseEnabled;
        public bool PowerNoiseEnabled;

        public bool Validate(out string error)
        {
            if (ScenarioSuiteId != KernelConstants.ScenarioSuiteId)
            {
                error = $"Scenario suite ID must be {KernelConstants.ScenarioSuiteId}.";
                return false;
            }

            if (!KernelMath.IsFinite(MinimumFlightTime) ||
                !KernelMath.IsFinite(MaximumFlightTime) ||
                MinimumFlightTime <= 0f ||
                MinimumFlightTime > MaximumFlightTime)
            {
                error = "Flight-time range is invalid.";
                return false;
            }

            if (!KernelMath.IsFinite(MinimumLaunchDelay) ||
                !KernelMath.IsFinite(MaximumLaunchDelay) ||
                MinimumLaunchDelay < 0f ||
                MinimumLaunchDelay > MaximumLaunchDelay)
            {
                error = "Launch-delay range is invalid.";
                return false;
            }

            if (!KernelMath.IsFinite(MinimumTargetXNormalized) ||
                !KernelMath.IsFinite(MaximumTargetXNormalized) ||
                !KernelMath.IsFinite(MinimumTargetYNormalized) ||
                !KernelMath.IsFinite(MaximumTargetYNormalized) ||
                MinimumTargetXNormalized < -1f ||
                MaximumTargetXNormalized > 1f ||
                MinimumTargetXNormalized > MaximumTargetXNormalized ||
                MinimumTargetYNormalized < 0f ||
                MaximumTargetYNormalized > 1f ||
                MinimumTargetYNormalized > MaximumTargetYNormalized)
            {
                error = "Normalized target ranges are invalid.";
                return false;
            }

            if (!KernelMath.IsFinite(AdditionalFrameClearance) ||
                AdditionalFrameClearance < 0f ||
                AdditionalFrameClearance >=
                KernelConstants.GoalHalfWidth - KernelConstants.BallRadius)
            {
                error = "Frame clearance is invalid.";
                return false;
            }

            if (!KernelMath.IsFinite(ReachFocusProbability) ||
                !KernelMath.IsFinite(ReachFocusMinimumAbsoluteXNormalized) ||
                !KernelMath.IsFinite(ReachFocusMaximumAbsoluteXNormalized) ||
                !KernelMath.IsFinite(ReachFocusMinimumYNormalized) ||
                !KernelMath.IsFinite(ReachFocusMaximumYNormalized) ||
                ReachFocusProbability < 0f ||
                ReachFocusProbability > 1f ||
                ReachFocusMinimumAbsoluteXNormalized < 0f ||
                ReachFocusMaximumAbsoluteXNormalized > 1f ||
                ReachFocusMinimumAbsoluteXNormalized >
                    ReachFocusMaximumAbsoluteXNormalized ||
                ReachFocusMinimumYNormalized < 0f ||
                ReachFocusMaximumYNormalized > 1f ||
                ReachFocusMinimumYNormalized >
                    ReachFocusMaximumYNormalized)
            {
                error = "Reach-focus target settings are invalid.";
                return false;
            }

            if (SpinEnabled || CurveEnabled || AimNoiseEnabled || PowerNoiseEnabled)
            {
                error =
                    "Stage 1 on-target-v0 requires spin, curve, aim noise, and power noise to be disabled.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
