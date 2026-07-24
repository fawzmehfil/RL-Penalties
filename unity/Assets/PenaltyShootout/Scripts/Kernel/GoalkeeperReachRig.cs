using System;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GoalkeeperReachRig : MonoBehaviour, IAttemptResettable
    {
        [SerializeField]
        private GoalkeeperMotorConfig configuration;

        [SerializeField]
        private Transform arenaOrigin;

        [SerializeField]
        private Transform leftShoulder;

        [SerializeField]
        private Transform rightShoulder;

        [SerializeField]
        private Transform leftArm;

        [SerializeField]
        private Transform rightArm;

        [SerializeField]
        private Transform leftGlove;

        [SerializeField]
        private Transform rightGlove;

        private TransformSnapshot readyLeftArm;
        private TransformSnapshot readyRightArm;
        private TransformSnapshot readyLeftGlove;
        private TransformSnapshot readyRightGlove;
        private bool initialized;
        private long attemptId;

        public GoalkeeperMotorConfig Configuration
        {
            get => configuration;
            set
            {
                configuration = value;
                initialized = false;
            }
        }

        public Transform ArenaOrigin
        {
            get => arenaOrigin;
            set => arenaOrigin = value;
        }

        public Transform LeftGlove => leftGlove;
        public Transform RightGlove => rightGlove;
        public Transform LeftArm => leftArm;
        public Transform RightArm => rightArm;

        private void Awake()
        {
            if (arenaOrigin == null)
            {
                arenaOrigin = transform.parent;
            }

            EnsureInitialized();
        }

        public void Configure(
            GoalkeeperMotorConfig motorConfiguration,
            Transform origin,
            Transform leftShoulderTransform,
            Transform rightShoulderTransform,
            Transform leftArmTransform,
            Transform rightArmTransform,
            Transform leftGloveTransform,
            Transform rightGloveTransform)
        {
            configuration = motorConfiguration;
            arenaOrigin = origin;
            leftShoulder = leftShoulderTransform;
            rightShoulder = rightShoulderTransform;
            leftArm = leftArmTransform;
            rightArm = rightArmTransform;
            leftGlove = leftGloveTransform;
            rightGlove = rightGloveTransform;
            initialized = false;
            EnsureInitialized();
        }

        public void ApplyDivePose(
            GoalkeeperAction action,
            float normalizedDivePhase,
            Vector3 bodyArenaLocalPosition,
            Vector3 plannedBodyWorldPosition,
            Quaternion plannedBodyWorldRotation)
        {
            EnsureInitialized();
            var extension = EvaluateDiveExtension(configuration, normalizedDivePhase);
            ApplyExtension(
                action,
                extension,
                bodyArenaLocalPosition,
                plannedBodyWorldPosition,
                plannedBodyWorldRotation);
        }

        public void ApplyRecoveryPose(
            GoalkeeperAction action,
            float normalizedRecoveryPhase,
            Vector3 bodyArenaLocalPosition,
            Vector3 plannedBodyWorldPosition,
            Quaternion plannedBodyWorldRotation)
        {
            EnsureInitialized();
            var extension = 1f - SmoothStep(Mathf.Clamp01(normalizedRecoveryPhase));
            ApplyExtension(
                action,
                extension,
                bodyArenaLocalPosition,
                plannedBodyWorldPosition,
                plannedBodyWorldRotation);
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            ResetPose();
        }

        public void ResetPose()
        {
            EnsureInitialized();
            if (!initialized)
            {
                return;
            }

            readyLeftArm.Apply(leftArm);
            readyRightArm.Apply(rightArm);
            readyLeftGlove.Apply(leftGlove);
            readyRightGlove.Apply(rightGlove);
        }

        public bool ValidateReset(out string error)
        {
            EnsureInitialized();
            if (!initialized)
            {
                error = $"Goalkeeper reach rig is incomplete for attempt {attemptId}.";
                return false;
            }

            if (!readyLeftArm.Matches(leftArm) ||
                !readyRightArm.Matches(rightArm) ||
                !readyLeftGlove.Matches(leftGlove) ||
                !readyRightGlove.Matches(rightGlove))
            {
                error = $"Goalkeeper hand pose leaked into attempt {attemptId}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static float EvaluateDiveExtension(
            GoalkeeperMotorConfig motorConfiguration,
            float normalizedDivePhase)
        {
            if (motorConfiguration == null)
            {
                return 0f;
            }

            var phase = Mathf.Clamp01(normalizedDivePhase);
            if (phase <= motorConfiguration.ReachStartNormalized)
            {
                return 0f;
            }

            if (phase >= motorConfiguration.FullExtensionNormalized)
            {
                return 1f;
            }

            var normalized = Mathf.InverseLerp(
                motorConfiguration.ReachStartNormalized,
                motorConfiguration.FullExtensionNormalized,
                phase);
            return SmoothStep(normalized);
        }

        public static GoalkeeperReachTargets EvaluateFullExtensionTargets(
            GoalkeeperMotorConfig motorConfiguration,
            GoalkeeperAction action,
            Vector3 bodyArenaLocalPosition)
        {
            if (motorConfiguration == null)
            {
                throw new ArgumentNullException(nameof(motorConfiguration));
            }

            if (!IsDive(action))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Hand reach targets exist only for dive actions.");
            }

            GetTierTargets(
                motorConfiguration,
                action,
                out var leadingLateral,
                out var trailingLateral,
                out var leadingHeight,
                out var trailingHeight);
            var direction = IsLeft(action) ? -1f : 1f;
            var leading = bodyArenaLocalPosition + new Vector3(
                direction * leadingLateral,
                leadingHeight,
                motorConfiguration.LeadingForwardReach);
            var trailing = bodyArenaLocalPosition + new Vector3(
                direction * trailingLateral,
                trailingHeight,
                motorConfiguration.TrailingForwardReach);

            return IsLeft(action)
                ? new GoalkeeperReachTargets(leading, trailing)
                : new GoalkeeperReachTargets(trailing, leading);
        }

        private void ApplyExtension(
            GoalkeeperAction action,
            float extension,
            Vector3 bodyArenaLocalPosition,
            Vector3 plannedBodyWorldPosition,
            Quaternion plannedBodyWorldRotation)
        {
            if (!initialized || !IsDive(action))
            {
                return;
            }

            if (extension <= 0f)
            {
                ResetPose();
                return;
            }

            var targets = EvaluateFullExtensionTargets(
                configuration,
                action,
                bodyArenaLocalPosition);
            ApplyGlove(
                leftGlove,
                readyLeftGlove,
                leftShoulder,
                targets.LeftGlove,
                extension,
                plannedBodyWorldPosition,
                plannedBodyWorldRotation);
            ApplyGlove(
                rightGlove,
                readyRightGlove,
                rightShoulder,
                targets.RightGlove,
                extension,
                plannedBodyWorldPosition,
                plannedBodyWorldRotation);
            FitArm(leftArm, leftShoulder.localPosition, leftGlove.localPosition);
            FitArm(rightArm, rightShoulder.localPosition, rightGlove.localPosition);
        }

        private void ApplyGlove(
            Transform glove,
            TransformSnapshot ready,
            Transform shoulder,
            Vector3 fullTargetArenaLocal,
            float extension,
            Vector3 plannedBodyWorldPosition,
            Quaternion plannedBodyWorldRotation)
        {
            var readyWorldPosition =
                plannedBodyWorldPosition +
                plannedBodyWorldRotation * ready.LocalPosition;
            var fullWorldPosition = arenaOrigin == null
                ? fullTargetArenaLocal
                : arenaOrigin.TransformPoint(fullTargetArenaLocal);
            var targetWorldPosition = Vector3.LerpUnclamped(
                readyWorldPosition,
                fullWorldPosition,
                extension);
            var shoulderWorldPosition =
                plannedBodyWorldPosition +
                plannedBodyWorldRotation * shoulder.localPosition;
            targetWorldPosition = ClampToArmLength(
                shoulderWorldPosition,
                targetWorldPosition);
            glove.localPosition =
                Quaternion.Inverse(plannedBodyWorldRotation) *
                (targetWorldPosition - plannedBodyWorldPosition);

            var arenaWorldRotation =
                arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            var readyWorldRotation = plannedBodyWorldRotation * ready.LocalRotation;
            var targetWorldRotation = Quaternion.SlerpUnclamped(
                readyWorldRotation,
                arenaWorldRotation,
                extension);
            glove.localRotation =
                Quaternion.Inverse(plannedBodyWorldRotation) * targetWorldRotation;
            glove.localScale = Vector3.one * (configuration.GloveRadius * 2f);
        }

        private void ApplyConfiguredReadyGeometry()
        {
            leftShoulder.localPosition = new Vector3(
                -configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
            rightShoulder.localPosition = new Vector3(
                configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
            leftGlove.localPosition = new Vector3(
                -configuration.ReadyGloveLateral,
                configuration.ReadyGloveHeight,
                configuration.ReadyGloveForward);
            rightGlove.localPosition = new Vector3(
                configuration.ReadyGloveLateral,
                configuration.ReadyGloveHeight,
                configuration.ReadyGloveForward);
            leftGlove.localRotation = Quaternion.identity;
            rightGlove.localRotation = Quaternion.identity;
            leftGlove.localScale = Vector3.one * (configuration.GloveRadius * 2f);
            rightGlove.localScale = Vector3.one * (configuration.GloveRadius * 2f);
            FitArm(leftArm, leftShoulder.localPosition, leftGlove.localPosition);
            FitArm(rightArm, rightShoulder.localPosition, rightGlove.localPosition);
        }

        private void FitArm(Transform arm, Vector3 shoulder, Vector3 glove)
        {
            var direction = glove - shoulder;
            var length = Mathf.Max(direction.magnitude, configuration.ArmRadius * 2f);
            arm.localPosition = (shoulder + glove) * 0.5f;
            arm.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
            arm.localScale = new Vector3(
                configuration.ArmRadius * 2f,
                length * 0.5f,
                configuration.ArmRadius * 2f);
        }

        private Vector3 ClampToArmLength(Vector3 shoulderWorldPosition, Vector3 targetWorldPosition)
        {
            var offset = targetWorldPosition - shoulderWorldPosition;
            var maximumLength = Mathf.Max(
                configuration.MaximumArmLength,
                configuration.GloveRadius);
            if (offset.sqrMagnitude <= maximumLength * maximumLength)
            {
                return targetWorldPosition;
            }

            return shoulderWorldPosition + offset.normalized * maximumLength;
        }

        private void EnsureInitialized()
        {
            if (initialized ||
                configuration == null ||
                leftShoulder == null ||
                rightShoulder == null ||
                leftArm == null ||
                rightArm == null ||
                leftGlove == null ||
                rightGlove == null)
            {
                return;
            }

            ApplyConfiguredReadyGeometry();
            readyLeftArm = TransformSnapshot.Capture(leftArm);
            readyRightArm = TransformSnapshot.Capture(rightArm);
            readyLeftGlove = TransformSnapshot.Capture(leftGlove);
            readyRightGlove = TransformSnapshot.Capture(rightGlove);
            initialized = true;
        }

        private static void GetTierTargets(
            GoalkeeperMotorConfig motorConfiguration,
            GoalkeeperAction action,
            out float leadingLateral,
            out float trailingLateral,
            out float leadingHeight,
            out float trailingHeight)
        {
            switch (action)
            {
                case GoalkeeperAction.DiveLeftLow:
                case GoalkeeperAction.DiveRightLow:
                    leadingLateral = motorConfiguration.LeadingLowLateralReach;
                    trailingLateral = motorConfiguration.TrailingLowLateralReach;
                    leadingHeight = motorConfiguration.LeadingLowHeight;
                    trailingHeight = motorConfiguration.TrailingLowHeight;
                    break;
                case GoalkeeperAction.DiveLeftMiddle:
                case GoalkeeperAction.DiveRightMiddle:
                    leadingLateral = motorConfiguration.LeadingMiddleLateralReach;
                    trailingLateral = motorConfiguration.TrailingMiddleLateralReach;
                    leadingHeight = motorConfiguration.LeadingMiddleHeight;
                    trailingHeight = motorConfiguration.TrailingMiddleHeight;
                    break;
                default:
                    leadingLateral = motorConfiguration.LeadingHighLateralReach;
                    trailingLateral = motorConfiguration.TrailingHighLateralReach;
                    leadingHeight = motorConfiguration.LeadingHighHeight;
                    trailingHeight = motorConfiguration.TrailingHighHeight;
                    break;
            }
        }

        private static bool IsDive(GoalkeeperAction action)
        {
            return action >= GoalkeeperAction.DiveLeftLow &&
                action <= GoalkeeperAction.DiveRightHigh;
        }

        private static bool IsLeft(GoalkeeperAction action)
        {
            return action == GoalkeeperAction.DiveLeftLow ||
                action == GoalkeeperAction.DiveLeftMiddle ||
                action == GoalkeeperAction.DiveLeftHigh;
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private readonly struct TransformSnapshot
        {
            public readonly Vector3 LocalPosition;
            public readonly Quaternion LocalRotation;
            public readonly Vector3 LocalScale;

            private TransformSnapshot(
                Vector3 localPosition,
                Quaternion localRotation,
                Vector3 localScale)
            {
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public static TransformSnapshot Capture(Transform target)
            {
                return new TransformSnapshot(
                    target.localPosition,
                    target.localRotation,
                    target.localScale);
            }

            public void Apply(Transform target)
            {
                target.localPosition = LocalPosition;
                target.localRotation = LocalRotation;
                target.localScale = LocalScale;
            }

            public bool Matches(Transform target)
            {
                return Vector3.Distance(target.localPosition, LocalPosition) <= 1e-6f &&
                    Quaternion.Angle(target.localRotation, LocalRotation) <= 1e-5f &&
                    Vector3.Distance(target.localScale, LocalScale) <= 1e-6f;
            }
        }
    }
}
