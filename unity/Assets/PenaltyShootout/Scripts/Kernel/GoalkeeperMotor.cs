using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GoalkeeperMotor : MonoBehaviour, IAttemptResettable
    {
        [SerializeField]
        private GoalkeeperMotorConfig configuration;

        [SerializeField]
        private Transform arenaOrigin;

        [SerializeField]
        private GoalkeeperReachRig reachRig;

        private Rigidbody body;
        private GoalkeeperMotorState state;
        private GoalkeeperAction activeAction;
        private GoalkeeperAction diveAction;
        private float stateTime;
        private float lateralVelocity;
        private Vector3 diveStartLocal;
        private Vector3 diveTargetLocal;
        private Quaternion diveStartRotation;
        private Quaternion diveTargetRotation;
        private long attemptId;

        public GoalkeeperMotorConfig Configuration
        {
            get => configuration;
            set => configuration = value;
        }

        public Transform ArenaOrigin
        {
            get => arenaOrigin;
            set => arenaOrigin = value;
        }

        public GoalkeeperReachRig ReachRig
        {
            get => reachRig;
            set => reachRig = value;
        }

        public GoalkeeperMotorState State => state;
        public GoalkeeperAction ActiveAction => activeAction;
        public GoalkeeperAction DiveAction => diveAction;
        public float LateralVelocity => lateralVelocity;
        public float StateTime => stateTime;
        public Vector3 LocalPosition => ToLocal(body == null ? transform.position : body.position);

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (arenaOrigin == null)
            {
                arenaOrigin = transform.parent;
            }

            if (reachRig == null)
            {
                reachRig = GetComponent<GoalkeeperReachRig>();
            }
        }

        public GoalkeeperActionMask GetActionMask()
        {
            if (state == GoalkeeperMotorState.Diving ||
                state == GoalkeeperMotorState.Recovering)
            {
                return GoalkeeperActionMask.HoldOnly;
            }

            var mask = GoalkeeperActionMask.All;
            var localX = LocalPosition.x;
            if (localX <= -configuration.LateralLimit + 1e-3f)
            {
                mask.Disallow(GoalkeeperAction.ShuffleLeft);
                mask.Disallow(GoalkeeperAction.DiveLeftLow);
                mask.Disallow(GoalkeeperAction.DiveLeftMiddle);
                mask.Disallow(GoalkeeperAction.DiveLeftHigh);
            }

            if (localX >= configuration.LateralLimit - 1e-3f)
            {
                mask.Disallow(GoalkeeperAction.ShuffleRight);
                mask.Disallow(GoalkeeperAction.DiveRightLow);
                mask.Disallow(GoalkeeperAction.DiveRightMiddle);
                mask.Disallow(GoalkeeperAction.DiveRightHigh);
            }

            return mask;
        }

        public bool TryApplyAction(GoalkeeperAction requested)
        {
            var mask = GetActionMask();
            if (!mask.IsAllowed(requested))
            {
                activeAction = GoalkeeperAction.Hold;
                return false;
            }

            activeAction = requested;
            if (IsDive(requested) && state != GoalkeeperMotorState.Diving)
            {
                BeginDive(requested);
            }
            else if (requested == GoalkeeperAction.ShuffleLeft ||
                requested == GoalkeeperAction.ShuffleRight)
            {
                state = GoalkeeperMotorState.Shuffling;
            }
            else if (state == GoalkeeperMotorState.Shuffling)
            {
                state = GoalkeeperMotorState.Ready;
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if (configuration == null || body == null)
            {
                return;
            }

            switch (state)
            {
                case GoalkeeperMotorState.Ready:
                case GoalkeeperMotorState.Shuffling:
                    TickShuffle(deltaTime);
                    break;
                case GoalkeeperMotorState.Diving:
                    TickDive(deltaTime);
                    break;
                case GoalkeeperMotorState.Recovering:
                    TickRecovery(deltaTime);
                    break;
            }
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            state = GoalkeeperMotorState.Ready;
            activeAction = GoalkeeperAction.Hold;
            diveAction = GoalkeeperAction.Hold;
            stateTime = 0f;
            lateralVelocity = 0f;
            var local = new Vector3(0f, 0f, configuration == null ? 0.30f : configuration.StandingZ);
            var world = ToWorld(local);
            var rotation = arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            body.position = world;
            body.rotation = rotation;
            transform.SetPositionAndRotation(world, rotation);
            reachRig?.ResetForAttempt(nextAttemptId, seed);
            Physics.SyncTransforms();
        }

        public bool ValidateReset(out string error)
        {
            if (state != GoalkeeperMotorState.Ready ||
                activeAction != GoalkeeperAction.Hold ||
                Mathf.Abs(lateralVelocity) > 1e-5f ||
                Mathf.Abs(LocalPosition.x) > 1e-4f ||
                Mathf.Abs(LocalPosition.y) > 1e-4f)
            {
                error = $"Goalkeeper motor did not reset for attempt {attemptId}.";
                return false;
            }

            if (reachRig != null && !reachRig.ValidateReset(out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void TickShuffle(float deltaTime)
        {
            var desiredVelocity = 0f;
            if (activeAction == GoalkeeperAction.ShuffleLeft)
            {
                desiredVelocity = -configuration.MaximumShuffleSpeed;
            }
            else if (activeAction == GoalkeeperAction.ShuffleRight)
            {
                desiredVelocity = configuration.MaximumShuffleSpeed;
            }

            var acceleration = Mathf.Abs(desiredVelocity) > 0f
                ? configuration.ShuffleAcceleration
                : configuration.ShuffleDeceleration;
            lateralVelocity = Mathf.MoveTowards(
                lateralVelocity,
                desiredVelocity,
                acceleration * deltaTime);

            var local = LocalPosition;
            local.x = Mathf.Clamp(
                local.x + lateralVelocity * deltaTime,
                -configuration.LateralLimit,
                configuration.LateralLimit);
            local.y = 0f;
            local.z = configuration.StandingZ;
            body.MovePosition(ToWorld(local));
            body.MoveRotation(arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation);
        }

        private void BeginDive(GoalkeeperAction action)
        {
            state = GoalkeeperMotorState.Diving;
            stateTime = 0f;
            lateralVelocity = 0f;
            diveAction = action;
            diveStartLocal = LocalPosition;
            var direction = IsLeft(action) ? -1f : 1f;
            var reach = GetDiveReach(action);
            var height = GetDiveHeight(action);
            diveTargetLocal = new Vector3(
                Mathf.Clamp(
                    diveStartLocal.x + direction * reach,
                    -configuration.LateralLimit,
                    configuration.LateralLimit),
                height,
                configuration.StandingZ);
            diveStartRotation = body.rotation;
            var localRoll = Quaternion.Euler(
                0f,
                0f,
                -direction * configuration.MaximumBodyRollDegrees);
            diveTargetRotation =
                (arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation) * localRoll;
        }

        private void TickDive(float deltaTime)
        {
            stateTime += deltaTime;
            var normalized = Mathf.Clamp01(stateTime / configuration.DiveDuration);
            var displacement = SmoothStep(normalized);
            var local = Vector3.LerpUnclamped(diveStartLocal, diveTargetLocal, displacement);
            var arc = 4f * normalized * (1f - normalized);
            local.y += arc * GetDiveHeight(diveAction) * 0.25f;
            var plannedWorldPosition = ToWorld(local);
            var plannedWorldRotation = Quaternion.SlerpUnclamped(
                diveStartRotation,
                diveTargetRotation,
                displacement);
            body.MovePosition(plannedWorldPosition);
            body.MoveRotation(plannedWorldRotation);
            reachRig?.ApplyDivePose(
                diveAction,
                normalized,
                local,
                plannedWorldPosition,
                plannedWorldRotation);

            if (normalized >= 1f)
            {
                state = GoalkeeperMotorState.Recovering;
                stateTime = 0f;
            }
        }

        private void TickRecovery(float deltaTime)
        {
            stateTime += deltaTime;
            var normalized = Mathf.Clamp01(stateTime / configuration.RecoveryDuration);
            var local = LocalPosition;
            local.y = Mathf.Lerp(local.y, 0f, SmoothStep(normalized));
            local.z = configuration.StandingZ;
            var standingRotation = arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            var plannedWorldPosition = ToWorld(local);
            var plannedWorldRotation = Quaternion.Slerp(
                body.rotation,
                standingRotation,
                SmoothStep(normalized));
            body.MovePosition(plannedWorldPosition);
            body.MoveRotation(plannedWorldRotation);
            reachRig?.ApplyRecoveryPose(
                diveAction,
                normalized,
                local,
                plannedWorldPosition,
                plannedWorldRotation);

            if (normalized >= 1f)
            {
                state = GoalkeeperMotorState.Ready;
                stateTime = 0f;
                activeAction = GoalkeeperAction.Hold;
                diveAction = GoalkeeperAction.Hold;
                local.y = 0f;
                body.position = ToWorld(local);
                body.rotation = standingRotation;
                reachRig?.ResetPose();
            }
        }

        private float GetDiveReach(GoalkeeperAction action)
        {
            switch (action)
            {
                case GoalkeeperAction.DiveLeftLow:
                case GoalkeeperAction.DiveRightLow:
                    return configuration.LowDiveReach;
                case GoalkeeperAction.DiveLeftMiddle:
                case GoalkeeperAction.DiveRightMiddle:
                    return configuration.MiddleDiveReach;
                default:
                    return configuration.HighDiveReach;
            }
        }

        private float GetDiveHeight(GoalkeeperAction action)
        {
            switch (action)
            {
                case GoalkeeperAction.DiveLeftLow:
                case GoalkeeperAction.DiveRightLow:
                    return configuration.LowDiveHeight;
                case GoalkeeperAction.DiveLeftMiddle:
                case GoalkeeperAction.DiveRightMiddle:
                    return configuration.MiddleDiveHeight;
                default:
                    return configuration.HighDiveHeight;
            }
        }

        private static bool IsDive(GoalkeeperAction action)
        {
            return action >= GoalkeeperAction.DiveLeftLow;
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

        private Vector3 ToLocal(Vector3 world)
        {
            return arenaOrigin == null ? world : arenaOrigin.InverseTransformPoint(world);
        }

        private Vector3 ToWorld(Vector3 local)
        {
            return arenaOrigin == null ? local : arenaOrigin.TransformPoint(local);
        }
    }
}
