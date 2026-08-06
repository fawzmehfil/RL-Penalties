using NUnit.Framework;
using PenaltyShootout.Gameplay;
using PenaltyShootout.Kernel;
using UnityEngine;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage7GameplayTests
    {
        private PlayerPenaltyInputConfigV1 configuration;

        [SetUp]
        public void SetUp()
        {
            configuration = ScriptableObject.CreateInstance<
                PlayerPenaltyInputConfigV1>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(configuration);
        }

        [Test]
        public void InputConfigurationAndAimBoundsAreValid()
        {
            Assert.That(configuration.Validate(out var error), Is.True, error);
            var bounds = PlayerPenaltyInputMathV1.CommandAimBounds;
            Assert.That(bounds.x, Is.InRange(0.85f, 0.86f));
            Assert.That(bounds.y, Is.InRange(0.58f, 0.60f));
            Assert.That(
                PlayerPenaltyInputMathV1.ClampAim(new Vector2(3f, 3f)),
                Is.EqualTo(bounds));
        }

        [Test]
        public void PowerTimingAndStyleContractsAreStable()
        {
            Assert.That(
                PlayerPenaltyInputMathV1.PowerForHold(0f, configuration),
                Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(
                PlayerPenaltyInputMathV1.PowerForHold(1.2f, configuration),
                Is.EqualTo(1f).Within(1e-6f));
            Assert.That(
                PlayerPenaltyInputMathV1.ComposureQuality(0.575f, configuration),
                Is.EqualTo(1f).Within(1e-5f));
            Assert.That(
                PlayerPenaltyInputMathV1.InferStyle(0.9f, 0.3f),
                Is.EqualTo(PlayerShotStyleV1.Curled));
            Assert.That(
                PlayerPenaltyInputMathV1.InferStyle(0.9f, 0f),
                Is.EqualTo(PlayerShotStyleV1.Power));
            Assert.That(
                PlayerPenaltyInputMathV1.InferStyle(0.6f, 0f),
                Is.EqualTo(PlayerShotStyleV1.Placed));
        }

        [Test]
        public void ContactErrorAndRequestAreDeterministicAndBounded()
        {
            var first = PlayerPenaltyInputMathV1.BuildRequest(
                new Vector2(0.4f, 0.2f),
                0.8f,
                -0.5f,
                0.9f,
                0.8f,
                20260805UL,
                2,
                PlayerShotInputDeviceV1.Pointer,
                configuration);
            var second = PlayerPenaltyInputMathV1.BuildRequest(
                new Vector2(0.4f, 0.2f),
                0.8f,
                -0.5f,
                0.9f,
                0.8f,
                20260805UL,
                2,
                PlayerShotInputDeviceV1.Pointer,
                configuration);

            Assert.That(first.Command.ContactErrorXMeters,
                Is.EqualTo(second.Command.ContactErrorXMeters));
            Assert.That(first.Command.ContactErrorYMeters,
                Is.EqualTo(second.Command.ContactErrorYMeters));
            Assert.That(Mathf.Abs(first.Command.ContactErrorXMeters), Is.AtMost(0.75f));
            Assert.That(Mathf.Abs(first.Command.ContactErrorYMeters), Is.AtMost(0.75f));
            Assert.That(first.Validate(out var error), Is.True, error);
        }

        [Test]
        public void ScoreConsumesOnlyValidFootballOutcomes()
        {
            var score = new PenaltySetScoreV1();
            Assert.That(score.Record(AttemptOutcome.Goal), Is.True);
            Assert.That(score.Record(AttemptOutcome.Saved), Is.True);
            Assert.That(score.Record(AttemptOutcome.BlockedThenOut), Is.True);
            Assert.That(score.Record(AttemptOutcome.MissWide), Is.True);
            Assert.That(score.Record(AttemptOutcome.Invalid), Is.False);
            Assert.That(score.ValidShots, Is.EqualTo(4));
            Assert.That(score.Goals, Is.EqualTo(1));
            Assert.That(score.Saves, Is.EqualTo(2));
            Assert.That(score.Misses, Is.EqualTo(1));
        }

        [Test]
        public void InteractiveScenarioUsesExistingShotPhysics()
        {
            var physics = ScriptableObject.CreateInstance<PlayerShotPhysicsConfigV1>();
            try
            {
                var request = PlayerPenaltyInputMathV1.BuildRequest(
                    new Vector2(-0.5f, 0.25f),
                    0.7f,
                    0.6f,
                    0.8f,
                    0.7f,
                    20260805UL,
                    0,
                    PlayerShotInputDeviceV1.Keyboard,
                    configuration);
                var scenario = PlayerShotScenarioFactoryV1.Resolve(
                    request,
                    99UL,
                    Physics.gravity,
                    0.02f,
                    physics);
                Assert.That(
                    scenario.ScenarioSuiteId,
                    Is.EqualTo(KernelConstants.PlayerInteractiveScenarioSuiteId));
                Assert.That(
                    scenario.PlayerShot.ShotPhysicsId,
                    Is.EqualTo(KernelConstants.PlayerShotPhysicsId));
                Assert.That(
                    PlayerShotScenarioFactoryV1.Validate(
                        scenario,
                        physics,
                        out var error),
                    Is.True,
                    error);
            }
            finally
            {
                Object.DestroyImmediate(physics);
            }
        }

        [Test]
        public void OneHundredFiveShotCommandSetsRemainValid()
        {
            var physics = ScriptableObject.CreateInstance<PlayerShotPhysicsConfigV1>();
            try
            {
                for (var set = 0; set < 100; set++)
                {
                    var score = new PenaltySetScoreV1();
                    for (var shot = 0; shot < PlayerPenaltyInputMathV1.ShotsPerSet; shot++)
                    {
                        var sample = set * PlayerPenaltyInputMathV1.ShotsPerSet + shot;
                        var aim = PlayerPenaltyInputMathV1.ClampAim(new Vector2(
                            Mathf.Sin(sample * 0.73f),
                            Mathf.Cos(sample * 0.41f)));
                        var power = Mathf.Lerp(0.2f, 1f, (sample % 17) / 16f);
                        var sideSpin = Mathf.Lerp(-1f, 1f, (sample % 13) / 12f);
                        var timing = (sample % 11) / 10f;
                        var request = PlayerPenaltyInputMathV1.BuildRequest(
                            aim,
                            power,
                            sideSpin,
                            timing,
                            power * configuration.MaximumChargeSeconds,
                            20260805UL + (ulong)set,
                            shot,
                            shot % 2 == 0
                                ? PlayerShotInputDeviceV1.Pointer
                                : PlayerShotInputDeviceV1.Keyboard,
                            configuration);
                        Assert.That(request.Validate(out var requestError), Is.True, requestError);
                        var scenario = PlayerShotScenarioFactoryV1.Resolve(
                            request,
                            (ulong)(sample + 1),
                            Physics.gravity,
                            0.02f,
                            physics);
                        Assert.That(
                            PlayerShotScenarioFactoryV1.Validate(
                                scenario,
                                physics,
                                out var scenarioError),
                            Is.True,
                            scenarioError);
                        Assert.That(
                            score.Record((AttemptOutcome)(1 + sample % 5)),
                            Is.True);
                    }
                    Assert.That(score.Complete, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(physics);
            }
        }

        [Test]
        public void MaximumPlayerCurveSaturatesInsidePhysicsEnvelope()
        {
            var physics = ScriptableObject.CreateInstance<PlayerShotPhysicsConfigV1>();
            try
            {
                var request = new PlayerPenaltyShotRequestV1
                {
                    Command = new PlayerShotCommandV1(
                        0f,
                        0f,
                        0.2f,
                        1f,
                        0.25f,
                        0f,
                        0f),
                    Style = PlayerShotStyleV1.Curled,
                    InputSeed = 55UL,
                    TimingQuality = 1f,
                    ChargeDuration = 0f,
                    InputDevice = PlayerShotInputDeviceV1.Pointer,
                };
                var scenario = PlayerShotScenarioFactoryV1.Resolve(
                    request,
                    55UL,
                    Physics.gravity,
                    0.02f,
                    physics);
                Assert.That(
                    scenario.PlayerShot.PredictedCurveDisplacement.magnitude,
                    Is.AtMost(physics.MaximumCurveDisplacement + 1e-4f));
                Assert.That(scenario.PlayerShot.SpinSaturationScale, Is.LessThan(1f));
                Assert.That(scenario.PlayerShot.Command.SideSpin, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(physics);
            }
        }
    }
}
