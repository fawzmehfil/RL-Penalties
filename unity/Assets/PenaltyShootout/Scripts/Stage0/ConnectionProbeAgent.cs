using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PenaltyShootout.Stage0
{
    public sealed class ConnectionProbeAgent : Agent
    {
        public PhysicsLabController Controller;

        public override void Initialize()
        {
            if (Controller == null)
            {
                Controller = FindFirstObjectByType<PhysicsLabController>();
            }
        }

        public override void OnEpisodeBegin()
        {
            Controller?.BeginAttempt();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (Controller == null || Controller.Ball == null)
            {
                for (var i = 0; i < 8; i++)
                {
                    sensor.AddObservation(0f);
                }

                return;
            }

            var position = Controller.Ball.position;
            var velocity = Controller.Ball.linearVelocity;
            sensor.AddObservation(position / 15f);
            sensor.AddObservation(velocity / 30f);
            sensor.AddObservation(Mathf.Clamp01(Controller.Elapsed / Stage0Constants.AttemptTimeout));
            sensor.AddObservation(Controller.IsActive ? 1f : 0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            // Stage 0 exposes one legal no-op action solely to verify the
            // Unity-to-Python decision loop. It is not a goalkeeper policy.
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discreteActions = actionsOut.DiscreteActions;
            discreteActions[0] = 0;
        }

        public void CompleteAttempt(ShotOutcome outcome)
        {
            SetReward(outcome == ShotOutcome.Goal ? 1f : 0f);
            EndEpisode();
        }
    }
}
