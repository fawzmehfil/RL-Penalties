using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public readonly struct GoalkeeperMotorTimingEstimateV1
    {
        public GoalkeeperMotorTimingEstimateV1(
            Vector3 aimTargetLocal,
            Vector3 rootTargetLocal,
            float direction,
            float lateralFraction,
            float heightFraction,
            float diveDuration,
            float rootTargetSaturationDistance,
            float fullReachTime)
        {
            AimTargetLocal = aimTargetLocal;
            RootTargetLocal = rootTargetLocal;
            Direction = direction;
            LateralFraction = lateralFraction;
            HeightFraction = heightFraction;
            DiveDuration = diveDuration;
            RootTargetSaturationDistance = rootTargetSaturationDistance;
            FullReachTime = fullReachTime;
        }

        public Vector3 AimTargetLocal { get; }
        public Vector3 RootTargetLocal { get; }
        public float Direction { get; }
        public float LateralFraction { get; }
        public float HeightFraction { get; }
        public float DiveDuration { get; }
        public float RootTargetSaturationDistance { get; }
        public float FullReachTime { get; }
        public bool RootTargetSaturated => RootTargetSaturationDistance > 1e-5f;
    }

    public static class GoalkeeperMotorTimingV1
    {
        public const string ContractId = "stage6-motor-timing-v1";

        public static GoalkeeperMotorTimingEstimateV1 Estimate(
            Vector2 aim,
            Vector3 commitStartLocal,
            GoalkeeperControlMotorConfig configuration)
        {
            var target = GoalkeeperControlSpace.AimToLocal(aim.x, aim.y);
            var deltaX = target.x - commitStartLocal.x;
            var direction = Mathf.Abs(deltaX) <= configuration.CentralBlockThreshold
                ? 0f
                : Mathf.Sign(deltaX);
            var armAllowance =
                (configuration.UpperArmLength + configuration.ForearmLength) *
                configuration.ArmAllowanceForBodyTarget;
            var desiredRootX = direction == 0f
                ? commitStartLocal.x
                : target.x - direction * armAllowance;
            var unclampedRootX = desiredRootX;
            desiredRootX = Mathf.Clamp(
                desiredRootX,
                commitStartLocal.x - configuration.MaximumDiveLateralDisplacement,
                commitStartLocal.x + configuration.MaximumDiveLateralDisplacement);
            desiredRootX = Mathf.Clamp(
                desiredRootX,
                -configuration.LateralLimit,
                configuration.LateralLimit);
            var desiredRootY = Mathf.Clamp(
                target.y -
                configuration.ShoulderHeight -
                (configuration.UpperArmLength + configuration.ForearmLength) * 0.62f,
                0f,
                configuration.MaximumDiveRootHeight);
            var rootTarget = new Vector3(
                desiredRootX,
                desiredRootY,
                configuration.StandingZ);
            var lateralFraction = Mathf.Clamp01(
                Mathf.Abs(rootTarget.x - commitStartLocal.x) /
                configuration.MaximumDiveLateralDisplacement);
            var heightFraction = configuration.MaximumDiveRootHeight <= 1e-6f
                ? 0f
                : Mathf.Clamp01(
                    rootTarget.y / configuration.MaximumDiveRootHeight);
            var difficulty = Mathf.Max(lateralFraction, heightFraction);
            var diveDuration = Mathf.Lerp(
                configuration.MinimumDiveDuration,
                configuration.MaximumDiveDuration,
                difficulty);
            var fullReachTime =
                configuration.PlantDuration +
                configuration.FullReachNormalized * diveDuration;
            return new GoalkeeperMotorTimingEstimateV1(
                target,
                rootTarget,
                direction,
                lateralFraction,
                heightFraction,
                diveDuration,
                Mathf.Abs(unclampedRootX - desiredRootX),
                fullReachTime);
        }

        public static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }
    }
}
