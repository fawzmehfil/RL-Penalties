using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GoalkeeperGloveHandlingV1 : MonoBehaviour, IAttemptResettable
    {
        [SerializeField] private GoalkeeperGloveHandlingConfigV1 configuration;
        [SerializeField] private GoalkeeperMotorV1 motor;
        [SerializeField] private Rigidbody ball;
        [SerializeField] private GoalkeeperGloveGeometryV1 geometry;
        [SerializeField] private bool handlingEnabled;

        private GloveHandlingDecisionV1 decision;
        private Transform possessionAnchor;
        private Vector3 possessionPalmNormalLocal;
        private float possessionDistance;
        private Vector3 appliedImpulseWorld;
        private float possessionTime;
        private float normalizedContactExtent;
        private float twoHandSeparation;
        private bool contactProcessed;
        private bool hasPossession;
        private long attemptId;

        public GoalkeeperGloveHandlingConfigV1 Configuration => configuration;
        public bool HandlingEnabled => handlingEnabled;
        public bool ContactProcessed => contactProcessed;
        public bool HasPossession => hasPossession;
        public bool PossessionComplete => hasPossession &&
            configuration != null &&
            possessionTime >= configuration.CatchPossessionDuration;
        public float PossessionTime => possessionTime;
        public float NormalizedContactExtent => normalizedContactExtent;
        public float TwoHandSeparation => twoHandSeparation;
        public Vector3 AppliedImpulseWorld => appliedImpulseWorld;
        public GloveHandlingDecisionV1 Decision => decision;

        public void Configure(
            GoalkeeperGloveHandlingConfigV1 handlingConfiguration,
            GoalkeeperMotorV1 goalkeeperMotor,
            Rigidbody ballBody,
            bool enabledByDefault = false)
        {
            configuration = handlingConfiguration;
            motor = goalkeeperMotor;
            ball = ballBody;
            geometry = GetComponent<GoalkeeperGloveGeometryV1>();
            if (geometry == null)
            {
                geometry = gameObject.AddComponent<GoalkeeperGloveGeometryV1>();
            }
            geometry.Configure(configuration, motor == null ? null : motor.ArmRig);
            SetHandlingEnabled(enabledByDefault);
        }

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponentInChildren<GoalkeeperMotorV1>(true);
            }
            if (geometry == null)
            {
                geometry = GetComponent<GoalkeeperGloveGeometryV1>();
            }
            if (geometry != null)
            {
                geometry.Configure(configuration, motor == null ? null : motor.ArmRig);
                geometry.SetHandlingEnabled(handlingEnabled);
            }
        }

        public void SetHandlingEnabled(bool enabled)
        {
            handlingEnabled = enabled;
            if (geometry != null)
            {
                geometry.SetHandlingEnabled(enabled);
            }
            if (!enabled)
            {
                ReleasePossession();
            }
        }

        public void ProcessContact(BallContactEventV1 contact)
        {
            if (!handlingEnabled || contactProcessed ||
                configuration == null || motor == null || ball == null ||
                contact.Kind != ContactKind.Goalkeeper ||
                contact.GloveSurface == null ||
                (contact.GoalkeeperPart != GoalkeeperContactPart.LeftGlove &&
                 contact.GoalkeeperPart != GoalkeeperContactPart.RightGlove))
            {
                return;
            }

            var surface = contact.GloveSurface;
            var point = contact.Kinematics.PointWorld;
            normalizedContactExtent = surface.NormalizedContactExtent(point);
            var left = motor.ArmRig == null ? null : motor.ArmRig.LeftGlove;
            var right = motor.ArmRig == null ? null : motor.ArmRig.RightGlove;
            twoHandSeparation = left == null || right == null
                ? float.PositiveInfinity
                : Vector3.Distance(left.position, right.position);
            var twoHandCandidate = left != null && right != null &&
                twoHandSeparation <= configuration.TwoHandMaximumSeparation &&
                Vector3.Distance(point, left.position) <=
                    configuration.TwoHandCaptureRadius &&
                Vector3.Distance(point, right.position) <=
                    configuration.TwoHandCaptureRadius;
            var gloveVelocity = contact.GoalkeeperPart ==
                GoalkeeperContactPart.LeftGlove
                ? motor.LeftGloveWorldVelocity
                : motor.RightGloveWorldVelocity;
            decision = GoalkeeperGloveHandlingPolicyV1.Resolve(
                new GloveHandlingInputV1(
                    surface.Region,
                    contact.Kinematics.BallVelocityWorld,
                    gloveVelocity,
                    surface.PalmNormalWorld,
                    normalizedContactExtent,
                    twoHandCandidate),
                configuration);
            contactProcessed = true;

            if (decision.Outcome == GloveHandlingOutcomeV1.Catch)
            {
                BeginPossession(surface.transform, surface.PalmNormalWorld);
                return;
            }

            if (decision.Outcome == GloveHandlingOutcomeV1.Parry ||
                decision.Outcome == GloveHandlingOutcomeV1.Punch ||
                decision.Outcome == GloveHandlingOutcomeV1.WeakDeflection)
            {
                appliedImpulseWorld = ball.mass *
                    (decision.OutgoingBallVelocity -
                     contact.Kinematics.BallVelocityWorld);
                ball.isKinematic = false;
                ball.linearVelocity = decision.OutgoingBallVelocity;
                ball.angularVelocity *= 0.45f;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!hasPossession || ball == null || possessionAnchor == null)
            {
                return;
            }
            possessionTime += Mathf.Max(0f, deltaTime);
            ball.position = possessionAnchor.position +
                possessionAnchor.TransformDirection(possessionPalmNormalLocal)
                    .normalized * possessionDistance;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            decision = default;
            possessionAnchor = null;
            possessionPalmNormalLocal = Vector3.forward;
            possessionDistance = 0f;
            appliedImpulseWorld = Vector3.zero;
            possessionTime = 0f;
            normalizedContactExtent = 0f;
            twoHandSeparation = -1f;
            contactProcessed = false;
            hasPossession = false;
            if (geometry != null)
            {
                geometry.SetHandlingEnabled(handlingEnabled);
            }
        }

        public bool ValidateReset(out string error)
        {
            if (contactProcessed || hasPossession || possessionTime > 1e-6f)
            {
                error = $"Glove handling state leaked into attempt {attemptId}.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void BeginPossession(Transform anchor, Vector3 palmNormal)
        {
            possessionAnchor = anchor;
            possessionPalmNormalLocal =
                anchor.InverseTransformDirection(palmNormal.normalized);
            possessionDistance =
                KernelConstants.BallRadius + configuration.PalmSize.z * 0.5f;
            possessionTime = 0f;
            hasPossession = true;
            appliedImpulseWorld = -ball.mass * ball.linearVelocity;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            ball.isKinematic = true;
            ball.position = possessionAnchor.position +
                possessionAnchor.TransformDirection(possessionPalmNormalLocal)
                    .normalized * possessionDistance;
        }

        private void ReleasePossession()
        {
            possessionAnchor = null;
            possessionTime = 0f;
            hasPossession = false;
        }
    }
}
