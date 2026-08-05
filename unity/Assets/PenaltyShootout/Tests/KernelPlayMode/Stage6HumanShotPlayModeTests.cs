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
            Assert.That(lab.GloveHandlingEnabled, Is.False);
            Assert.That(controller.GoalkeeperGloveHandling, Is.Not.Null);
            Assert.That(
                controller.GoalkeeperGloveHandling.HandlingEnabled,
                Is.False);
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

            controller.GoalkeeperGloveHandling.SetHandlingEnabled(true);
            var armRig = controller.GoalkeeperControlMotor.ArmRig;
            Assert.That(
                armRig.LeftGlove.GetComponent<SphereCollider>().enabled,
                Is.False);
            Assert.That(
                armRig.LeftGlove.Find("GloveHandlingV1_Palm").gameObject.activeSelf,
                Is.True);

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
            Assert.That(controller.LastResult.GloveHandlingEnabled, Is.True);
            Assert.That(
                controller.LastResult.GloveHandlingId,
                Is.EqualTo(KernelConstants.GoalkeeperGloveHandlingContractId));
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
            Assert.That(template.GoalkeeperGloveHandling, Is.Not.Null);
            Assert.That(template.GoalkeeperGloveHandling.HandlingEnabled, Is.True);
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
                Assert.That(result.GloveHandlingEnabled, Is.True);
                Assert.That(
                    result.GloveHandlingId,
                    Is.EqualTo(KernelConstants.GoalkeeperGloveHandlingContractId));
                Assert.That(
                    result.GloveGeometryId,
                    Is.EqualTo(KernelConstants.GoalkeeperPalmGeometryId));

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

        [UnityTest]
        [Category("Stage6Acceptance")]
        public IEnumerator GloveImpactVelocityMatchesCommandedIncomingSpeed()
        {
            var glove = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glove.name = "Stage6V2ImpactFixtureGlove";
            glove.transform.position = new Vector3(100f, 1f, 0f);
            glove.transform.localScale = new Vector3(0.5f, 0.5f, 0.1f);
            var marker = glove.AddComponent<ContactMarker>();
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = GoalkeeperContactPart.LeftGlove;
            glove.AddComponent<GloveContactSurfaceV1>().Configure(
                GoalkeeperContactPart.LeftGlove,
                GloveContactRegionV1.Palm);

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Stage6V2ImpactFixtureBall";
            ball.transform.position = new Vector3(100f, 1f, 0.8f);
            ball.transform.localScale = Vector3.one *
                KernelConstants.BallRadius * 2f;
            var body = ball.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var sensor = ball.AddComponent<BallContactSensor>();
            body.linearVelocity = new Vector3(0f, 0f, -15f);

            var history = new ContactHistory();
            BallContactEventV1? captured = null;
            for (var tick = 0; tick < 20 && !captured.HasValue; tick++)
            {
                yield return new WaitForFixedUpdate();
                sensor.Drain(
                    history,
                    tick * Time.fixedDeltaTime,
                    null,
                    contacts =>
                    {
                        if (contacts.Count > 0)
                        {
                            captured = contacts[0];
                        }
                    });
            }

            Assert.That(captured.HasValue, Is.True);
            Assert.That(
                captured.Value.Kinematics.RelativeVelocityWorld.magnitude,
                Is.EqualTo(15f).Within(0.5f));
            Assert.That(
                captured.Value.Kinematics.RelativeVelocityWorld.z,
                Is.EqualTo(15f).Within(0.5f));
            var reconstructed =
                GoalkeeperGloveHandlingV1.ReconstructIncomingBallVelocity(
                    captured.Value.Kinematics.RelativeVelocityWorld,
                    Vector3.zero);
            Assert.That(reconstructed.z, Is.EqualTo(-15f).Within(0.5f));

            UnityEngine.Object.Destroy(ball);
            UnityEngine.Object.Destroy(glove);
            yield return null;
        }

        [UnityTest]
        [Category("Stage6Acceptance")]
        public IEnumerator GloveHandlingVersionsCompletePairedSixteenArenaSmoke()
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
            foreach (var arena in arenas)
            {
                arena.AutoRun = false;
            }
            var frames = 0;
            while (arenas.Any(item => item.LastResult == null) && frames < 400)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(arenas.All(item => item.LastResult != null), Is.True);

            foreach (var version in new[] { 0, 1, 2 })
            {
                var previousAttemptIds = arenas
                    .Select(item => item.LastResult.AttemptId)
                    .ToArray();
                foreach (var arena in arenas)
                {
                    arena.GoalkeeperGloveHandling.SetHandlingVersion(version);
                    arena.BeginNextAttempt();
                }

                frames = 0;
                while (arenas.Where((item, index) =>
                           item.LastResult == null ||
                           item.LastResult.AttemptId == previousAttemptIds[index])
                       .Any() && frames < 400)
                {
                    frames++;
                    yield return new WaitForFixedUpdate();
                }

                foreach (var arena in arenas)
                {
                    var result = arena.LastResult;
                    Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
                    Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
                    Assert.That(result.ActionMaskViolations, Is.Zero);
                    Assert.That(result.GloveHandlingVersion, Is.EqualTo(version));
                    Assert.That(result.GloveControlledResponseCount, Is.LessThanOrEqualTo(1));
                    if (version == 2 && result.GloveControlledResponseCount > 0)
                    {
                        Assert.That(
                            result.GloveOutgoingEnergyRatio,
                            Is.LessThanOrEqualTo(0.9501f));
                    }
                    Assert.That(
                        result.GloveHandlingId,
                        Is.EqualTo(version == 0
                            ? KernelConstants.GoalkeeperLegacyGloveHandlingId
                            : version == 1
                                ? KernelConstants.GoalkeeperGloveHandlingContractId
                                : KernelConstants.GoalkeeperGloveHandlingV2ContractId));
                }
            }
        }
    }
}
