using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "EnvironmentKernelConfig",
        menuName = "Penalty Shootout/Stage 1/Environment Kernel Config")]
    public sealed class EnvironmentKernelConfig : ScriptableObject
    {
        public string EnvironmentId = KernelConstants.EnvironmentId;
        public float FixedTimestep = 0.02f;
        public int DecisionPeriodTicks = 2;
        public int ResetStabilizationTicks = 1;
        public float ReadyDuration = 0.10f;
        public float AttemptTimeout = 4f;
        public float TerminalHoldDuration = 0.30f;
        public float PostContactSafetyHorizon = 2f;
        public float RestSpeedThreshold = 0.15f;
        public float RestDwellTime = 0.25f;
        public Vector3 DangerMinimum = new Vector3(-5f, -0.25f, -2f);
        public Vector3 DangerMaximum = new Vector3(5f, 4f, 13f);

        public bool Validate(out string error)
        {
            if (EnvironmentId != KernelConstants.EnvironmentId)
            {
                error = $"Environment ID must be {KernelConstants.EnvironmentId}.";
                return false;
            }

            if (!KernelMath.IsFinite(FixedTimestep) || FixedTimestep <= 0f)
            {
                error = "Fixed timestep must be finite and positive.";
                return false;
            }

            if (DecisionPeriodTicks <= 0 || ResetStabilizationTicks < 1)
            {
                error = "Decision period and reset stabilization ticks must be positive.";
                return false;
            }

            if (AttemptTimeout <= 0f ||
                PostContactSafetyHorizon <= 0f ||
                RestSpeedThreshold < 0f ||
                RestDwellTime <= 0f)
            {
                error = "Attempt and save-resolution timing must be positive.";
                return false;
            }

            if (!KernelMath.IsFinite(DangerMinimum) ||
                !KernelMath.IsFinite(DangerMaximum) ||
                DangerMinimum.x >= DangerMaximum.x ||
                DangerMinimum.y >= DangerMaximum.y ||
                DangerMinimum.z >= DangerMaximum.z)
            {
                error = "Danger bounds are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
