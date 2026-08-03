using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GoalkeeperArmRigV1 : MonoBehaviour, IAttemptResettable
    {
        [SerializeField]
        private GoalkeeperControlMotorConfig configuration;

        [SerializeField]
        private Transform arenaOrigin;

        [SerializeField]
        private Transform leftShoulder;

        [SerializeField]
        private Transform rightShoulder;

        [SerializeField]
        private Transform leftUpperArm;

        [SerializeField]
        private Transform rightUpperArm;

        [SerializeField]
        private Transform leftForearm;

        [SerializeField]
        private Transform rightForearm;

        [SerializeField]
        private Transform leftGlove;

        [SerializeField]
        private Transform rightGlove;

        private Vector3 currentLeftTargetBodyLocal;
        private Vector3 currentRightTargetBodyLocal;
        private Vector3 currentLeftGloveWorldPosition;
        private Vector3 currentRightGloveWorldPosition;
        private Vector3 leftGloveWorldVelocity;
        private Vector3 rightGloveWorldVelocity;
        private float currentExtension;
        private bool initialized;
        private bool hasWorldPoseSample;
        private long attemptId;

        public GoalkeeperControlMotorConfig Configuration
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
        public Transform LeftUpperArm => leftUpperArm;
        public Transform RightUpperArm => rightUpperArm;
        public Transform LeftForearm => leftForearm;
        public Transform RightForearm => rightForearm;
        public float CurrentExtension => currentExtension;
        public float MaximumArmLength =>
            configuration == null
                ? 0f
                : configuration.UpperArmLength + configuration.ForearmLength;

        public Vector3 LeftGloveArenaLocal =>
            leftGlove == null ? Vector3.zero : ToArenaLocal(leftGlove.position);

        public Vector3 RightGloveArenaLocal =>
            rightGlove == null ? Vector3.zero : ToArenaLocal(rightGlove.position);

        public Vector3 LeftGloveWorldVelocity => leftGloveWorldVelocity;
        public Vector3 RightGloveWorldVelocity => rightGloveWorldVelocity;

        private void Awake()
        {
            if (arenaOrigin == null)
            {
                arenaOrigin = transform.parent;
            }

            EnsureInitialized();
        }

        public void Configure(
            GoalkeeperControlMotorConfig motorConfiguration,
            Transform origin,
            Transform leftShoulderTransform,
            Transform rightShoulderTransform,
            Transform leftUpperArmTransform,
            Transform rightUpperArmTransform,
            Transform leftForearmTransform,
            Transform rightForearmTransform,
            Transform leftGloveTransform,
            Transform rightGloveTransform)
        {
            configuration = motorConfiguration;
            arenaOrigin = origin;
            leftShoulder = leftShoulderTransform;
            rightShoulder = rightShoulderTransform;
            leftUpperArm = leftUpperArmTransform;
            rightUpperArm = rightUpperArmTransform;
            leftForearm = leftForearmTransform;
            rightForearm = rightForearmTransform;
            leftGlove = leftGloveTransform;
            rightGlove = rightGloveTransform;
            initialized = false;
            EnsureInitialized();
        }

        public void ApplyPose(
            Vector3 targetArenaLocal,
            float extension,
            Vector3 bodyWorldPosition,
            Quaternion bodyWorldRotation,
            float deltaTime)
        {
            EnsureInitialized();
            if (!initialized)
            {
                return;
            }

            currentExtension = Mathf.Clamp01(extension);
            var targetWorld = ToWorld(targetArenaLocal);
            var targetSide = targetArenaLocal.x - ToArenaLocal(bodyWorldPosition).x;
            var halfSeparation = configuration.GloveSeparation * 0.5f;
            var leftOffset = new Vector3(-halfSeparation, 0f, 0f);
            var rightOffset = new Vector3(halfSeparation, 0f, 0f);
            if (targetSide < -configuration.CentralBlockThreshold)
            {
                rightOffset.y -= configuration.TrailingGloveDrop;
            }
            else if (targetSide > configuration.CentralBlockThreshold)
            {
                leftOffset.y -= configuration.TrailingGloveDrop;
            }

            var arenaRotation =
                arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            var desiredLeftWorld =
                targetWorld + arenaRotation * leftOffset;
            var desiredRightWorld =
                targetWorld + arenaRotation * rightOffset;
            var inverseBodyRotation = Quaternion.Inverse(bodyWorldRotation);
            var desiredLeftBodyLocal =
                inverseBodyRotation * (desiredLeftWorld - bodyWorldPosition);
            var desiredRightBodyLocal =
                inverseBodyRotation * (desiredRightWorld - bodyWorldPosition);
            desiredLeftBodyLocal = Vector3.LerpUnclamped(
                ReadyGloveLocal(-1f),
                desiredLeftBodyLocal,
                currentExtension);
            desiredRightBodyLocal = Vector3.LerpUnclamped(
                ReadyGloveLocal(1f),
                desiredRightBodyLocal,
                currentExtension);

            var maximumStep = configuration.MaximumGloveTargetSpeed *
                Mathf.Max(0f, deltaTime);
            currentLeftTargetBodyLocal = Vector3.MoveTowards(
                currentLeftTargetBodyLocal,
                desiredLeftBodyLocal,
                maximumStep);
            currentRightTargetBodyLocal = Vector3.MoveTowards(
                currentRightTargetBodyLocal,
                desiredRightBodyLocal,
                maximumStep);

            SolveArm(
                leftShoulder,
                leftUpperArm,
                leftForearm,
                leftGlove,
                currentLeftTargetBodyLocal,
                -1f,
                bodyWorldRotation);
            SolveArm(
                rightShoulder,
                rightUpperArm,
                rightForearm,
                rightGlove,
                currentRightTargetBodyLocal,
                1f,
                bodyWorldRotation);
            UpdateWorldPoseKinematics(
                bodyWorldPosition,
                bodyWorldRotation,
                deltaTime);
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

            var bodyWorldRotation = transform.rotation;
            currentExtension = 0f;
            currentLeftTargetBodyLocal = ReadyGloveLocal(-1f);
            currentRightTargetBodyLocal = ReadyGloveLocal(1f);
            SolveArm(
                leftShoulder,
                leftUpperArm,
                leftForearm,
                leftGlove,
                currentLeftTargetBodyLocal,
                -1f,
                bodyWorldRotation);
            SolveArm(
                rightShoulder,
                rightUpperArm,
                rightForearm,
                rightGlove,
                currentRightTargetBodyLocal,
                1f,
                bodyWorldRotation);
            currentLeftGloveWorldPosition = leftGlove.position;
            currentRightGloveWorldPosition = rightGlove.position;
            leftGloveWorldVelocity = Vector3.zero;
            rightGloveWorldVelocity = Vector3.zero;
            hasWorldPoseSample = true;
        }

        public bool ValidateReset(out string error)
        {
            EnsureInitialized();
            if (!initialized)
            {
                error = $"Stage 5 arm rig is incomplete for attempt {attemptId}.";
                return false;
            }

            if (currentExtension > 1e-5f ||
                Vector3.Distance(leftGlove.localPosition, ReadyGloveLocal(-1f)) > 1e-4f ||
                Vector3.Distance(rightGlove.localPosition, ReadyGloveLocal(1f)) > 1e-4f)
            {
                error = $"Stage 5 arm pose leaked into attempt {attemptId}.";
                return false;
            }

            if (SegmentLength(leftUpperArm) > configuration.UpperArmLength + 1e-4f ||
                SegmentLength(rightUpperArm) > configuration.UpperArmLength + 1e-4f ||
                SegmentLength(leftForearm) > configuration.ForearmLength + 1e-4f ||
                SegmentLength(rightForearm) > configuration.ForearmLength + 1e-4f)
            {
                error = "Stage 5 arm colliders exceed their configured segment lengths.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static Vector3 SolveElbow(
            Vector3 shoulder,
            Vector3 requestedHand,
            Vector3 poleDirection,
            float upperArmLength,
            float forearmLength,
            out Vector3 clampedHand)
        {
            var offset = requestedHand - shoulder;
            var distance = offset.magnitude;
            var direction = distance > 1e-6f ? offset / distance : Vector3.right;
            var minimumReach = Mathf.Abs(upperArmLength - forearmLength) + 1e-4f;
            var maximumReach = upperArmLength + forearmLength - 1e-4f;
            var clampedDistance = Mathf.Clamp(distance, minimumReach, maximumReach);
            clampedHand = shoulder + direction * clampedDistance;

            var along =
                (upperArmLength * upperArmLength -
                 forearmLength * forearmLength +
                 clampedDistance * clampedDistance) /
                (2f * clampedDistance);
            var perpendicularLength = Mathf.Sqrt(
                Mathf.Max(0f, upperArmLength * upperArmLength - along * along));
            var perpendicular = Vector3.ProjectOnPlane(poleDirection, direction);
            if (perpendicular.sqrMagnitude <= 1e-8f)
            {
                perpendicular = Vector3.ProjectOnPlane(Vector3.forward, direction);
            }

            if (perpendicular.sqrMagnitude <= 1e-8f)
            {
                perpendicular = Vector3.ProjectOnPlane(Vector3.up, direction);
            }

            return shoulder +
                direction * along +
                perpendicular.normalized * perpendicularLength;
        }

        private void SolveArm(
            Transform shoulder,
            Transform upperArm,
            Transform forearm,
            Transform glove,
            Vector3 requestedHandBodyLocal,
            float side,
            Quaternion bodyWorldRotation)
        {
            var poleLocal = new Vector3(
                side * configuration.ElbowPoleOutward,
                -configuration.ElbowPoleDown,
                configuration.ElbowPoleForward);
            var elbowBodyLocal = SolveElbow(
                shoulder.localPosition,
                requestedHandBodyLocal,
                poleLocal,
                configuration.UpperArmLength,
                configuration.ForearmLength,
                out var handBodyLocal);
            FitSegment(upperArm, shoulder.localPosition, elbowBodyLocal);
            FitSegment(forearm, elbowBodyLocal, handBodyLocal);
            glove.localPosition = handBodyLocal;
            var arenaWorldRotation =
                arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            glove.localRotation =
                Quaternion.Inverse(bodyWorldRotation) * arenaWorldRotation;
            glove.localScale = Vector3.one * (configuration.GloveRadius * 2f);
        }

        private void FitSegment(Transform segment, Vector3 start, Vector3 end)
        {
            var direction = end - start;
            var length = Mathf.Max(direction.magnitude, configuration.ArmRadius * 2f);
            segment.localPosition = (start + end) * 0.5f;
            segment.localRotation =
                Quaternion.FromToRotation(Vector3.up, direction.normalized);
            segment.localScale = new Vector3(
                configuration.ArmRadius * 2f,
                length * 0.5f,
                configuration.ArmRadius * 2f);
        }

        private void UpdateWorldPoseKinematics(
            Vector3 bodyWorldPosition,
            Quaternion bodyWorldRotation,
            float deltaTime)
        {
            var nextLeft =
                bodyWorldPosition + bodyWorldRotation * leftGlove.localPosition;
            var nextRight =
                bodyWorldPosition + bodyWorldRotation * rightGlove.localPosition;
            if (hasWorldPoseSample && deltaTime > 1e-6f)
            {
                leftGloveWorldVelocity =
                    (nextLeft - currentLeftGloveWorldPosition) / deltaTime;
                rightGloveWorldVelocity =
                    (nextRight - currentRightGloveWorldPosition) / deltaTime;
            }
            else
            {
                leftGloveWorldVelocity = Vector3.zero;
                rightGloveWorldVelocity = Vector3.zero;
            }

            currentLeftGloveWorldPosition = nextLeft;
            currentRightGloveWorldPosition = nextRight;
            hasWorldPoseSample = true;
        }

        private Vector3 ReadyGloveLocal(float side)
        {
            return new Vector3(
                side * configuration.ReadyGloveLateral,
                configuration.ReadyGloveHeight,
                configuration.ReadyGloveForward);
        }

        private void ApplyShoulderGeometry()
        {
            leftShoulder.localPosition = new Vector3(
                -configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
            rightShoulder.localPosition = new Vector3(
                configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
        }

        private void EnsureInitialized()
        {
            if (initialized ||
                configuration == null ||
                leftShoulder == null ||
                rightShoulder == null ||
                leftUpperArm == null ||
                rightUpperArm == null ||
                leftForearm == null ||
                rightForearm == null ||
                leftGlove == null ||
                rightGlove == null)
            {
                return;
            }

            ApplyShoulderGeometry();
            initialized = true;
            ResetPose();
        }

        private Vector3 ToArenaLocal(Vector3 world)
        {
            return arenaOrigin == null ? world : arenaOrigin.InverseTransformPoint(world);
        }

        private Vector3 ToWorld(Vector3 local)
        {
            return arenaOrigin == null ? local : arenaOrigin.TransformPoint(local);
        }

        private static float SegmentLength(Transform segment)
        {
            return segment.localScale.y * 2f;
        }
    }
}
