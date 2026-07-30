using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class KernelContractTests
    {
        private EnvironmentKernelConfig environment;
        private ShotDistributionConfig shots;
        private GoalkeeperMotorConfig motor;
        private GoalkeeperControlMotorConfig controlMotor;

        [SetUp]
        public void SetUp()
        {
            environment = ScriptableObject.CreateInstance<EnvironmentKernelConfig>();
            shots = ScriptableObject.CreateInstance<ShotDistributionConfig>();
            motor = ScriptableObject.CreateInstance<GoalkeeperMotorConfig>();
            controlMotor =
                ScriptableObject.CreateInstance<GoalkeeperControlMotorConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(environment);
            Object.DestroyImmediate(shots);
            Object.DestroyImmediate(motor);
            Object.DestroyImmediate(controlMotor);
        }

        [Test]
        public void AttemptStateMachineAcceptsOnlyDeclaredLifecycle()
        {
            var state = new AttemptStateMachine();
            state.InitializeTerminal();
            Assert.That(state.TryTransition(AttemptPhase.Resetting), Is.True);
            Assert.That(state.TryTransition(AttemptPhase.Ready), Is.True);
            Assert.That(state.TryTransition(AttemptPhase.RunUp), Is.True);
            Assert.That(state.TryTransition(AttemptPhase.BallInFlight), Is.True);
            Assert.That(state.TryTransition(AttemptPhase.Resolving), Is.True);
            Assert.That(state.TryTransition(AttemptPhase.Terminal), Is.True);
            Assert.That(state.InvalidTransitionCount, Is.Zero);

            Assert.That(state.TryTransition(AttemptPhase.BallInFlight), Is.False);
            Assert.That(state.InvalidTransitionCount, Is.EqualTo(1));
        }

        [Test]
        public void GoalkeeperActionIdsAreStable()
        {
            Assert.That((int)GoalkeeperAction.Hold, Is.Zero);
            Assert.That((int)GoalkeeperAction.ShuffleLeft, Is.EqualTo(1));
            Assert.That((int)GoalkeeperAction.ShuffleRight, Is.EqualTo(2));
            Assert.That((int)GoalkeeperAction.DiveLeftLow, Is.EqualTo(3));
            Assert.That((int)GoalkeeperAction.DiveLeftMiddle, Is.EqualTo(4));
            Assert.That((int)GoalkeeperAction.DiveLeftHigh, Is.EqualTo(5));
            Assert.That((int)GoalkeeperAction.DiveRightLow, Is.EqualTo(6));
            Assert.That((int)GoalkeeperAction.DiveRightMiddle, Is.EqualTo(7));
            Assert.That((int)GoalkeeperAction.DiveRightHigh, Is.EqualTo(8));
        }

        [Test]
        public void Stage2StateObservationManifestIsStable()
        {
            Assert.That(KernelConstants.GoalkeeperStateBehaviorName, Is.EqualTo("GoalkeeperState-v0"));
            Assert.That(KernelConstants.GoalkeeperStateObservationSpecId, Is.EqualTo("state-v0"));
            Assert.That(KernelConstants.GoalkeeperSparseRewardSpecId, Is.EqualTo("goalkeeper-sparse-v0"));
            Assert.That(KernelConstants.GoalkeeperStateObservationSize, Is.EqualTo(24));

            var observations = new List<float>();
            GoalkeeperTrainingContracts.WriteStateV0(null, observations.Add);
            Assert.That(observations, Has.Count.EqualTo(24));
            Assert.That(observations, Is.All.InRange(-1f, 1f));

            var manifest = KernelManifestUtility.CreateGoalkeeperStateJson();
            StringAssert.Contains(KernelConstants.GoalkeeperStateBehaviorName, manifest);
            StringAssert.Contains(KernelConstants.GoalkeeperStateObservationSpecId, manifest);
            StringAssert.Contains("requested_target", manifest);
            StringAssert.Contains("future_goal_plane_intersection", manifest);
        }

        [Test]
        public void Stage4PartialObservationManifestPreservesStateVectorShape()
        {
            Assert.That(
                KernelConstants.GoalkeeperRobustBehaviorName,
                Is.EqualTo("GoalkeeperRobust-v0"));
            Assert.That(
                KernelConstants.GoalkeeperPartialObservationSpecId,
                Is.EqualTo("state-po-v0"));

            var observations = new List<float>();
            GoalkeeperTrainingContracts.WriteStatePartialV0(
                new GoalkeeperVisibleStateSnapshot
                {
                    MotorState = GoalkeeperMotorState.Ready,
                    DiveAction = GoalkeeperAction.Hold,
                },
                GoalkeeperPartialObservationSettings.None,
                observations.Add);
            Assert.That(observations, Has.Count.EqualTo(24));
            Assert.That(observations, Is.All.InRange(-1f, 1f));

            var manifest = KernelManifestUtility.CreateGoalkeeperRobustJson();
            StringAssert.Contains(KernelConstants.GoalkeeperRobustBehaviorName, manifest);
            StringAssert.Contains(KernelConstants.GoalkeeperPartialObservationSpecId, manifest);
            StringAssert.Contains("requested_target", manifest);
            StringAssert.Contains("future_goal_plane_intersection", manifest);
        }

        [Test]
        public void Stage5ControlContractIsVersionedAndObservationShapeIsStable()
        {
            Assert.That(
                KernelConstants.GoalkeeperControlBehaviorName,
                Is.EqualTo("GoalkeeperControl-v1"));
            Assert.That(
                KernelConstants.GoalkeeperControlObservationSpecId,
                Is.EqualTo("control-state-v1"));
            Assert.That(
                KernelConstants.GoalkeeperControlActionSpecId,
                Is.EqualTo("goalkeeper-hybrid-v1"));
            Assert.That(
                KernelConstants.GoalkeeperControlMotorProfileId,
                Is.EqualTo("keeper-control-v1"));
            Assert.That(
                GoalkeeperControlSpace.ContinuousActionCount,
                Is.EqualTo(4));
            Assert.That(
                GoalkeeperControlSpace.CommitBranchSize,
                Is.EqualTo(2));

            var observations = new List<float>();
            GoalkeeperTrainingContracts.WriteControlStateV1(
                null,
                observations.Add);
            Assert.That(
                observations,
                Has.Count.EqualTo(
                    KernelConstants.GoalkeeperControlObservationSize));
            Assert.That(observations, Is.All.InRange(-1f, 1f));

            var manifest =
                KernelManifestUtility.CreateGoalkeeperControlJson(controlMotor);
            StringAssert.Contains("\"continuous_actions\": 4", manifest);
            StringAssert.Contains("\"discrete_branches\"", manifest);
            StringAssert.Contains("\"commit_save\"", manifest);
            StringAssert.Contains("\"left_glove_x\"", manifest);
            StringAssert.Contains("\"right_glove_y\"", manifest);
            StringAssert.Contains("\"requested_target\"", manifest);
            StringAssert.Contains("\"future_goal_plane_intersection\"", manifest);
            StringAssert.DoesNotContain("\"requested_target_local\"", manifest);
        }

        [Test]
        public void Stage5ControlObservationOrderAndRangesAreExact()
        {
            var snapshot = new GoalkeeperControlVisibleStateSnapshot
            {
                BallLocalPosition = new Vector3(5f, -4f, 11f),
                BallLocalVelocity = new Vector3(25f, -25f, 12.5f),
                BallAngularVelocity = new Vector3(50f, -50f, 25f),
                GoalkeeperRootLocalPosition = new Vector3(3.1f, 1.2f, 0.3f),
                GoalkeeperRootLocalVelocity = new Vector3(5f, -5f, 0f),
                GoalkeeperBodyRoll = 1f,
                MotorState = GoalkeeperControlMotorState.Planting,
                MotorStateProgress = 0.25f,
                LatchedAim = new Vector2(-0.4f, 0.6f),
                ReachAim = new Vector2(-0.2f, 0.8f),
                ReachExtension = 0.75f,
                LeftGloveLocalPosition = new Vector3(-1.83f, 1.22f, 0f),
                RightGloveLocalPosition = new Vector3(1.83f, 2.44f, 0f),
                CanCommit = false,
                AttemptTime = 2f,
                BallFlightTime = 0.5f,
            };
            var observations = new List<float>();
            GoalkeeperTrainingContracts.WriteControlStateV1(
                snapshot,
                observations.Add);

            Assert.That(observations, Has.Count.EqualTo(32));
            Assert.That(observations[0], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(observations[9], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(observations[14], Is.Zero);
            Assert.That(observations[16], Is.EqualTo(1f));
            Assert.That(observations[19], Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That(observations[24], Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(observations[29], Is.Zero);
            Assert.That(observations[30], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(observations[31], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(observations, Is.All.InRange(-1f, 1f));
        }

        [Test]
        public void Stage5HybridCommandsClampAndRejectNonFiniteValues()
        {
            var command = new GoalkeeperControlCommand
            {
                MoveX = 2f,
                AimX = -3f,
                AimY = float.NaN,
                Reach = float.PositiveInfinity,
                Commit = true,
            };
            var sanitized = command.Sanitized(out var clamped);
            Assert.That(clamped, Is.True);
            Assert.That(sanitized.MoveX, Is.EqualTo(1f));
            Assert.That(sanitized.AimX, Is.EqualTo(-1f));
            Assert.That(sanitized.AimY, Is.Zero);
            Assert.That(sanitized.Reach, Is.Zero);
            Assert.That(sanitized.Commit, Is.True);
        }

        [Test]
        public void Stage5TwoBoneSolverPreservesSegmentLengthsAndReachBound()
        {
            var shoulder = new Vector3(-0.25f, 1.3f, 0f);
            var requested = new Vector3(-5f, 4f, 1f);
            var elbow = GoalkeeperArmRigV1.SolveElbow(
                shoulder,
                requested,
                Vector3.forward,
                controlMotor.UpperArmLength,
                controlMotor.ForearmLength,
                out var hand);
            Assert.That(
                Vector3.Distance(shoulder, elbow),
                Is.EqualTo(controlMotor.UpperArmLength).Within(1e-4f));
            Assert.That(
                Vector3.Distance(elbow, hand),
                Is.EqualTo(controlMotor.ForearmLength).Within(1e-4f));
            Assert.That(
                Vector3.Distance(shoulder, hand),
                Is.LessThanOrEqualTo(
                    controlMotor.UpperArmLength +
                    controlMotor.ForearmLength));
        }

        [Test]
        public void Stage5MotorCommitsOnceMasksDuringSaveAndResetsCleanly()
        {
            var root = CreateControlMotor(out var goalkeeper);
            goalkeeper.ResetForAttempt(1, 1UL);
            Assert.That(goalkeeper.ValidateReset(out var resetError), Is.True, resetError);

            var move = GoalkeeperControlCommand.Neutral;
            move.MoveX = 1f;
            Assert.That(goalkeeper.TryApplyCommand(move), Is.True);
            goalkeeper.Tick(0.02f);
            Assert.That(
                goalkeeper.State,
                Is.EqualTo(GoalkeeperControlMotorState.Moving));
            Assert.That(goalkeeper.LocalPosition.x, Is.GreaterThan(0f));

            var commit = new GoalkeeperControlCommand
            {
                MoveX = 0f,
                AimX = 0.85f,
                AimY = 0.75f,
                Reach = 1f,
                Commit = true,
            };
            Assert.That(goalkeeper.TryApplyCommand(commit), Is.True);
            Assert.That(
                goalkeeper.State,
                Is.EqualTo(GoalkeeperControlMotorState.Planting));
            Assert.That(goalkeeper.GetActionMask().CanCommit, Is.False);
            Assert.That(goalkeeper.TryApplyCommand(commit), Is.False);

            for (var index = 0; index < 100; index++)
            {
                goalkeeper.Tick(0.02f);
            }

            Assert.That(
                goalkeeper.State,
                Is.EqualTo(GoalkeeperControlMotorState.Ready));
            Assert.That(goalkeeper.PeakRootSpeed, Is.GreaterThan(0f));
            Assert.That(goalkeeper.PeakReachExtension, Is.GreaterThan(0.9f));
            Assert.That(
                Mathf.Abs(goalkeeper.LocalPosition.x),
                Is.LessThanOrEqualTo(controlMotor.LateralLimit + 1e-4f));

            goalkeeper.ResetForAttempt(2, 2UL);
            Assert.That(goalkeeper.ValidateReset(out resetError), Is.True, resetError);
            Assert.That(goalkeeper.TotalRootDistance, Is.Zero);
            Assert.That(goalkeeper.PeakReachExtension, Is.Zero);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Stage5LeftAndRightDiveRootsAreMirrored()
        {
            var left = SampleControlDive(-0.8f, 0.4f);
            var right = SampleControlDive(0.8f, 0.4f);
            Assert.That(left.x, Is.LessThan(0f));
            Assert.That(right.x, Is.GreaterThan(0f));
            Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-4f));
            Assert.That(left.y, Is.EqualTo(right.y).Within(1e-4f));
        }

        [Test]
        public void Stage5ReadyBodyGeometryIsCrouchedAndSymmetric()
        {
            var root = CreateControlMotor(out var goalkeeper);
            goalkeeper.ResetForAttempt(1, 1UL);
            var keeper = goalkeeper.transform;
            var torso = keeper.Find("Torso");
            var head = keeper.Find("Head");
            var leftLeg = keeper.Find("LeftLeg");
            var rightLeg = keeper.Find("RightLeg");

            Assert.That(torso.localScale, Is.EqualTo(controlMotor.TorsoScale));
            Assert.That(
                torso.localPosition.y,
                Is.EqualTo(controlMotor.TorsoCenterHeight));
            Assert.That(
                head.localPosition.y,
                Is.EqualTo(controlMotor.HeadCenterHeight));
            Assert.That(
                leftLeg.localPosition.x,
                Is.EqualTo(-controlMotor.LegLateral));
            Assert.That(
                rightLeg.localPosition.x,
                Is.EqualTo(controlMotor.LegLateral));
            Assert.That(
                leftLeg.localPosition.y,
                Is.EqualTo(rightLeg.localPosition.y));
            Assert.That(
                torso.localPosition.z,
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(
                Mathf.Abs(leftLeg.localRotation.eulerAngles.z),
                Is.GreaterThan(0f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Stage5ReachDoesNotRetractBetweenPlantAndDive()
        {
            var root = CreateControlMotor(out var goalkeeper);
            goalkeeper.ResetForAttempt(1, 1UL);
            Assert.That(
                goalkeeper.TryApplyCommand(
                    new GoalkeeperControlCommand
                    {
                        AimX = 0.85f,
                        AimY = 0.65f,
                        Reach = 1f,
                        Commit = true,
                    }),
                Is.True);

            var previousExtension = goalkeeper.CurrentReachExtension;
            var sawDive = false;
            var attemptedRetraction = false;
            for (var index = 0; index < 100; index++)
            {
                goalkeeper.Tick(0.02f);
                if (goalkeeper.State == GoalkeeperControlMotorState.Diving)
                {
                    sawDive = true;
                    if (!attemptedRetraction)
                    {
                        attemptedRetraction = true;
                        Assert.That(
                            goalkeeper.TryApplyCommand(
                                new GoalkeeperControlCommand
                                {
                                    AimX = 0.85f,
                                    AimY = 0.65f,
                                    Reach = -1f,
                                }),
                            Is.True);
                    }
                }

                if (goalkeeper.State == GoalkeeperControlMotorState.Recovering)
                {
                    break;
                }

                Assert.That(
                    goalkeeper.CurrentReachExtension,
                    Is.GreaterThanOrEqualTo(previousExtension - 1e-5f),
                    $"Reach retracted at motor step {index}.");
                previousExtension = goalkeeper.CurrentReachExtension;
            }

            Assert.That(sawDive, Is.True);
            Assert.That(attemptedRetraction, Is.True);
            Assert.That(previousExtension, Is.GreaterThan(0.9f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Stage4ObservationDelayBufferReturnsDeterministicDelayedSnapshots()
        {
            var buffer = new GoalkeeperObservationDelayBuffer(4);
            var first = new GoalkeeperVisibleStateSnapshot { AttemptTime = 0.1f };
            var second = new GoalkeeperVisibleStateSnapshot { AttemptTime = 0.2f };
            var third = new GoalkeeperVisibleStateSnapshot { AttemptTime = 0.3f };

            Assert.That(buffer.PushAndRead(first, 2).AttemptTime, Is.EqualTo(0.1f));
            Assert.That(buffer.PushAndRead(second, 1).AttemptTime, Is.EqualTo(0.1f));
            Assert.That(buffer.PushAndRead(third, 1).AttemptTime, Is.EqualTo(0.2f));
            Assert.That(buffer.Count, Is.EqualTo(3));

            buffer.Reset();
            Assert.That(buffer.Count, Is.Zero);
            Assert.That(buffer.PushAndRead(third, 0).AttemptTime, Is.EqualTo(0.3f));
        }

        [Test]
        public void Stage4ObservationPerturbationIsSeededAndDropoutIsBounded()
        {
            var snapshot = new GoalkeeperVisibleStateSnapshot
            {
                BallLocalPosition = new Vector3(1f, 2f, 3f),
                BallLocalVelocity = new Vector3(4f, 5f, 6f),
                BallAngularVelocity = new Vector3(7f, 8f, 9f),
                GoalkeeperLocalX = 0.5f,
                MotorState = GoalkeeperMotorState.Ready,
                DiveAction = GoalkeeperAction.Hold,
            };
            var settings = new GoalkeeperPartialObservationSettings
            {
                DelaySteps = 3,
                BallPositionNoiseMeters = 0.04f,
                BallVelocityNoiseMetersPerSecond = 0.25f,
                GoalkeeperPositionNoiseMeters = 0.03f,
                DropoutProbability = 0f,
                Seed = 123UL,
                ObservationIndex = 7,
            };

            var first = GoalkeeperTrainingContracts.PerturbVisibleState(snapshot, settings);
            var second = GoalkeeperTrainingContracts.PerturbVisibleState(snapshot, settings);
            Assert.That(first.BallLocalPosition, Is.EqualTo(second.BallLocalPosition));
            Assert.That(first.BallLocalVelocity, Is.EqualTo(second.BallLocalVelocity));
            Assert.That(first.GoalkeeperLocalX, Is.EqualTo(second.GoalkeeperLocalX));

            settings.DropoutProbability = 1f;
            var dropped = GoalkeeperTrainingContracts.PerturbVisibleState(snapshot, settings);
            Assert.That(dropped.BallLocalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(dropped.BallLocalVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(dropped.BallAngularVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Stage2SparseRewardMapsOnlyTerminalGoalkeeperTaskOutcomes()
        {
            Assert.That(
                GoalkeeperTrainingContracts.SparseReward(AttemptOutcome.Saved),
                Is.EqualTo(1f));
            Assert.That(
                GoalkeeperTrainingContracts.SparseReward(AttemptOutcome.BlockedThenOut),
                Is.EqualTo(1f));
            Assert.That(
                GoalkeeperTrainingContracts.SparseReward(AttemptOutcome.Goal),
                Is.EqualTo(-1f));
            Assert.That(
                GoalkeeperTrainingContracts.SparseReward(AttemptOutcome.Invalid),
                Is.Zero);
            Assert.That(
                GoalkeeperTrainingContracts.SparseReward(AttemptOutcome.Timeout),
                Is.Zero);
        }

        [Test]
        public void Stage3BenchmarkMasterSeedArgumentParsesStableForms()
        {
            Assert.That(
                Stage3BenchmarkRuntime.TryParseMasterSeed(
                    new[] { "player", "--stage3-master-seed=20260723" },
                    out var inlineSeed),
                Is.True);
            Assert.That(inlineSeed, Is.EqualTo(20260723UL));

            Assert.That(
                Stage3BenchmarkRuntime.TryParseMasterSeed(
                    new[] { "player", "--stage3-master-seed", "99" },
                    out var separatedSeed),
                Is.True);
            Assert.That(separatedSeed, Is.EqualTo(99UL));

            Assert.That(
                Stage3BenchmarkRuntime.TryParseMasterSeed(
                    new[] { "player", "--stage3-master-seed=not-a-seed" },
                    out _),
                Is.False);
        }

        [Test]
        public void CurriculumTargetRangeValidationPreservesFullRangeDefault()
        {
            Assert.That(shots.Validate(out var error), Is.True, error);
            Assert.That(shots.MinimumTargetXNormalized, Is.EqualTo(-1f));
            Assert.That(shots.MaximumTargetXNormalized, Is.EqualTo(1f));
            Assert.That(shots.MinimumTargetYNormalized, Is.EqualTo(0f));
            Assert.That(shots.MaximumTargetYNormalized, Is.EqualTo(1f));

            shots.MinimumTargetXNormalized = -0.25f;
            shots.MaximumTargetXNormalized = 0.25f;
            shots.MinimumTargetYNormalized = 0.45f;
            shots.MaximumTargetYNormalized = 0.55f;
            var scenario = ProceduralShotGenerator.Sample(
                shots,
                20260724UL,
                Physics.gravity,
                environment.FixedTimestep);
            Assert.That(scenario.TargetXNormalized, Is.InRange(-0.25f, 0.25f));
            Assert.That(scenario.TargetYNormalized, Is.InRange(0.45f, 0.55f));

            shots.MinimumTargetXNormalized = 0.5f;
            shots.MaximumTargetXNormalized = -0.5f;
            Assert.That(shots.Validate(out _), Is.False);
        }

        [Test]
        public void ReachFocusSamplingIsOptionalDeterministicAndInRange()
        {
            var baseline =
                ProceduralShotGenerator.Sample(
                    shots,
                    20260729UL,
                    Physics.gravity,
                    environment.FixedTimestep);
            shots.ReachFocusMinimumAbsoluteXNormalized = 0.8f;
            shots.ReachFocusMaximumAbsoluteXNormalized = 0.9f;
            shots.ReachFocusMinimumYNormalized = 0.7f;
            shots.ReachFocusMaximumYNormalized = 0.8f;
            var focusDisabled =
                ProceduralShotGenerator.Sample(
                    shots,
                    20260729UL,
                    Physics.gravity,
                    environment.FixedTimestep);
            Assert.That(focusDisabled.ReachFocusSample, Is.False);
            Assert.That(
                focusDisabled.TargetXNormalized,
                Is.EqualTo(baseline.TargetXNormalized));
            Assert.That(
                focusDisabled.TargetYNormalized,
                Is.EqualTo(baseline.TargetYNormalized));
            Assert.That(focusDisabled.FlightTime, Is.EqualTo(baseline.FlightTime));
            Assert.That(focusDisabled.LaunchDelay, Is.EqualTo(baseline.LaunchDelay));

            shots.ReachFocusProbability = 1f;
            var sawLeft = false;
            var sawRight = false;
            for (ulong seed = 1; seed <= 256; seed++)
            {
                var scenario =
                    ProceduralShotGenerator.Sample(
                        shots,
                        seed,
                        Physics.gravity,
                        environment.FixedTimestep);
                var repeated =
                    ProceduralShotGenerator.Sample(
                        shots,
                        seed,
                        Physics.gravity,
                        environment.FixedTimestep);
                Assert.That(scenario.ReachFocusSample, Is.True);
                Assert.That(
                    Mathf.Abs(scenario.TargetXNormalized),
                    Is.InRange(0.8f, 0.9f));
                Assert.That(
                    scenario.TargetYNormalized,
                    Is.InRange(0.7f, 0.8f));
                Assert.That(
                    repeated.TargetXNormalized,
                    Is.EqualTo(scenario.TargetXNormalized));
                Assert.That(
                    ProceduralShotGenerator.ValidateOnTarget(
                        scenario,
                        shots,
                        out var error),
                    Is.True,
                    error);
                sawLeft |= scenario.TargetXNormalized < 0f;
                sawRight |= scenario.TargetXNormalized > 0f;
            }

            Assert.That(sawLeft, Is.True);
            Assert.That(sawRight, Is.True);
        }

        [Test]
        public void ReachFocusBalancedHeightBandsCoverEveryBandDeterministically()
        {
            shots.ReachFocusProbability = 1f;
            shots.ReachFocusMinimumAbsoluteXNormalized = 0.4f;
            shots.ReachFocusMaximumAbsoluteXNormalized = 0.9f;
            shots.ReachFocusMinimumYNormalized = 0.03f;
            shots.ReachFocusMaximumYNormalized = 0.97f;
            shots.ReachFocusBalancedHeightBands = true;
            var bandCounts = new int[3];
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var scenario = ProceduralShotGenerator.Sample(
                    shots,
                    seed,
                    Physics.gravity,
                    environment.FixedTimestep);
                var repeated = ProceduralShotGenerator.Sample(
                    shots,
                    seed,
                    Physics.gravity,
                    environment.FixedTimestep);
                var normalizedBand =
                    Mathf.InverseLerp(
                        shots.ReachFocusMinimumYNormalized,
                        shots.ReachFocusMaximumYNormalized,
                        scenario.TargetYNormalized);
                var band = Mathf.Min(
                    2,
                    Mathf.FloorToInt(normalizedBand * 3f));
                bandCounts[band]++;
                Assert.That(
                    repeated.TargetYNormalized,
                    Is.EqualTo(scenario.TargetYNormalized));
            }

            Assert.That(bandCounts, Is.All.GreaterThan(50));
        }

        [Test]
        public void Stage5ReachTrainingRewardPrefersSuccessfulGloveContactOnly()
        {
            var gloveSave = new AttemptResult
            {
                Outcome = AttemptOutcome.Saved,
                GloveContact = true,
            };
            var bodySave = new AttemptResult
            {
                Outcome = AttemptOutcome.BlockedThenOut,
                GloveContact = false,
            };
            var gloveThenGoal = new AttemptResult
            {
                Outcome = AttemptOutcome.Goal,
                GloveContact = true,
            };
            var invalid = new AttemptResult
            {
                Outcome = AttemptOutcome.Invalid,
                GloveContact = true,
            };

            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    gloveSave,
                    true),
                Is.EqualTo(1f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    bodySave,
                    true),
                Is.EqualTo(0.8f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    gloveThenGoal,
                    true),
                Is.EqualTo(-1f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    invalid,
                    true),
                Is.Zero);
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    bodySave,
                    false),
                Is.EqualTo(1f));
        }

        [Test]
        public void Stage5ReachV2RewardStronglyPrefersGloveSaves()
        {
            var gloveSave = new AttemptResult
            {
                Outcome = AttemptOutcome.Saved,
                GloveContact = true,
                MinimumGloveBallDistance = 0f,
            };
            var bodySave = new AttemptResult
            {
                Outcome = AttemptOutcome.BlockedThenOut,
                MinimumGloveBallDistance = 0.5f,
            };
            var gloveGoal = new AttemptResult
            {
                Outcome = AttemptOutcome.Goal,
                GloveContact = true,
                MinimumGloveBallDistance = 0f,
            };
            var closeGoal = new AttemptResult
            {
                Outcome = AttemptOutcome.Goal,
                MinimumGloveBallDistance = 0f,
            };
            var distantGoal = new AttemptResult
            {
                Outcome = AttemptOutcome.Goal,
                MinimumGloveBallDistance = 1f,
            };

            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    gloveSave,
                    true,
                    2),
                Is.EqualTo(1f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    bodySave,
                    true,
                    2),
                Is.EqualTo(0.25f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    gloveGoal,
                    true,
                    2),
                Is.EqualTo(-0.75f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    closeGoal,
                    true,
                    2),
                Is.EqualTo(-0.85f).Within(1e-5f));
            Assert.That(
                GoalkeeperControlTrainingContracts.TrainingReward(
                    distantGoal,
                    true,
                    2),
                Is.EqualTo(-1f));
        }

        [Test]
        public void Stage5VisibleTimeToPlaneAndV2CommitGuardAreDeterministic()
        {
            Assert.That(
                GoalkeeperControlTrainingContracts
                    .EstimateVisibleTimeToGoalPlane(
                        new Vector3(0f, 1f, 10f),
                        new Vector3(0f, 0f, -20f)),
                Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(
                GoalkeeperControlTrainingContracts
                    .EstimateVisibleTimeToGoalPlane(
                        new Vector3(0f, 1f, 10f),
                        Vector3.zero),
                Is.EqualTo(-1f));

            var motorMask = new GoalkeeperControlActionMask(true);
            var early = new GoalkeeperControlDecisionContext(
                1,
                2,
                3,
                0.04f,
                0.9f);
            var inWindow = new GoalkeeperControlDecisionContext(
                1,
                3,
                4,
                0.20f,
                0.6f);
            Assert.That(
                GoalkeeperControlTrainingContracts.ApplyCommitGuard(
                    motorMask,
                    early,
                    true,
                    2,
                    1).CanCommit,
                Is.False);
            Assert.That(
                GoalkeeperControlTrainingContracts.ApplyCommitGuard(
                    motorMask,
                    inWindow,
                    true,
                    2,
                    1).CanCommit,
                Is.True);
            Assert.That(
                GoalkeeperControlTrainingContracts.ApplyCommitGuard(
                    motorMask,
                    early,
                    true,
                    2,
                    3).CanCommit,
                Is.True);
        }

        [Test]
        public void Stage5ReachV2ScaffoldReleasesCommitAndReachByLessonThree()
        {
            var command = GoalkeeperControlCommand.Neutral;
            command.AimX = -0.5f;
            command.AimY = 0.25f;
            var inWindow = new GoalkeeperControlDecisionContext(
                1,
                2,
                3,
                0.2f,
                0.6f);
            var mask = new GoalkeeperControlActionMask(true);
            var guided =
                GoalkeeperControlTrainingContracts.ApplyScaffold(
                    command,
                    inWindow,
                    mask,
                    true,
                    2,
                    0,
                    out var autoCommit,
                    out var reachFloor);
            Assert.That(guided.Commit, Is.True);
            Assert.That(guided.Reach01, Is.EqualTo(1f));
            Assert.That(autoCommit, Is.True);
            Assert.That(reachFloor, Is.True);

            var independent =
                GoalkeeperControlTrainingContracts.ApplyScaffold(
                    command,
                    inWindow,
                    mask,
                    true,
                    2,
                    3,
                    out autoCommit,
                    out reachFloor);
            Assert.That(independent.Commit, Is.False);
            Assert.That(independent.Reach, Is.EqualTo(-1f));
            Assert.That(autoCommit, Is.False);
            Assert.That(reachFloor, Is.False);
        }

        [Test]
        public void Stage5ReachScaffoldingEndsBeforeIndependentReachLesson()
        {
            var command = GoalkeeperControlCommand.Neutral;
            command.AimX = 0.4f;
            command.AimY = 0.7f;
            var context = new GoalkeeperControlDecisionContext(
                1,
                2,
                3,
                0.12f);
            var mask = new GoalkeeperControlActionMask(true);

            var lessonZero =
                GoalkeeperControlTrainingContracts.ApplyScaffold(
                    command,
                    context,
                    mask,
                    true,
                    0,
                    out var autoCommit,
                    out var reachFloor);
            Assert.That(lessonZero.Commit, Is.True);
            Assert.That(lessonZero.Reach, Is.EqualTo(1f));
            Assert.That(lessonZero.AimX, Is.EqualTo(command.AimX));
            Assert.That(lessonZero.AimY, Is.EqualTo(command.AimY));
            Assert.That(autoCommit, Is.True);
            Assert.That(reachFloor, Is.True);

            var lessonTwo =
                GoalkeeperControlTrainingContracts.ApplyScaffold(
                    command,
                    context,
                    mask,
                    true,
                    2,
                    out autoCommit,
                    out reachFloor);
            Assert.That(lessonTwo.Commit, Is.False);
            Assert.That(lessonTwo.Reach, Is.EqualTo(-1f));
            Assert.That(autoCommit, Is.False);
            Assert.That(reachFloor, Is.False);

            var disabled =
                GoalkeeperControlTrainingContracts.ApplyScaffold(
                    command,
                    context,
                    mask,
                    false,
                    0,
                    out autoCommit,
                    out reachFloor);
            Assert.That(disabled.Commit, Is.False);
            Assert.That(disabled.Reach, Is.EqualTo(-1f));
            Assert.That(autoCommit, Is.False);
            Assert.That(reachFloor, Is.False);
        }

        [Test]
        public void Pcg32MatchesPublishedGoldenSequence()
        {
            var random = new Pcg32(42UL);
            Assert.That(random.NextUInt(), Is.EqualTo(492690617U));
            Assert.That(random.NextUInt(), Is.EqualTo(1919685028U));
            Assert.That(random.NextUInt(), Is.EqualTo(3561993920U));
            Assert.That(random.NextUInt(), Is.EqualTo(683038915U));
            Assert.That(random.NextUInt(), Is.EqualTo(1183706632U));
        }

        [Test]
        public void DerivedSeedsAreStableAndArenaIsolated()
        {
            var first = Pcg32.DeriveSeed(1234UL, 0, 100);
            Assert.That(Pcg32.DeriveSeed(1234UL, 0, 100), Is.EqualTo(first));
            Assert.That(Pcg32.DeriveSeed(1234UL, 1, 100), Is.Not.EqualTo(first));
            Assert.That(Pcg32.DeriveSeed(1234UL, 0, 101), Is.Not.EqualTo(first));
        }

        [Test]
        public void SampledShotsRemainInsideDeclaredOnTargetRegion()
        {
            for (ulong seed = 1; seed <= 10000; seed++)
            {
                var scenario = ProceduralShotGenerator.Sample(
                    shots,
                    seed,
                    Physics.gravity,
                    environment.FixedTimestep);
                Assert.That(
                    ProceduralShotGenerator.ValidateOnTarget(
                        scenario,
                        shots,
                        out var error),
                    Is.True,
                    error);
                Assert.That(scenario.TargetXNormalized, Is.InRange(-1f, 1f));
                Assert.That(scenario.TargetYNormalized, Is.InRange(0f, 1f));
                Assert.That(
                    scenario.FlightTime,
                    Is.InRange(shots.MinimumFlightTime, shots.MaximumFlightTime));
                Assert.That(
                    scenario.LaunchDelay,
                    Is.InRange(shots.MinimumLaunchDelay, shots.MaximumLaunchDelay));
                Assert.That(scenario.Spin, Is.EqualTo(Vector3.zero));
            }
        }

        [Test]
        public void PhysXSolverReconstructsTargetAcrossSupportedRange()
        {
            for (ulong seed = 1; seed <= 10000; seed++)
            {
                var scenario = ProceduralShotGenerator.Sample(
                    shots,
                    seed,
                    Physics.gravity,
                    environment.FixedTimestep);
                var correctedContinuousVelocity =
                    scenario.LaunchVelocityLocal +
                    0.5f * Physics.gravity * environment.FixedTimestep;
                var time = scenario.FlightTime;
                var reconstructed =
                    KernelConstants.CanonicalLaunch +
                    correctedContinuousVelocity * time +
                    0.5f * Physics.gravity * time * time;
                Assert.That(
                    Vector3.Distance(reconstructed, scenario.TargetLocal),
                    Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void WholeBallGoalGeometryHonoursPostsCrossbarAndPartialCrossing()
        {
            var valid = new Vector3(0f, 1.2f, -KernelConstants.BallRadius);
            Assert.That(KernelGoalGeometry.IsWholeBallInsideGoal(valid), Is.True);

            var tooWide = new Vector3(
                KernelConstants.GoalHalfWidth - KernelConstants.BallRadius + 0.001f,
                1.2f,
                -KernelConstants.BallRadius);
            Assert.That(KernelGoalGeometry.IsWholeBallInsideGoal(tooWide), Is.False);

            var tooHigh = new Vector3(
                0f,
                KernelConstants.CrossbarLowerEdge - KernelConstants.BallRadius + 0.001f,
                -KernelConstants.BallRadius);
            Assert.That(KernelGoalGeometry.IsWholeBallInsideGoal(tooHigh), Is.False);

            Assert.That(
                KernelGoalGeometry.TryIntersectPlane(
                    new Vector3(0f, 1f, 0.2f),
                    new Vector3(0f, 1f, -0.05f),
                    -KernelConstants.BallRadius,
                    out _),
                Is.False);
        }

        [Test]
        public void GoalTakesPrecedenceAfterKeeperOrFrameContact()
        {
            var contacts = new ContactHistory();
            contacts.Record(
                ContactKind.Goalkeeper,
                0.2f,
                GoalkeeperContactPart.LeftGlove);
            contacts.Record(ContactKind.GoalFrame, 0.3f);
            var result = OutcomeResolver.ResolveGoalPlaneCrossing(
                new Vector3(0f, 1.2f, -KernelConstants.BallRadius),
                contacts);
            Assert.That(result, Is.EqualTo(AttemptOutcome.Goal));
            Assert.That(contacts.GloveTouched, Is.True);
            Assert.That(contacts.GloveContactCount, Is.EqualTo(1));
            Assert.That(
                contacts.LastGoalkeeperContactPart,
                Is.EqualTo(GoalkeeperContactPart.LeftGlove));
        }

        [Test]
        public void ContactHistoryPreservesFirstGoalkeeperContactAcrossReset()
        {
            var contacts = new ContactHistory();
            contacts.Record(
                ContactKind.Goalkeeper,
                0.4f,
                GoalkeeperContactPart.TorsoOrHead);
            contacts.Record(
                ContactKind.Goalkeeper,
                0.5f,
                GoalkeeperContactPart.RightGlove);
            Assert.That(
                contacts.FirstGoalkeeperContactPart,
                Is.EqualTo(GoalkeeperContactPart.TorsoOrHead));
            Assert.That(
                contacts.FirstGoalkeeperContactTime,
                Is.EqualTo(0.4f));
            Assert.That(
                contacts.LastGoalkeeperContactPart,
                Is.EqualTo(GoalkeeperContactPart.RightGlove));

            contacts.Reset();
            Assert.That(
                contacts.FirstGoalkeeperContactPart,
                Is.EqualTo(GoalkeeperContactPart.None));
            Assert.That(
                contacts.FirstGoalkeeperContactTime,
                Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void ContactProvenanceDeterminesSafeOutcomes()
        {
            var keeper = new ContactHistory();
            keeper.Record(ContactKind.Goalkeeper, 0.2f);
            Assert.That(
                OutcomeResolver.ResolveGoalPlaneCrossing(
                    new Vector3(5f, 1f, -KernelConstants.BallRadius),
                    keeper),
                Is.EqualTo(AttemptOutcome.BlockedThenOut));

            var frame = new ContactHistory();
            frame.Record(ContactKind.GoalFrame, 0.2f);
            Assert.That(
                OutcomeResolver.ResolveGoalPlaneCrossing(
                    new Vector3(5f, 1f, -KernelConstants.BallRadius),
                    frame),
                Is.EqualTo(AttemptOutcome.PostOrCrossbarOut));

            Assert.That(
                OutcomeResolver.ResolveGoalPlaneCrossing(
                    new Vector3(5f, 1f, -KernelConstants.BallRadius),
                    new ContactHistory()),
                Is.EqualTo(AttemptOutcome.MissWide));
        }

        [Test]
        public void SaveRequiresRestDwellOrPostContactHorizon()
        {
            var contacts = new ContactHistory();
            contacts.Record(ContactKind.Goalkeeper, 0.5f);
            var restTime = 0f;
            Assert.That(
                OutcomeResolver.TryResolveSave(
                    contacts,
                    0.6f,
                    0.1f,
                    0.1f,
                    environment,
                    ref restTime),
                Is.False);
            Assert.That(
                OutcomeResolver.TryResolveSave(
                    contacts,
                    0.8f,
                    0.1f,
                    0.2f,
                    environment,
                    ref restTime),
                Is.True);

            restTime = 0f;
            Assert.That(
                OutcomeResolver.TryResolveSave(
                    contacts,
                    2.5f,
                    2f,
                    0.02f,
                    environment,
                    ref restTime),
                Is.True);
        }

        [Test]
        public void DiveMacroMasksFurtherMovementUntilRecovery()
        {
            var root = new GameObject("MotorTestRoot");
            var keeper = new GameObject("Keeper");
            keeper.transform.SetParent(root.transform, false);
            keeper.AddComponent<Rigidbody>();
            var goalkeeper = keeper.AddComponent<GoalkeeperMotor>();
            goalkeeper.Configuration = motor;
            goalkeeper.ArenaOrigin = root.transform;
            goalkeeper.ResetForAttempt(1, 1UL);

            Assert.That(goalkeeper.TryApplyAction(GoalkeeperAction.DiveLeftHigh), Is.True);
            Assert.That(goalkeeper.State, Is.EqualTo(GoalkeeperMotorState.Diving));
            var mask = goalkeeper.GetActionMask();
            Assert.That(mask.IsAllowed(GoalkeeperAction.Hold), Is.True);
            Assert.That(mask.IsAllowed(GoalkeeperAction.ShuffleLeft), Is.False);
            Assert.That(mask.IsAllowed(GoalkeeperAction.DiveRightHigh), Is.False);
            Assert.That(goalkeeper.TryApplyAction(GoalkeeperAction.DiveRightHigh), Is.False);

            for (var index = 0; index < 100; index++)
            {
                goalkeeper.Tick(0.02f);
            }

            Assert.That(goalkeeper.State, Is.EqualTo(GoalkeeperMotorState.Ready));
            Assert.That(goalkeeper.LocalPosition.x, Is.LessThan(0f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void DiveProfilesAreMirroredAndHeightSeparated()
        {
            var leftLow = SampleDive(GoalkeeperAction.DiveLeftLow);
            var leftMiddle = SampleDive(GoalkeeperAction.DiveLeftMiddle);
            var leftHigh = SampleDive(GoalkeeperAction.DiveLeftHigh);
            var rightLow = SampleDive(GoalkeeperAction.DiveRightLow);
            var rightMiddle = SampleDive(GoalkeeperAction.DiveRightMiddle);
            var rightHigh = SampleDive(GoalkeeperAction.DiveRightHigh);

            Assert.That(leftLow.x, Is.LessThan(0f));
            Assert.That(leftMiddle.x, Is.LessThan(0f));
            Assert.That(leftHigh.x, Is.LessThan(0f));
            Assert.That(rightLow.x, Is.GreaterThan(0f));
            Assert.That(rightMiddle.x, Is.GreaterThan(0f));
            Assert.That(rightHigh.x, Is.GreaterThan(0f));
            Assert.That(Mathf.Abs(leftLow.x), Is.EqualTo(Mathf.Abs(rightLow.x)).Within(1e-4f));
            Assert.That(
                Mathf.Abs(leftMiddle.x),
                Is.EqualTo(Mathf.Abs(rightMiddle.x)).Within(1e-4f));
            Assert.That(
                Mathf.Abs(leftHigh.x),
                Is.EqualTo(Mathf.Abs(rightHigh.x)).Within(1e-4f));
            Assert.That(leftMiddle.y, Is.GreaterThan(leftLow.y + 0.1f));
            Assert.That(leftHigh.y, Is.GreaterThan(leftMiddle.y + 0.1f));
        }

        [Test]
        public void HandTargetsAreMirroredAndTierOrdered()
        {
            var body = new Vector3(0.25f, 0.4f, motor.StandingZ);
            var leftLow = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveLeftLow,
                body);
            var rightLow = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveRightLow,
                body);
            var leftMiddle = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveLeftMiddle,
                body);
            var leftHigh = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveLeftHigh,
                body);

            Assert.That(
                leftLow.LeftGlove.x - body.x,
                Is.EqualTo(-(rightLow.RightGlove.x - body.x)).Within(1e-6f));
            Assert.That(
                leftLow.RightGlove.x - body.x,
                Is.EqualTo(-(rightLow.LeftGlove.x - body.x)).Within(1e-6f));
            Assert.That(
                leftLow.LeftGlove.y,
                Is.EqualTo(rightLow.RightGlove.y).Within(1e-6f));
            Assert.That(leftMiddle.LeftGlove.y, Is.GreaterThan(leftLow.LeftGlove.y));
            Assert.That(leftHigh.LeftGlove.y, Is.GreaterThan(leftMiddle.LeftGlove.y));
            Assert.That(
                Mathf.Abs(leftHigh.LeftGlove.x - body.x),
                Is.GreaterThan(Mathf.Abs(leftMiddle.LeftGlove.x - body.x)));
        }

        [Test]
        public void HandReachUsesDeclaredStartAndFullExtensionThresholds()
        {
            Assert.That(
                GoalkeeperReachRig.EvaluateDiveExtension(
                    motor,
                    motor.ReachStartNormalized - 0.001f),
                Is.Zero);
            Assert.That(
                GoalkeeperReachRig.EvaluateDiveExtension(
                    motor,
                    motor.ReachStartNormalized),
                Is.Zero);
            Assert.That(
                GoalkeeperReachRig.EvaluateDiveExtension(
                    motor,
                    (motor.ReachStartNormalized + motor.FullExtensionNormalized) * 0.5f),
                Is.InRange(0.49f, 0.51f));
            Assert.That(
                GoalkeeperReachRig.EvaluateDiveExtension(
                    motor,
                    motor.FullExtensionNormalized),
                Is.EqualTo(1f));
            Assert.That(
                GoalkeeperReachRig.EvaluateDiveExtension(motor, 1f),
                Is.EqualTo(1f));
        }

        [Test]
        public void ReachPoseRecoversAndResetsWithoutLeakage()
        {
            var root = CreateReachRig(out var rig);
            var readyLeft = rig.LeftGlove.localPosition;
            var readyRight = rig.RightGlove.localPosition;

            rig.ApplyDivePose(
                GoalkeeperAction.DiveRightHigh,
                motor.FullExtensionNormalized,
                Vector3.zero,
                root.transform.position,
                root.transform.rotation);
            Assert.That(rig.LeftGlove.localPosition, Is.Not.EqualTo(readyLeft));
            Assert.That(rig.RightGlove.localPosition, Is.Not.EqualTo(readyRight));

            rig.ApplyRecoveryPose(
                GoalkeeperAction.DiveRightHigh,
                1f,
                Vector3.zero,
                root.transform.position,
                root.transform.rotation);
            Assert.That(rig.LeftGlove.localPosition, Is.EqualTo(readyLeft));
            Assert.That(rig.RightGlove.localPosition, Is.EqualTo(readyRight));
            Assert.That(rig.ValidateReset(out var error), Is.True, error);

            rig.ApplyDivePose(
                GoalkeeperAction.DiveLeftLow,
                1f,
                Vector3.zero,
                root.transform.position,
                root.transform.rotation);
            rig.ResetForAttempt(2, 123UL);
            Assert.That(rig.ValidateReset(out error), Is.True, error);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void RolledHighDiveDoesNotOverextendArmCapsules()
        {
            var root = CreateReachRig(out var rig);
            var bodyPosition = new Vector3(1.4f, motor.HighDiveHeight, motor.StandingZ);
            var bodyRotation = Quaternion.Euler(0f, 0f, -motor.MaximumBodyRollDegrees);

            rig.ApplyDivePose(
                GoalkeeperAction.DiveRightHigh,
                1f,
                bodyPosition,
                bodyPosition,
                bodyRotation);

            Assert.That(
                ArmLength(rig.LeftArm),
                Is.LessThanOrEqualTo(motor.MaximumArmLength + 1e-5f));
            Assert.That(
                ArmLength(rig.RightArm),
                Is.LessThanOrEqualTo(motor.MaximumArmLength + 1e-5f));
            Object.DestroyImmediate(root);
        }

        [Test]
        public void MatchingGloveHasControlledCoverageButWrongSideAndHeightDoNot()
        {
            var root = CreateReachRig(out var rig);
            var target = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveRightHigh,
                Vector3.zero).RightGlove;
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            probe.name = "BallProbe";
            probe.transform.position = target;
            probe.transform.localScale = Vector3.one * (KernelConstants.BallRadius * 2f);
            var probeCollider = probe.GetComponent<Collider>();

            ApplyFullReach(rig, root.transform, GoalkeeperAction.DiveRightHigh);
            Assert.That(
                Physics.ComputePenetration(
                    probeCollider,
                    probe.transform.position,
                    probe.transform.rotation,
                    rig.RightGlove.GetComponent<Collider>(),
                    rig.RightGlove.position,
                    rig.RightGlove.rotation,
                    out _,
                    out _),
                Is.True);

            ApplyFullReach(rig, root.transform, GoalkeeperAction.DiveLeftHigh);
            Assert.That(
                Physics.ComputePenetration(
                    probeCollider,
                    probe.transform.position,
                    probe.transform.rotation,
                    rig.LeftGlove.GetComponent<Collider>(),
                    rig.LeftGlove.position,
                    rig.LeftGlove.rotation,
                    out _,
                    out _),
                Is.False);

            ApplyFullReach(rig, root.transform, GoalkeeperAction.DiveRightLow);
            Assert.That(
                Physics.ComputePenetration(
                    probeCollider,
                    probe.transform.position,
                    probe.transform.rotation,
                    rig.RightGlove.GetComponent<Collider>(),
                    rig.RightGlove.position,
                    rig.RightGlove.rotation,
                    out _,
                    out _),
                Is.False);

            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void ReachTargetsDependOnlyOnActionPhaseAndBodyPose()
        {
            var body = new Vector3(-0.3f, 0.2f, motor.StandingZ);
            var first = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                motor,
                GoalkeeperAction.DiveLeftMiddle,
                body);
            for (var index = 0; index < 100; index++)
            {
                var repeated = GoalkeeperReachRig.EvaluateFullExtensionTargets(
                    motor,
                    GoalkeeperAction.DiveLeftMiddle,
                    body);
                Assert.That(repeated.LeftGlove, Is.EqualTo(first.LeftGlove));
                Assert.That(repeated.RightGlove, Is.EqualTo(first.RightGlove));
            }
        }

        [Test]
        public void AttemptResetClearsContactAndOutcomeState()
        {
            var contacts = new ContactHistory();
            contacts.Record(
                ContactKind.Goalkeeper,
                0.4f,
                GoalkeeperContactPart.RightGlove);
            contacts.Record(ContactKind.GoalFrame, 0.5f);
            contacts.Reset();
            Assert.That(contacts.GoalkeeperTouched, Is.False);
            Assert.That(contacts.GoalFrameTouched, Is.False);
            Assert.That(contacts.GoalkeeperContactCount, Is.Zero);
            Assert.That(contacts.GoalFrameContactCount, Is.Zero);
            Assert.That(float.IsNegativeInfinity(contacts.LastGoalkeeperContactTime), Is.True);
            Assert.That(float.IsNegativeInfinity(contacts.LastGoalFrameContactTime), Is.True);
            Assert.That(contacts.GloveTouched, Is.False);
            Assert.That(contacts.GloveContactCount, Is.Zero);
            Assert.That(
                contacts.LastGoalkeeperContactPart,
                Is.EqualTo(GoalkeeperContactPart.None));

            var outcome = new AttemptOutcomeLatch();
            Assert.That(outcome.TrySet(AttemptOutcome.Goal), Is.True);
            Assert.That(outcome.TrySet(AttemptOutcome.Saved), Is.False);
            outcome.Reset();
            Assert.That(outcome.Outcome, Is.EqualTo(AttemptOutcome.None));
            Assert.That(outcome.DuplicateTerminalEvents, Is.Zero);
        }

        [Test]
        public void ManifestIsStableAndIncludesEveryVersionedContract()
        {
            var first = KernelManifestUtility.CreateJson(environment, shots, motor);
            var second = KernelManifestUtility.CreateJson(environment, shots, motor);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(KernelManifestUtility.Sha256(first), Has.Length.EqualTo(64));
            StringAssert.Contains(KernelConstants.EnvironmentId, first);
            StringAssert.Contains(KernelConstants.ScenarioSuiteId, first);
            StringAssert.Contains(KernelConstants.ActionSpecId, first);
            StringAssert.Contains(KernelConstants.MotorProfileId, first);
            StringAssert.Contains("goalkeeper_contact_parts", first);
            StringAssert.Contains("full_extension_normalized", first);
        }

        private GameObject CreateReachRig(out GoalkeeperReachRig rig)
        {
            var root = new GameObject("ReachRigTest");
            var leftShoulder = new GameObject("LeftShoulder").transform;
            leftShoulder.SetParent(root.transform, false);
            var rightShoulder = new GameObject("RightShoulder").transform;
            rightShoulder.SetParent(root.transform, false);
            var leftArm = CreatePrimitiveChild(root.transform, PrimitiveType.Capsule, "LeftArm");
            var rightArm = CreatePrimitiveChild(root.transform, PrimitiveType.Capsule, "RightArm");
            var leftGlove = CreatePrimitiveChild(root.transform, PrimitiveType.Sphere, "LeftGlove");
            var rightGlove = CreatePrimitiveChild(root.transform, PrimitiveType.Sphere, "RightGlove");
            rig = root.AddComponent<GoalkeeperReachRig>();
            rig.Configure(
                motor,
                null,
                leftShoulder,
                rightShoulder,
                leftArm.transform,
                rightArm.transform,
                leftGlove.transform,
                rightGlove.transform);
            return root;
        }

        private static GameObject CreatePrimitiveChild(
            Transform parent,
            PrimitiveType primitive,
            string name)
        {
            var child = GameObject.CreatePrimitive(primitive);
            child.name = name;
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void ApplyFullReach(
            GoalkeeperReachRig rig,
            Transform root,
            GoalkeeperAction action)
        {
            rig.ApplyDivePose(
                action,
                1f,
                Vector3.zero,
                root.position,
                root.rotation);
            Physics.SyncTransforms();
        }

        private static float ArmLength(Transform arm)
        {
            return arm.localScale.y * 2f;
        }

        private Vector3 SampleDive(GoalkeeperAction action)
        {
            var root = new GameObject($"MotorRoot_{action}");
            var keeper = new GameObject($"Keeper_{action}");
            keeper.transform.SetParent(root.transform, false);
            keeper.AddComponent<Rigidbody>();
            var goalkeeper = keeper.AddComponent<GoalkeeperMotor>();
            goalkeeper.Configuration = motor;
            goalkeeper.ArenaOrigin = root.transform;
            goalkeeper.ResetForAttempt(1, 1UL);
            Assert.That(goalkeeper.TryApplyAction(action), Is.True);
            for (var index = 0; index < 18; index++)
            {
                goalkeeper.Tick(0.02f);
            }

            var sampled = goalkeeper.LocalPosition;
            Object.DestroyImmediate(root);
            return sampled;
        }

        private GameObject CreateControlMotor(out GoalkeeperMotorV1 goalkeeper)
        {
            var root = new GameObject("ControlMotorRoot");
            var keeper = new GameObject("ControlKeeper");
            keeper.transform.SetParent(root.transform, false);
            keeper.AddComponent<Rigidbody>();
            CreatePrimitiveChild(
                keeper.transform,
                PrimitiveType.Capsule,
                "Torso");
            CreatePrimitiveChild(
                keeper.transform,
                PrimitiveType.Sphere,
                "Head");
            CreatePrimitiveChild(
                keeper.transform,
                PrimitiveType.Capsule,
                "LeftLeg");
            CreatePrimitiveChild(
                keeper.transform,
                PrimitiveType.Capsule,
                "RightLeg");
            goalkeeper = keeper.AddComponent<GoalkeeperMotorV1>();
            goalkeeper.Configuration = controlMotor;
            goalkeeper.ArenaOrigin = root.transform;
            return root;
        }

        private Vector3 SampleControlDive(float aimX, float aimY)
        {
            var root = CreateControlMotor(out var goalkeeper);
            goalkeeper.ResetForAttempt(1, 1UL);
            goalkeeper.TryApplyCommand(
                new GoalkeeperControlCommand
                {
                    AimX = aimX,
                    AimY = aimY,
                    Reach = 1f,
                    Commit = true,
                });
            for (var index = 0; index < 20; index++)
            {
                goalkeeper.Tick(0.02f);
            }

            var sampled = goalkeeper.LocalPosition;
            Object.DestroyImmediate(root);
            return sampled;
        }
    }
}
