using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PenaltyShootout.Stage0.Tests
{
    public sealed class Stage0AcceptancePlayModeTests
    {
        [Serializable]
        private sealed class AcceptanceReport
        {
            public string environment_id;
            public string unity_editor;
            public int requested_attempts;
            public int terminal_attempts;
            public int goal_attempts;
            public int invalid_outcomes;
            public int duplicate_terminal_events;
            public float maximum_target_error_m;
            public float mean_target_error_m;
            public float tolerance_m;
            public bool passed;
        }

        [Test]
        public void CanonicalShotPassesOneThousandFixedSeedAttempts()
        {
            const int requestedAttempts = 1000;
            var report = new AcceptanceReport
            {
                environment_id = Stage0Constants.EnvironmentId,
                unity_editor = Application.unityVersion,
                requested_attempts = requestedAttempts,
                tolerance_m = Stage0Constants.TargetTolerance,
            };

            var scene = SceneManager.CreateScene(
                "Stage0AcceptancePhysics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var physicsScene = scene.GetPhysicsScene();
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "AcceptanceBall";
            ball.transform.localScale = Vector3.one * (Stage0Constants.BallRadius * 2f);
            SceneManager.MoveGameObjectToScene(ball, scene);

            var body = ball.AddComponent<Rigidbody>();
            body.mass = Stage0Constants.BallMass;
            body.linearDamping = 0f;
            body.angularDamping = 0.05f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.None;

            var totalError = 0f;
            var maximumError = 0f;

            try
            {
                for (var attempt = 0; attempt < requestedAttempts; attempt++)
                {
                    RunAttempt(
                        physicsScene,
                        body,
                        report,
                        ref totalError,
                        ref maximumError);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(ball);
                SceneManager.UnloadSceneAsync(scene);
            }

            report.maximum_target_error_m = maximumError;
            report.mean_target_error_m = totalError / requestedAttempts;
            report.passed =
                report.terminal_attempts == requestedAttempts &&
                report.goal_attempts == requestedAttempts &&
                report.invalid_outcomes == 0 &&
                report.duplicate_terminal_events == 0 &&
                report.maximum_target_error_m <= Stage0Constants.TargetTolerance;

            var reportPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../docs/stage0-acceptance.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true) + Environment.NewLine);

            Assert.That(report.terminal_attempts, Is.EqualTo(requestedAttempts));
            Assert.That(report.goal_attempts, Is.EqualTo(requestedAttempts));
            Assert.That(report.invalid_outcomes, Is.Zero);
            Assert.That(report.duplicate_terminal_events, Is.Zero);
            Assert.That(report.maximum_target_error_m, Is.LessThanOrEqualTo(Stage0Constants.TargetTolerance));
            Assert.That(report.passed, Is.True);
        }

        private static void RunAttempt(
            PhysicsScene physicsScene,
            Rigidbody body,
            AcceptanceReport report,
            ref float totalError,
            ref float maximumError)
        {
            PhysicsLabController.ResetRigidBody(body, Stage0Constants.CanonicalLaunch);
            body.linearVelocity = BallisticShotSolver.SolvePhysXInitialVelocity(
                Stage0Constants.CanonicalLaunch,
                Stage0Constants.CanonicalTarget,
                Stage0Constants.CanonicalFlightTime,
                Physics.gravity,
                Stage0Constants.FixedTimestep);
            body.WakeUp();

            var previous = Stage0Constants.CanonicalLaunch;
            var elapsed = 0f;
            var terminal = ShotOutcome.None;
            var centreIntersectionFound = false;
            var centreIntersection = default(Vector3);
            var latch = new OutcomeLatch();

            while (elapsed < Stage0Constants.AttemptTimeout && terminal == ShotOutcome.None)
            {
                physicsScene.Simulate(Stage0Constants.FixedTimestep);
                elapsed += Stage0Constants.FixedTimestep;
                var current = body.position;

                if (!BallisticShotSolver.IsFinite(current) ||
                    !BallisticShotSolver.IsFinite(body.linearVelocity) ||
                    !BallisticShotSolver.IsFinite(body.angularVelocity))
                {
                    terminal = ShotOutcome.Invalid;
                    latch.TrySet(terminal);
                    break;
                }

                if (!centreIntersectionFound &&
                    GoalLineGeometry.TryIntersectPlane(previous, current, 0f, out var centre))
                {
                    centreIntersection = centre;
                    centreIntersectionFound = true;
                }

                if (GoalLineGeometry.TryIntersectPlane(
                        previous,
                        current,
                        -Stage0Constants.BallRadius,
                        out var wholeBall))
                {
                    terminal = GoalLineGeometry.ClassifyWholeBallCrossing(wholeBall);
                    latch.TrySet(terminal);
                    break;
                }

                previous = current;
            }

            if (terminal == ShotOutcome.None)
            {
                terminal = ShotOutcome.Timeout;
                latch.TrySet(terminal);
            }

            report.terminal_attempts++;
            report.duplicate_terminal_events += latch.DuplicateTerminalEvents;
            if (terminal == ShotOutcome.Goal)
            {
                report.goal_attempts++;
            }

            var error = float.PositiveInfinity;
            if (centreIntersectionFound)
            {
                error = Vector2.Distance(
                    new Vector2(centreIntersection.x, centreIntersection.y),
                    new Vector2(Stage0Constants.CanonicalTarget.x, Stage0Constants.CanonicalTarget.y));
                totalError += error;
                maximumError = Mathf.Max(maximumError, error);
            }

            if (terminal != ShotOutcome.Goal ||
                !centreIntersectionFound ||
                !BallisticShotSolver.IsFinite(error) ||
                error > Stage0Constants.TargetTolerance)
            {
                report.invalid_outcomes++;
            }
        }
    }
}
