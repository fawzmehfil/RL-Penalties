using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GoalkeeperMotorV1 : MonoBehaviour, IAttemptResettable
    {
        [SerializeField]
        private GoalkeeperControlMotorConfig configuration;

        [SerializeField]
        private Transform arenaOrigin;

        [SerializeField]
        private GoalkeeperArmRigV1 armRig;

        [SerializeField]
        private Transform torso;

        [SerializeField]
        private Transform head;

        [SerializeField]
        private Transform leftLeg;

        [SerializeField]
        private Transform rightLeg;

        private Rigidbody body;
        private GoalkeeperControlMotorState state;
        private GoalkeeperControlCommand activeCommand;
        private float stateTime;
        private float stateDuration;
        private float lateralVelocity;
        private Vector3 rootVelocity;
        private Vector3 currentLocal;
        private Vector3 commitStartLocal;
        private Vector3 diveTargetLocal;
        private Vector3 recoveryStartLocal;
        private Quaternion commitStartRotation;
        private Quaternion diveTargetRotation;
        private Quaternion recoveryStartRotation;
        private float recoveryStartReachExtension;
        private Vector2 latchedAim;
        private Vector2 currentReachAim;
        private float bodyRollDegrees;
        private float currentReachExtension;
        private float totalRootDistance;
        private float peakRootSpeed;
        private float peakReachExtension;
        private int commandClampCount;
        private int targetClampCount;
        private long attemptId;

        public GoalkeeperControlMotorConfig Configuration
        {
            get => configuration;
            set => configuration = value;
        }

        public Transform ArenaOrigin
        {
            get => arenaOrigin;
            set => arenaOrigin = value;
        }

        public GoalkeeperArmRigV1 ArmRig
        {
            get => armRig;
            set => armRig = value;
        }

        public GoalkeeperControlMotorState State => state;
        public GoalkeeperControlCommand ActiveCommand => activeCommand;
        public float StateTime => stateTime;
        public float StateProgress =>
            stateDuration <= 1e-6f ? 0f : Mathf.Clamp01(stateTime / stateDuration);
        public float LateralVelocity => lateralVelocity;
        public Vector3 RootVelocity => rootVelocity;
        public Vector3 LocalPosition => currentLocal;
        public float BodyRollNormalized =>
            configuration == null || configuration.MaximumBodyRollDegrees <= 0f
                ? 0f
                : Mathf.Clamp(
                    bodyRollDegrees / configuration.MaximumBodyRollDegrees,
                    -1f,
                    1f);
        public Vector2 LatchedAim => latchedAim;
        public Vector2 CurrentReachAim => currentReachAim;
        public float CurrentReachExtension => currentReachExtension;
        public bool CanCommit =>
            state == GoalkeeperControlMotorState.Ready ||
            state == GoalkeeperControlMotorState.Moving;
        public float TotalRootDistance => totalRootDistance;
        public float PeakRootSpeed => peakRootSpeed;
        public float PeakReachExtension => peakReachExtension;
        public int CommandClampCount => commandClampCount;
        public int TargetClampCount => targetClampCount;
        public Vector3 LeftGloveArenaLocal =>
            armRig == null ? Vector3.zero : armRig.LeftGloveArenaLocal;
        public Vector3 RightGloveArenaLocal =>
            armRig == null ? Vector3.zero : armRig.RightGloveArenaLocal;

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

            if (armRig == null)
            {
                armRig = GetComponent<GoalkeeperArmRigV1>();
            }

            ResolveBodyParts();
        }

        public GoalkeeperControlActionMask GetActionMask()
        {
            return new GoalkeeperControlActionMask(CanCommit);
        }

        public bool TryApplyCommand(GoalkeeperControlCommand requested)
        {
            var sanitized = requested.Sanitized(out var clamped);
            if (clamped)
            {
                commandClampCount++;
            }

            if (sanitized.Commit && !CanCommit)
            {
                sanitized.Commit = false;
                sanitized.Reach = Mathf.Max(
                    activeCommand.Reach,
                    sanitized.Reach);
                activeCommand = sanitized;
                return false;
            }

            if (!CanCommit)
            {
                sanitized.Reach = Mathf.Max(
                    activeCommand.Reach,
                    sanitized.Reach);
            }

            activeCommand = sanitized;
            if (sanitized.Commit)
            {
                BeginCommit(sanitized);
            }
            else if (CanCommit)
            {
                state = Mathf.Abs(sanitized.MoveX) > 0.02f
                    ? GoalkeeperControlMotorState.Moving
                    : GoalkeeperControlMotorState.Ready;
                stateDuration = 0f;
            }

            return true;
        }

        public void Tick(float deltaTime)
        {
            if (configuration == null || body == null || deltaTime <= 0f)
            {
                return;
            }

            switch (state)
            {
                case GoalkeeperControlMotorState.Ready:
                case GoalkeeperControlMotorState.Moving:
                    TickGrounded(deltaTime);
                    break;
                case GoalkeeperControlMotorState.Planting:
                    TickPlanting(deltaTime);
                    break;
                case GoalkeeperControlMotorState.Diving:
                    TickDive(deltaTime);
                    break;
                case GoalkeeperControlMotorState.Recovering:
                    TickRecovery(deltaTime);
                    break;
            }
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            state = GoalkeeperControlMotorState.Ready;
            activeCommand = GoalkeeperControlCommand.Neutral;
            stateTime = 0f;
            stateDuration = 0f;
            lateralVelocity = 0f;
            rootVelocity = Vector3.zero;
            currentLocal = new Vector3(
                0f,
                0f,
                configuration == null ? 0.30f : configuration.StandingZ);
            commitStartLocal = currentLocal;
            diveTargetLocal = currentLocal;
            recoveryStartLocal = currentLocal;
            latchedAim = Vector2.zero;
            currentReachAim = Vector2.zero;
            bodyRollDegrees = 0f;
            currentReachExtension = 0f;
            recoveryStartReachExtension = 0f;
            totalRootDistance = 0f;
            peakRootSpeed = 0f;
            peakReachExtension = 0f;
            commandClampCount = 0;
            targetClampCount = 0;
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            var rotation = arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;
            var world = ToWorld(currentLocal);
            body.position = world;
            body.rotation = rotation;
            transform.SetPositionAndRotation(world, rotation);
            ApplyReadyBodyGeometry();
            armRig?.ResetForAttempt(nextAttemptId, seed);
            Physics.SyncTransforms();
        }

        public bool ValidateReset(out string error)
        {
            if (state != GoalkeeperControlMotorState.Ready ||
                activeCommand.Commit ||
                Mathf.Abs(lateralVelocity) > 1e-5f ||
                rootVelocity.sqrMagnitude > 1e-8f ||
                Mathf.Abs(currentLocal.x) > 1e-4f ||
                Mathf.Abs(currentLocal.y) > 1e-4f ||
                currentReachExtension > 1e-5f)
            {
                error = $"Stage 5 motor did not reset for attempt {attemptId}.";
                return false;
            }

            if (armRig != null && !armRig.ValidateReset(out error))
            {
                return false;
            }

            if (!ValidateReadyBodyGeometry(out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void ResolveBodyParts()
        {
            if (torso == null)
            {
                torso = transform.Find("Torso");
            }

            if (head == null)
            {
                head = transform.Find("Head");
            }

            if (leftLeg == null)
            {
                leftLeg = transform.Find("LeftLeg");
            }

            if (rightLeg == null)
            {
                rightLeg = transform.Find("RightLeg");
            }
        }

        private void ApplyReadyBodyGeometry()
        {
            if (configuration == null)
            {
                return;
            }

            ResolveBodyParts();
            if (torso != null)
            {
                torso.localPosition = new Vector3(
                    0f,
                    configuration.TorsoCenterHeight,
                    configuration.TorsoForward);
                torso.localRotation = Quaternion.Euler(
                    configuration.TorsoForwardLeanDegrees,
                    0f,
                    0f);
                torso.localScale = configuration.TorsoScale;
            }

            if (head != null)
            {
                head.localPosition = new Vector3(
                    0f,
                    configuration.HeadCenterHeight,
                    configuration.HeadForward);
                head.localRotation = Quaternion.identity;
                head.localScale =
                    Vector3.one * configuration.HeadDiameter;
            }

            ApplyLegGeometry(leftLeg, -1f);
            ApplyLegGeometry(rightLeg, 1f);
        }

        private void ApplyLegGeometry(Transform leg, float side)
        {
            if (leg == null)
            {
                return;
            }

            leg.localPosition = new Vector3(
                side * configuration.LegLateral,
                configuration.LegCenterHeight,
                configuration.LegForward);
            leg.localRotation = Quaternion.Euler(
                configuration.LegForwardLeanDegrees,
                0f,
                side * configuration.LegSplayDegrees);
            leg.localScale = configuration.LegScale;
        }

        private bool ValidateReadyBodyGeometry(out string error)
        {
            ResolveBodyParts();
            if (configuration == null ||
                torso == null ||
                head == null ||
                leftLeg == null ||
                rightLeg == null)
            {
                error = "Stage 5 ready body geometry is incomplete.";
                return false;
            }

            var tolerance = 1e-4f;
            if (Vector3.Distance(
                    torso.localPosition,
                    new Vector3(
                        0f,
                        configuration.TorsoCenterHeight,
                        configuration.TorsoForward)) > tolerance ||
                Vector3.Distance(
                    head.localPosition,
                    new Vector3(
                        0f,
                        configuration.HeadCenterHeight,
                        configuration.HeadForward)) > tolerance ||
                Vector3.Distance(
                    leftLeg.localPosition,
                    new Vector3(
                        -configuration.LegLateral,
                        configuration.LegCenterHeight,
                        configuration.LegForward)) > tolerance ||
                Vector3.Distance(
                    rightLeg.localPosition,
                    new Vector3(
                        configuration.LegLateral,
                        configuration.LegCenterHeight,
                        configuration.LegForward)) > tolerance ||
                Vector3.Distance(
                    torso.localScale,
                    configuration.TorsoScale) > tolerance ||
                Vector3.Distance(
                    head.localScale,
                    Vector3.one * configuration.HeadDiameter) > tolerance ||
                Vector3.Distance(
                    leftLeg.localScale,
                    configuration.LegScale) > tolerance ||
                Vector3.Distance(
                    rightLeg.localScale,
                    configuration.LegScale) > tolerance ||
                Quaternion.Angle(
                    torso.localRotation,
                    Quaternion.Euler(
                        configuration.TorsoForwardLeanDegrees,
                        0f,
                        0f)) > tolerance ||
                Quaternion.Angle(
                    leftLeg.localRotation,
                    Quaternion.Euler(
                        configuration.LegForwardLeanDegrees,
                        0f,
                        -configuration.LegSplayDegrees)) > tolerance ||
                Quaternion.Angle(
                    rightLeg.localRotation,
                    Quaternion.Euler(
                        configuration.LegForwardLeanDegrees,
                        0f,
                        configuration.LegSplayDegrees)) > tolerance)
            {
                error = "Stage 5 ready body pose leaked between attempts.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void TickGrounded(float deltaTime)
        {
            stateTime += deltaTime;
            var desiredVelocity = activeCommand.MoveX * configuration.MaximumMoveSpeed;
            var acceleration = Mathf.Abs(desiredVelocity) > Mathf.Abs(lateralVelocity)
                ? configuration.MoveAcceleration
                : configuration.MoveDeceleration;
            lateralVelocity = Mathf.MoveTowards(
                lateralVelocity,
                desiredVelocity,
                acceleration * deltaTime);
            var next = currentLocal;
            next.x = Mathf.Clamp(
                next.x + lateralVelocity * deltaTime,
                -configuration.LateralLimit,
                configuration.LateralLimit);
            if ((Mathf.Approximately(next.x, -configuration.LateralLimit) &&
                 lateralVelocity < 0f) ||
                (Mathf.Approximately(next.x, configuration.LateralLimit) &&
                 lateralVelocity > 0f))
            {
                lateralVelocity = 0f;
            }

            next.y = 0f;
            next.z = configuration.StandingZ;
            var lean = -Mathf.Clamp(
                lateralVelocity / configuration.MaximumMoveSpeed,
                -1f,
                1f) * configuration.MaximumGroundLeanDegrees;
            var rotation = ArenaRotation * Quaternion.Euler(0f, 0f, lean);
            bodyRollDegrees = lean;
            MoveBody(next, rotation, deltaTime);
            currentReachExtension = 0f;
            var aimLocal = GoalkeeperControlSpace.AimToLocal(
                activeCommand.AimX,
                activeCommand.AimY);
            armRig?.ApplyPose(
                new Vector3(aimLocal.x, aimLocal.y, configuration.StandingZ),
                0f,
                ToWorld(currentLocal),
                rotation,
                deltaTime);
        }

        private void BeginCommit(GoalkeeperControlCommand command)
        {
            state = GoalkeeperControlMotorState.Planting;
            stateTime = 0f;
            stateDuration = configuration.PlantDuration;
            lateralVelocity = 0f;
            commitStartLocal = currentLocal;
            commitStartRotation = body.rotation;
            latchedAim = new Vector2(command.AimX, command.AimY);
            currentReachAim = latchedAim;
            var target = GoalkeeperControlSpace.AimToLocal(command.AimX, command.AimY);
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
            if (!Mathf.Approximately(unclampedRootX, desiredRootX))
            {
                targetClampCount++;
            }

            diveTargetLocal = new Vector3(
                desiredRootX,
                desiredRootY,
                configuration.StandingZ);
            var lateralFraction = Mathf.Clamp01(
                Mathf.Abs(diveTargetLocal.x - commitStartLocal.x) /
                configuration.MaximumDiveLateralDisplacement);
            var heightFraction = configuration.MaximumDiveRootHeight <= 1e-6f
                ? 0f
                : Mathf.Clamp01(diveTargetLocal.y / configuration.MaximumDiveRootHeight);
            stateDuration = configuration.PlantDuration;
            var difficulty = Mathf.Max(lateralFraction, heightFraction);
            var diveDuration = Mathf.Lerp(
                configuration.MinimumDiveDuration,
                configuration.MaximumDiveDuration,
                difficulty);
            diveTargetRotation =
                ArenaRotation *
                Quaternion.Euler(
                    0f,
                    0f,
                    -direction * configuration.MaximumBodyRollDegrees * lateralFraction);
            recoveryStartRotation = diveTargetRotation;
            recoveryStartLocal = diveTargetLocal;
            pendingDiveDuration = diveDuration;
        }

        private float pendingDiveDuration;

        private void TickPlanting(float deltaTime)
        {
            stateTime += deltaTime;
            var normalized = Mathf.Clamp01(stateTime / configuration.PlantDuration);
            var direction = Mathf.Sign(diveTargetLocal.x - commitStartLocal.x);
            var plantRoll =
                -direction *
                configuration.MaximumBodyRollDegrees *
                0.12f *
                SmoothStep(normalized);
            var rotation = ArenaRotation * Quaternion.Euler(0f, 0f, plantRoll);
            bodyRollDegrees = plantRoll;
            MoveBody(commitStartLocal, rotation, deltaTime);
            var extension =
                activeCommand.Reach01 *
                configuration.PlantReachFraction *
                SmoothStep(normalized);
            ApplyReach(extension, rotation, deltaTime, false);
            if (normalized >= 1f)
            {
                state = GoalkeeperControlMotorState.Diving;
                stateTime = 0f;
                stateDuration = pendingDiveDuration;
            }
        }

        private void TickDive(float deltaTime)
        {
            stateTime += deltaTime;
            var normalized = Mathf.Clamp01(stateTime / stateDuration);
            var displacement = SmoothStep(normalized);
            var next = Vector3.LerpUnclamped(
                commitStartLocal,
                diveTargetLocal,
                displacement);
            var heightFactor = configuration.MaximumDiveRootHeight <= 1e-6f
                ? 0f
                : Mathf.Clamp01(diveTargetLocal.y / configuration.MaximumDiveRootHeight);
            next.y +=
                Mathf.Sin(normalized * Mathf.PI) *
                configuration.DiveArcHeight *
                Mathf.Lerp(0.35f, 1f, heightFactor);
            var rotation = Quaternion.SlerpUnclamped(
                commitStartRotation,
                diveTargetRotation,
                displacement);
            bodyRollDegrees = SignedArenaRoll(rotation);
            MoveBody(next, rotation, deltaTime);

            var reachEnvelope = ReachEnvelope(normalized);
            ApplyReach(activeCommand.Reach01 * reachEnvelope, rotation, deltaTime, true);
            if (normalized >= 1f)
            {
                state = GoalkeeperControlMotorState.Recovering;
                stateTime = 0f;
                stateDuration = configuration.RecoveryDuration;
                recoveryStartLocal = currentLocal;
                recoveryStartRotation = rotation;
                recoveryStartReachExtension = currentReachExtension;
            }
        }

        private void TickRecovery(float deltaTime)
        {
            stateTime += deltaTime;
            var normalized = Mathf.Clamp01(stateTime / configuration.RecoveryDuration);
            var displacement = SmoothStep(normalized);
            var target = new Vector3(
                recoveryStartLocal.x,
                0f,
                configuration.StandingZ);
            var next = Vector3.LerpUnclamped(recoveryStartLocal, target, displacement);
            var rotation = Quaternion.SlerpUnclamped(
                recoveryStartRotation,
                ArenaRotation,
                displacement);
            bodyRollDegrees = SignedArenaRoll(rotation);
            MoveBody(next, rotation, deltaTime);
            ApplyReach(
                recoveryStartReachExtension * (1f - displacement),
                rotation,
                deltaTime,
                false);
            if (normalized >= 1f)
            {
                state = GoalkeeperControlMotorState.Ready;
                stateTime = 0f;
                stateDuration = 0f;
                activeCommand = GoalkeeperControlCommand.Neutral;
                lateralVelocity = 0f;
                rootVelocity = Vector3.zero;
                bodyRollDegrees = 0f;
                currentReachExtension = 0f;
                armRig?.ResetPose();
            }
        }

        private void ApplyReach(
            float extension,
            Quaternion bodyRotation,
            float deltaTime,
            bool allowCorrection)
        {
            var requested = new Vector2(activeCommand.AimX, activeCommand.AimY);
            if (allowCorrection)
            {
                var latchedLocal = GoalkeeperControlSpace.AimToLocal(
                    latchedAim.x,
                    latchedAim.y);
                var requestedLocal = GoalkeeperControlSpace.AimToLocal(
                    requested.x,
                    requested.y);
                requestedLocal.x = Mathf.Clamp(
                    requestedLocal.x,
                    latchedLocal.x - configuration.MaximumAimCorrection,
                    latchedLocal.x + configuration.MaximumAimCorrection);
                requestedLocal.y = Mathf.Clamp(
                    requestedLocal.y,
                    latchedLocal.y - configuration.MaximumAimCorrection,
                    latchedLocal.y + configuration.MaximumAimCorrection);
                currentReachAim = GoalkeeperControlSpace.LocalToAim(requestedLocal);
            }
            else
            {
                currentReachAim = latchedAim;
            }

            var target = GoalkeeperControlSpace.AimToLocal(
                currentReachAim.x,
                currentReachAim.y);
            currentReachExtension = Mathf.Clamp01(extension);
            peakReachExtension = Mathf.Max(
                peakReachExtension,
                currentReachExtension);
            armRig?.ApplyPose(
                new Vector3(target.x, target.y, configuration.StandingZ),
                currentReachExtension,
                ToWorld(currentLocal),
                bodyRotation,
                deltaTime);
        }

        private void MoveBody(
            Vector3 nextLocal,
            Quaternion nextRotation,
            float deltaTime)
        {
            var previous = currentLocal;
            currentLocal = nextLocal;
            rootVelocity = deltaTime <= 1e-6f
                ? Vector3.zero
                : (currentLocal - previous) / deltaTime;
            totalRootDistance += Vector3.Distance(previous, currentLocal);
            peakRootSpeed = Mathf.Max(peakRootSpeed, rootVelocity.magnitude);
            body.MovePosition(ToWorld(currentLocal));
            body.MoveRotation(nextRotation);
        }

        private float ReachEnvelope(float normalizedDivePhase)
        {
            if (normalizedDivePhase <= configuration.ReachStartNormalized)
            {
                return configuration.PlantReachFraction;
            }

            if (normalizedDivePhase >= configuration.FullReachNormalized)
            {
                return 1f;
            }

            var progress = SmoothStep(
                Mathf.InverseLerp(
                    configuration.ReachStartNormalized,
                    configuration.FullReachNormalized,
                    normalizedDivePhase));
            return Mathf.Lerp(
                configuration.PlantReachFraction,
                1f,
                progress);
        }

        private float SignedArenaRoll(Quaternion worldRotation)
        {
            var relative = Quaternion.Inverse(ArenaRotation) * worldRotation;
            var roll = relative.eulerAngles.z;
            return roll > 180f ? roll - 360f : roll;
        }

        private Quaternion ArenaRotation =>
            arenaOrigin == null ? Quaternion.identity : arenaOrigin.rotation;

        private Vector3 ToWorld(Vector3 local)
        {
            return arenaOrigin == null ? local : arenaOrigin.TransformPoint(local);
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }
    }
}
