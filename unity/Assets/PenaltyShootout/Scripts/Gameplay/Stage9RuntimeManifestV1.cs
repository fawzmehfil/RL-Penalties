using UnityEngine;

namespace PenaltyShootout.Gameplay
{
    [CreateAssetMenu(
        fileName = "Stage9RuntimeManifestV1",
        menuName = "Penalty Shootout/Stage 9/Runtime Manifest V1")]
    public sealed class Stage9RuntimeManifestV1 : ScriptableObject
    {
        public string SceneId = Stage9PresentationContractsV1.SceneId;
        public string StyleId = Stage9PresentationContractsV1.StyleId;
        public string BuildId = "stage9-development";
        public string GitCommit = "unknown";
        public string Stage8ArtifactHash = "unknown";
        public string InterceptionModelHash = "unknown";
        public string TimingModelHash = "unknown";
    }
}
