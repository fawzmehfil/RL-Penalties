using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [CreateAssetMenu(
        fileName = "Stage7RuntimeManifestV1",
        menuName = "Penalty Shootout/Stage 7/Runtime Manifest V1")]
    public sealed class Stage7RuntimeManifestV1 : ScriptableObject
    {
        public string BuildId = "development";
        public string GitCommit = "unknown";
        public string InputConfigHash = "unknown";
        public string InterceptionModelHash = "unknown";
        public string TimingModelHash = "unknown";
    }
}
