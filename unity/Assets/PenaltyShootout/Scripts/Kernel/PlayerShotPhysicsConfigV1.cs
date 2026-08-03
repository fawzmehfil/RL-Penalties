using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [CreateAssetMenu(
        fileName = "PlayerShotPhysicsV1",
        menuName = "Penalty Shootout/Stage 6/Player Shot Physics V1")]
    public sealed class PlayerShotPhysicsConfigV1 : ScriptableObject
    {
        public string PhysicsId = KernelConstants.PlayerShotPhysicsId;
        public float FixedTimestep = 0.02f;
        public float MaximumSideSpin = 28f;
        public float MaximumVerticalSpin = 18f;
        public float MagnusCoefficient = 0.009f;
        public float MaximumMagnusAcceleration = 5f;
        public float SpinDecay = 0.15f;
        public float MinimumFlightTime = 0.40f;
        public float MaximumFlightTime = 0.78f;
        public int SolverIterations = 8;
        public float SolverTargetTolerance = 0.03f;
        public float MaximumAcceptedSolverError = 0.08f;
        public float MaximumCurveDisplacement = 0.75f;

        public bool Validate(out string error)
        {
            if (PhysicsId != KernelConstants.PlayerShotPhysicsId)
            {
                error = $"Physics ID must be {KernelConstants.PlayerShotPhysicsId}.";
                return false;
            }

            if (!KernelMath.IsFinite(FixedTimestep) || FixedTimestep <= 0f ||
                !KernelMath.IsFinite(MaximumSideSpin) || MaximumSideSpin < 0f ||
                !KernelMath.IsFinite(MaximumVerticalSpin) || MaximumVerticalSpin < 0f ||
                !KernelMath.IsFinite(MagnusCoefficient) || MagnusCoefficient < 0f ||
                !KernelMath.IsFinite(MaximumMagnusAcceleration) || MaximumMagnusAcceleration <= 0f ||
                !KernelMath.IsFinite(SpinDecay) || SpinDecay < 0f ||
                !KernelMath.IsFinite(MinimumFlightTime) || MinimumFlightTime <= 0f ||
                !KernelMath.IsFinite(MaximumFlightTime) || MaximumFlightTime < MinimumFlightTime ||
                SolverIterations <= 0 ||
                !KernelMath.IsFinite(SolverTargetTolerance) || SolverTargetTolerance <= 0f ||
                !KernelMath.IsFinite(MaximumAcceptedSolverError) ||
                MaximumAcceptedSolverError < SolverTargetTolerance ||
                !KernelMath.IsFinite(MaximumCurveDisplacement) || MaximumCurveDisplacement <= 0f)
            {
                error = "football-flight-v1 configuration is invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
