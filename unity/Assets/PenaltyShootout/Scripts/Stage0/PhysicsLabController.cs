using System.Collections.Generic;
using UnityEngine;

namespace PenaltyShootout.Stage0
{
    [DefaultExecutionOrder(-100)]
    public sealed class PhysicsLabController : MonoBehaviour
    {
        [Header("Scene references")]
        public Rigidbody Ball;
        public Transform TargetMarker;
        public LineRenderer Trajectory;
        public ConnectionProbeAgent ProbeAgent;

        [Header("Canonical shot")]
        public Vector3 LaunchPosition = new Vector3(0f, Stage0Constants.BallRadius, Stage0Constants.PenaltyMarkDistance);
        public Vector3 TargetPosition = new Vector3(0f, 1.2f, 0f);
        public float FlightTime = Stage0Constants.CanonicalFlightTime;
        public float Timeout = Stage0Constants.AttemptTimeout;
        public bool AutoLaunch = true;

        private readonly OutcomeLatch outcomeLatch = new OutcomeLatch();
        private readonly List<Vector3> trajectoryPoints = new List<Vector3>(128);
        private Vector3 previousPosition;
        private bool active;
        private float elapsed;
        private bool hasCentrePlaneIntersection;
        private Vector3 centrePlaneIntersection;
        private int completedAttempts;
        private int invalidAttempts;

        public ShotOutcome CurrentOutcome => outcomeLatch.Outcome;
        public bool IsActive => active;
        public float Elapsed => elapsed;
        public Vector3 CentrePlaneIntersection => centrePlaneIntersection;
        public bool HasCentrePlaneIntersection => hasCentrePlaneIntersection;
        public int CompletedAttempts => completedAttempts;
        public int InvalidAttempts => invalidAttempts;

        private void Awake()
        {
            Time.fixedDeltaTime = Stage0Constants.FixedTimestep;
            if (TargetMarker != null)
            {
                TargetMarker.position = TargetPosition;
            }

            if (Ball == null)
            {
                enabled = false;
                Debug.LogError("PhysicsLabController requires a ball Rigidbody.");
            }
        }

        private void Start()
        {
            if (ProbeAgent == null && AutoLaunch)
            {
                BeginAttempt();
            }
        }

        public void BeginAttempt()
        {
            if (Ball == null)
            {
                return;
            }

            outcomeLatch.Reset();
            elapsed = 0f;
            active = false;
            hasCentrePlaneIntersection = false;
            centrePlaneIntersection = default;
            trajectoryPoints.Clear();

            ResetRigidBody(Ball, LaunchPosition);
            previousPosition = LaunchPosition;
            AddTrajectoryPoint(LaunchPosition);

            if (TargetMarker != null)
            {
                TargetMarker.position = TargetPosition;
            }

            if (AutoLaunch)
            {
                LaunchCanonicalShot();
            }
        }

        public void LaunchCanonicalShot()
        {
            if (Ball == null || active)
            {
                return;
            }

            var velocity = BallisticShotSolver.SolvePhysXInitialVelocity(
                LaunchPosition,
                TargetPosition,
                FlightTime,
                Physics.gravity,
                Time.fixedDeltaTime);

            Ball.linearVelocity = velocity;
            Ball.angularVelocity = Vector3.zero;
            Ball.WakeUp();
            previousPosition = Ball.position;
            active = true;
        }

        public void ResetOnly()
        {
            if (Ball == null)
            {
                return;
            }

            outcomeLatch.Reset();
            elapsed = 0f;
            active = false;
            hasCentrePlaneIntersection = false;
            centrePlaneIntersection = default;
            trajectoryPoints.Clear();
            ResetRigidBody(Ball, LaunchPosition);
            previousPosition = LaunchPosition;
            AddTrajectoryPoint(LaunchPosition);
        }

        public static void ResetRigidBody(Rigidbody body, Vector3 position)
        {
            body.position = position;
            body.rotation = Quaternion.identity;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
        }

        private void FixedUpdate()
        {
            if (!active || Ball == null)
            {
                return;
            }

            elapsed += Time.fixedDeltaTime;
            var current = Ball.position;
            AddTrajectoryPoint(current);

            if (!BallisticShotSolver.IsFinite(current) ||
                !BallisticShotSolver.IsFinite(Ball.linearVelocity) ||
                !BallisticShotSolver.IsFinite(Ball.angularVelocity))
            {
                Complete(ShotOutcome.Invalid);
                return;
            }

            if (!hasCentrePlaneIntersection &&
                GoalLineGeometry.TryIntersectPlane(previousPosition, current, 0f, out var centreIntersection))
            {
                centrePlaneIntersection = centreIntersection;
                hasCentrePlaneIntersection = true;
            }

            if (GoalLineGeometry.TryIntersectPlane(
                    previousPosition,
                    current,
                    -Stage0Constants.BallRadius,
                    out var wholeBallIntersection))
            {
                Complete(GoalLineGeometry.ClassifyWholeBallCrossing(wholeBallIntersection));
                return;
            }

            if (elapsed >= Timeout)
            {
                Complete(ShotOutcome.Timeout);
                return;
            }

            previousPosition = current;
        }

        private void Complete(ShotOutcome outcome)
        {
            if (!outcomeLatch.TrySet(outcome))
            {
                return;
            }

            active = false;
            completedAttempts++;
            if (outcome == ShotOutcome.Invalid)
            {
                invalidAttempts++;
            }

            ProbeAgent?.CompleteAttempt(outcome);
        }

        private void AddTrajectoryPoint(Vector3 point)
        {
            trajectoryPoints.Add(point);
            if (Trajectory == null)
            {
                return;
            }

            Trajectory.positionCount = trajectoryPoints.Count;
            Trajectory.SetPosition(trajectoryPoints.Count - 1, point);
        }

        private void OnGUI()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(20f, 20f, 360f, 210f), GUI.skin.box);
            GUILayout.Label("Penalty Shootout RL — PhysicsLab");
            GUILayout.Label($"Environment: {Stage0Constants.EnvironmentId}");
            GUILayout.Label($"Outcome: {CurrentOutcome}");
            GUILayout.Label($"Elapsed: {elapsed:F2}s");
            GUILayout.Label(hasCentrePlaneIntersection
                ? $"Goal-plane centre: {centrePlaneIntersection:F3}"
                : "Goal-plane centre: pending");

            if (GUILayout.Button("Reset and launch canonical shot"))
            {
                BeginAttempt();
            }

            if (GUILayout.Button("Reset only"))
            {
                ResetOnly();
            }

            if (GUILayout.Button("Launch"))
            {
                LaunchCanonicalShot();
            }

            GUILayout.EndArea();
        }
    }
}
