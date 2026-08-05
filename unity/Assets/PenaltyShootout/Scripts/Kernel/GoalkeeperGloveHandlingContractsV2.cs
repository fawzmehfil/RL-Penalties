using System;
using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Kernel
{
    public enum GloveHandlingRejectionReasonV2
    {
        None = 0,
        NonFiniteKinematics = 1,
        AcrossGoalLine = 2,
        MovingAway = 3,
        BackFace = 4,
        EdgeContact = 5,
        Misaligned = 6,
        CatchTooFast = 7,
        CaptureTooFar = 8,
        NotCommitted = 9,
        PunchTooSlow = 10,
        PunchImpactTooLow = 11,
    }

    public readonly struct GloveContactCandidateV2
    {
        public readonly BallContactEventV1 Contact;
        public readonly GloveContactRegionV1 SurfaceRegion;
        public readonly Vector3 IncomingBallVelocity;
        public readonly Vector3 GloveVelocity;
        public readonly Vector3 PalmNormal;
        public readonly float NormalizedContactExtent;
        public readonly float PalmAlignment;
        public readonly float ClosingSpeed;
        public readonly float ForwardGloveSpeed;
        public readonly float CaptureDistance;
        public readonly bool TwoHandCandidate;
        public readonly float TwoHandSeparation;
        public readonly bool Goalward;
        public readonly bool WholeBallAcrossGoalLine;
        public readonly bool Committed;

        public GloveContactCandidateV2(
            BallContactEventV1 contact,
            Vector3 incomingBallVelocity,
            Vector3 gloveVelocity,
            Vector3 palmNormal,
            float normalizedContactExtent,
            float captureDistance,
            bool twoHandCandidate,
            float twoHandSeparation,
            bool goalward,
            bool wholeBallAcrossGoalLine,
            bool committed)
        {
            Contact = contact;
            SurfaceRegion = contact.GloveSurface == null
                ? GloveContactRegionV1.None
                : contact.GloveSurface.Region;
            IncomingBallVelocity = incomingBallVelocity;
            GloveVelocity = gloveVelocity;
            PalmNormal = palmNormal.sqrMagnitude <= 1e-8f
                ? Vector3.forward
                : palmNormal.normalized;
            NormalizedContactExtent = normalizedContactExtent;
            var speed = incomingBallVelocity.magnitude;
            PalmAlignment = speed <= 1e-5f
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(
                    -incomingBallVelocity / speed,
                    PalmNormal));
            ClosingSpeed = Mathf.Max(
                0f,
                -Vector3.Dot(incomingBallVelocity - gloveVelocity, PalmNormal));
            ForwardGloveSpeed = Vector3.Dot(gloveVelocity, PalmNormal);
            CaptureDistance = captureDistance;
            TwoHandCandidate = twoHandCandidate;
            TwoHandSeparation = twoHandSeparation;
            Goalward = goalward;
            WholeBallAcrossGoalLine = wholeBallAcrossGoalLine;
            Committed = committed;
        }

        public bool IsFinite =>
            KernelMath.IsFinite(Contact.Kinematics.RelativeVelocityWorld) &&
            KernelMath.IsFinite(IncomingBallVelocity) &&
            KernelMath.IsFinite(GloveVelocity) &&
            KernelMath.IsFinite(PalmNormal) &&
            KernelMath.IsFinite(NormalizedContactExtent) &&
            KernelMath.IsFinite(PalmAlignment) &&
            KernelMath.IsFinite(ClosingSpeed) &&
            KernelMath.IsFinite(ForwardGloveSpeed) &&
            KernelMath.IsFinite(CaptureDistance);
    }

    public readonly struct GloveHandlingDecisionV2
    {
        public readonly GloveHandlingOutcomeV1 Outcome;
        public readonly GloveContactRegionV1 InitialRegion;
        public readonly GloveContactRegionV1 Region;
        public readonly GloveHandlingRejectionReasonV2 RejectionReason;
        public readonly Vector3 OutgoingBallVelocity;
        public readonly float PalmAlignment;
        public readonly float IncomingSpeed;
        public readonly float OutgoingSpeed;
        public readonly float EnergyRatio;
        public readonly float ForwardGloveSpeed;
        public readonly float CaptureDistance;
        public readonly bool CatchEligible;
        public readonly bool PunchEligible;
        public readonly bool TwoHandCandidate;

        public GloveHandlingDecisionV2(
            GloveHandlingOutcomeV1 outcome,
            GloveContactRegionV1 initialRegion,
            GloveContactRegionV1 region,
            GloveHandlingRejectionReasonV2 rejectionReason,
            Vector3 outgoingBallVelocity,
            float palmAlignment,
            float incomingSpeed,
            float forwardGloveSpeed,
            float captureDistance,
            bool catchEligible,
            bool punchEligible,
            bool twoHandCandidate)
        {
            Outcome = outcome;
            InitialRegion = initialRegion;
            Region = region;
            RejectionReason = rejectionReason;
            OutgoingBallVelocity = outgoingBallVelocity;
            PalmAlignment = palmAlignment;
            IncomingSpeed = incomingSpeed;
            OutgoingSpeed = outgoingBallVelocity.magnitude;
            EnergyRatio = incomingSpeed <= 1e-5f
                ? 0f
                : OutgoingSpeed * OutgoingSpeed / (incomingSpeed * incomingSpeed);
            ForwardGloveSpeed = forwardGloveSpeed;
            CaptureDistance = captureDistance;
            CatchEligible = catchEligible;
            PunchEligible = punchEligible;
            TwoHandCandidate = twoHandCandidate;
        }
    }

    public static class GoalkeeperGloveHandlingPolicyV2
    {
        public static int SelectCandidate(
            IReadOnlyList<GloveContactCandidateV2> candidates,
            GoalkeeperGloveThresholdsV2 thresholds)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return -1;
            }

            var best = 0;
            for (var index = 1; index < candidates.Count; index++)
            {
                if (Compare(candidates[index], candidates[best], thresholds) < 0)
                {
                    best = index;
                }
            }
            return best;
        }

        public static GloveHandlingDecisionV2 Resolve(
            GloveContactCandidateV2 input,
            GoalkeeperGloveThresholdsV2 thresholds,
            GoalkeeperGloveHandlingConfigV2 configuration)
        {
            if (configuration == null ||
                !configuration.Validate(out _) ||
                !input.IsFinite)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Uncontrolled,
                    input.SurfaceRegion,
                    GloveHandlingRejectionReasonV2.NonFiniteKinematics,
                    input.IncomingBallVelocity,
                    false,
                    false);
            }

            var region = ClassifiedRegion(input, thresholds);
            if (input.WholeBallAcrossGoalLine)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Uncontrolled,
                    region,
                    GloveHandlingRejectionReasonV2.AcrossGoalLine,
                    input.IncomingBallVelocity,
                    false,
                    false);
            }
            if (!input.Goalward)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Uncontrolled,
                    region,
                    GloveHandlingRejectionReasonV2.MovingAway,
                    input.IncomingBallVelocity,
                    false,
                    false);
            }

            var incomingSpeed = input.IncomingBallVelocity.magnitude;
            var catchRegion = region == GloveContactRegionV1.Palm ||
                (input.TwoHandCandidate && region == GloveContactRegionV1.Fingers);
            var catchSpeed = input.TwoHandCandidate
                ? thresholds.TwoHandCatchMaximumSpeed
                : thresholds.OneHandCatchMaximumSpeed;
            var catchEligible = catchRegion &&
                input.PalmAlignment >= thresholds.CatchAlignment &&
                incomingSpeed <= catchSpeed &&
                input.CaptureDistance <= configuration.MaximumCaptureDistance;
            if (catchEligible)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Catch,
                    region,
                    GloveHandlingRejectionReasonV2.None,
                    Vector3.zero,
                    true,
                    false);
            }

            var rejection = CatchRejection(
                input,
                region,
                thresholds,
                configuration,
                incomingSpeed);
            var punchRegion = region == GloveContactRegionV1.Palm ||
                region == GloveContactRegionV1.Fingers;
            var punchEligible = input.Committed &&
                punchRegion &&
                incomingSpeed >= configuration.PunchMinimumImpactSpeed &&
                input.PalmAlignment >= thresholds.PunchAlignment &&
                input.ForwardGloveSpeed >= thresholds.PunchForwardSpeed;
            if (punchEligible)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Punch,
                    region,
                    GloveHandlingRejectionReasonV2.None,
                    Redirect(input, configuration, true),
                    false,
                    true);
            }

            if (punchRegion &&
                incomingSpeed >= configuration.PunchMinimumImpactSpeed)
            {
                var punchRejection = PunchRejection(
                    input,
                    region,
                    thresholds,
                    configuration,
                    incomingSpeed);
                if (punchRejection != GloveHandlingRejectionReasonV2.None)
                {
                    rejection = punchRejection;
                }
            }

            if (region == GloveContactRegionV1.Back ||
                input.PalmAlignment < configuration.MinimumParryAlignment)
            {
                return Decision(
                    input,
                    GloveHandlingOutcomeV1.Uncontrolled,
                    region,
                    rejection,
                    input.IncomingBallVelocity,
                    false,
                    false);
            }

            var outcome = region == GloveContactRegionV1.Edge ||
                region == GloveContactRegionV1.Fingers
                ? GloveHandlingOutcomeV1.WeakDeflection
                : GloveHandlingOutcomeV1.Parry;
            return Decision(
                input,
                outcome,
                region,
                rejection,
                Redirect(input, configuration, false),
                false,
                false);
        }

        private static int Compare(
            GloveContactCandidateV2 left,
            GloveContactCandidateV2 right,
            GoalkeeperGloveThresholdsV2 thresholds)
        {
            var leftFront = left.PalmAlignment > 0.01f;
            var rightFront = right.PalmAlignment > 0.01f;
            var result = CompareDescending(leftFront, rightFront);
            if (result != 0) return result;

            result = RegionPriority(left.SurfaceRegion).CompareTo(
                RegionPriority(right.SurfaceRegion));
            if (result != 0) return result;
            result = left.NormalizedContactExtent.CompareTo(
                right.NormalizedContactExtent);
            if (result != 0) return result;
            result = right.PalmAlignment.CompareTo(left.PalmAlignment);
            if (result != 0) return result;
            result = right.ClosingSpeed.CompareTo(left.ClosingSpeed);
            if (result != 0) return result;
            return PartPriority(left.Contact.GoalkeeperPart).CompareTo(
                PartPriority(right.Contact.GoalkeeperPart));
        }

        private static int CompareDescending(bool left, bool right)
        {
            return left == right ? 0 : left ? -1 : 1;
        }

        private static int RegionPriority(GloveContactRegionV1 region)
        {
            return region == GloveContactRegionV1.Palm ? 0 :
                region == GloveContactRegionV1.Fingers ? 1 : 2;
        }

        private static int PartPriority(GoalkeeperContactPart part)
        {
            return part == GoalkeeperContactPart.LeftGlove ? 0 : 1;
        }

        private static GloveContactRegionV1 ClassifiedRegion(
            GloveContactCandidateV2 input,
            GoalkeeperGloveThresholdsV2 thresholds)
        {
            if (input.PalmAlignment <= 0.01f)
            {
                return GloveContactRegionV1.Back;
            }
            if (input.NormalizedContactExtent > thresholds.CentralExtent)
            {
                return GloveContactRegionV1.Edge;
            }
            return input.SurfaceRegion;
        }

        private static GloveHandlingRejectionReasonV2 CatchRejection(
            GloveContactCandidateV2 input,
            GloveContactRegionV1 region,
            GoalkeeperGloveThresholdsV2 thresholds,
            GoalkeeperGloveHandlingConfigV2 configuration,
            float incomingSpeed)
        {
            if (region == GloveContactRegionV1.Back)
                return GloveHandlingRejectionReasonV2.BackFace;
            if (region == GloveContactRegionV1.Edge)
                return GloveHandlingRejectionReasonV2.EdgeContact;
            if (input.PalmAlignment < thresholds.CatchAlignment)
                return GloveHandlingRejectionReasonV2.Misaligned;
            if (input.CaptureDistance > configuration.MaximumCaptureDistance)
                return GloveHandlingRejectionReasonV2.CaptureTooFar;
            var maximum = input.TwoHandCandidate
                ? thresholds.TwoHandCatchMaximumSpeed
                : thresholds.OneHandCatchMaximumSpeed;
            if (incomingSpeed > maximum)
                return GloveHandlingRejectionReasonV2.CatchTooFast;
            return GloveHandlingRejectionReasonV2.None;
        }

        private static GloveHandlingRejectionReasonV2 PunchRejection(
            GloveContactCandidateV2 input,
            GloveContactRegionV1 region,
            GoalkeeperGloveThresholdsV2 thresholds,
            GoalkeeperGloveHandlingConfigV2 configuration,
            float incomingSpeed)
        {
            if (region == GloveContactRegionV1.Back)
                return GloveHandlingRejectionReasonV2.BackFace;
            if (region == GloveContactRegionV1.Edge)
                return GloveHandlingRejectionReasonV2.EdgeContact;
            if (!input.Committed)
                return GloveHandlingRejectionReasonV2.NotCommitted;
            if (incomingSpeed < configuration.PunchMinimumImpactSpeed)
                return GloveHandlingRejectionReasonV2.PunchImpactTooLow;
            if (input.PalmAlignment < thresholds.PunchAlignment)
                return GloveHandlingRejectionReasonV2.Misaligned;
            if (input.ForwardGloveSpeed < thresholds.PunchForwardSpeed)
                return GloveHandlingRejectionReasonV2.PunchTooSlow;
            return GloveHandlingRejectionReasonV2.None;
        }

        private static Vector3 Redirect(
            GloveContactCandidateV2 input,
            GoalkeeperGloveHandlingConfigV2 configuration,
            bool punch)
        {
            var relative = input.IncomingBallVelocity - input.GloveVelocity;
            var normalSpeed = Vector3.Dot(relative, input.PalmNormal);
            var tangent = relative - input.PalmNormal * normalSpeed;
            var restitution = punch
                ? configuration.PunchRestitution
                : configuration.ParryRestitution;
            var retention = punch
                ? configuration.PunchTangentialRetention
                : configuration.ParryTangentialRetention;
            var transfer = punch
                ? configuration.PunchGloveVelocityTransfer
                : configuration.ParryGloveVelocityTransfer;
            var redirected =
                tangent * retention +
                input.PalmNormal * Mathf.Max(0f, -normalSpeed * restitution) +
                input.GloveVelocity * transfer;
            var maximumSpeed = input.IncomingBallVelocity.magnitude *
                Mathf.Sqrt(configuration.MaximumOutgoingEnergyRatio);
            return redirected.magnitude <= maximumSpeed
                ? redirected
                : redirected.normalized * maximumSpeed;
        }

        private static GloveHandlingDecisionV2 Decision(
            GloveContactCandidateV2 input,
            GloveHandlingOutcomeV1 outcome,
            GloveContactRegionV1 region,
            GloveHandlingRejectionReasonV2 rejection,
            Vector3 outgoingVelocity,
            bool catchEligible,
            bool punchEligible)
        {
            return new GloveHandlingDecisionV2(
                outcome,
                input.SurfaceRegion,
                region,
                rejection,
                outgoingVelocity,
                input.PalmAlignment,
                input.IncomingBallVelocity.magnitude,
                input.ForwardGloveSpeed,
                input.CaptureDistance,
                catchEligible,
                punchEligible,
                input.TwoHandCandidate);
        }
    }
}
