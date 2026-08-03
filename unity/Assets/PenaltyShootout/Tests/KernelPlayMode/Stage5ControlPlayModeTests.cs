using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage5ControlPlayModeTests
    {
        [Serializable]
        private sealed class MotorValidationReport
        {
            public int schema_version = 1;
            public int stage = 5;
            public string status;
            public string behavior_name =
                KernelConstants.GoalkeeperControlV2BehaviorName;
            public string observation_spec_id =
                KernelConstants.GoalkeeperControlV2ObservationSpecId;
            public string action_spec_id =
                KernelConstants.GoalkeeperControlActionSpecId;
            public string motor_profile_id =
                KernelConstants.GoalkeeperControlMotorProfileId;
            public int attempts_per_policy;
            public int terminal_attempts;
            public int invalid_outcomes;
            public int timeout_outcomes;
            public int action_mask_violations;
            public int commitments;
            public int goalkeeper_contacts;
            public int glove_contacts;
            public float maximum_peak_reach;
            public bool passed;
        }

        [Test]
        [Category("Stage5Acceptance")]
        public void Stage5TelemetryPublishesHybridControlTrace()
        {
            var result = new AttemptResult
            {
                EnvironmentId = KernelConstants.EnvironmentId,
                ScenarioSuiteId = KernelConstants.ScenarioSuiteId,
                AttemptId = 11,
                ArenaId = 2,
                Seed = 20260723UL,
                Outcome = AttemptOutcome.Saved,
                ControlMode = GoalkeeperControlMode.HybridV1,
                InitialControlCommand = GoalkeeperControlCommand.Neutral,
                LastControlCommand = new GoalkeeperControlCommand
                {
                    MoveX = 0.25f,
                    AimX = -0.8f,
                    AimY = 0.6f,
                    Reach = 1f,
                },
                HasSaveCommitment = true,
                FirstCommitDecisionIndex = 2,
                FirstCommitAttemptTime = 0.52f,
                FirstCommitBallFlightTime = 0.12f,
                FirstCommitVisibleTimeToGoalPlane = 0.58f,
                FirstCommitReachDemand = 1f,
                FirstCommitReachExtension = 0.72f,
                FirstCommitWasImmediate = false,
                FirstCommitWasPremature = false,
                FirstCommitWasLate = false,
                FirstCommitWasTimely = true,
                FirstCommitAim = new Vector2(-0.8f, 0.6f),
                FirstCommitRawPolicyAim = new Vector2(-0.7f, 0.5f),
                HasFirstCommitVisiblePrediction = true,
                FirstCommitVisiblePredictedAim =
                    new Vector2(-0.75f, 0.55f),
                FirstCommitVisibleAimError = 0.12f,
                FirstCommitDesiredReach = 0.9f,
                FirstCommitReachShortfall = 0.1f,
                FirstEligibleCommitDecisionIndex = 2,
                FirstEligibleCommitBallFlightTime = 0.08f,
                FirstEligibleCommitVisibleTimeToGoalPlane = 0.62f,
                EligibleCommitDecisionsBeforeCommit = 1,
                FirstGoalkeeperContactPart =
                    GoalkeeperContactPart.LeftGlove,
                FirstGoalkeeperContactTime = 0.74f,
                HasFirstGoalkeeperContactKinematics = true,
                FirstGoalkeeperContactPointLocal = new Vector3(-1f, 0.4f, 0.5f),
                FirstGoalkeeperContactNormalLocal = Vector3.forward,
                FirstGoalkeeperContactImpulseLocal = new Vector3(0f, 1f, 2f),
                FirstGoalkeeperContactRelativeVelocityLocal =
                    new Vector3(3f, 4f, 5f),
                FirstGoalkeeperContactBallVelocityLocal =
                    new Vector3(6f, 7f, 8f),
                FirstGoalkeeperContactRootVelocityLocal =
                    new Vector3(-2f, -0.3f, 0f),
                FirstGoalkeeperContactLeftGloveVelocityLocal =
                    new Vector3(-3f, -0.4f, 0f),
                FirstGoalkeeperContactRightGloveVelocityLocal =
                    new Vector3(-2f, -0.2f, 0f),
                GoalkeeperRootDistance = 2.1f,
                GoalkeeperPeakRootSpeed = 5.4f,
                GoalkeeperPeakReachExtension = 1f,
                ControlTargetClampCount = 1,
                RootTargetSaturationDistance = 0.22f,
                TrainingDecisionShapingReward = 0.05f,
                PolicyActionOverrideCount = 0,
                MinimumGloveBallDistance = 0.04f,
                CommittedGloveForward = 0.28f,
                SampledShotFlightTime = 0.58f,
                SampledLaunchDelay = 0.24f,
                AcceptedControlDecisionCount = 5,
                ControlMoveCommandCount = 3,
                ControlReachCommandCount = 4,
                ControlAbsoluteActionSums =
                    new[] { 2f, 3f, 2.5f, 4f },
                ControlSaturationCounts =
                    new[] { 0, 1, 0, 2 },
                PolicyDecisionRequestCount = 6,
                PolicyDecisionConsumedCount = 5,
                PolicyDecisionDiscardedCount = 1,
                PolicyDecisionDuplicateRequestCount = 0,
                PolicyDecisionMissingActionCount = 0,
                NativeInferenceEvaluationCount = 6,
                NativeInferenceMaximumActionError = 0.00001f,
                NativeInferenceCommitMismatchCount = 0,
                NativeInferenceInvalidOutputCount = 0,
            };
            var json = GoalkeeperBenchmarkTelemetry.CreateJson(
                result,
                1f,
                KernelConstants.GoalkeeperControlV2ObservationSpecId);

            StringAssert.Contains(
                "\"behavior_name\":\"GoalkeeperControl-v2\"",
                json);
            StringAssert.Contains(
                "\"observation_spec_id\":\"control-state-v2\"",
                json);
            StringAssert.Contains(
                "\"action_spec_id\":\"goalkeeper-hybrid-v1\"",
                json);
            StringAssert.Contains("\"control_mode\":\"HybridV1\"", json);
            StringAssert.Contains("\"has_save_commitment\":true", json);
            StringAssert.Contains("\"first_commit_aim\"", json);
            StringAssert.Contains(
                "\"first_commit_visible_time_to_goal_plane\":",
                json);
            StringAssert.Contains("\"first_commit_reach_demand\":1.0", json);
            StringAssert.Contains(
                "\"first_goalkeeper_contact_part\":\"LeftGlove\"",
                json);
            StringAssert.Contains("\"goalkeeper_peak_reach_extension\":1.0", json);
            StringAssert.Contains("\"minimum_glove_ball_distance\"", json);
            StringAssert.Contains(
                "\"has_first_goalkeeper_contact_kinematics\":true",
                json);
            StringAssert.Contains(
                "\"first_goalkeeper_contact_ball_velocity_local\"",
                json);
            StringAssert.Contains(
                "\"first_goalkeeper_contact_left_glove_velocity_local\"",
                json);
            StringAssert.Contains("\"committed_glove_forward_m\":0.28", json);
            StringAssert.Contains(
                "\"first_commit_was_premature\":false",
                json);
            StringAssert.Contains(
                "\"first_commit_was_late\":false",
                json);
            StringAssert.Contains(
                "\"first_commit_was_timely\":true",
                json);
            StringAssert.Contains("\"first_commit_raw_policy_aim\"", json);
            StringAssert.Contains(
                "\"first_commit_visible_aim_error\":",
                json);
            StringAssert.Contains(
                "\"first_eligible_commit_decision_index\":2",
                json);
            StringAssert.Contains(
                "\"root_target_saturation_distance\":",
                json);
            StringAssert.Contains(
                "\"first_commit_desired_reach\":",
                json);
            StringAssert.Contains(
                "\"first_commit_reach_shortfall\":",
                json);
            StringAssert.Contains(
                "\"policy_action_override_count\":0",
                json);
            StringAssert.Contains("\"sampled_shot_flight_time\"", json);
            StringAssert.Contains(
                "\"accepted_control_decision_count\":5",
                json);
            StringAssert.Contains("\"control_saturation_counts\":[0,1,0,2]", json);
            StringAssert.Contains(
                "\"policy_decision_request_count\":6",
                json);
            StringAssert.Contains(
                "\"policy_decision_consumed_count\":5",
                json);
            StringAssert.Contains(
                "\"policy_decision_discarded_count\":1",
                json);
            StringAssert.Contains(
                "\"native_inference_evaluation_count\":6",
                json);
            StringAssert.Contains(
                "\"native_inference_maximum_action_error\":",
                json);
            StringAssert.Contains(
                "\"native_inference_commit_mismatch_count\":0",
                json);
        }

        [UnityTest]
        [Category("Stage5Acceptance")]
        public IEnumerator Stage5ScenePublishesHybridSpecAndScriptedBatchesTerminate()
        {
            var load = SceneManager.LoadSceneAsync(
                "ControlTraining",
                LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            PenaltyAreaController template = null;
            foreach (var candidate in
                     UnityEngine.Object.FindObjectsByType<PenaltyAreaController>(
                         FindObjectsSortMode.None))
            {
                if (candidate.ArenaId == 0)
                {
                    template = candidate;
                    break;
                }
            }
            Assert.That(template, Is.Not.Null);
            Assert.That(
                template.ControlMode,
                Is.EqualTo(GoalkeeperControlMode.HybridV1));
            var agent =
                template.GetComponentInChildren<GoalkeeperControlAgent>();
            var behavior =
                agent == null ? null : agent.GetComponent<BehaviorParameters>();
            Assert.That(agent, Is.Not.Null);
            Assert.That(behavior, Is.Not.Null);
            Assert.That(
                behavior.BehaviorName,
                Is.EqualTo(
                    KernelConstants.GoalkeeperControlV2BehaviorName));
            Assert.That(
                behavior.BrainParameters.VectorObservationSize,
                Is.EqualTo(
                    KernelConstants.GoalkeeperControlV2ObservationSize));
            Assert.That(
                behavior.BrainParameters.ActionSpec.NumContinuousActions,
                Is.EqualTo(4));
            Assert.That(
                behavior.BrainParameters.ActionSpec.BranchSizes,
                Is.EqualTo(new[] { 2 }));
            Assert.That(
                agent.GetComponent<Unity.MLAgents.DecisionRequester>(),
                Is.Null);
            var nativePolicy =
                agent.GetComponent<GoalkeeperSplitInferencePolicyV1>();
            Assert.That(nativePolicy, Is.Not.Null);
            Assert.That(nativePolicy.InterceptionModel, Is.Not.Null);
            Assert.That(nativePolicy.TimingModel, Is.Not.Null);
            Assert.That(agent.NativeSplitInferenceByDefault, Is.False);
            Assert.That(
                nativePolicy.CommitThreshold,
                Is.EqualTo(
                    GoalkeeperSplitInferencePolicyV1
                        .DefaultCommitThreshold)
                    .Within(0.000001f));
            Assert.That(
                GoalkeeperSplitInferencePolicyV1.Sigmoid(0f),
                Is.EqualTo(0.5f).Within(0.000001f));

            var waitFrames = 0;
            while (template.LastResult == null && waitFrames < 300)
            {
                waitFrames++;
                yield return new WaitForFixedUpdate();
            }

            Assert.That(template.LastResult, Is.Not.Null);
            var lifecycle = template.LastResult;
            Assert.That(
                lifecycle.PolicyDecisionRequestCount,
                Is.EqualTo(
                    lifecycle.PolicyDecisionConsumedCount +
                    lifecycle.PolicyDecisionDiscardedCount));
            Assert.That(
                lifecycle.PolicyDecisionConsumedCount,
                Is.EqualTo(lifecycle.AcceptedControlDecisionCount));
            Assert.That(
                lifecycle.PolicyDecisionDuplicateRequestCount,
                Is.Zero);
            Assert.That(
                lifecycle.PolicyDecisionMissingActionCount,
                Is.Zero);
            Assert.That(
                lifecycle.PolicyDecisionDiscardedCount,
                Is.LessThanOrEqualTo(1));

            const int attemptsPerPolicy = 32;
            var report = new MotorValidationReport
            {
                attempts_per_policy = attemptsPerPolicy,
            };
            var policies = new[]
            {
                ScriptedGoalkeeperControlPolicyV1.StandCenter,
                ScriptedGoalkeeperControlPolicyV1.RandomLegal,
                ScriptedGoalkeeperControlPolicyV1.ReactiveIntercept,
                ScriptedGoalkeeperControlPolicyV1.OracleReachBound,
            };
            foreach (var policy in policies)
            {
                report.terminal_attempts += RunPolicy(
                    template.gameObject,
                    attemptsPerPolicy,
                    policy,
                    report);
            }

            report.passed =
                report.terminal_attempts ==
                    attemptsPerPolicy * policies.Length &&
                report.invalid_outcomes == 0 &&
                report.timeout_outcomes == 0 &&
                report.action_mask_violations == 0 &&
                report.commitments >= attemptsPerPolicy &&
                report.maximum_peak_reach > 0.9f;
            report.status = report.passed ? "passed" : "failed";
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../docs/stage5-motor-validation-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(
                output,
                JsonUtility.ToJson(report, true) + Environment.NewLine);

            Assert.That(report.passed, Is.True);
            yield return null;
        }

        [UnityTest]
        [Category("Stage5Acceptance")]
        public IEnumerator Stage5ReactiveDemonstrationsCloseAtTerminalQuotas()
        {
            var load = SceneManager.LoadSceneAsync(
                "ControlDemonstration",
                LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            var coordinator =
                UnityEngine.Object.FindFirstObjectByType<
                    Stage5ReactiveDemonstrationCoordinator>();
            var controllers =
                UnityEngine.Object.FindObjectsByType<PenaltyAreaController>(
                    FindObjectsSortMode.None);
            Assert.That(coordinator, Is.Not.Null);
            Assert.That(controllers, Has.Length.EqualTo(16));
            Assert.That(coordinator.Completed, Is.False);

            foreach (var controller in controllers)
            {
                var agent =
                    controller.GetComponentInChildren<
                        GoalkeeperControlAgent>(true);
                var behavior =
                    agent == null
                        ? null
                        : agent.GetComponent<BehaviorParameters>();
                var recorder =
                    agent == null
                        ? null
                        : agent.GetComponent<DemonstrationRecorder>();
                Assert.That(agent, Is.Not.Null);
                Assert.That(behavior, Is.Not.Null);
                Assert.That(recorder, Is.Not.Null);
                Assert.That(
                    behavior.BehaviorName,
                    Is.EqualTo(
                        KernelConstants
                            .GoalkeeperControlV2BehaviorName));
                Assert.That(
                    behavior.BrainParameters.VectorObservationSize,
                    Is.EqualTo(
                        KernelConstants
                            .GoalkeeperControlV2ObservationSize));
                Assert.That(
                    behavior.BrainParameters.ActionSpec
                        .NumContinuousActions,
                    Is.EqualTo(4));
                Assert.That(
                    behavior.BrainParameters.ActionSpec.BranchSizes,
                    Is.EqualTo(new[] { 2 }));
                Assert.That(
                    behavior.BehaviorType,
                    Is.EqualTo(BehaviorType.HeuristicOnly));
                Assert.That(
                    agent.HeuristicMode,
                    Is.EqualTo(
                        GoalkeeperControlHeuristicMode
                            .ReactiveTeacher));
                Assert.That(recorder.Record, Is.False);
            }

            var output = Path.Combine(
                Path.GetTempPath(),
                "penalty-shootout-stage5-demo-" +
                Guid.NewGuid().ToString("N"));
            try
            {
                coordinator.AttemptsPerArena = 4;
                coordinator.MasterSeed = 20260723UL;
                coordinator.DemonstrationDirectory = output;
                coordinator.QuitWhenComplete = false;
                coordinator.BeginRecording();

                var waitFrames = 0;
                while (!coordinator.Completed && waitFrames < 2500)
                {
                    waitFrames++;
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(coordinator.Completed, Is.True);
                Assert.That(coordinator.ClosedArenaCount, Is.EqualTo(16));
                Assert.That(
                    Directory.GetFiles(output, "*.demo"),
                    Has.Length.EqualTo(16));
                Assert.That(
                    File.Exists(
                        Path.Combine(output, "teacher-report.json")),
                    Is.True);
                foreach (var controller in controllers)
                {
                    Assert.That(controller.LastResult, Is.Not.Null);
                    Assert.That(
                        controller.LastResult.AttemptId,
                        Is.EqualTo(4));
                    Assert.That(
                        controller.LastResult.Outcome,
                        Is.Not.EqualTo(AttemptOutcome.Invalid));
                    Assert.That(
                        controller.LastResult.Outcome,
                        Is.Not.EqualTo(AttemptOutcome.Timeout));
                    Assert.That(
                        controller.LastResult.ActionMaskViolations,
                        Is.Zero);
                    Assert.That(
                        controller.LastResult
                            .PolicyDecisionDuplicateRequestCount,
                        Is.Zero);
                    Assert.That(
                        controller.LastResult
                            .PolicyDecisionMissingActionCount,
                        Is.Zero);
                }
            }
            finally
            {
                if (Directory.Exists(output))
                {
                    Directory.Delete(output, true);
                }
            }
        }

        private static int RunPolicy(
            GameObject template,
            int requestedAttempts,
            ScriptedGoalkeeperControlPolicyV1 policy,
            MotorValidationReport report)
        {
            var scene = SceneManager.CreateScene(
                $"Stage5_{policy}",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physicsScene = scene.GetPhysicsScene();
            var instance = UnityEngine.Object.Instantiate(template);
            SceneManager.MoveGameObjectToScene(instance, scene);
            var controller = instance.GetComponent<PenaltyAreaController>();
            controller.AutoRun = false;
            controller.ManualSimulationMode = true;
            controller.ShowDebugUi = false;
            var existingAgent =
                instance.GetComponentInChildren<GoalkeeperControlAgent>();
            if (existingAgent != null)
            {
                existingAgent.gameObject.SetActive(false);
            }

            var source =
                instance.AddComponent<ScriptedGoalkeeperControlSourceV1>();
            source.Controller = controller;
            source.Policy = policy;
            controller.ActionSource = source;
            controller.BeginNextAttempt();

            var terminalAttempts = 0;
            var consumedAttemptId = 0L;
            for (var step = 0;
                 step < 100000 && terminalAttempts < requestedAttempts;
                 step++)
            {
                controller.ManualFixedStep();
                physicsScene.Simulate(
                    controller.EnvironmentConfiguration.FixedTimestep);
                if (!controller.IsTerminal ||
                    controller.LastResult == null ||
                    controller.LastResult.AttemptId == consumedAttemptId)
                {
                    continue;
                }

                var result = controller.LastResult;
                consumedAttemptId = result.AttemptId;
                terminalAttempts++;
                report.invalid_outcomes +=
                    result.Outcome == AttemptOutcome.Invalid ? 1 : 0;
                report.timeout_outcomes +=
                    result.Outcome == AttemptOutcome.Timeout ? 1 : 0;
                report.action_mask_violations += result.ActionMaskViolations;
                report.commitments += result.HasSaveCommitment ? 1 : 0;
                report.goalkeeper_contacts += result.GoalkeeperContact ? 1 : 0;
                report.glove_contacts += result.GloveContact ? 1 : 0;
                report.maximum_peak_reach = Mathf.Max(
                    report.maximum_peak_reach,
                    result.GoalkeeperPeakReachExtension);
                if (terminalAttempts < requestedAttempts)
                {
                    controller.BeginNextAttempt();
                }
            }

            UnityEngine.Object.DestroyImmediate(instance);
            SceneManager.UnloadSceneAsync(scene);
            return terminalAttempts;
        }
    }
}
