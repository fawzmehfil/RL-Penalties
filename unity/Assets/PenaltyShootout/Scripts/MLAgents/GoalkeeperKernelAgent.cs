using PenaltyShootout.Kernel;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    /// <summary>
    /// Stage 1 exposes the stable nine-action transport plus one constant
    /// transport-health value. Semantic observations and rewards are
    /// deliberately introduced as a versioned Stage 2 contract.
    /// </summary>
    public sealed class GoalkeeperKernelAgent : Agent, IGoalkeeperActionSource
    {
        private GoalkeeperAction pendingAction = GoalkeeperAction.Hold;
        private GoalkeeperActionMask currentMask = GoalkeeperActionMask.HoldOnly;
        private GoalkeeperAction? bufferedDiveAction;
        private bool hasPendingAction;

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Q))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftLow;
            }
            else if (Input.GetKeyDown(KeyCode.W))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                bufferedDiveAction = GoalkeeperAction.DiveLeftHigh;
            }
            else if (Input.GetKeyDown(KeyCode.U))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightLow;
            }
            else if (Input.GetKeyDown(KeyCode.I))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightMiddle;
            }
            else if (Input.GetKeyDown(KeyCode.O))
            {
                bufferedDiveAction = GoalkeeperAction.DiveRightHigh;
            }
#endif
        }

        public override void OnEpisodeBegin()
        {
            pendingAction = GoalkeeperAction.Hold;
            currentMask = GoalkeeperActionMask.HoldOnly;
            bufferedDiveAction = null;
            hasPendingAction = false;
            // An initial decision is required so Python reset() receives the
            // behavior specification before the physical shot begins.
            RequestDecision();
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            // ML-Agents requires a sensor value to emit a decision step. This
            // constant carries no environment state and is not a learnable
            // observation contract; Stage 2 replaces it with state-v0.
            sensor.AddObservation(0.0f);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var discrete = actions.DiscreteActions;
            if (discrete.Length == 0)
            {
                pendingAction = GoalkeeperAction.Hold;
                hasPendingAction = true;
                return;
            }

            var requested = (GoalkeeperAction)discrete[0];
            pendingAction = currentMask.IsAllowed(requested)
                ? requested
                : GoalkeeperAction.Hold;
            hasPendingAction = true;
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            for (var action = 0; action <= (int)GoalkeeperAction.DiveRightHigh; action++)
            {
                if (!currentMask.IsAllowed((GoalkeeperAction)action))
                {
                    actionMask.SetActionEnabled(0, action, false);
                }
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var action = GoalkeeperAction.Hold;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (bufferedDiveAction.HasValue &&
                currentMask.IsAllowed(bufferedDiveAction.Value))
            {
                action = bufferedDiveAction.Value;
                bufferedDiveAction = null;
            }
            else if (Input.GetKey(KeyCode.A))
            {
                action = GoalkeeperAction.ShuffleLeft;
            }
            else if (Input.GetKey(KeyCode.D))
            {
                action = GoalkeeperAction.ShuffleRight;
            }
#endif
            var discreteActions = actionsOut.DiscreteActions;
            discreteActions[0] = (int)action;
        }

        public GoalkeeperAction Decide(
            GoalkeeperDecisionContext context,
            GoalkeeperActionMask actionMask)
        {
            currentMask = actionMask;
            var result = hasPendingAction && currentMask.IsAllowed(pendingAction)
                ? pendingAction
                : GoalkeeperAction.Hold;
            hasPendingAction = false;
            RequestDecision();
            return result;
        }

        public void OnAttemptStarted(long attemptId)
        {
            pendingAction = GoalkeeperAction.Hold;
            currentMask = GoalkeeperActionMask.HoldOnly;
            bufferedDiveAction = null;
            hasPendingAction = false;
        }

        public void OnAttemptEnded(AttemptResult result)
        {
            EndEpisode();
        }
    }
}
