using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PenaltyShootout.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage9SimulationInvariancePlayModeTests
    {
        private const int AttemptCount = 400;
        private const int ExactTraceAttemptCount = 20;
        private const int MaximumPairedSemanticMismatches = 4;
        private const int MaximumAggregateCountDifference = 2;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Stage9PenaltyAudioV1.ForceMutedForAutomation = true;
            AudioListener.pause = true;
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            Stage9PenaltyAudioV1.ForceMutedForAutomation = true;
            AudioListener.pause = true;
            yield return null;
        }

        [UnityTest]
        [Category("Stage9SimulationInvariance")]
        [Timeout(2400000)]
        public IEnumerator FinalPresentationPreservesPairedFourHundredShotSimulation()
        {
            List<AttemptTrace> baseline = null;
            List<AttemptTrace> candidate = null;

            yield return RunScene("PenaltyGame", traces => baseline = traces);
            yield return RunScene("PenaltyShootoutFinal", traces => candidate = traces);

            Assert.That(baseline, Is.Not.Null);
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate.Count, Is.EqualTo(AttemptCount));
            Assert.That(baseline.Count, Is.EqualTo(AttemptCount));

            var saveClassMismatches = 0;
            var gloveContactMismatches = 0;
            var commitDecisionDelta = 0;
            var maximumCommitDecisionDelta = 0;
            for (var index = 0; index < AttemptCount; index++)
            {
                Assert.That(
                    candidate[index].EpisodeKey,
                    Is.EqualTo(baseline[index].EpisodeKey),
                    $"Episode key changed at paired shot {index + 1}.");
                if (index < ExactTraceAttemptCount)
                {
                    Assert.That(
                        candidate[index].CommandTrace,
                        Is.EqualTo(baseline[index].CommandTrace),
                        $"Keeper command trace changed during exact-prefix shot {index + 1}.");
                    Assert.That(
                        candidate[index].ContactTrace,
                        Is.EqualTo(baseline[index].ContactTrace),
                        $"Contact sequence changed during exact-prefix shot {index + 1}.");
                    Assert.That(
                        candidate[index].Outcome,
                        Is.EqualTo(baseline[index].Outcome),
                        $"Outcome changed during exact-prefix shot {index + 1}.");
                }

                if (candidate[index].IsSave != baseline[index].IsSave)
                {
                    saveClassMismatches++;
                }
                if (candidate[index].GloveContact != baseline[index].GloveContact)
                {
                    gloveContactMismatches++;
                }
                var delta = Mathf.Abs(
                    candidate[index].FirstCommitDecisionIndex -
                    baseline[index].FirstCommitDecisionIndex);
                commitDecisionDelta += delta;
                maximumCommitDecisionDelta = Mathf.Max(maximumCommitDecisionDelta, delta);
            }

            var baselineSaves = baseline.Count(trace => trace.IsSave);
            var candidateSaves = candidate.Count(trace => trace.IsSave);
            var baselineGloveContacts = baseline.Count(trace => trace.GloveContact);
            var candidateGloveContacts = candidate.Count(trace => trace.GloveContact);

            Debug.Log(
                "Stage 9 paired simulation: " +
                $"shots={AttemptCount}, exact_prefix={ExactTraceAttemptCount}, " +
                $"save_mismatches={saveClassMismatches}, " +
                $"save_counts={baselineSaves}/{candidateSaves}, " +
                $"glove_mismatches={gloveContactMismatches}, " +
                $"glove_counts={baselineGloveContacts}/{candidateGloveContacts}, " +
                $"commit_delta_total={commitDecisionDelta}, " +
                $"commit_delta_max={maximumCommitDecisionDelta}.");

            Assert.That(
                saveClassMismatches,
                Is.LessThanOrEqualTo(MaximumPairedSemanticMismatches),
                "Stage 9 changed the paired save/goal class on more than 1% of shots.");
            Assert.That(
                Mathf.Abs(candidateSaves - baselineSaves),
                Is.LessThanOrEqualTo(MaximumAggregateCountDifference),
                "Stage 9 changed aggregate saves by more than 0.5 percentage points.");
            Assert.That(
                gloveContactMismatches,
                Is.LessThanOrEqualTo(MaximumPairedSemanticMismatches),
                "Stage 9 changed paired glove-contact status on more than 1% of shots.");
            Assert.That(
                Mathf.Abs(candidateGloveContacts - baselineGloveContacts),
                Is.LessThanOrEqualTo(MaximumAggregateCountDifference),
                "Stage 9 changed aggregate glove contacts by more than 0.5 percentage points.");
            Assert.That(
                maximumCommitDecisionDelta,
                Is.LessThanOrEqualTo(1),
                "Stage 9 changed a first-commit decision by more than one decision tick.");
            Assert.That(
                commitDecisionDelta,
                Is.LessThanOrEqualTo(MaximumPairedSemanticMismatches),
                "Stage 9 changed first-commit timing on more than 1% of shots.");
        }

        private static IEnumerator RunScene(
            string sceneName,
            System.Action<List<AttemptTrace>> completed)
        {
            var load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            Stage9PenaltyAudioV1.ForceMutedForAutomation = true;
            AudioListener.pause = true;
            var game = Object.FindFirstObjectByType<Stage7PenaltyGameV1>();
            var controller = Object.FindFirstObjectByType<PenaltyAreaController>();
            Assert.That(game, Is.Not.Null, $"{sceneName} game is missing.");
            Assert.That(controller, Is.Not.Null, $"{sceneName} controller is missing.");

            var startupFrames = 0;
            while ((!controller.IsAwaitingPreparedPlayerShot ||
                    controller.Phase != AttemptPhase.Ready) &&
                   startupFrames++ < 300)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.That(controller.IsAwaitingPreparedPlayerShot, Is.True);
            Assert.That(controller.Phase, Is.EqualTo(AttemptPhase.Ready));

            game.enabled = false;
            Time.timeScale = 1f;
            var commandEvents = new List<GoalkeeperControlCommandEventV1>(64);
            var contactEvents = new List<BallContactReplayEventV1>(16);
            controller.GoalkeeperControlCommandAccepted += commandEvents.Add;
            controller.ContactRecorded += contactEvents.Add;

            var traces = new List<AttemptTrace>(AttemptCount);
            for (var index = 0; index < AttemptCount; index++)
            {
                if (index > 0)
                {
                    Assert.That(
                        controller.PrepareNextPlayerAttempt(out var prepareError),
                        Is.True,
                        prepareError);
                    var prepareFrames = 0;
                    while ((!controller.IsAwaitingPreparedPlayerShot ||
                            controller.Phase != AttemptPhase.Ready) &&
                           prepareFrames++ < 160)
                    {
                        yield return new WaitForFixedUpdate();
                    }
                    Assert.That(controller.Phase, Is.EqualTo(AttemptPhase.Ready));
                }

                commandEvents.Clear();
                contactEvents.Clear();
                var request = BuildRequest(index);
                Assert.That(
                    controller.TrySubmitPreparedPlayerShot(request, out var submitError),
                    Is.True,
                    submitError);
                var attemptId = controller.AttemptId;
                var attemptFrames = 0;
                while ((controller.LastResult == null ||
                        controller.LastResult.AttemptId != attemptId) &&
                       attemptFrames++ < 700)
                {
                    yield return new WaitForFixedUpdate();
                }

                var result = controller.LastResult;
                Assert.That(result, Is.Not.Null, $"{sceneName} shot {index + 1} did not finish.");
                AssertSafety(result, sceneName, index);
                traces.Add(AttemptTrace.Capture(result, commandEvents, contactEvents));
            }

            controller.GoalkeeperControlCommandAccepted -= commandEvents.Add;
            controller.ContactRecorded -= contactEvents.Add;
            completed(traces);
        }

        private static PlayerPenaltyShotRequestV1 BuildRequest(int index)
        {
            var aimX = Mathf.Lerp(-0.9f, 0.9f, ((index * 37) % 199) / 198f);
            var aimY = Mathf.Lerp(-0.8f, 0.8f, ((index * 53) % 197) / 196f);
            var power = Mathf.Lerp(0.3f, 0.94f, ((index * 29) % 193) / 192f);
            var sideSpin = Mathf.Lerp(-0.72f, 0.72f, ((index * 43) % 191) / 190f);
            var verticalSpin = Mathf.Lerp(0.05f, 0.25f, ((index * 31) % 181) / 180f);
            var style = Mathf.Abs(sideSpin) >= 0.25f
                ? PlayerShotStyleV1.Curled
                : power >= 0.72f
                    ? PlayerShotStyleV1.Power
                    : PlayerShotStyleV1.Placed;
            return new PlayerPenaltyShotRequestV1
            {
                Command = new PlayerShotCommandV1(
                    aimX,
                    aimY,
                    power,
                    sideSpin,
                    verticalSpin,
                    0f,
                    0f),
                Style = style,
                InputSeed = 202609000UL + (ulong)index,
                TimingQuality = 0.9f,
                ChargeDuration = Mathf.Lerp(0.3f, 1.1f, power),
                InputDevice = PlayerShotInputDeviceV1.AutomatedTest,
            };
        }

        private static void AssertSafety(
            AttemptResult result,
            string sceneName,
            int index)
        {
            var label = $"{sceneName} paired shot {index + 1}";
            Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Invalid), label);
            Assert.That(result.Outcome, Is.Not.EqualTo(AttemptOutcome.Timeout), label);
            Assert.That(result.ActionMaskViolations, Is.Zero, label);
            Assert.That(result.ControlCommandClampCount, Is.Zero, label);
            Assert.That(result.PolicyDecisionDuplicateRequestCount, Is.Zero, label);
            Assert.That(result.PolicyDecisionMissingActionCount, Is.Zero, label);
            Assert.That(result.NativeInferenceInvalidOutputCount, Is.Zero, label);
        }

        private sealed class AttemptTrace
        {
            public string EpisodeKey;
            public AttemptOutcome Outcome;
            public string CommandTrace;
            public string ContactTrace;
            public bool IsSave;
            public bool GloveContact;
            public int FirstCommitDecisionIndex;

            public static AttemptTrace Capture(
                AttemptResult result,
                IEnumerable<GoalkeeperControlCommandEventV1> commands,
                IEnumerable<BallContactReplayEventV1> contacts)
            {
                return new AttemptTrace
                {
                    EpisodeKey = $"{result.ArenaId}:{result.AttemptId}:{result.Seed}",
                    Outcome = result.Outcome,
                    CommandTrace = string.Join("|", commands.Select(FormatCommand)),
                    ContactTrace = FormatContacts(contacts),
                    IsSave = result.Outcome == AttemptOutcome.Saved ||
                        result.Outcome == AttemptOutcome.BlockedThenOut,
                    GloveContact = result.GloveContact,
                    FirstCommitDecisionIndex = result.FirstCommitDecisionIndex,
                };
            }

            private static string FormatContacts(
                IEnumerable<BallContactReplayEventV1> contacts)
            {
                // PhysX does not guarantee callback order for contacts drained in
                // the same fixed tick. Preserve tick order and canonicalize only
                // the simultaneous contacts inside each tick.
                return string.Join("|", contacts
                    .GroupBy(contact => contact.AttemptTime)
                    .OrderBy(group => group.Key)
                    .Select(group =>
                        Float(group.Key) + "[" +
                        string.Join(",", group
                            .OrderBy(contact => (int)contact.Kind)
                            .ThenBy(contact => (int)contact.GoalkeeperPart)
                            .Select(contact =>
                                $"{(int)contact.Kind}:{(int)contact.GoalkeeperPart}")) +
                        "]"));
            }

            private static string FormatCommand(GoalkeeperControlCommandEventV1 accepted)
            {
                var command = accepted.Command;
                var output = new StringBuilder(96);
                output.Append(accepted.DecisionIndex).Append(':')
                    .Append(accepted.PhysicsTick).Append(':')
                    .Append(Float(command.MoveX)).Append(':')
                    .Append(Float(command.AimX)).Append(':')
                    .Append(Float(command.AimY)).Append(':')
                    .Append(Float(command.Reach)).Append(':')
                    .Append(command.Commit ? '1' : '0');
                return output.ToString();
            }

            private static string Float(float value)
            {
                return value.ToString("R", CultureInfo.InvariantCulture);
            }
        }
    }
}
