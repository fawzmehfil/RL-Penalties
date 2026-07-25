using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage2AcceptancePlayModeTests
    {
        [Serializable]
        private sealed class BaselineReport
        {
            public int schema_version = 1;
            public string behavior_name = KernelConstants.GoalkeeperStateBehaviorName;
            public string observation_spec_id = KernelConstants.GoalkeeperStateObservationSpecId;
            public string reward_spec_id = KernelConstants.GoalkeeperSparseRewardSpecId;
            public int vector_observation_size = KernelConstants.GoalkeeperStateObservationSize;
            public int requested_attempts;
            public int stand_center_terminal_attempts;
            public int random_legal_terminal_attempts;
            public int invalid_outcomes;
            public int timeout_outcomes;
            public int action_mask_violations;
            public bool passed;
        }

        [Test]
        [Category("Stage3Acceptance")]
        public void Stage3TelemetryPayloadPublishesTerminalBenchmarkFields()
        {
            var result = new AttemptResult
            {
                EnvironmentId = KernelConstants.EnvironmentId,
                ScenarioSuiteId = KernelConstants.ScenarioSuiteId,
                AttemptId = 7,
                ArenaId = 3,
                Seed = 20260723UL,
                Outcome = AttemptOutcome.BlockedThenOut,
                AttemptTime = 1.2f,
                BallFlightTime = 0.64f,
                GoalkeeperContact = true,
                GoalkeeperContactCount = 1,
                LastGoalkeeperContactPart = GoalkeeperContactPart.LeftGlove,
                GloveContact = true,
                GloveContactCount = 1,
                LeftGloveContactCount = 1,
                RequestedTargetLocal = new Vector3(-1f, 1.5f, 0f),
                HasCentrePlaneIntersection = true,
                MeasuredCentrePlaneIntersectionLocal = new Vector3(-1.01f, 1.49f, 0f),
                TargetError = 0.02f,
                InitialAction = GoalkeeperAction.DiveLeftMiddle,
                LastAction = GoalkeeperAction.Hold,
                FirstAcceptedDiveAction = GoalkeeperAction.DiveLeftMiddle,
                FirstDiveDecisionIndex = 0,
                FirstDiveAttemptTime = 0.5f,
                FirstDiveBallFlightTime = 0.1f,
                AcceptedActionCounts = new[] { 3, 0, 0, 0, 1, 0, 0, 0, 0 },
            };

            var json = GoalkeeperBenchmarkTelemetry.CreateJson(result, 1f);
            StringAssert.Contains("\"message_type\":\"stage3_attempt_result\"", json);
            StringAssert.Contains("\"benchmark_id\":\"goalkeeper-state-v0-id-20k\"", json);
            StringAssert.Contains("\"outcome\":\"BlockedThenOut\"", json);
            StringAssert.Contains("\"first_accepted_dive_action\":\"DiveLeftMiddle\"", json);
            StringAssert.Contains("\"accepted_action_counts\":[3,0,0,0,1,0,0,0,0]", json);
            StringAssert.Contains("\"requested_target_local\"", json);
            StringAssert.Contains("\"measured_centre_plane_intersection_local\"", json);
        }

        [UnityTest]
        [Category("Stage2Acceptance")]
        public IEnumerator Stage2TrainingScenePublishesStateV0AndBaselinesTerminate()
        {
            var load = SceneManager.LoadSceneAsync("Training", LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            var template = UnityEngine.Object.FindFirstObjectByType<PenaltyAreaController>();
            Assert.That(template, Is.Not.Null);
            var agent = template.GetComponentInChildren<GoalkeeperKernelAgent>();
            var behavior = agent == null ? null : agent.GetComponent<BehaviorParameters>();
            Assert.That(agent, Is.Not.Null);
            Assert.That(behavior, Is.Not.Null);
            Assert.That(agent.ObservationProfile, Is.EqualTo(GoalkeeperObservationProfile.StateV0));
            Assert.That(behavior.BehaviorName, Is.EqualTo(KernelConstants.GoalkeeperStateBehaviorName));
            Assert.That(
                behavior.BrainParameters.VectorObservationSize,
                Is.EqualTo(KernelConstants.GoalkeeperStateObservationSize));
            Assert.That(
                behavior.BrainParameters.ActionSpec.BranchSizes,
                Is.EqualTo(new[] { 9 }));

            const int attemptsPerBaseline = 128;
            var report = new BaselineReport
            {
                requested_attempts = attemptsPerBaseline,
            };
            report.stand_center_terminal_attempts =
                RunBaseline(template.gameObject, attemptsPerBaseline, false, report);
            report.random_legal_terminal_attempts =
                RunBaseline(template.gameObject, attemptsPerBaseline, true, report);
            report.passed =
                report.stand_center_terminal_attempts == attemptsPerBaseline &&
                report.random_legal_terminal_attempts == attemptsPerBaseline &&
                report.invalid_outcomes == 0 &&
                report.timeout_outcomes == 0 &&
                report.action_mask_violations == 0;

            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../docs/stage2-baseline-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonUtility.ToJson(report, true) + Environment.NewLine);

            Assert.That(report.passed, Is.True);
            yield return null;
        }

        private static int RunBaseline(
            GameObject template,
            int requestedAttempts,
            bool randomLegal,
            BaselineReport report)
        {
            var scene = SceneManager.CreateScene(
                randomLegal ? "Stage2RandomLegalBaseline" : "Stage2StandCenterBaseline",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physicsScene = scene.GetPhysicsScene();
            var instance = UnityEngine.Object.Instantiate(template);
            SceneManager.MoveGameObjectToScene(instance, scene);
            var controller = instance.GetComponent<PenaltyAreaController>();
            controller.AutoRun = false;
            controller.ManualSimulationMode = true;
            controller.ShowDebugUi = false;
            var existingAgent = instance.GetComponentInChildren<GoalkeeperKernelAgent>();
            if (existingAgent != null)
            {
                existingAgent.gameObject.SetActive(false);
            }

            var source = randomLegal
                ? (GoalkeeperActionSourceBehaviour)instance.AddComponent<RandomLegalGoalkeeperActionSource>()
                : instance.AddComponent<HoldGoalkeeperActionSource>();
            controller.ActionSource = source;
            controller.BeginNextAttempt();

            var terminalAttempts = 0;
            var consumedAttemptId = 0L;
            for (var step = 0; step < 100000 && terminalAttempts < requestedAttempts; step++)
            {
                controller.ManualFixedStep();
                physicsScene.Simulate(controller.EnvironmentConfiguration.FixedTimestep);
                if (!controller.IsTerminal ||
                    controller.LastResult == null ||
                    controller.LastResult.AttemptId == consumedAttemptId)
                {
                    continue;
                }

                consumedAttemptId = controller.LastResult.AttemptId;
                terminalAttempts++;
                if (controller.LastResult.Outcome == AttemptOutcome.Invalid)
                {
                    report.invalid_outcomes++;
                }

                if (controller.LastResult.Outcome == AttemptOutcome.Timeout)
                {
                    report.timeout_outcomes++;
                }

                report.action_mask_violations += controller.LastResult.ActionMaskViolations;
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
