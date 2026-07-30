using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PenaltyShootout.MLAgents;
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
                KernelConstants.GoalkeeperControlBehaviorName;
            public string observation_spec_id =
                KernelConstants.GoalkeeperControlObservationSpecId;
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
                FirstCommitAim = new Vector2(-0.8f, 0.6f),
                FirstGoalkeeperContactPart =
                    GoalkeeperContactPart.LeftGlove,
                FirstGoalkeeperContactTime = 0.74f,
                GoalkeeperRootDistance = 2.1f,
                GoalkeeperPeakRootSpeed = 5.4f,
                GoalkeeperPeakReachExtension = 1f,
                MinimumGloveBallDistance = 0.04f,
                SampledShotFlightTime = 0.58f,
                SampledLaunchDelay = 0.24f,
                AcceptedControlDecisionCount = 5,
                ControlMoveCommandCount = 3,
                ControlReachCommandCount = 4,
                ControlAbsoluteActionSums =
                    new[] { 2f, 3f, 2.5f, 4f },
                ControlSaturationCounts =
                    new[] { 0, 1, 0, 2 },
            };
            var json = GoalkeeperBenchmarkTelemetry.CreateJson(
                result,
                1f,
                KernelConstants.GoalkeeperControlObservationSpecId);

            StringAssert.Contains(
                "\"behavior_name\":\"GoalkeeperControl-v1\"",
                json);
            StringAssert.Contains(
                "\"observation_spec_id\":\"control-state-v1\"",
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
            StringAssert.Contains("\"sampled_shot_flight_time\"", json);
            StringAssert.Contains(
                "\"accepted_control_decision_count\":5",
                json);
            StringAssert.Contains("\"control_saturation_counts\":[0,1,0,2]", json);
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
                Is.EqualTo(KernelConstants.GoalkeeperControlBehaviorName));
            Assert.That(
                behavior.BrainParameters.VectorObservationSize,
                Is.EqualTo(KernelConstants.GoalkeeperControlObservationSize));
            Assert.That(
                behavior.BrainParameters.ActionSpec.NumContinuousActions,
                Is.EqualTo(4));
            Assert.That(
                behavior.BrainParameters.ActionSpec.BranchSizes,
                Is.EqualTo(new[] { 2 }));

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
