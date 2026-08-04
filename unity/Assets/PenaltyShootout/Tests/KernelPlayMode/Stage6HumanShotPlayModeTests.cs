using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage6HumanShotPlayModeTests
    {
        [UnityTest]
        [Category("Stage6Acceptance")]
        public IEnumerator ContactReviewLabRunsFixedReplayWithNativeInference()
        {
            var load = SceneManager.LoadSceneAsync(
                "ShotVarietyLab",
                LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }
            yield return null;

            var lab = UnityEngine.Object.FindFirstObjectByType<
                Stage6ShotVarietyLab>();
            var controller = UnityEngine.Object.FindFirstObjectByType<
                PenaltyAreaController>();
            var agent = UnityEngine.Object.FindFirstObjectByType<
                GoalkeeperControlAgent>();
            Assert.That(lab, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(agent, Is.Not.Null);
            Assert.That(lab.ReplayCount, Is.EqualTo(12));
            Assert.That(lab.UsesNativeGoalkeeper, Is.True);
            Assert.That(lab.ContactCandidateEnabled, Is.False);
            Assert.That(agent.NativeSplitInferenceEnabled, Is.True);
            Assert.That(controller.DebugUiIgnoresArenaId, Is.True);
            Assert.That(
                controller.TryConfigureNextReplayAttempt(
                    20260803UL,
                    3,
                    10,
                    out var error),
                Is.True,
                error);

            controller.BeginNextAttempt();
            var frames = 0;
            while (controller.LastResult == null && frames < 400)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }

            Assert.That(controller.LastResult, Is.Not.Null);
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
            Assert.That(controller.LastResult.NativeInferenceEvaluationCount, Is.GreaterThan(0));
            Assert.That(controller.LastResult.NativeInferenceInvalidOutputCount, Is.Zero);
        }

        [UnityTest]
        [Category("Stage6Acceptance")]
        public IEnumerator BaselineScenePublishesGameplayContractAndTerminates()
        {
            var load = SceneManager.LoadSceneAsync(
                "Stage6Baseline",
                LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            var arenas = UnityEngine.Object
                .FindObjectsByType<PenaltyAreaController>(
                    FindObjectsSortMode.None)
                .OrderBy(item => item.ArenaId)
                .ToArray();
            Assert.That(arenas, Has.Length.EqualTo(16));
            var template = arenas[0];
            Assert.That(template.UsesHumanShots, Is.True);
            Assert.That(template.GameplayObservationDelayTicks, Is.EqualTo(2));
            var agent = template.GetComponentInChildren<GoalkeeperControlAgent>();
            var behavior = agent.GetComponent<BehaviorParameters>();
            Assert.That(
                behavior.BehaviorName,
                Is.EqualTo(KernelConstants.GoalkeeperControlV2BehaviorName));
            Assert.That(
                behavior.BrainParameters.VectorObservationSize,
                Is.EqualTo(35));
            Assert.That(
                behavior.BrainParameters.ActionSpec.NumContinuousActions,
                Is.EqualTo(4));
            Assert.That(
                behavior.BrainParameters.ActionSpec.BranchSizes,
                Is.EqualTo(new[] { 2 }));

            var frames = 0;
            while ((template.Phase != AttemptPhase.BallInFlight ||
                    template.BallFlightTime < 0.08f) && frames < 100)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(template.Phase, Is.EqualTo(AttemptPhase.BallInFlight));
            Assert.That(
                template.TryGetDelayedBallVisibleSnapshot(0, out var current),
                Is.True);
            Assert.That(
                template.TryGetDelayedBallVisibleSnapshot(2, out var delayed),
                Is.True);
            Assert.That(
                current.BallFlightTime - delayed.BallFlightTime,
                Is.EqualTo(0.04f).Within(0.021f));

            frames = 0;
            while (arenas.Any(item => item.LastResult == null) && frames < 400)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }

            Assert.That(arenas.All(item => item.LastResult != null), Is.True);
            var runtimeCrossingErrors = new List<float>();
            foreach (var arena in arenas)
            {
                var result = arena.LastResult;
                Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
                Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
                Assert.That(result.ActionMaskViolations, Is.Zero);
                Assert.That(
                    result.PlayerShot.ShotContractId,
                    Is.EqualTo(KernelConstants.PlayerShotContractId));
                Assert.That(
                    result.PlayerShot.ShotPhysicsId,
                    Is.EqualTo(KernelConstants.PlayerShotPhysicsId));
                Assert.That(result.ObservationDelayTicks, Is.EqualTo(2));

                if (!result.GoalkeeperContact &&
                    !result.GoalFrameContact &&
                    result.HasCentrePlaneIntersection)
                {
                    var crossingError = Vector2.Distance(
                        result.MeasuredCentrePlaneIntersectionLocal,
                        result.PlayerShot.PredictedUnopposedCrossingLocal);
                    runtimeCrossingErrors.Add(crossingError);
                    Assert.That(crossingError, Is.LessThanOrEqualTo(0.12f));
                }

                var telemetry = GoalkeeperBenchmarkTelemetry.CreateJson(
                    result,
                    0f,
                    KernelConstants.GoalkeeperGameplayObservationSpecId);
                StringAssert.Contains(
                    "\"observation_spec_id\":\"control-state-v2-gameplay-v1\"",
                    telemetry);
                StringAssert.Contains(
                    "\"shot_contract_id\":\"player-shot-v1\"",
                    telemetry);
                StringAssert.Contains("\"observation_delay_ticks\":2", telemetry);
            }

            Assert.That(runtimeCrossingErrors, Is.Not.Empty);
        }
    }
}
