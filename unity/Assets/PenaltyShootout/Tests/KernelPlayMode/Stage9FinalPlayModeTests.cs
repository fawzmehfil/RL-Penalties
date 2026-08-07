using System.Collections;
using System.Linq;
using NUnit.Framework;
using PenaltyShootout.Gameplay;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage9FinalPlayModeTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Stage9PenaltyAudioV1.ForceMutedForAutomation = true;
            var load = SceneManager.LoadSceneAsync(
                "PenaltyShootoutFinal",
                LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Stage9PenaltyAudioV1.ForceMutedForAutomation = true;
            yield return null;
        }

        [UnityTest]
        [Category("Stage9Acceptance")]
        public IEnumerator FinalSceneRunsFrozenNativeGoalkeeperSilently()
        {
            var game = Object.FindFirstObjectByType<Stage7PenaltyGameV1>();
            var controller = Object.FindFirstObjectByType<PenaltyAreaController>();
            var agent = Object.FindFirstObjectByType<GoalkeeperControlAgent>();
            var audio = Object.FindFirstObjectByType<Stage9PenaltyAudioV1>();
            var net = Object.FindFirstObjectByType<Stage9NetPresentationV1>();
            Assert.That(game, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(agent, Is.Not.Null);
            Assert.That(audio, Is.Not.Null);
            Assert.That(net, Is.Not.Null);
            Assert.That(Object.FindFirstObjectByType<Stage7PenaltyAudioV1>(), Is.Null);
            Assert.That(GameObject.Find("PenaltyTakerPresentation"), Is.Null);

            var behavior = agent.GetComponent<BehaviorParameters>();
            Assert.That(behavior.BehaviorType, Is.EqualTo(BehaviorType.HeuristicOnly));
            Assert.That(agent.NativeSplitInferenceEnabled, Is.True);
            Assert.That(controller.GameplayObservationDelayTicks, Is.EqualTo(2));
            Assert.That(controller.GoalkeeperGloveHandling.HandlingVersion, Is.EqualTo(1));

            var frames = 0;
            while (game.State != Stage7GameplayStateV1.Aiming && frames++ < 180)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.Aiming));
            var request = new PlayerPenaltyShotRequestV1
            {
                Command = new PlayerShotCommandV1(
                    0.25f,
                    0.1f,
                    0.68f,
                    0.35f,
                    0.15f,
                    0f,
                    0f),
                Style = PlayerShotStyleV1.Curled,
                InputSeed = 20260807UL,
                TimingQuality = 0.92f,
                ChargeDuration = 0.74f,
                InputDevice = PlayerShotInputDeviceV1.AutomatedTest,
            };
            Assert.That(game.TrySubmitAutomatedShot(request, out var error), Is.True, error);
            var attemptId = controller.AttemptId;
            frames = 0;
            while ((controller.LastResult == null ||
                    controller.LastResult.AttemptId != attemptId) && frames++ < 600)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(controller.LastResult, Is.Not.Null);
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid));
            Assert.That(controller.LastResult.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout));
            Assert.That(controller.LastResult.NativeInferenceInvalidOutputCount, Is.Zero);
            Assert.That(audio.PlayedEventCount, Is.GreaterThanOrEqualTo(2));
        }

        [UnityTest]
        [Category("Stage9Acceptance")]
        public IEnumerator FiveShotSetRetainsReplayAndLifecycleContracts()
        {
            var game = Object.FindFirstObjectByType<Stage7PenaltyGameV1>();
            var controller = Object.FindFirstObjectByType<PenaltyAreaController>();
            var replay = Object.FindFirstObjectByType<PenaltyReplayRecorderV1>();
            Assert.That(game, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(replay, Is.Not.Null);

            for (var shot = 0; shot < PlayerPenaltyInputMathV1.ShotsPerSet; shot++)
            {
                var frames = 0;
                while (game.State != Stage7GameplayStateV1.Aiming && frames++ < 700)
                {
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.Aiming));
                var request = new PlayerPenaltyShotRequestV1
                {
                    Command = new PlayerShotCommandV1(
                        Mathf.Lerp(-0.6f, 0.6f, shot / 4f),
                        Mathf.Lerp(-0.35f, 0.45f, shot / 4f),
                        Mathf.Lerp(0.42f, 0.88f, shot / 4f),
                        shot % 2 == 0 ? -0.32f : 0.32f,
                        0.14f,
                        0f,
                        0f),
                    Style = shot >= 3 ? PlayerShotStyleV1.Power : PlayerShotStyleV1.Curled,
                    InputSeed = (ulong)(20260870 + shot),
                    TimingQuality = 0.88f,
                    ChargeDuration = 0.72f,
                    InputDevice = PlayerShotInputDeviceV1.AutomatedTest,
                };
                Assert.That(game.TrySubmitAutomatedShot(request, out var error), Is.True, error);
                var attemptId = controller.AttemptId;
                frames = 0;
                while ((controller.LastResult == null ||
                        controller.LastResult.AttemptId != attemptId) && frames++ < 700)
                {
                    yield return new WaitForFixedUpdate();
                }
                Assert.That(controller.LastResult, Is.Not.Null);
                Assert.That(controller.LastResult.Outcome,
                    Is.Not.EqualTo(AttemptOutcome.Invalid));
                Assert.That(controller.LastResult.Outcome,
                    Is.Not.EqualTo(AttemptOutcome.Timeout));
            }

            var deadline = Time.realtimeSinceStartup + 3f;
            while (game.State != Stage7GameplayStateV1.SetComplete &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(game.State, Is.EqualTo(Stage7GameplayStateV1.SetComplete));
            Assert.That(game.Score.ValidShots, Is.EqualTo(5));
            Assert.That(replay.Session.Attempts.Count, Is.EqualTo(5));
            Assert.That(replay.Session.Attempts.All(attempt =>
                attempt.HasLaunch && attempt.Outcome != AttemptOutcome.None), Is.True);
            if (!string.IsNullOrEmpty(replay.LastWrittenPath) &&
                System.IO.File.Exists(replay.LastWrittenPath))
            {
                System.IO.File.Delete(replay.LastWrittenPath);
            }
        }
    }
}
