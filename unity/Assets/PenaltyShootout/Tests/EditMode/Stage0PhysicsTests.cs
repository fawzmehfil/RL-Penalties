using NUnit.Framework;
using UnityEngine;

namespace PenaltyShootout.Stage0.Tests
{
    public sealed class Stage0PhysicsTests
    {
        [Test]
        public void CanonicalBallisticSolutionHitsRequestedTarget()
        {
            var velocity = BallisticShotSolver.SolveInitialVelocity(
                Stage0Constants.CanonicalLaunch,
                Stage0Constants.CanonicalTarget,
                Stage0Constants.CanonicalFlightTime,
                Physics.gravity);

            var t = Stage0Constants.CanonicalFlightTime;
            var reconstructed =
                Stage0Constants.CanonicalLaunch +
                velocity * t +
                0.5f * Physics.gravity * t * t;

            Assert.That(Vector3.Distance(reconstructed, Stage0Constants.CanonicalTarget), Is.LessThan(1e-4f));
            Assert.That(BallisticShotSolver.IsFinite(velocity), Is.True);
        }

        [Test]
        public void SolverRejectsInvalidAndNonFiniteInputs()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                BallisticShotSolver.SolveInitialVelocity(Vector3.zero, Vector3.one, 0f, Physics.gravity));

            Assert.Throws<System.ArgumentException>(() =>
                BallisticShotSolver.SolveInitialVelocity(
                    new Vector3(float.NaN, 0f, 0f),
                    Vector3.one,
                    1f,
                    Physics.gravity));
        }

        [Test]
        public void PhysXSolverAppliesSemiImplicitGravityCorrection()
        {
            var analytical = BallisticShotSolver.SolveInitialVelocity(
                Stage0Constants.CanonicalLaunch,
                Stage0Constants.CanonicalTarget,
                Stage0Constants.CanonicalFlightTime,
                Physics.gravity);
            var physX = BallisticShotSolver.SolvePhysXInitialVelocity(
                Stage0Constants.CanonicalLaunch,
                Stage0Constants.CanonicalTarget,
                Stage0Constants.CanonicalFlightTime,
                Physics.gravity,
                Stage0Constants.FixedTimestep);

            var expected = analytical - 0.5f * Physics.gravity * Stage0Constants.FixedTimestep;
            Assert.That(Vector3.Distance(physX, expected), Is.LessThan(1e-6f));
        }

        [Test]
        public void WholeBallInsideGoalRequiresFullClearance()
        {
            var legal = new Vector3(0f, 1.2f, -Stage0Constants.BallRadius);
            Assert.That(
                GoalLineGeometry.IsWholeBallInsideGoal(
                    legal,
                    Stage0Constants.BallRadius,
                    Stage0Constants.GoalHalfWidth,
                    Stage0Constants.CrossbarLowerEdge),
                Is.True);

            var touchingOutsidePost = new Vector3(
                Stage0Constants.GoalHalfWidth - Stage0Constants.BallRadius + 0.001f,
                1.2f,
                -Stage0Constants.BallRadius);
            Assert.That(
                GoalLineGeometry.IsWholeBallInsideGoal(
                    touchingOutsidePost,
                    Stage0Constants.BallRadius,
                    Stage0Constants.GoalHalfWidth,
                    Stage0Constants.CrossbarLowerEdge),
                Is.False);

            var touchingAboveBar = new Vector3(
                0f,
                Stage0Constants.CrossbarLowerEdge - Stage0Constants.BallRadius + 0.001f,
                -Stage0Constants.BallRadius);
            Assert.That(
                GoalLineGeometry.IsWholeBallInsideGoal(
                    touchingAboveBar,
                    Stage0Constants.BallRadius,
                    Stage0Constants.GoalHalfWidth,
                    Stage0Constants.CrossbarLowerEdge),
                Is.False);
        }

        [Test]
        public void PartialCrossingDoesNotReachWholeBallPlane()
        {
            var previous = new Vector3(0f, 1f, 0.2f);
            var partial = new Vector3(0f, 1f, -0.05f);
            Assert.That(
                GoalLineGeometry.TryIntersectPlane(
                    previous,
                    partial,
                    -Stage0Constants.BallRadius,
                    out _),
                Is.False);
        }

        [Test]
        public void HighSpeedSweptCrossingCannotSkipGoalPlane()
        {
            var previous = new Vector3(0f, 1.2f, 11f);
            var current = new Vector3(0f, 1.2f, -5f);
            Assert.That(
                GoalLineGeometry.TryIntersectPlane(
                    previous,
                    current,
                    -Stage0Constants.BallRadius,
                    out var intersection),
                Is.True);
            Assert.That(intersection.z, Is.EqualTo(-Stage0Constants.BallRadius).Within(1e-5f));
            Assert.That(GoalLineGeometry.ClassifyWholeBallCrossing(intersection), Is.EqualTo(ShotOutcome.Goal));
        }

        [Test]
        public void WideAndHighCrossingsAreNotGoals()
        {
            var wide = new Vector3(Stage0Constants.GoalHalfWidth + 0.5f, 1f, -Stage0Constants.BallRadius);
            var high = new Vector3(0f, Stage0Constants.CrossbarLowerEdge + 0.5f, -Stage0Constants.BallRadius);

            Assert.That(GoalLineGeometry.ClassifyWholeBallCrossing(wide), Is.EqualTo(ShotOutcome.MissWide));
            Assert.That(GoalLineGeometry.ClassifyWholeBallCrossing(high), Is.EqualTo(ShotOutcome.MissHigh));
        }

        [Test]
        public void ResetClearsRigidBodyAndAttemptState()
        {
            var ballObject = new GameObject("ResetTestBall");
            var body = ballObject.AddComponent<Rigidbody>();
            body.position = new Vector3(4f, 5f, 6f);
            body.rotation = Quaternion.Euler(12f, 34f, 56f);
            body.linearVelocity = new Vector3(10f, 11f, 12f);
            body.angularVelocity = new Vector3(2f, 3f, 4f);

            var controllerObject = new GameObject("ResetTestController");
            var controller = controllerObject.AddComponent<PhysicsLabController>();
            controller.Ball = body;
            controller.LaunchPosition = Stage0Constants.CanonicalLaunch;
            controller.ResetOnly();

            Assert.That(body.position, Is.EqualTo(Stage0Constants.CanonicalLaunch));
            Assert.That(body.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero));
            Assert.That(controller.Elapsed, Is.EqualTo(0f));
            Assert.That(controller.IsActive, Is.False);
            Assert.That(controller.CurrentOutcome, Is.EqualTo(ShotOutcome.None));
            Assert.That(controller.HasCentrePlaneIntersection, Is.False);

            Object.DestroyImmediate(controllerObject);
            Object.DestroyImmediate(ballObject);
        }

        [Test]
        public void OutcomeLatchRejectsDuplicateTerminalEvents()
        {
            var latch = new OutcomeLatch();
            Assert.That(latch.TrySet(ShotOutcome.Goal), Is.True);
            Assert.That(latch.TrySet(ShotOutcome.MissWide), Is.False);
            Assert.That(latch.Outcome, Is.EqualTo(ShotOutcome.Goal));
            Assert.That(latch.DuplicateTerminalEvents, Is.EqualTo(1));
        }
    }
}
