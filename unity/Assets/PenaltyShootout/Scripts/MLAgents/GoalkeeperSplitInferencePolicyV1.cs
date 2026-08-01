using System;
using PenaltyShootout.Kernel;
using Unity.InferenceEngine;
using UnityEngine;

namespace PenaltyShootout.MLAgents
{
    public sealed class GoalkeeperSplitInferencePolicyV1 : MonoBehaviour
    {
        public const string ContractId =
            "goalkeeper-control-v2-split-native-v1";
        public const string EnableArgument =
            "--stage5-native-split-inference";
        public const string InterceptionOutputName = "continuous_actions";
        public const string TimingOutputName = "commit_logit";
        public const float DefaultCommitThreshold = 0.47f;

        [SerializeField]
        private ModelAsset interceptionModel;

        [SerializeField]
        private ModelAsset timingModel;

        [SerializeField]
        [Range(0f, 1f)]
        private float commitThreshold = DefaultCommitThreshold;

        private Worker interceptionWorker;
        private Worker timingWorker;
        private bool hasCommitted;
        private int evaluationCount;
        private int invalidOutputCount;

        public ModelAsset InterceptionModel => interceptionModel;
        public ModelAsset TimingModel => timingModel;
        public float CommitThreshold => commitThreshold;
        public int EvaluationCount => evaluationCount;
        public int InvalidOutputCount => invalidOutputCount;

        public void Configure(
            ModelAsset interception,
            ModelAsset timing,
            float threshold = DefaultCommitThreshold)
        {
            interceptionModel = interception;
            timingModel = timing;
            commitThreshold = Mathf.Clamp01(threshold);
            DisposeWorkers();
        }

        public void ResetAttempt()
        {
            hasCommitted = false;
        }

        public bool TryEvaluate(
            float[] observations,
            GoalkeeperControlActionMask actionMask,
            out GoalkeeperControlCommand command,
            out float commitProbability)
        {
            command = GoalkeeperControlCommand.Neutral;
            commitProbability = 0f;
            if (observations == null ||
                observations.Length !=
                    KernelConstants.GoalkeeperControlV2ObservationSize ||
                !EnsureWorkers())
            {
                invalidOutputCount++;
                return false;
            }

            using var input = new Tensor<float>(
                new TensorShape(
                    1,
                    KernelConstants.GoalkeeperControlV2ObservationSize),
                observations);
            interceptionWorker.Schedule(input);
            var interceptionOutput =
                interceptionWorker.PeekOutput(InterceptionOutputName)
                    as Tensor<float>;
            if (interceptionOutput == null)
            {
                invalidOutputCount++;
                return false;
            }

            using var continuous = interceptionOutput.ReadbackAndClone();
            if (continuous.count !=
                GoalkeeperControlSpace.ContinuousActionCount)
            {
                invalidOutputCount++;
                return false;
            }

            timingWorker.Schedule(input);
            var timingOutput =
                timingWorker.PeekOutput(TimingOutputName) as Tensor<float>;
            if (timingOutput == null || timingOutput.count != 1)
            {
                invalidOutputCount++;
                return false;
            }

            using var timing = timingOutput.ReadbackAndClone();
            var moveX = continuous[0];
            var aimX = continuous[1];
            var aimY = continuous[2];
            var reach = continuous[3];
            var logit = timing[0];
            if (!KernelMath.IsFinite(moveX) ||
                !KernelMath.IsFinite(aimX) ||
                !KernelMath.IsFinite(aimY) ||
                !KernelMath.IsFinite(reach) ||
                !KernelMath.IsFinite(logit))
            {
                invalidOutputCount++;
                return false;
            }

            commitProbability = Sigmoid(logit);
            var commit =
                !hasCommitted &&
                actionMask.CanCommit &&
                commitProbability >= commitThreshold;
            if (commit)
            {
                hasCommitted = true;
            }

            command = new GoalkeeperControlCommand
            {
                MoveX = Mathf.Clamp(moveX, -1f, 1f),
                AimX = Mathf.Clamp(aimX, -1f, 1f),
                AimY = Mathf.Clamp(aimY, -1f, 1f),
                Reach = Mathf.Clamp(reach, -1f, 1f),
                Commit = commit,
            };
            evaluationCount++;
            return true;
        }

        public static float Sigmoid(float value)
        {
            return 1f / (1f + Mathf.Exp(-Mathf.Clamp(value, -60f, 60f)));
        }

        private bool EnsureWorkers()
        {
            if (interceptionWorker != null && timingWorker != null)
            {
                return true;
            }

            if (interceptionModel == null || timingModel == null)
            {
                return false;
            }

            DisposeWorkers();
            interceptionWorker = new Worker(
                ModelLoader.Load(interceptionModel),
                BackendType.CPU);
            timingWorker = new Worker(
                ModelLoader.Load(timingModel),
                BackendType.CPU);
            return true;
        }

        private void OnDisable()
        {
            DisposeWorkers();
        }

        private void DisposeWorkers()
        {
            interceptionWorker?.Dispose();
            timingWorker?.Dispose();
            interceptionWorker = null;
            timingWorker = null;
        }
    }
}
