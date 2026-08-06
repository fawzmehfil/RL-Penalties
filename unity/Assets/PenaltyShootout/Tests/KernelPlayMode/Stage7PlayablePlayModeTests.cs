using System.Collections;
using System.IO;
using NUnit.Framework;
using PenaltyShootout.Gameplay;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage7PlayablePlayModeTests
    {
        [UnityTest]
        [Category("Stage7Acceptance")]
        public IEnumerator PlayableScenePreparesAndRunsNativePlayerShot()
        {
            var load = SceneManager.LoadSceneAsync("PenaltyGame", LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            var game = Object.FindFirstObjectByType<Stage7PenaltyGameV1>();
            var controller = Object.FindFirstObjectByType<PenaltyAreaController>();
            var agent = Object.FindFirstObjectByType<GoalkeeperControlAgent>();
            var replay = Object.FindFirstObjectByType<PenaltyReplayRecorderV1>();
            Assert.That(game, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(agent, Is.Not.Null);
            Assert.That(replay, Is.Not.Null);

            var frames = 0;
            while (game.State != Stage7GameplayStateV1.Aiming && frames < 120)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.Aiming));
            Assert.That(controller.IsAwaitingPreparedPlayerShot, Is.True);
            Assert.That(controller.AttemptTime, Is.EqualTo(0f).Within(1e-6f));
            var ballPosition = controller.Ball.position;
            for (var index = 0; index < 10; index++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(controller.Ball.position, Is.EqualTo(ballPosition));
            Assert.That(controller.AttemptTime, Is.EqualTo(0f).Within(1e-6f));

            var behavior = agent.GetComponent<BehaviorParameters>();
            Assert.That(behavior.BehaviorType, Is.EqualTo(BehaviorType.HeuristicOnly));
            Assert.That(agent.NativeSplitInferenceEnabled, Is.True);
            Assert.That(controller.GameplayObservationDelayTicks, Is.EqualTo(2));
            Assert.That(controller.GoalkeeperGloveHandling.HandlingVersion, Is.EqualTo(1));

            var request = new PlayerPenaltyShotRequestV1
            {
                Command = new PlayerShotCommandV1(
                    0.2f,
                    -0.2f,
                    0.65f,
                    0.45f,
                    0.15f,
                    0f,
                    0f),
                Style = PlayerShotStyleV1.Curled,
                InputSeed = 10UL,
                TimingQuality = 1f,
                ChargeDuration = 0.7f,
                InputDevice = PlayerShotInputDeviceV1.AutomatedTest,
            };
            Assert.That(
                game.TrySubmitAutomatedShot(request, out var error),
                Is.True,
                error);
            Assert.That(
                game.TrySubmitAutomatedShot(request, out _),
                Is.False,
                "A prepared shot may only be submitted once.");

            frames = 0;
            while (controller.LastResult == null && frames < 500)
            {
                frames++;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(controller.LastResult, Is.Not.Null);
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
            Assert.That(controller.LastResult.NativeInferenceInvalidOutputCount, Is.Zero);
            Assert.That(replay.Session.Attempts, Has.Count.EqualTo(1));
            Assert.That(replay.Session.Attempts[0].HasLaunch, Is.True);
            Assert.That(replay.Session.Attempts[0].Frames, Is.Not.Empty);
            Assert.That(replay.Session.Attempts[0].KeeperCommands, Is.Not.Empty);
        }

        [UnityTest]
        [Category("Stage7Acceptance")]
        public IEnumerator FiveShotSetCompletesAndWritesReplay()
        {
            var load = SceneManager.LoadSceneAsync("PenaltyGame", LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            var game = Object.FindFirstObjectByType<Stage7PenaltyGameV1>();
            var controller = Object.FindFirstObjectByType<PenaltyAreaController>();
            var replay = Object.FindFirstObjectByType<PenaltyReplayRecorderV1>();
            Assert.That(game, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(replay, Is.Not.Null);

            for (var shot = 0; shot < PlayerPenaltyInputMathV1.ShotsPerSet; shot++)
            {
                var waitFrames = 0;
                while (game.State != Stage7GameplayStateV1.Aiming && waitFrames < 600)
                {
                    waitFrames++;
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.Aiming));

                var request = new PlayerPenaltyShotRequestV1
                {
                    Command = new PlayerShotCommandV1(
                        Mathf.Lerp(-0.65f, 0.65f, shot / 4f),
                        Mathf.Lerp(-0.45f, 0.45f, (shot % 3) / 2f),
                        Mathf.Lerp(0.4f, 0.85f, shot / 4f),
                        shot % 2 == 0 ? -0.35f : 0.35f,
                        0.12f,
                        0f,
                        0f),
                    Style = shot >= 3
                        ? PlayerShotStyleV1.Power
                        : PlayerShotStyleV1.Curled,
                    InputSeed = (ulong)(100 + shot),
                    TimingQuality = 0.85f,
                    ChargeDuration = 0.65f,
                    InputDevice = PlayerShotInputDeviceV1.AutomatedTest,
                };
                Assert.That(
                    game.TrySubmitAutomatedShot(request, out var error),
                    Is.True,
                    error);

                var attemptId = controller.AttemptId;
                waitFrames = 0;
                while ((controller.LastResult == null ||
                        controller.LastResult.AttemptId != attemptId) &&
                    waitFrames < 600)
                {
                    waitFrames++;
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(controller.LastResult, Is.Not.Null);
                Assert.That(controller.LastResult.AttemptId, Is.EqualTo(attemptId));
                Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
                Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
            }

            var completeDeadline = Time.realtimeSinceStartup + 3f;
            while (game.State != Stage7GameplayStateV1.SetComplete &&
                Time.realtimeSinceStartup < completeDeadline)
            {
                yield return null;
            }
            Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.SetComplete));
            Assert.That(game.Score.ValidShots, Is.EqualTo(5));
            Assert.That(replay.Session.Attempts, Has.Count.EqualTo(5));
            Assert.That(replay.LastWriteError, Is.Empty);
            Assert.That(File.Exists(replay.LastWrittenPath), Is.True);
            File.Delete(replay.LastWrittenPath);
        }
    }
}
