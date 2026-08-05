using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    [DisallowMultipleComponent]
    public sealed class GoalkeeperGloveHandlingV1 : MonoBehaviour, IAttemptResettable
    {
        [SerializeField] private GoalkeeperGloveHandlingConfigV1 configuration;
        [SerializeField] private GoalkeeperGloveHandlingConfigV2 configurationV2;
        [SerializeField] private GoalkeeperMotorV1 motor;
        [SerializeField] private Rigidbody ball;
        [SerializeField] private GoalkeeperGloveGeometryV1 geometry;
        [SerializeField] private bool handlingEnabled;
        [SerializeField] private int handlingVersion = 1;
        [SerializeField] private GoalkeeperGloveCalibrationProfileV2 profileV2 =
            GoalkeeperGloveCalibrationProfileV2.Balanced;

        private readonly List<GloveContactCandidateV2> candidateBuffer =
            new List<GloveContactCandidateV2>(8);
        private GloveHandlingDecisionV1 decision;
        private GloveHandlingDecisionV2 decisionV2;
        private Transform possessionAnchor;
        private Vector3 possessionPalmNormalLocal;
        private Vector3 possessionStartWorld;
        private float possessionDistance;
        private Vector3 appliedImpulseWorld;
        private Vector3 relativeImpactVelocityWorld;
        private Vector3 reconstructedIncomingVelocityWorld;
        private float possessionTime;
        private float normalizedContactExtent;
        private float twoHandSeparation;
        private GloveContactRegionV1 initialContactRegionV2;
        private int candidateContactCount;
        private int controlledResponseCount;
        private bool contactProcessed;
        private bool hasPossession;
        private bool twoHandPossession;
        private long attemptId;

        public GoalkeeperGloveHandlingConfigV1 Configuration => configuration;
        public GoalkeeperGloveHandlingConfigV2 ConfigurationV2 => configurationV2;
        public bool HandlingEnabled => handlingVersion > 0;
        public int HandlingVersion => handlingVersion;
        public string HandlingContractId => handlingVersion == 2
            ? KernelConstants.GoalkeeperGloveHandlingV2ContractId
            : handlingVersion == 1
                ? KernelConstants.GoalkeeperGloveHandlingContractId
                : KernelConstants.GoalkeeperLegacyGloveHandlingId;
        public GoalkeeperGloveCalibrationProfileV2 ProfileV2 => profileV2;
        public string ProfileId => handlingVersion == 2
            ? GoalkeeperGloveCalibrationProfilesV2.Get(profileV2).Id
            : string.Empty;
        public bool ContactProcessed => contactProcessed;
        public bool HasPossession => hasPossession;
        public bool PossessionComplete => hasPossession &&
            possessionTime >= PossessionDuration;
        public float PossessionTime => possessionTime;
        public float NormalizedContactExtent => normalizedContactExtent;
        public float TwoHandSeparation => twoHandSeparation;
        public Vector3 AppliedImpulseWorld => appliedImpulseWorld;
        public Vector3 RelativeImpactVelocityWorld => relativeImpactVelocityWorld;
        public Vector3 ReconstructedIncomingVelocityWorld =>
            reconstructedIncomingVelocityWorld;
        public static Vector3 ReconstructIncomingBallVelocity(
            Vector3 relativeImpactVelocity,
            Vector3 gloveVelocity)
        {
            return relativeImpactVelocity + gloveVelocity;
        }
        public int CandidateContactCount => candidateContactCount;
        public GloveContactRegionV1 InitialContactRegionV2 =>
            initialContactRegionV2;
        public int ControlledResponseCount => controlledResponseCount;
        public int PossessionHandCount => hasPossession
            ? twoHandPossession ? 2 : 1
            : decisionV2.Outcome == GloveHandlingOutcomeV1.Catch
                ? decisionV2.TwoHandCandidate ? 2 : 1
                : 0;
        public GloveHandlingDecisionV1 Decision => decision;
        public GloveHandlingDecisionV2 DecisionV2 => decisionV2;

        private float PossessionDuration => handlingVersion == 2 &&
            configurationV2 != null
                ? configurationV2.CatchPossessionDuration
                : configuration == null ? 0f : configuration.CatchPossessionDuration;

        public void Configure(
            GoalkeeperGloveHandlingConfigV1 handlingConfiguration,
            GoalkeeperMotorV1 goalkeeperMotor,
            Rigidbody ballBody,
            bool enabledByDefault = false)
        {
            Configure(
                handlingConfiguration,
                configurationV2,
                goalkeeperMotor,
                ballBody,
                enabledByDefault ? 1 : 0);
        }

        public void Configure(
            GoalkeeperGloveHandlingConfigV1 handlingConfiguration,
            GoalkeeperGloveHandlingConfigV2 handlingConfigurationV2,
            GoalkeeperMotorV1 goalkeeperMotor,
            Rigidbody ballBody,
            int defaultVersion)
        {
            configuration = handlingConfiguration;
            configurationV2 = handlingConfigurationV2;
            motor = goalkeeperMotor;
            ball = ballBody;
            geometry = GetComponent<GoalkeeperGloveGeometryV1>();
            if (geometry == null)
            {
                geometry = gameObject.AddComponent<GoalkeeperGloveGeometryV1>();
            }
            geometry.Configure(configuration, motor == null ? null : motor.ArmRig);
            SetHandlingVersion(defaultVersion);
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
            if (!handlingEnabled)
            {
                handlingVersion = 0;
            }
            else if (handlingVersion < 1 || handlingVersion > 2)
            {
                handlingVersion = 1;
            }
            if (geometry != null)
            {
                geometry.Configure(configuration, motor == null ? null : motor.ArmRig);
                geometry.SetHandlingEnabled(handlingVersion > 0);
            }
        }

        public void SetHandlingEnabled(bool enabled)
        {
            SetHandlingVersion(enabled ? 1 : 0);
        }

        public void SetHandlingVersion(int version)
        {
            handlingVersion = Mathf.Clamp(version, 0, 2);
            handlingEnabled = handlingVersion > 0;
            if (geometry != null)
            {
                geometry.SetHandlingEnabled(handlingEnabled);
            }
            if (!handlingEnabled)
            {
                ReleasePossession();
            }
        }

        public void SetV2Profile(int profile)
        {
            if (GoalkeeperGloveCalibrationProfilesV2.IsDefined(profile))
            {
                profileV2 = (GoalkeeperGloveCalibrationProfileV2)profile;
            }
        }

        // Frozen v1 path: event order and policy inputs intentionally remain unchanged.
        public void ProcessContact(BallContactEventV1 contact)
        {
            if (handlingVersion != 1 || contactProcessed ||
                configuration == null || motor == null || ball == null ||
                contact.Kind != ContactKind.Goalkeeper ||
                contact.GloveSurface == null ||
                !IsGlove(contact.GoalkeeperPart))
            {
                return;
            }

            var surface = contact.GloveSurface;
            var point = contact.Kinematics.PointWorld;
            normalizedContactExtent = surface.NormalizedContactExtent(point);
            MeasureTwoHand(point, out var twoHandCandidate);
            var gloveVelocity = GloveVelocity(contact.GoalkeeperPart);
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
                BeginPossession(surface.transform, surface.PalmNormalWorld, false);
                return;
            }

            if (IsControlled(decision.Outcome))
            {
                ApplyRedirect(
                    decision.OutgoingBallVelocity,
                    contact.Kinematics.BallVelocityWorld);
            }
        }

        public void ProcessContactsV2(IReadOnlyList<BallContactEventV1> contacts)
        {
            if (handlingVersion != 2 || contactProcessed ||
                configurationV2 == null || motor == null || ball == null ||
                contacts == null || contacts.Count == 0)
            {
                return;
            }

            candidateBuffer.Clear();
            for (var index = 0; index < contacts.Count; index++)
            {
                var contact = contacts[index];
                if (contact.Kind != ContactKind.Goalkeeper ||
                    contact.GloveSurface == null ||
                    !IsGlove(contact.GoalkeeperPart))
                {
                    continue;
                }

                var surface = contact.GloveSurface;
                var gloveVelocity = GloveVelocity(contact.GoalkeeperPart);
                // Collision.relativeVelocity is captured at impact; adding the
                // measured kinematic glove velocity reconstructs ball velocity.
                var incoming = ReconstructIncomingBallVelocity(
                    contact.Kinematics.RelativeVelocityWorld,
                    gloveVelocity);
                var point = contact.Kinematics.PointWorld;
                MeasureTwoHand(point, out var twoHandCandidate);
                var origin = motor.ArenaOrigin;
                var localBall = origin == null
                    ? ball.position
                    : origin.InverseTransformPoint(ball.position);
                var localIncoming = origin == null
                    ? incoming
                    : origin.InverseTransformDirection(incoming);
                candidateBuffer.Add(new GloveContactCandidateV2(
                    contact,
                    incoming,
                    gloveVelocity,
                    surface.PalmNormalWorld,
                    surface.NormalizedContactExtent(point),
                    Vector3.Distance(ball.position, surface.transform.position),
                    twoHandCandidate,
                    twoHandSeparation,
                    localIncoming.z < -1e-4f,
                    localBall.z <= -KernelConstants.BallRadius,
                    !motor.CanCommit));
            }

            if (candidateBuffer.Count == 0)
            {
                return;
            }

            candidateContactCount = candidateBuffer.Count;
            initialContactRegionV2 = candidateBuffer[0].SurfaceRegion;
            var thresholds = GoalkeeperGloveCalibrationProfilesV2.Get(profileV2);
            var selectedIndex = GoalkeeperGloveHandlingPolicyV2.SelectCandidate(
                candidateBuffer,
                thresholds);
            var selected = candidateBuffer[selectedIndex];
            normalizedContactExtent = selected.NormalizedContactExtent;
            twoHandSeparation = selected.TwoHandSeparation;
            relativeImpactVelocityWorld =
                selected.Contact.Kinematics.RelativeVelocityWorld;
            reconstructedIncomingVelocityWorld = selected.IncomingBallVelocity;
            decisionV2 = GoalkeeperGloveHandlingPolicyV2.Resolve(
                selected,
                thresholds,
                configurationV2);
            decision = new GloveHandlingDecisionV1(
                decisionV2.Outcome,
                decisionV2.Region,
                decisionV2.OutgoingBallVelocity,
                decisionV2.PalmAlignment,
                decisionV2.IncomingSpeed,
                decisionV2.OutgoingSpeed,
                decisionV2.EnergyRatio,
                decisionV2.TwoHandCandidate);
            contactProcessed = true;

            if (decisionV2.Outcome == GloveHandlingOutcomeV1.Catch)
            {
                controlledResponseCount = 1;
                BeginPossession(
                    selected.Contact.GloveSurface.transform,
                    selected.PalmNormal,
                    selected.TwoHandCandidate);
                return;
            }

            if (IsControlled(decisionV2.Outcome))
            {
                controlledResponseCount = 1;
                ApplyRedirect(
                    decisionV2.OutgoingBallVelocity,
                    selected.IncomingBallVelocity);
            }
        }

        public void Tick(float deltaTime)
        {
            if (!hasPossession || ball == null)
            {
                return;
            }
            possessionTime += Mathf.Max(0f, deltaTime);
            var target = PossessionTargetWorld();
            if (handlingVersion == 2 && configurationV2 != null)
            {
                var blend = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(
                        possessionTime / configurationV2.CaptureBlendDuration));
                ball.position = Vector3.Lerp(possessionStartWorld, target, blend);
            }
            else
            {
                ball.position = target;
            }
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
        }

        public void ResetForAttempt(long nextAttemptId, ulong seed)
        {
            attemptId = nextAttemptId;
            decision = default;
            decisionV2 = default;
            possessionAnchor = null;
            possessionPalmNormalLocal = Vector3.forward;
            possessionStartWorld = Vector3.zero;
            possessionDistance = 0f;
            appliedImpulseWorld = Vector3.zero;
            relativeImpactVelocityWorld = Vector3.zero;
            reconstructedIncomingVelocityWorld = Vector3.zero;
            possessionTime = 0f;
            normalizedContactExtent = 0f;
            twoHandSeparation = -1f;
            initialContactRegionV2 = GloveContactRegionV1.None;
            candidateContactCount = 0;
            controlledResponseCount = 0;
            contactProcessed = false;
            hasPossession = false;
            twoHandPossession = false;
            candidateBuffer.Clear();
            if (ball != null && ball.isKinematic)
            {
                ball.isKinematic = false;
            }
            if (geometry != null)
            {
                geometry.SetHandlingEnabled(handlingVersion > 0);
            }
        }

        public bool ValidateReset(out string error)
        {
            if (contactProcessed || hasPossession || possessionTime > 1e-6f ||
                candidateBuffer.Count != 0 || controlledResponseCount != 0)
            {
                error = $"Glove handling state leaked into attempt {attemptId}.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private void MeasureTwoHand(Vector3 point, out bool candidate)
        {
            var left = motor.ArmRig == null ? null : motor.ArmRig.LeftGlove;
            var right = motor.ArmRig == null ? null : motor.ArmRig.RightGlove;
            twoHandSeparation = left == null || right == null
                ? float.PositiveInfinity
                : Vector3.Distance(left.position, right.position);
            var radius = handlingVersion == 2 && configurationV2 != null
                ? configurationV2.TwoHandCaptureRadius
                : configuration.TwoHandCaptureRadius;
            var separation = handlingVersion == 2 && configurationV2 != null
                ? configurationV2.TwoHandMaximumSeparation
                : configuration.TwoHandMaximumSeparation;
            candidate = left != null && right != null &&
                twoHandSeparation <= separation &&
                Vector3.Distance(point, left.position) <= radius &&
                Vector3.Distance(point, right.position) <= radius;
        }

        private Vector3 GloveVelocity(GoalkeeperContactPart part)
        {
            return part == GoalkeeperContactPart.LeftGlove
                ? motor.LeftGloveWorldVelocity
                : motor.RightGloveWorldVelocity;
        }

        private static bool IsGlove(GoalkeeperContactPart part)
        {
            return part == GoalkeeperContactPart.LeftGlove ||
                part == GoalkeeperContactPart.RightGlove;
        }

        private static bool IsControlled(GloveHandlingOutcomeV1 outcome)
        {
            return outcome == GloveHandlingOutcomeV1.Parry ||
                outcome == GloveHandlingOutcomeV1.Punch ||
                outcome == GloveHandlingOutcomeV1.WeakDeflection;
        }

        private void ApplyRedirect(Vector3 outgoing, Vector3 incoming)
        {
            appliedImpulseWorld = ball.mass * (outgoing - incoming);
            ball.isKinematic = false;
            ball.linearVelocity = outgoing;
            ball.angularVelocity *= 0.45f;
        }

        private void BeginPossession(
            Transform anchor,
            Vector3 palmNormal,
            bool useTwoHands)
        {
            possessionAnchor = anchor;
            possessionPalmNormalLocal = anchor == null
                ? Vector3.forward
                : anchor.InverseTransformDirection(palmNormal.normalized);
            possessionDistance = KernelConstants.BallRadius +
                (configuration == null ? 0.0275f : configuration.PalmSize.z * 0.5f);
            possessionTime = 0f;
            possessionStartWorld = ball.position;
            hasPossession = true;
            twoHandPossession = useTwoHands;
            appliedImpulseWorld = -ball.mass * ball.linearVelocity;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            ball.isKinematic = true;
            if (handlingVersion != 2)
            {
                ball.position = PossessionTargetWorld();
            }
        }

        private Vector3 PossessionTargetWorld()
        {
            if (twoHandPossession && motor != null && motor.ArmRig != null &&
                motor.ArmRig.LeftGlove != null && motor.ArmRig.RightGlove != null)
            {
                var left = motor.ArmRig.LeftGlove;
                var right = motor.ArmRig.RightGlove;
                var midpoint = (left.position + right.position) * 0.5f;
                var normal = (left.forward + right.forward).normalized;
                if (normal.sqrMagnitude <= 1e-8f)
                {
                    normal = Vector3.forward;
                }
                return midpoint + normal * possessionDistance;
            }
            if (possessionAnchor == null)
            {
                return ball == null ? Vector3.zero : ball.position;
            }
            var normalWorld = possessionAnchor.TransformDirection(
                possessionPalmNormalLocal).normalized;
            return possessionAnchor.position + normalWorld * possessionDistance;
        }

        private void ReleasePossession()
        {
            possessionAnchor = null;
            possessionTime = 0f;
            hasPossession = false;
            twoHandPossession = false;
            if (ball != null && ball.isKinematic)
            {
                ball.isKinematic = false;
            }
        }
    }
}
