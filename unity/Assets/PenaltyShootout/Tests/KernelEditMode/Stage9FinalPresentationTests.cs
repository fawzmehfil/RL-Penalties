using System.Linq;
using System.IO;
using NUnit.Framework;
using PenaltyShootout.Gameplay;
using PenaltyShootout.Stage0.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PenaltyShootout.Kernel.Tests
{
    public sealed class Stage9FinalPresentationTests
    {
        [Test]
        public void FrozenContractsAndGeometryRemainUnchanged()
        {
            Assert.That(
                Stage9PresentationContractsV1.StyleId,
                Is.EqualTo("rounded-football-v1"));
            Assert.That(
                Stage9PresentationContractsV1.SceneId,
                Is.EqualTo("penalty-shootout-final-v1"));
            Assert.That(
                Stage9ProjectBuilder.ValidateGeometryInvariance(out var error),
                Is.True,
                error);
        }

        [Test]
        public void PresentationObjectsHaveNoPhysicsAndNoShooterCharacter()
        {
            var prefab = PrefabUtility.LoadPrefabContents(Stage9ProjectBuilder.PrefabPath);
            try
            {
                var presentation = prefab.transform.Find("Stage9Presentation");
                Assert.That(presentation, Is.Not.Null);
                Assert.That(
                    presentation.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);

                Assert.That(Find(presentation, "PenaltyTakerPresentation"), Is.Null);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        [Test]
        public void AudioLibraryIsCompleteAndSelectionIsDeterministic()
        {
            var library = AssetDatabase.LoadAssetAtPath<Stage9AudioLibraryV1>(
                Stage9ProjectBuilder.AudioLibraryPath);
            Assert.That(library, Is.Not.Null);
            Assert.That(library.Validate(out var error), Is.True, error);
            var first = Stage9AudioLibraryV1.SelectIndex(
                3,
                20260807UL,
                4,
                Stage9AudioEventV1.GloveContact);
            var second = Stage9AudioLibraryV1.SelectIndex(
                3,
                20260807UL,
                4,
                Stage9AudioEventV1.GloveContact);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.InRange(0, 2));
        }

        [Test]
        public void Stage8ArtifactAndRuntimeManifestArePackaged()
        {
            var analysisPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets/Stage8Analysis/index.html");
            Assert.That(File.Exists(analysisPath), Is.True);
            var analysis = File.ReadAllText(analysisPath);
            Assert.That(analysis, Does.Not.Contain("src=\"./assets/"));
            Assert.That(analysis, Does.Not.Contain("href=\"./assets/"));
            Assert.That(
                analysis,
                Does.Contain("goalkeeper-control-v2-stage8-heatmap-source-20k"));
            var manifest = AssetDatabase.LoadAssetAtPath<Stage9RuntimeManifestV1>(
                Stage9ProjectBuilder.ManifestPath);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.InterceptionModelHash,
                Is.EqualTo("ad95050acb5032abffd005e9d5ddf78b8e1c362d79a5d9871b05c50a342b20b0"));
            Assert.That(manifest.TimingModelHash,
                Is.EqualTo("26c3a80b375574a4e1c02b97183e2ab390736eae76879296ad3daaf85492850b"));
        }

        [Test]
        public void FinalMenusRetainReadableExplicitLayoutHeights()
        {
            var prefab = PrefabUtility.LoadPrefabContents(Stage9ProjectBuilder.HudPrefabPath);
            try
            {
                foreach (var panelName in new[]
                         {
                             "PausePanel",
                             "CompletePanel",
                             "Stage9AboutPanel",
                         })
                {
                    var panel = Find(prefab.transform, panelName);
                    var title = panel.Find("Title") as RectTransform;
                    var body = panel.Find("Body") as RectTransform;
                    Assert.That(title, Is.Not.Null, $"{panelName} title missing");
                    Assert.That(body, Is.Not.Null, $"{panelName} body missing");
                    Assert.That(title.anchorMin.y, Is.EqualTo(1f));
                    Assert.That(title.anchorMax.y, Is.EqualTo(1f));
                    Assert.That(body.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
                    foreach (Transform child in body)
                    {
                        var layout = child.GetComponent<LayoutElement>();
                        Assert.That(layout, Is.Not.Null, $"{panelName}/{child.name}");
                        Assert.That(layout.preferredHeight, Is.GreaterThanOrEqualTo(32f),
                            $"{panelName}/{child.name}");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == name);
        }

    }
}
