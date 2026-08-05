using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GloveContactRegionV1
    {
        None = 0,
        Palm = 1,
        Fingers = 2,
        Edge = 3,
        Back = 4,
    }

    public enum GloveHandlingOutcomeV1
    {
        None = 0,
        Catch = 1,
        Parry = 2,
        Punch = 3,
        WeakDeflection = 4,
        Uncontrolled = 5,
    }

    public readonly struct BallContactEventV1
    {
        public readonly ContactKind Kind;
        public readonly GoalkeeperContactPart GoalkeeperPart;
        public readonly ContactKinematics Kinematics;
        public readonly GloveContactSurfaceV1 GloveSurface;

        public BallContactEventV1(
            ContactKind kind,
            GoalkeeperContactPart goalkeeperPart,
            ContactKinematics kinematics,
            GloveContactSurfaceV1 gloveSurface)
        {
            Kind = kind;
            GoalkeeperPart = goalkeeperPart;
            Kinematics = kinematics;
            GloveSurface = gloveSurface;
        }
    }

    public readonly struct GloveHandlingInputV1
    {
        public readonly GloveContactRegionV1 Region;
        public readonly Vector3 IncomingBallVelocity;
        public readonly Vector3 GloveVelocity;
        public readonly Vector3 PalmNormal;
        public readonly float NormalizedContactExtent;
        public readonly bool TwoHandCandidate;

        public GloveHandlingInputV1(
            GloveContactRegionV1 region,
            Vector3 incomingBallVelocity,
            Vector3 gloveVelocity,
            Vector3 palmNormal,
            float normalizedContactExtent,
            bool twoHandCandidate)
        {
            Region = region;
            IncomingBallVelocity = incomingBallVelocity;
            GloveVelocity = gloveVelocity;
            PalmNormal = palmNormal;
            NormalizedContactExtent = normalizedContactExtent;
            TwoHandCandidate = twoHandCandidate;
        }
    }

    public readonly struct GloveHandlingDecisionV1
    {
        public readonly GloveHandlingOutcomeV1 Outcome;
        public readonly GloveContactRegionV1 Region;
        public readonly Vector3 OutgoingBallVelocity;
        public readonly float PalmAlignment;
        public readonly float IncomingSpeed;
        public readonly float OutgoingSpeed;
        public readonly float EnergyRatio;
        public readonly bool TwoHandCandidate;

        public GloveHandlingDecisionV1(
            GloveHandlingOutcomeV1 outcome,
            GloveContactRegionV1 region,
            Vector3 outgoingBallVelocity,
            float palmAlignment,
            float incomingSpeed,
            float outgoingSpeed,
            float energyRatio,
            bool twoHandCandidate)
        {
            Outcome = outcome;
            Region = region;
            OutgoingBallVelocity = outgoingBallVelocity;
            PalmAlignment = palmAlignment;
            IncomingSpeed = incomingSpeed;
            OutgoingSpeed = outgoingSpeed;
            EnergyRatio = energyRatio;
            TwoHandCandidate = twoHandCandidate;
        }
    }

    public static class GoalkeeperGloveHandlingPolicyV1
    {
        public static GloveHandlingDecisionV1 Resolve(
            GloveHandlingInputV1 input,
            GoalkeeperGloveHandlingConfigV1 configuration)
        {
            if (configuration == null ||
                !configuration.Validate(out _) ||
                !KernelMath.IsFinite(input.IncomingBallVelocity) ||
                !KernelMath.IsFinite(input.GloveVelocity) ||
                !KernelMath.IsFinite(input.PalmNormal))
            {
                return Decision(
                    GloveHandlingOutcomeV1.Uncontrolled,
                    input.Region,
                    input.IncomingBallVelocity,
                    input.IncomingBallVelocity.magnitude,
                    0f,
                    input.TwoHandCandidate);
            }

            var palmNormal = input.PalmNormal.sqrMagnitude <= 1e-8f
                ? Vector3.forward
                : input.PalmNormal.normalized;
            var incoming = input.IncomingBallVelocity;
            var incomingSpeed = incoming.magnitude;
            var alignment = incomingSpeed <= 1e-5f
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(-incoming / incomingSpeed, palmNormal));
            var region = input.Region;
            if (alignment <= 0.01f)
            {
                region = GloveContactRegionV1.Back;
            }
            else if (input.NormalizedContactExtent > configuration.CentralPalmExtent)
            {
                region = GloveContactRegionV1.Edge;
            }

            var catchSpeed = input.TwoHandCandidate
                ? configuration.TwoHandCatchMaximumSpeed
                : configuration.OneHandCatchMaximumSpeed;
            var catchRegion = region == GloveContactRegionV1.Palm ||
                (input.TwoHandCandidate && region == GloveContactRegionV1.Fingers);
            if (catchRegion &&
                alignment >= configuration.MinimumCatchAlignment &&
                incomingSpeed <= catchSpeed)
            {
                return Decision(
                    GloveHandlingOutcomeV1.Catch,
                    region,
                    Vector3.zero,
                    incomingSpeed,
                    alignment,
                    input.TwoHandCandidate);
            }

            if (region == GloveContactRegionV1.Back ||
                alignment < configuration.MinimumParryAlignment)
            {
                return Decision(
                    GloveHandlingOutcomeV1.Uncontrolled,
                    region,
                    incoming,
                    incomingSpeed,
                    alignment,
                    input.TwoHandCandidate);
            }

            var relative = incoming - input.GloveVelocity;
            var normalSpeed = Vector3.Dot(relative, palmNormal);
            var tangent = relative - palmNormal * normalSpeed;
            var redirected =
                tangent * configuration.TangentialRetention +
                palmNormal * Mathf.Max(
                    0f,
                    -normalSpeed * configuration.ParryRestitution) +
                input.GloveVelocity * configuration.GloveVelocityTransfer;
            var maximumOutgoingSpeed = incomingSpeed *
                Mathf.Sqrt(configuration.MaximumOutgoingEnergyRatio);
            if (redirected.magnitude > maximumOutgoingSpeed)
            {
                redirected = redirected.normalized * maximumOutgoingSpeed;
            }

            var punch = Vector3.Dot(input.GloveVelocity, palmNormal) >=
                configuration.PunchMinimumForwardSpeed;
            var outcome = punch
                ? GloveHandlingOutcomeV1.Punch
                : region == GloveContactRegionV1.Edge ||
                    region == GloveContactRegionV1.Fingers
                    ? GloveHandlingOutcomeV1.WeakDeflection
                    : GloveHandlingOutcomeV1.Parry;
            return Decision(
                outcome,
                region,
                redirected,
                incomingSpeed,
                alignment,
                input.TwoHandCandidate);
        }

        private static GloveHandlingDecisionV1 Decision(
            GloveHandlingOutcomeV1 outcome,
            GloveContactRegionV1 region,
            Vector3 outgoingVelocity,
            float incomingSpeed,
            float alignment,
            bool twoHandCandidate)
        {
            var outgoingSpeed = outgoingVelocity.magnitude;
            var energyRatio = incomingSpeed <= 1e-5f
                ? 0f
                : outgoingSpeed * outgoingSpeed / (incomingSpeed * incomingSpeed);
            return new GloveHandlingDecisionV1(
                outcome,
                region,
                outgoingVelocity,
                alignment,
                incomingSpeed,
                outgoingSpeed,
                energyRatio,
                twoHandCandidate);
        }
    }
}
