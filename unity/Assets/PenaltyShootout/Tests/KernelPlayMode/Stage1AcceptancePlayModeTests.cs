using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage1AcceptancePlayModeTests
    {
        [Serializable]
        private sealed class OutcomeReport
        {
            public int goal;
            public int saved;
            public int miss_wide;
            public int miss_high;
            public int post_or_crossbar_out;
            public int blocked_then_out;
            public int timeout;
            public int invalid;
        }

        [Serializable]
        private sealed class AcceptanceReport
        {
            public int schema_version;
            public string environment_id;
            public string scenario_suite_id;
            public string manifest_sha256;
            public string unity_editor;
            public ulong master_seed;
            public int arena_count;
            public int requested_attempts;
            public int terminal_attempts;
            public int goalkeeper_contacts;
            public int goal_frame_contacts;
            public int contact_then_goal;
            public int glove_contacts;
            public int left_glove_contacts;
            public int right_glove_contacts;
            public int arm_contacts;
            public int torso_or_head_contacts;
            public int leg_contacts;
            public int glove_touch_then_goal;
            public int invalid_outcomes;
            public int timeout_outcomes;
            public int duplicate_terminal_events;
            public int action_mask_violations;
            public int non_finite_states;
            public int[] action_attempts;
            public int[] action_contacts;
            public int[] glove_contacts_by_action;
            public int[] goals_by_action;
            public float simulation_wall_time_s;
            public float attempts_per_second;
            public float maximum_unobstructed_target_error_m;
            public float mean_unobstructed_target_error_m;
            public int measured_unobstructed_attempts;
            public float tolerance_m;
            public OutcomeReport outcomes;
            public bool passed;
        }

        [UnityTest]
        [Category("Stage1Acceptance")]
        public IEnumerator TenThousandSeededKernelAttemptsTerminateCleanly()
        {
            var load = SceneManager.LoadSceneAsync("KernelLab", LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            const int requestedAttempts = 10000;
            const int arenaCount = 16;
            const ulong masterSeed = 20260723UL;
            var prefabController =
                UnityEngine.Object.FindFirstObjectByType<PenaltyAreaController>();
            Assert.That(prefabController, Is.Not.Null);
            Assert.That(prefabController.ValidateDependencies(out var dependencyError), Is.True, dependencyError);
            var arenaTemplate = prefabController.gameObject;

            var environment = prefabController.EnvironmentConfiguration;
            var shots = prefabController.ShotConfiguration;
            var motor = prefabController.MotorConfiguration;
            var manifestJson = KernelManifestUtility.CreateJson(environment, shots, motor);
            var report = new AcceptanceReport
            {
                schema_version = KernelConstants.AcceptanceSchemaVersion,
                environment_id = KernelConstants.EnvironmentId,
                scenario_suite_id = KernelConstants.ScenarioSuiteId,
                manifest_sha256 = KernelManifestUtility.Sha256(manifestJson),
                unity_editor = Application.unityVersion,
                master_seed = masterSeed,
                arena_count = arenaCount,
                requested_attempts = requestedAttempts,
                tolerance_m = KernelConstants.TargetTolerance,
                outcomes = new OutcomeReport(),
                action_attempts = new int[9],
                action_contacts = new int[9],
                glove_contacts_by_action = new int[9],
                goals_by_action = new int[9],
            };

            var scene = SceneManager.CreateScene(
                "Stage1AcceptancePhysics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physicsScene = scene.GetPhysicsScene();
            var controllers = new List<PenaltyAreaController>(arenaCount);
            var consumedAttemptIds = new long[arenaCount];
            var targetErrorTotal = 0f;
            var simulationWallTime = 0d;

            try
            {
                for (var index = 0; index < arenaCount; index++)
                {
                    var instance = UnityEngine.Object.Instantiate(arenaTemplate);
                    instance.name = $"AcceptanceArena_{index:000}";
                    instance.transform.position = new Vector3(index * 30f, 0f, 0f);
                    SceneManager.MoveGameObjectToScene(instance, scene);
                    var controller = instance.GetComponent<PenaltyAreaController>();
                    controller.AutoRun = false;
                    controller.ManualSimulationMode = true;
                    controller.ShowDebugUi = false;
                    controller.ArenaId = index;
                    controller.MasterSeed = masterSeed;
                    var cyclicSource = instance.AddComponent<CyclicGoalkeeperActionSource>();
                    controller.ActionSource = cyclicSource;
                    var agent = instance.transform.Find("GoalkeeperKernelAgent");
                    if (agent != null)
                    {
                        agent.gameObject.SetActive(false);
                    }

                    Assert.That(controller.ValidateDependencies(out dependencyError), Is.True, dependencyError);
                    controllers.Add(controller);
                    controller.BeginNextAttempt();
                }

                var simulationSteps = 0;
                const int maximumSimulationSteps = 1000000;
                var simulationStart = System.Diagnostics.Stopwatch.GetTimestamp();
                while (report.terminal_attempts < requestedAttempts &&
                    simulationSteps < maximumSimulationSteps)
                {
                    for (var index = 0; index < controllers.Count; index++)
                    {
                        controllers[index].ManualFixedStep();
                    }

                    physicsScene.Simulate(environment.FixedTimestep);
                    simulationSteps++;

                    for (var index = 0; index < controllers.Count; index++)
                    {
                        if (report.terminal_attempts >= requestedAttempts)
                        {
                            break;
                        }

                        var controller = controllers[index];
                        if (!controller.IsTerminal ||
                            controller.LastResult == null ||
                            controller.AttemptId == consumedAttemptIds[index])
                        {
                            continue;
                        }

                        consumedAttemptIds[index] = controller.AttemptId;
                        Accumulate(controller.LastResult, report, ref targetErrorTotal);
                        if (report.terminal_attempts < requestedAttempts)
                        {
                            controller.BeginNextAttempt();
                        }
                    }
                }

                simulationWallTime =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - simulationStart) /
                    (double)System.Diagnostics.Stopwatch.Frequency;
            }
            finally
            {
                for (var index = 0; index < controllers.Count; index++)
                {
                    if (controllers[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(controllers[index].gameObject);
                    }
                }

                SceneManager.UnloadSceneAsync(scene);
            }

            report.mean_unobstructed_target_error_m =
                report.measured_unobstructed_attempts == 0
                    ? float.PositiveInfinity
                    : targetErrorTotal / report.measured_unobstructed_attempts;
            report.simulation_wall_time_s = (float)simulationWallTime;
            report.attempts_per_second = simulationWallTime <= 0d
                ? 0f
                : (float)(report.terminal_attempts / simulationWallTime);
            report.passed =
                report.terminal_attempts == requestedAttempts &&
                report.invalid_outcomes == 0 &&
                report.timeout_outcomes == 0 &&
                report.duplicate_terminal_events == 0 &&
                report.action_mask_violations == 0 &&
                report.non_finite_states == 0 &&
                EveryActionWasExercised(report.action_attempts) &&
                EveryDiveMadeContact(report.action_contacts) &&
                EveryDiveMadeContact(report.glove_contacts_by_action) &&
                report.glove_touch_then_goal > 0 &&
                report.attempts_per_second > 0f &&
                report.measured_unobstructed_attempts > 0 &&
                report.maximum_unobstructed_target_error_m <=
                    KernelConstants.TargetTolerance;

            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../docs/stage1-acceptance.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonUtility.ToJson(report, true) + Environment.NewLine);

            Assert.That(report.terminal_attempts, Is.EqualTo(requestedAttempts));
            Assert.That(report.invalid_outcomes, Is.Zero);
            Assert.That(report.timeout_outcomes, Is.Zero);
            Assert.That(report.duplicate_terminal_events, Is.Zero);
            Assert.That(report.action_mask_violations, Is.Zero);
            Assert.That(report.non_finite_states, Is.Zero);
            Assert.That(EveryActionWasExercised(report.action_attempts), Is.True);
            Assert.That(EveryDiveMadeContact(report.action_contacts), Is.True);
            Assert.That(EveryDiveMadeContact(report.glove_contacts_by_action), Is.True);
            Assert.That(report.glove_touch_then_goal, Is.GreaterThan(0));
            Assert.That(report.goals_by_action[(int)GoalkeeperAction.Hold], Is.GreaterThan(0));
            Assert.That(report.attempts_per_second, Is.GreaterThan(0f));
            Assert.That(report.measured_unobstructed_attempts, Is.GreaterThan(0));
            Assert.That(
                report.maximum_unobstructed_target_error_m,
                Is.LessThanOrEqualTo(KernelConstants.TargetTolerance));
            Assert.That(report.passed, Is.True);
            yield return null;
        }

        private static void Accumulate(
            AttemptResult result,
            AcceptanceReport report,
            ref float targetErrorTotal)
        {
            report.terminal_attempts++;
            report.goalkeeper_contacts += result.GoalkeeperContactCount;
            report.goal_frame_contacts += result.GoalFrameContactCount;
            report.glove_contacts += result.GloveContactCount;
            report.left_glove_contacts += result.LeftGloveContactCount;
            report.right_glove_contacts += result.RightGloveContactCount;
            report.arm_contacts += result.ArmContactCount;
            report.torso_or_head_contacts += result.TorsoOrHeadContactCount;
            report.leg_contacts += result.LegContactCount;
            report.duplicate_terminal_events += result.DuplicateTerminalEvents;
            report.action_mask_violations += result.ActionMaskViolations;
            report.action_attempts[(int)result.InitialAction]++;
            if (result.GoalkeeperContact)
            {
                report.action_contacts[(int)result.InitialAction]++;
            }
            if (result.GloveContact)
            {
                report.glove_contacts_by_action[(int)result.InitialAction]++;
            }
            if (result.GoalkeeperContact && result.Outcome == AttemptOutcome.Goal)
            {
                report.contact_then_goal++;
            }
            if (result.GloveContact && result.Outcome == AttemptOutcome.Goal)
            {
                report.glove_touch_then_goal++;
            }
            if (result.Outcome == AttemptOutcome.Goal)
            {
                report.goals_by_action[(int)result.InitialAction]++;
            }

            switch (result.Outcome)
            {
                case AttemptOutcome.Goal:
                    report.outcomes.goal++;
                    break;
                case AttemptOutcome.Saved:
                    report.outcomes.saved++;
                    break;
                case AttemptOutcome.MissWide:
                    report.outcomes.miss_wide++;
                    break;
                case AttemptOutcome.MissHigh:
                    report.outcomes.miss_high++;
                    break;
                case AttemptOutcome.PostOrCrossbarOut:
                    report.outcomes.post_or_crossbar_out++;
                    break;
                case AttemptOutcome.BlockedThenOut:
                    report.outcomes.blocked_then_out++;
                    break;
                case AttemptOutcome.Timeout:
                    report.outcomes.timeout++;
                    report.timeout_outcomes++;
                    break;
                case AttemptOutcome.Invalid:
                    report.outcomes.invalid++;
                    report.invalid_outcomes++;
                    break;
            }

            if (!KernelMath.IsFinite(result.MeasuredCentrePlaneIntersectionLocal) &&
                result.HasCentrePlaneIntersection)
            {
                report.non_finite_states++;
            }

            if (!result.GoalkeeperContact &&
                !result.GoalFrameContact &&
                result.HasCentrePlaneIntersection &&
                KernelMath.IsFinite(result.TargetError))
            {
                report.measured_unobstructed_attempts++;
                targetErrorTotal += result.TargetError;
                report.maximum_unobstructed_target_error_m = Mathf.Max(
                    report.maximum_unobstructed_target_error_m,
                    result.TargetError);
            }
        }

        private static bool EveryActionWasExercised(int[] counts)
        {
            if (counts == null || counts.Length != 9)
            {
                return false;
            }

            for (var index = 0; index < counts.Length; index++)
            {
                if (counts[index] <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EveryDiveMadeContact(int[] counts)
        {
            if (counts == null || counts.Length != 9)
            {
                return false;
            }

            for (var index = (int)GoalkeeperAction.DiveLeftLow;
                index <= (int)GoalkeeperAction.DiveRightHigh;
                index++)
            {
                if (counts[index] <= 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
