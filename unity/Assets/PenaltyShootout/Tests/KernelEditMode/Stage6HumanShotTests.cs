using System;
using System.Collections.Generic;
using NUnit.Framework;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage6HumanShotTests
    {
        private readonly List<UnityEngine.Object> created =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in created)
            {
                UnityEngine.Object.DestroyImmediate(item);
            }
            created.Clear();
        }

        [Test]
        public void PlayerShotCommandRejectsInvalidValuesWithoutClamping()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerShotCommandV1(1.01f, 0f, 0.5f, 0f, 0f, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlayerShotCommandV1(0f, 0f, float.NaN, 0f, 0f, 0f, 0f));
        }

        [Test]
        public void PowerMappingIsSmoothAndMonotonic()
        {
            var physics = CreatePhysics();
            Assert.That(
                PlayerShotResolverV1.FlightTimeForPower(0f, physics),
                Is.EqualTo(0.78f).Within(1e-6f));
            Assert.That(
                PlayerShotResolverV1.FlightTimeForPower(1f, physics),
                Is.EqualTo(0.40f).Within(1e-6f));
            Assert.That(
                PlayerShotResolverV1.FlightTimeForPower(0.75f, physics),
                Is.LessThan(PlayerShotResolverV1.FlightTimeForPower(0.5f, physics)));
        }

        [Test]
        public void PositiveSpinDirectionsMatchPlayerShotContract()
        {
            var physics = CreatePhysics();
            var velocity = new Vector3(0f, 1f, -20f);
            var rightBend = PlayerShotFlightModelV1.MagnusAcceleration(
                new Vector3(0f, -physics.MaximumSideSpin, 0f),
                velocity,
                physics);
            var topSpin = PlayerShotFlightModelV1.MagnusAcceleration(
                new Vector3(-physics.MaximumVerticalSpin, 0f, 0f),
                velocity,
                physics);

            Assert.That(rightBend.x, Is.GreaterThan(0f));
            Assert.That(topSpin.y, Is.LessThan(0f));
        }

        [Test]
        public void ResolutionIsDeterministicAndWithinSolverContract()
        {
            var physics = CreatePhysics();
            var command = new PlayerShotCommandV1(
                0.55f,
                0.25f,
                0.65f,
                0.75f,
                0.2f,
                0.05f,
                -0.03f);
            var first = PlayerShotResolverV1.Resolve(
                command,
                PlayerShotStyleV1.Curled,
                "fixture",
                false,
                Physics.gravity,
                0.02f,
                physics);
            var second = PlayerShotResolverV1.Resolve(
                command,
                PlayerShotStyleV1.Curled,
                "fixture",
                false,
                Physics.gravity,
                0.02f,
                physics);

            Assert.That(first.LaunchVelocityLocal, Is.EqualTo(second.LaunchVelocityLocal));
            Assert.That(first.PredictedUnopposedCrossingLocal, Is.EqualTo(second.PredictedUnopposedCrossingLocal));
            Assert.That(first.SolverCrossingError, Is.LessThanOrEqualTo(0.08f));
            Assert.That(first.PredictedCurveDisplacement.magnitude, Is.LessThanOrEqualTo(0.75f));
        }

        [Test]
        public void ResolverRejectsMismatchedPhysicsTimestep()
        {
            var physics = CreatePhysics();
            var command = new PlayerShotCommandV1(
                0f, 0f, 0.5f, 0f, 0f, 0f, 0f);

            Assert.Throws<ArgumentException>(() =>
                PlayerShotResolverV1.Resolve(
                    command,
                    PlayerShotStyleV1.Placed,
                    "fixture",
                    false,
                    Physics.gravity,
                    0.01f,
                    physics));
        }

        [Test]
        public void ReactiveCurveParityFixtureUsesVisiblePredictionOnly()
        {
            var command = GoalkeeperReactiveCurvePolicyV1
                .DecideFromVisiblePrediction(
                    0.30f,
                    new Vector2(0.30f, 0.60f),
                    0.20f,
                    new GoalkeeperControlActionMask(true));

            Assert.That(command.MoveX, Is.EqualTo(0.692f).Within(1e-5f));
            Assert.That(command.AimX, Is.EqualTo(0.30f).Within(1e-6f));
            Assert.That(command.AimY, Is.EqualTo(0.60f).Within(1e-6f));
            Assert.That(command.Reach, Is.EqualTo(1f));
            Assert.That(command.Commit, Is.True);
        }

        [Test]
        public void MotorTimingExtractionPreservesFrozenDiveGeometry()
        {
            var motor = CreateMotor();
            var estimate = GoalkeeperMotorTimingV1.Estimate(
                new Vector2(0.30f, 0.60f),
                new Vector3(0.20f, 0f, motor.StandingZ),
                motor);

            Assert.That(estimate.RootTargetLocal.x, Is.EqualTo(0.5006f).Within(1e-4f));
            Assert.That(estimate.RootTargetLocal.y, Is.EqualTo(0.0714f).Within(1e-4f));
            Assert.That(estimate.DiveDuration, Is.EqualTo(0.51536f).Within(1e-4f));
            Assert.That(estimate.FullReachTime, Is.EqualTo(0.33645f).Within(1e-4f));
            Assert.That(estimate.RootTargetSaturated, Is.False);
        }

        [Test]
        public void ReactiveMotorParityFixtureUsesMotorReachTime()
        {
            var motor = CreateMotor();
            var command = GoalkeeperReactiveMotorPolicyV1.DecideFromVisiblePrediction(
                0.30f,
                new Vector2(0.30f, 0.60f),
                0.20f,
                motor,
                new GoalkeeperControlActionMask(true));

            Assert.That(command.MoveX, Is.EqualTo(0.24048f).Within(1e-5f));
            Assert.That(command.AimX, Is.EqualTo(0.30f).Within(1e-6f));
            Assert.That(command.AimY, Is.EqualTo(0.60f).Within(1e-6f));
            Assert.That(command.Reach, Is.EqualTo(1f));
            Assert.That(command.Commit, Is.True);
        }

        [Test]
        public void AuditContactMaterialIsExplicitAndBounded()
        {
            var material = GoalkeeperAuditContactPhysicsV1.CreateGloveMaterial(
                0.35f,
                0.15f);
            created.Add(material);

            Assert.That(material.bounciness, Is.EqualTo(0.35f));
            Assert.That(material.dynamicFriction, Is.EqualTo(0.15f));
            Assert.That(material.bounceCombine, Is.EqualTo(PhysicsMaterialCombine.Maximum));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GoalkeeperAuditContactPhysicsV1.CreateGloveMaterial(1.1f, 0.1f));
        }

        [Test]
        public void AuditContactMaterialRestoresEachOriginalGloveMaterial()
        {
            var root = new GameObject("Arena");
            created.Add(root);
            var controller = root.AddComponent<PenaltyAreaController>();
            var original = new PhysicsMaterial("Original Glove");
            created.Add(original);
            var glove = new GameObject("LeftGlove");
            glove.transform.SetParent(root.transform);
            var marker = glove.AddComponent<ContactMarker>();
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = GoalkeeperContactPart.LeftGlove;
            var collider = glove.AddComponent<SphereCollider>();
            collider.sharedMaterial = original;

            controller.ConfigureAuditGloveContactMaterial(0.35f, 0.15f);

            Assert.That(controller.AuditGloveContactMaterialEnabled, Is.True);
            Assert.That(collider.sharedMaterial, Is.Not.SameAs(original));
            Assert.That(collider.sharedMaterial.bounciness, Is.EqualTo(0.35f));

            controller.ClearAuditGloveContactMaterial();

            Assert.That(controller.AuditGloveContactMaterialEnabled, Is.False);
            Assert.That(collider.sharedMaterial, Is.SameAs(original));
        }

        [Test]
        public void ContactReviewMetricsSeparateGoalwardAndAwayVelocity()
        {
            var result = new AttemptResult
            {
                HasFirstGoalkeeperContactKinematics = true,
                FirstGoalkeeperContactBallVelocityLocal =
                    new Vector3(2f, 3f, 4f),
                FirstGoalkeeperContactImpulseLocal =
                    new Vector3(0f, 0f, 1.5f),
            };

            var metrics = Stage6ContactReviewMetricsV1.FromResult(result);

            Assert.That(metrics.HasContact, Is.True);
            Assert.That(metrics.ContactBallSpeed, Is.EqualTo(Mathf.Sqrt(29f)).Within(1e-6f));
            Assert.That(metrics.AwayFromGoalSpeed, Is.EqualTo(4f));
            Assert.That(metrics.GoalwardSpeed, Is.Zero);
            Assert.That(metrics.VerticalSpeed, Is.EqualTo(3f));
            Assert.That(metrics.ImpulseMagnitude, Is.EqualTo(1.5f));
        }

        [Test]
        public void ContactReviewCatalogPreservesFixedReplayKeys()
        {
            const string json =
                "{\"schema_version\":1,\"entries\":[{" +
                "\"arena_id\":3,\"attempt_id\":10,\"shot_style\":\"Power\"," +
                "\"replay_arguments\":[\"--stage6-replay-master-seed=20260803\"]}]}";

            var valid = Stage6ContactReviewReplayCatalogV1.TryParse(
                json,
                1UL,
                out var keys,
                out var error);

            Assert.That(valid, Is.True, error);
            Assert.That(keys, Has.Length.EqualTo(1));
            Assert.That(keys[0].MasterSeed, Is.EqualTo(20260803UL));
            Assert.That(keys[0].ArenaId, Is.EqualTo(3));
            Assert.That(keys[0].AttemptId, Is.EqualTo(10));
            Assert.That(keys[0].ShotStyle, Is.EqualTo("Power"));
        }

        [Test]
        public void HumanShotSamplingIsDeterministicAndFinite()
        {
            var physics = CreatePhysics();
            var distribution = CreateDistribution();
            var first = HumanShotGeneratorV1.Sample(
                distribution,
                physics,
                20260803UL,
                Physics.gravity,
                0.02f);
            var second = HumanShotGeneratorV1.Sample(
                distribution,
                physics,
                20260803UL,
                Physics.gravity,
                0.02f);

            Assert.That(first.PlayerShot.ShotContractId, Is.EqualTo("player-shot-v1"));
            Assert.That(first.LaunchVelocityLocal, Is.EqualTo(second.LaunchVelocityLocal));
            Assert.That(first.PlayerShot.LaunchSpeed, Is.InRange(14f, 30f));
            Assert.That(HumanShotGeneratorV1.Validate(
                first,
                distribution,
                physics,
                out var error), Is.True, error);
        }

        private GoalkeeperControlMotorConfig CreateMotor()
        {
            var motor = ScriptableObject.CreateInstance<GoalkeeperControlMotorConfig>();
            created.Add(motor);
            return motor;
        }

        [Test]
        public void FixedTwoThousandShotSuiteMeetsDistributionAndPhysicsBounds()
        {
            var physics = CreatePhysics();
            var distribution = CreateDistribution();
            var styles = new int[3];
            var classes = new int[4];
            var curves = new List<float>(2000);
            var errors = new List<float>(2000);
            var contactErrorsX = new List<float>(2000);
            var contactErrorsY = new List<float>(2000);
            var leftAims = 0;
            var rightAims = 0;
            for (var arena = 0; arena < 16; arena++)
            {
                for (var attempt = 1; attempt <= 125; attempt++)
                {
                    var seed = Pcg32.DeriveSeed(20260803UL, arena, attempt);
                    var scenario = HumanShotGeneratorV1.Sample(
                        distribution,
                        physics,
                        seed,
                        Physics.gravity,
                        0.02f,
                        ((arena + attempt) & 1) == 0 ? 1f : -1f);
                    styles[(int)scenario.PlayerShot.ShotStyle]++;
                    classes[(int)scenario.PlayerShot.ExpectedTargetClass]++;
                    curves.Add(scenario.PlayerShot.PredictedCurveDisplacement.magnitude);
                    errors.Add(scenario.PlayerShot.SolverCrossingError);
                    contactErrorsX.Add(
                        scenario.PlayerShot.Command.ContactErrorXMeters);
                    contactErrorsY.Add(
                        scenario.PlayerShot.Command.ContactErrorYMeters);
                    var aimX = scenario.PlayerShot.Command.AimX;
                    leftAims += aimX < 0f ? 1 : 0;
                    rightAims += aimX > 0f ? 1 : 0;
                    Assert.That(scenario.PlayerShot.LaunchSpeed, Is.InRange(14f, 30f));
                    Assert.That(
                        Mathf.Abs(contactErrorsX[contactErrorsX.Count - 1]),
                        Is.LessThanOrEqualTo(0.75f));
                    Assert.That(
                        Mathf.Abs(contactErrorsY[contactErrorsY.Count - 1]),
                        Is.LessThanOrEqualTo(0.75f));
                }
            }

            curves.Sort();
            errors.Sort();
            var sampleSummary =
                $"styles={styles[0]}/{styles[1]}/{styles[2]}, " +
                $"classes={classes[0]}/{classes[1]}/{classes[2]}/{classes[3]}, " +
                $"curve_p95={curves[1899]:F3}, curve_max={curves[1999]:F3}, " +
                $"error_p95={errors[1899]:F4}, error_max={errors[1999]:F4}";
            TestContext.WriteLine(sampleSummary);
            Assert.That(styles[0] / 2000f, Is.EqualTo(0.45f).Within(0.015f), sampleSummary);
            Assert.That(styles[1] / 2000f, Is.EqualTo(0.35f).Within(0.015f), sampleSummary);
            Assert.That(styles[2] / 2000f, Is.EqualTo(0.20f).Within(0.015f), sampleSummary);
            Assert.That(classes[0] / 2000f, Is.EqualTo(0.92f).Within(0.015f), sampleSummary);
            Assert.That(1f - classes[0] / 2000f, Is.EqualTo(0.08f).Within(0.015f), sampleSummary);
            Assert.That(errors[1899], Is.LessThanOrEqualTo(0.08f));
            Assert.That(errors[1999], Is.LessThanOrEqualTo(0.08f));
            Assert.That(curves[1899], Is.InRange(0.50f, 0.65f));
            Assert.That(curves[1999], Is.LessThanOrEqualTo(0.75f));
            Assert.That(leftAims, Is.EqualTo(1000), sampleSummary);
            Assert.That(rightAims, Is.EqualTo(1000), sampleSummary);
            Assert.That(
                PearsonCorrelation(contactErrorsX, contactErrorsY),
                Is.InRange(0.10f, 0.40f),
                sampleSummary);
        }

        [Test]
        public void ExistingControlObservationOrderRemainsThirtyFiveFloats()
        {
            var values = new List<float>();
            GoalkeeperTrainingContracts.WriteControlStateV2(
                new GoalkeeperControlVisibleStateSnapshot(),
                Physics.gravity,
                values.Add);

            Assert.That(values, Has.Count.EqualTo(35));
        }

        [Test]
        public void GloveHandlingGeometryDoesNotExtendLegacyReachEnvelope()
        {
            var configuration = CreateGloveHandling();

            Assert.That(configuration.Validate(out var error), Is.True, error);
            Assert.That(
                configuration.CompoundRadialExtent,
                Is.LessThanOrEqualTo(configuration.MaximumRadialExtent));
        }

        [Test]
        public void CentralAlignedContactCanBeCaught()
        {
            var decision = GoalkeeperGloveHandlingPolicyV1.Resolve(
                new GloveHandlingInputV1(
                    GloveContactRegionV1.Palm,
                    new Vector3(0f, 0f, -8f),
                    Vector3.zero,
                    Vector3.forward,
                    0.2f,
                    false),
                CreateGloveHandling());

            Assert.That(decision.Outcome, Is.EqualTo(GloveHandlingOutcomeV1.Catch));
            Assert.That(decision.OutgoingSpeed, Is.Zero);
            Assert.That(decision.PalmAlignment, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void FastAlignedContactParriesWithoutAddingEnergy()
        {
            var configuration = CreateGloveHandling();
            var decision = GoalkeeperGloveHandlingPolicyV1.Resolve(
                new GloveHandlingInputV1(
                    GloveContactRegionV1.Palm,
                    new Vector3(3f, -1f, -20f),
                    new Vector3(1f, 0.5f, 0.5f),
                    Vector3.forward,
                    0.2f,
                    false),
                configuration);

            Assert.That(decision.Outcome, Is.EqualTo(GloveHandlingOutcomeV1.Parry));
            Assert.That(
                decision.EnergyRatio,
                Is.LessThanOrEqualTo(
                    configuration.MaximumOutgoingEnergyRatio + 1e-5f));
            Assert.That(decision.OutgoingBallVelocity.z, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void EdgeAndBackContactsDoNotBecomeAutomaticCatches()
        {
            var configuration = CreateGloveHandling();
            var edge = GoalkeeperGloveHandlingPolicyV1.Resolve(
                new GloveHandlingInputV1(
                    GloveContactRegionV1.Palm,
                    new Vector3(0f, 0f, -7f),
                    Vector3.zero,
                    Vector3.forward,
                    0.95f,
                    false),
                configuration);
            var back = GoalkeeperGloveHandlingPolicyV1.Resolve(
                new GloveHandlingInputV1(
                    GloveContactRegionV1.Palm,
                    new Vector3(0f, 0f, 7f),
                    Vector3.zero,
                    Vector3.forward,
                    0.2f,
                    false),
                configuration);

            Assert.That(
                edge.Outcome,
                Is.EqualTo(GloveHandlingOutcomeV1.WeakDeflection));
            Assert.That(edge.Region, Is.EqualTo(GloveContactRegionV1.Edge));
            Assert.That(
                back.Outcome,
                Is.EqualTo(GloveHandlingOutcomeV1.Uncontrolled));
            Assert.That(back.Region, Is.EqualTo(GloveContactRegionV1.Back));
        }

        [Test]
        public void GloveV2ProfilesAreFrozenToTheCalibrationTable()
        {
            var conservative = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Conservative);
            var balanced = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Balanced);
            var permissive = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Permissive);

            Assert.That(conservative.CatchAlignment, Is.EqualTo(0.70f));
            Assert.That(conservative.PunchForwardSpeed, Is.EqualTo(1.00f));
            Assert.That(balanced.OneHandCatchMaximumSpeed, Is.EqualTo(9f));
            Assert.That(balanced.CentralExtent, Is.EqualTo(0.82f));
            Assert.That(permissive.TwoHandCatchMaximumSpeed, Is.EqualTo(20f));
            Assert.That(permissive.PunchAlignment, Is.EqualTo(0.30f));
        }

        [Test]
        public void GloveV2ReconstructsCommandedImpactVelocity()
        {
            var commandedIncoming = new Vector3(1.5f, -0.25f, -17f);
            var measuredGlove = new Vector3(-0.5f, 0.75f, -1.25f);
            var relativeImpact = measuredGlove - commandedIncoming;

            var reconstructed =
                GoalkeeperGloveHandlingV1.ReconstructIncomingBallVelocity(
                    relativeImpact,
                    measuredGlove);

            Assert.That(
                Vector3.Distance(reconstructed, commandedIncoming),
                Is.LessThanOrEqualTo(0.0001f));
            Assert.That(
                Vector3.Dot(reconstructed, commandedIncoming),
                Is.GreaterThan(0f));
            Assert.That(
                Mathf.Abs(reconstructed.magnitude - commandedIncoming.magnitude),
                Is.LessThanOrEqualTo(0.5f));
        }

        [Test]
        public void GloveV2MeasuresCaptureDistanceToImpactPoint()
        {
            var ballCenter = new Vector3(2f, 1f, 0.11f);
            var palmCenter = new Vector3(1.8f, 0.8f, 0f);
            var impactPoint = new Vector3(2f, 1f, 0f);

            var captureDistance =
                GoalkeeperGloveHandlingV1.MeasureCaptureDistance(
                    ballCenter,
                    impactPoint);

            Assert.That(captureDistance, Is.EqualTo(0.11f).Within(0.0001f));
            Assert.That(
                captureDistance,
                Is.LessThan(Vector3.Distance(ballCenter, palmCenter)));
        }

        [Test]
        public void GloveV2CandidateSelectionPrefersFrontPalmThenStableLeftHand()
        {
            var thresholds = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Balanced);
            var candidates = new[]
            {
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    new Vector3(0f, 0f, 12f),
                    Vector3.zero,
                    0.1f),
                CreateV2Candidate(
                    GloveContactRegionV1.Fingers,
                    GoalkeeperContactPart.RightGlove,
                    new Vector3(0f, 0f, -12f),
                    Vector3.zero,
                    0.1f),
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.RightGlove,
                    new Vector3(0f, 0f, -12f),
                    Vector3.zero,
                    0.1f),
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    new Vector3(0f, 0f, -12f),
                    Vector3.zero,
                    0.1f),
            };

            var selected = GoalkeeperGloveHandlingPolicyV2.SelectCandidate(
                candidates,
                thresholds);

            Assert.That(selected, Is.EqualTo(3));
        }

        [Test]
        public void GloveV2BalancedProfileSeparatesCatchAndMeasuredPunch()
        {
            var configuration = CreateGloveHandlingV2();
            var thresholds = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Balanced);
            var caught = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    new Vector3(0f, 0f, -8.5f),
                    Vector3.zero,
                    0.2f),
                thresholds,
                configuration);
            var punched = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.RightGlove,
                    new Vector3(0f, 0f, -14f),
                    new Vector3(0f, 0f, 0.9f),
                    0.2f),
                thresholds,
                configuration);
            var belowThreshold = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.RightGlove,
                    new Vector3(0f, 0f, -14f),
                    new Vector3(0f, 0f, 0.79f),
                    0.2f),
                thresholds,
                configuration);

            Assert.That(caught.Outcome, Is.EqualTo(GloveHandlingOutcomeV1.Catch));
            Assert.That(punched.Outcome, Is.EqualTo(GloveHandlingOutcomeV1.Punch));
            Assert.That(punched.PunchEligible, Is.True);
            Assert.That(punched.EnergyRatio, Is.LessThanOrEqualTo(0.95001f));
            Assert.That(
                belowThreshold.Outcome,
                Is.Not.EqualTo(GloveHandlingOutcomeV1.Punch));
            Assert.That(
                belowThreshold.RejectionReason,
                Is.EqualTo(GloveHandlingRejectionReasonV2.PunchTooSlow));
        }

        [Test]
        public void GloveV2RejectsCatchAfterWholeBallCrossesGoalLine()
        {
            var decision = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    new Vector3(0f, 0f, -7f),
                    Vector3.zero,
                    0.1f,
                    wholeBallAcross: true),
                GoalkeeperGloveCalibrationProfilesV2.Get(
                    GoalkeeperGloveCalibrationProfileV2.Balanced),
                CreateGloveHandlingV2());

            Assert.That(
                decision.Outcome,
                Is.EqualTo(GloveHandlingOutcomeV1.Uncontrolled));
            Assert.That(
                decision.RejectionReason,
                Is.EqualTo(GloveHandlingRejectionReasonV2.AcrossGoalLine));
        }

        [Test]
        public void GloveV2LeavesOnlyExtremeGlancingEdgesUncontrolled()
        {
            const float speed = 16f;
            var glancingAlignment = 0.30f;
            var glancing = new Vector3(
                Mathf.Sqrt(1f - glancingAlignment * glancingAlignment) * speed,
                0f,
                -glancingAlignment * speed);
            var directAlignment = 0.60f;
            var direct = new Vector3(
                Mathf.Sqrt(1f - directAlignment * directAlignment) * speed,
                0f,
                -directAlignment * speed);
            var configuration = CreateGloveHandlingV2();
            var thresholds = GoalkeeperGloveCalibrationProfilesV2.Get(
                GoalkeeperGloveCalibrationProfileV2.Balanced);

            var uncontrolled = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    glancing,
                    Vector3.zero,
                    0.99f),
                thresholds,
                configuration);
            var controlled = GoalkeeperGloveHandlingPolicyV2.Resolve(
                CreateV2Candidate(
                    GloveContactRegionV1.Palm,
                    GoalkeeperContactPart.LeftGlove,
                    direct,
                    Vector3.zero,
                    0.99f),
                thresholds,
                configuration);

            Assert.That(
                uncontrolled.Outcome,
                Is.EqualTo(GloveHandlingOutcomeV1.Uncontrolled));
            Assert.That(
                uncontrolled.RejectionReason,
                Is.EqualTo(GloveHandlingRejectionReasonV2.EdgeContact));
            Assert.That(
                controlled.Outcome,
                Is.EqualTo(GloveHandlingOutcomeV1.WeakDeflection));
        }

        private PlayerShotPhysicsConfigV1 CreatePhysics()
        {
            var configuration =
                ScriptableObject.CreateInstance<PlayerShotPhysicsConfigV1>();
            created.Add(configuration);
            return configuration;
        }

        private GoalkeeperGloveHandlingConfigV1 CreateGloveHandling()
        {
            var configuration =
                ScriptableObject.CreateInstance<GoalkeeperGloveHandlingConfigV1>();
            created.Add(configuration);
            return configuration;
        }

        private GoalkeeperGloveHandlingConfigV2 CreateGloveHandlingV2()
        {
            var configuration =
                ScriptableObject.CreateInstance<GoalkeeperGloveHandlingConfigV2>();
            created.Add(configuration);
            return configuration;
        }

        private GloveContactCandidateV2 CreateV2Candidate(
            GloveContactRegionV1 region,
            GoalkeeperContactPart part,
            Vector3 incoming,
            Vector3 gloveVelocity,
            float extent,
            bool wholeBallAcross = false)
        {
            var surfaceObject = new GameObject($"Test_{part}_{region}");
            created.Add(surfaceObject);
            var surface = surfaceObject.AddComponent<GloveContactSurfaceV1>();
            surface.Configure(part, region);
            var contact = new BallContactEventV1(
                ContactKind.Goalkeeper,
                part,
                new ContactKinematics
                {
                    HasValue = true,
                    RelativeVelocityWorld = gloveVelocity - incoming,
                    BallVelocityWorld = incoming,
                },
                surface);
            return new GloveContactCandidateV2(
                contact,
                incoming,
                gloveVelocity,
                Vector3.forward,
                extent,
                0.14f,
                false,
                0.5f,
                true,
                wholeBallAcross,
                true);
        }

        private static float PearsonCorrelation(
            IReadOnlyList<float> first,
            IReadOnlyList<float> second)
        {
            var count = Mathf.Min(first.Count, second.Count);
            var firstMean = 0f;
            var secondMean = 0f;
            for (var index = 0; index < count; index++)
            {
                firstMean += first[index];
                secondMean += second[index];
            }
            firstMean /= count;
            secondMean /= count;

            var covariance = 0f;
            var firstVariance = 0f;
            var secondVariance = 0f;
            for (var index = 0; index < count; index++)
            {
                var firstDelta = first[index] - firstMean;
                var secondDelta = second[index] - secondMean;
                covariance += firstDelta * secondDelta;
                firstVariance += firstDelta * firstDelta;
                secondVariance += secondDelta * secondDelta;
            }
            return covariance /
                Mathf.Sqrt(firstVariance * secondVariance);
        }

        private HumanShotDistributionConfigV1 CreateDistribution()
        {
            var configuration =
                ScriptableObject.CreateInstance<HumanShotDistributionConfigV1>();
            created.Add(configuration);
            return configuration;
        }
    }
}
