using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using PenaltyShootout.Gameplay;
using PenaltyShootout.Kernel;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PenaltyShootout.Stage0.Editor
{
    public static class Stage9ProjectBuilder
    {
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage9PlayableArena.prefab";
        public const string HudPrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage9GameplayHud.prefab";
        public const string ScenePath =
            "Assets/PenaltyShootout/Scenes/PenaltyShootoutFinal.unity";
        public const string ManifestPath =
            "Assets/PenaltyShootout/Config/Stage9RuntimeManifestV1.asset";
        public const string AudioLibraryPath =
            "Assets/PenaltyShootout/Config/Stage9AudioLibraryV1.asset";
        public const string AudioMixerPath =
            "Assets/PenaltyShootout/Audio/Stage9/Stage9AudioMixer.mixer";

        private const string MaterialDirectory =
            "Assets/PenaltyShootout/Materials/Stage9";
        private const string StreamingAnalysisDirectory =
            "Assets/StreamingAssets/Stage8Analysis";

        private static readonly Color Charcoal = new Color(0.055f, 0.07f, 0.075f, 0.94f);
        private static readonly Color Teal = new Color(0.045f, 0.36f, 0.39f, 1f);
        private static readonly Color TealDark = new Color(0.025f, 0.18f, 0.22f, 1f);
        private static readonly Color Navy = new Color(0.035f, 0.09f, 0.18f, 1f);
        private static readonly Color Amber = new Color(1f, 0.69f, 0.12f, 1f);
        private static readonly Color Cyan = new Color(0.16f, 0.76f, 0.76f, 1f);
        private static readonly Color SoftWhite = new Color(0.93f, 0.94f, 0.9f, 1f);

        [MenuItem("Penalty Shootout/Stage 9/Prepare Final Presentation")]
        public static void PrepareProject()
        {
            RequireFrozenAssets();
            EnsureFolders();
            CreateMaterials();
            var audioLibrary = CreateAudioLibrary();
            CreateAudioMixerIfMissing();
            CopyStage8Analysis();
            var manifest = CreateManifest();
            CreateArenaPrefab();
            CreateHudPrefab();
            CreateScene(audioLibrary, manifest);
            AddSceneToBuildSettings();
            if (!ValidateGeometryInvariance(out var geometryError))
            {
                throw new InvalidOperationException(geometryError);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Debug.Log(
                "Stage 9 final presentation prepared with frozen gameplay geometry.");
        }

        [MenuItem("Penalty Shootout/Stage 9/Rebuild Final Presentation")]
        public static void RebuildProject()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(HudPrefabPath);
            AssetDatabase.DeleteAsset(PrefabPath);
            PrepareProject();
        }

        [MenuItem("Penalty Shootout/Stage 9/Validate Geometry Invariance")]
        public static void ValidateGeometryMenu()
        {
            if (!ValidateGeometryInvariance(out var error))
            {
                throw new InvalidOperationException(error);
            }
            UnityEngine.Debug.Log(
                "Stage 9 geometry invariance passed: authoritative Stage 7 geometry is unchanged.");
        }

        [MenuItem("Penalty Shootout/Stage 9/Build Final macOS Demo")]
        public static void BuildMac()
        {
            PrepareProject();
            var output = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../builds/macos/PenaltyShootoutFinal.app"));
            if (Directory.Exists(output))
            {
                Directory.Delete(output, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"Stage 9 build failed with {report.summary.totalErrors} errors.");
            }
            UnityEngine.Debug.Log($"Stage 9 final demo built at {output}");
        }

        public static bool ValidateGeometryInvariance(out string error)
        {
            var baselineAsset = AssetDatabase.LoadAssetAtPath<GameObject>(
                Stage7ProjectBuilder.PrefabPath);
            var candidateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (baselineAsset == null || candidateAsset == null)
            {
                error = "Stage 7 and Stage 9 arena prefabs must exist.";
                return false;
            }
            var baseline = PrefabUtility.LoadPrefabContents(Stage7ProjectBuilder.PrefabPath);
            var candidate = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (var baselineTransform in baseline.GetComponentsInChildren<Transform>(true))
                {
                    if (baselineTransform == baseline.transform)
                    {
                        continue;
                    }
                    var path = RelativePath(baseline.transform, baselineTransform);
                    var candidateTransform = candidate.transform.Find(path);
                    if (candidateTransform == null)
                    {
                        error = $"Stage 9 is missing frozen transform '{path}'.";
                        return false;
                    }
                    if (!Approximately(baselineTransform.localPosition, candidateTransform.localPosition) ||
                        !Approximately(baselineTransform.localRotation, candidateTransform.localRotation) ||
                        !Approximately(baselineTransform.localScale, candidateTransform.localScale))
                    {
                        error = $"Stage 9 changed frozen transform '{path}'.";
                        return false;
                    }
                    if (!CompareMesh(baselineTransform, candidateTransform) ||
                        !CompareCollider(baselineTransform, candidateTransform) ||
                        !CompareRigidbody(baselineTransform, candidateTransform))
                    {
                        error = $"Stage 9 changed frozen geometry or physics at '{path}'.";
                        return false;
                    }
                }
                var baselineColliderCount = baseline.GetComponentsInChildren<Collider>(true).Length;
                var candidateFrozenColliderCount = candidate.GetComponentsInChildren<Collider>(true)
                    .Count(collider => !IsUnderPresentation(collider.transform, candidate.transform));
                if (baselineColliderCount != candidateFrozenColliderCount)
                {
                    error = "Stage 9 changed the authoritative collider count.";
                    return false;
                }
                var presentation = candidate.transform.Find("Stage9Presentation");
                if (presentation == null ||
                    presentation.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    presentation.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                {
                    error = "Stage 9 presentation objects must be non-physical.";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(baseline);
                PrefabUtility.UnloadPrefabContents(candidate);
            }
        }

        private static void RequireFrozenAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Stage7ProjectBuilder.PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(Stage7ProjectBuilder.HudPrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(Stage7ProjectBuilder.RuntimeInputActionsPath) == null ||
                AssetDatabase.LoadAssetAtPath<Stage7RuntimeManifestV1>(Stage7ProjectBuilder.RuntimeManifestPath) == null ||
                AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(
                    Stage5ProjectBuilder.SplitInterceptionModelPath) == null ||
                AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(
                    Stage5ProjectBuilder.SplitTimingModelPath) == null)
            {
                throw new InvalidOperationException(
                    "Stage 9 requires the frozen Stage 7 game and selected native models.");
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/PenaltyShootout/Materials", "Stage9");
            EnsureFolder("Assets", "StreamingAssets");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void CreateMaterials()
        {
            CreateMaterial("KeeperKit", Teal, 0.38f, 0f);
            CreateMaterial("KeeperShorts", Navy, 0.32f, 0f);
            CreateMaterial("KeeperGloves", Amber, 0.58f, 0f);
            CreateMaterial("ToySkin", new Color(0.73f, 0.49f, 0.31f, 1f), 0.42f, 0f);
            CreateMaterial("PitchLight", new Color(0.14f, 0.45f, 0.24f, 1f), 0.16f, 0f);
            CreateMaterial("PitchDark", new Color(0.105f, 0.37f, 0.20f, 1f), 0.16f, 0f);
            CreateMaterial("PaintWhite", SoftWhite, 0.34f, 0f);
            CreateMaterial("GoalFrame", new Color(0.94f, 0.95f, 0.91f, 1f), 0.62f, 0.03f);
            CreateMaterial("Net", new Color(0.72f, 0.78f, 0.76f, 1f), 0.18f, 0f);
            CreateMaterial("Concrete", new Color(0.22f, 0.24f, 0.25f, 1f), 0.18f, 0f);
            CreateMaterial("SeatTeal", new Color(0.045f, 0.27f, 0.30f, 1f), 0.25f, 0f);
            CreateMaterial("SeatCoral", new Color(0.48f, 0.12f, 0.13f, 1f), 0.25f, 0f);
            CreateMaterial("Ball", new Color(0.97f, 0.97f, 0.92f, 1f), 0.48f, 0f);
        }

        private static Material CreateMaterial(
            string name,
            Color color,
            float smoothness,
            float metallic)
        {
            var path = $"{MaterialDirectory}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                    Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            material.color = color;
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Stage9AudioLibraryV1 CreateAudioLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<Stage9AudioLibraryV1>(
                AudioLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<Stage9AudioLibraryV1>();
                AssetDatabase.CreateAsset(library, AudioLibraryPath);
            }
            library.Strike = Clips("KenneyImpact/strike_", 3);
            library.GloveContact = Clips("KenneyImpact/glove_", 3);
            library.BodyContact = Clips("KenneyImpact/body_", 2);
            library.GoalFrame = Clips("KenneyImpact/frame_", 2);
            library.GoalNet = Clips("KenneyImpact/net_", 2);
            library.GroundBounce = Clips("KenneyImpact/bounce_", 2);
            library.UiConfirm = Clips("KenneyInterface/ui_confirm_", 2);
            library.UiBack = Clips("KenneyInterface/ui_back_", 2);
            library.Ambience = new[] { Clip("Freesound/stadium_ambience_cc0.ogg") };
            library.GoalReaction = new[] { Clip("Freesound/goal_reaction_cc0.ogg") };
            library.SaveReaction = new[] { Clip("Freesound/save_reaction_cc0.ogg") };
            library.MissReaction = new[] { Clip("Freesound/miss_reaction_cc0.ogg") };
            EditorUtility.SetDirty(library);
            if (!library.Validate(out var error))
            {
                throw new InvalidOperationException(error);
            }
            return library;
        }

        private static AudioClip[] Clips(string prefix, int count)
        {
            var clips = new AudioClip[count];
            for (var index = 0; index < count; index++)
            {
                clips[index] = Clip($"{prefix}{index + 1:00}.ogg");
            }
            return clips;
        }

        private static AudioClip Clip(string relativePath)
        {
            var path = "Assets/PenaltyShootout/Audio/Stage9/" + relativePath;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                throw new InvalidOperationException($"Missing Stage 9 audio clip: {path}");
            }
            return clip;
        }

        private static void CreateAudioMixerIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerPath) != null)
            {
                return;
            }
            var type = Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor");
            var method = type?.GetMethod(
                "CreateMixerControllerAtPath",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            method?.Invoke(null, new object[] { AudioMixerPath });
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioMixerPath) == null)
            {
                throw new InvalidOperationException(
                    "Unity could not create Stage9AudioMixer.mixer.");
            }
        }

        private static Stage9RuntimeManifestV1 CreateManifest()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<Stage9RuntimeManifestV1>(ManifestPath);
            if (manifest == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(ManifestPath) != null)
                {
                    AssetDatabase.DeleteAsset(ManifestPath);
                }
                manifest = ScriptableObject.CreateInstance<Stage9RuntimeManifestV1>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
            }
            manifest.SceneId = Stage9PresentationContractsV1.SceneId;
            manifest.StyleId = Stage9PresentationContractsV1.StyleId;
            manifest.GitCommit = ReadGitCommit();
            manifest.BuildId = "stage9-" + ShortHash(manifest.GitCommit);
            manifest.InterceptionModelHash = Sha256File(
                Stage5ProjectBuilder.SplitInterceptionModelPath);
            manifest.TimingModelHash = Sha256File(
                Stage5ProjectBuilder.SplitTimingModelPath);
            manifest.Stage8ArtifactHash = Sha256File(
                "Assets/StreamingAssets/Stage8Analysis/index.html");
            EditorUtility.SetDirty(manifest);
            return manifest;
        }

        private static void CreateArenaPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(Stage7ProjectBuilder.PrefabPath);
            try
            {
                root.name = "Stage9PlayableArena";
                var controller = root.GetComponent<PenaltyAreaController>();
                var keeper = Find(root.transform, "GoalkeeperProxy");
                var ball = controller.Ball.transform;
                AssignKeeperMaterials(keeper);
                AssignMaterial(ball, Material("Ball"));
                AssignByToken(root.transform, "Ground", Material("PitchDark"));
                AssignByToken(root.transform, "Post", Material("GoalFrame"));
                AssignByToken(root.transform, "Crossbar", Material("GoalFrame"));

                var presentation = new GameObject("Stage9Presentation").transform;
                presentation.SetParent(root.transform, false);
                CreatePitchPresentation(presentation);
                CreateVenuePresentation(presentation);
                CreateNetPresentation(presentation, controller);
                CreateKeeperFeedback(root, presentation, keeper, controller);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssignKeeperMaterials(Transform keeper)
        {
            if (keeper == null)
            {
                throw new InvalidOperationException("GoalkeeperProxy was not found.");
            }
            foreach (var renderer in keeper.GetComponentsInChildren<Renderer>(true))
            {
                var token = renderer.name;
                if (token.IndexOf("Glove", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.sharedMaterial = Material("KeeperGloves");
                }
                else if (token.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.sharedMaterial = Material("ToySkin");
                }
                else if (token.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.sharedMaterial = Material("KeeperShorts");
                }
                else
                {
                    renderer.sharedMaterial = Material("KeeperKit");
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static void CreateKeeperFeedback(
            GameObject root,
            Transform presentation,
            Transform keeper,
            PenaltyAreaController controller)
        {
            var effects = new GameObject("ContactFeedback", typeof(ParticleSystem));
            effects.transform.SetParent(presentation, false);
            var particles = effects.GetComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.15f;
            main.startLifetime = 0.16f;
            main.startSpeed = 0.35f;
            main.startSize = 0.055f;
            main.startColor = Amber;
            main.maxParticles = 18;
            var emission = particles.emission;
            emission.enabled = false;
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = Material("KeeperGloves");
            var component = root.AddComponent<GoalkeeperPresentationV1>();
            component.Configure(
                controller,
                keeper.GetComponentsInChildren<Renderer>(true),
                Find(keeper, "LeftGlove")?.GetComponent<Renderer>(),
                Find(keeper, "RightGlove")?.GetComponent<Renderer>(),
                particles);
        }

        private static void CreatePitchPresentation(Transform parent)
        {
            var pitch = new GameObject("PitchPresentation").transform;
            pitch.SetParent(parent, false);
            for (var index = 0; index < 7; index++)
            {
                CreateCubeVisual(
                    pitch,
                    $"MowingBand{index + 1:00}",
                    new Vector3(0f, 0.008f, -1f + index * 2.4f),
                    new Vector3(23f, 0.006f, 2.4f),
                    index % 2 == 0 ? Material("PitchLight") : Material("PitchDark"));
            }
            CreateCubeVisual(pitch, "GoalLine", new Vector3(0f, 0.016f, 0f),
                new Vector3(8.2f, 0.008f, 0.055f), Material("PaintWhite"));
            CreateCubeVisual(pitch, "PenaltyAreaLine", new Vector3(0f, 0.016f, 5.5f),
                new Vector3(18.3f, 0.008f, 0.055f), Material("PaintWhite"));
            CreateCubeVisual(pitch, "PenaltyAreaLeft", new Vector3(-9.15f, 0.016f, 2.75f),
                new Vector3(0.055f, 0.008f, 5.5f), Material("PaintWhite"));
            CreateCubeVisual(pitch, "PenaltyAreaRight", new Vector3(9.15f, 0.016f, 2.75f),
                new Vector3(0.055f, 0.008f, 5.5f), Material("PaintWhite"));
            var spot = CreatePrimitiveVisual(
                PrimitiveType.Cylinder,
                pitch,
                "PenaltySpot",
                new Vector3(0f, 0.019f, 11f),
                new Vector3(0.12f, 0.004f, 0.12f),
                Material("PaintWhite"));
            spot.transform.localRotation = Quaternion.identity;
        }

        private static void CreateNetPresentation(
            Transform parent,
            PenaltyAreaController controller)
        {
            var net = new GameObject("GoalNetPresentation").transform;
            net.SetParent(parent, false);
            net.localPosition = Vector3.zero;
            var renderers = new List<Renderer>();
            for (var index = 0; index <= 12; index++)
            {
                var x = Mathf.Lerp(-3.66f, 3.66f, index / 12f);
                renderers.Add(CreateLine(net, $"NetVertical{index:00}",
                    new Vector3(x, 0.08f, -0.42f),
                    new Vector3(x, 2.44f, -0.42f)));
            }
            for (var index = 0; index <= 6; index++)
            {
                var y = Mathf.Lerp(0.08f, 2.44f, index / 6f);
                renderers.Add(CreateLine(net, $"NetHorizontal{index:00}",
                    new Vector3(-3.66f, y, -0.42f),
                    new Vector3(3.66f, y, -0.42f)));
            }
            var component = net.gameObject.AddComponent<Stage9NetPresentationV1>();
            component.Configure(controller);
        }

        private static LineRenderer CreateLine(
            Transform parent,
            string name,
            Vector3 start,
            Vector3 end)
        {
            var gameObject = new GameObject(name, typeof(LineRenderer));
            gameObject.transform.SetParent(parent, false);
            var line = gameObject.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.018f;
            line.endWidth = 0.018f;
            line.numCapVertices = 2;
            line.sharedMaterial = Material("Net");
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static void CreateVenuePresentation(Transform parent)
        {
            var venue = new GameObject("VenuePresentation").transform;
            venue.SetParent(parent, false);
            CreateCubeVisual(venue, "BackStand", new Vector3(0f, 2.0f, -7.2f),
                new Vector3(22f, 4f, 2.2f), Material("Concrete"));
            CreateCubeVisual(venue, "LeftStand", new Vector3(-10.6f, 1.35f, 3.2f),
                new Vector3(2.2f, 2.7f, 16f), Material("Concrete"));
            CreateCubeVisual(venue, "RightStand", new Vector3(10.6f, 1.35f, 3.2f),
                new Vector3(2.2f, 2.7f, 16f), Material("Concrete"));
            CreateCubeVisual(venue, "SeatBandTeal", new Vector3(0f, 2.1f, -5.95f),
                new Vector3(19.5f, 0.72f, 0.22f), Material("SeatTeal"));
            CreateCubeVisual(venue, "SeatBandCoral", new Vector3(0f, 3.05f, -6.0f),
                new Vector3(19.5f, 0.42f, 0.22f), Material("SeatCoral"));
            CreateCubeVisual(venue, "Tunnel", new Vector3(0f, 0.9f, -5.82f),
                new Vector3(2.3f, 1.8f, 0.28f), Material("KeeperShorts"));
        }

        private static GameObject CreateCubeVisual(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material) =>
            CreatePrimitiveVisual(PrimitiveType.Cube, parent, name, position, scale, material);

        private static GameObject CreatePrimitiveVisual(
            PrimitiveType primitive,
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(primitive);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localScale = scale;
            var collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static void CreateHudPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(Stage7ProjectBuilder.HudPrefabPath);
            try
            {
                root.name = "Stage9GameplayHud";
                StyleHud(root.transform);
                var pausePanel = Find(root.transform, "PausePanel");
                var pauseBody = Find(pausePanel, "Body");
                if (pausePanel != null)
                {
                    var rect = (RectTransform)pausePanel;
                    rect.sizeDelta = new Vector2(500f, 700f);
                }
                if (pauseBody != null)
                {
                    var rect = (RectTransform)pauseBody;
                    rect.sizeDelta = new Vector2(410f, 565f);
                }
                var pauseAnalysis = Button(pauseBody, "Analysis", "GOALKEEPER ANALYSIS");
                var pauseAbout = Button(pauseBody, "AudioAbout", "AUDIO / ABOUT");
                ConfigureMenuLayout(
                    pausePanel,
                    new Vector2(500f, 700f),
                    new Vector2(410f, 560f),
                    -52f,
                    -26f);

                var completePanel = Find(root.transform, "CompletePanel");
                var completeBody = Find(completePanel, "Body");
                var completeAnalysis = Button(completeBody, "Analysis", "GOALKEEPER ANALYSIS");
                ConfigureMenuLayout(
                    completePanel,
                    new Vector2(500f, 410f),
                    new Vector2(410f, 230f),
                    -52f,
                    -22f);

                var aboutPanel = CreateMenu(root.transform, "Stage9AboutPanel", "AUDIO / ABOUT", out var aboutBody);
                ((RectTransform)aboutPanel.transform).sizeDelta = new Vector2(520f, 650f);
                ((RectTransform)aboutBody).sizeDelta = new Vector2(430f, 520f);
                var master = Slider(aboutBody, "MasterVolume", "MASTER VOLUME");
                var effects = Slider(aboutBody, "EffectsVolume", "EFFECTS VOLUME");
                var ambience = Slider(aboutBody, "AmbienceVolume", "AMBIENCE VOLUME");
                var aboutText = Text(aboutBody, "AboutText", string.Empty, 16, TextAnchor.MiddleCenter);
                ((RectTransform)aboutText.transform).sizeDelta = new Vector2(420f, 150f);
                var aboutAnalysis = Button(aboutBody, "Analysis", "OPEN ANALYSIS");
                var aboutClose = Button(aboutBody, "Back", "BACK");
                ConfigureMenuLayout(
                    aboutPanel.transform,
                    new Vector2(520f, 650f),
                    new Vector2(430f, 510f),
                    -52f,
                    -22f);
                aboutPanel.SetActive(false);

                var ui = root.AddComponent<Stage9FinalUiV1>();
                ui.Configure(
                    null,
                    new[] { pauseAnalysis, completeAnalysis, aboutAnalysis },
                    new[] { pauseAbout },
                    aboutClose,
                    aboutPanel,
                    aboutText,
                    master,
                    effects,
                    ambience,
                    "stage9-development");
                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void StyleHud(Transform root)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name.IndexOf("PowerFill", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = Amber;
                }
                else if (image.name.IndexOf("CurveMarker", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = Cyan;
                }
                else if (image.name.IndexOf("Reticle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = Amber;
                }
                else if (image.name.IndexOf("Composure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = new Color(1f, 1f, 1f, 0.30f);
                }
                else if (image.name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         image.name.IndexOf("Band", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = Charcoal;
                }
                else if (image.GetComponent<Button>() != null)
                {
                    image.color = TealDark;
                }
                else if (image.GetComponent<Slider>() != null ||
                         image.name.IndexOf("Track", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = new Color(0.12f, 0.15f, 0.16f, 1f);
                }
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.color = SoftWhite;
                if (text.name == "Outcome" || text.name == "Title")
                {
                    text.fontStyle = FontStyle.Bold;
                }
            }
        }

        private static void ConfigureMenuLayout(
            Transform panel,
            Vector2 panelSize,
            Vector2 bodySize,
            float titleY,
            float bodyY)
        {
            if (panel == null)
            {
                throw new InvalidOperationException("Stage 9 menu panel is missing.");
            }

            Anchor(
                (RectTransform)panel,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                panelSize);
            var title = Find(panel, "Title");
            Anchor(
                (RectTransform)title,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, titleY),
                new Vector2(panelSize.x - 70f, 64f));

            var body = (RectTransform)Find(panel, "Body");
            Anchor(
                body,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, bodyY),
                bodySize);
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            foreach (Transform child in body)
            {
                var preferredHeight = PreferredMenuHeight(child);
                var element = child.GetComponent<LayoutElement>() ??
                    child.gameObject.AddComponent<LayoutElement>();
                element.minHeight = preferredHeight;
                element.preferredHeight = preferredHeight;
                element.flexibleHeight = 0f;
                if (child.GetComponentInChildren<Slider>(true) != null)
                {
                    ConfigureSliderLayout(child);
                }
            }
        }

        private static float PreferredMenuHeight(Transform child)
        {
            if (child.GetComponent<Button>() != null)
            {
                return 46f;
            }
            if (child.GetComponentInChildren<Slider>(true) != null)
            {
                return 58f;
            }
            if (child.name.IndexOf("Score", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 70f;
            }
            if (child.name == "AboutText")
            {
                return 145f;
            }
            return Mathf.Max(32f, ((RectTransform)child).sizeDelta.y);
        }

        private static void ConfigureSliderLayout(Transform root)
        {
            var layout = root.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 4f;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
            }
            var label = Find(root, "Label");
            var track = Find(root, "Track");
            SetPreferredHeight(label, 20f);
            SetPreferredHeight(track, 20f);
        }

        private static void SetPreferredHeight(Transform target, float height)
        {
            if (target == null)
            {
                return;
            }
            var element = target.GetComponent<LayoutElement>() ??
                target.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        private static void CreateScene(
            Stage9AudioLibraryV1 audioLibrary,
            Stage9RuntimeManifestV1 manifest)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var arena = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
            arena.name = "Stage9PlayableArena";
            var controller = arena.GetComponent<PenaltyAreaController>();

            var cameraObject = new GameObject(
                "GameplayCamera",
                typeof(Camera),
                typeof(AudioListener),
                typeof(Stage7PenaltyCameraDirectorV1));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.64f, 0.76f, 0.82f, 1f);
            camera.allowHDR = false;
            camera.transform.position = new Vector3(0f, 1.35f, 14.6f);
            camera.transform.LookAt(new Vector3(0f, 1.25f, 0f));
            var cameraDirector = cameraObject.GetComponent<Stage7PenaltyCameraDirectorV1>();
            cameraDirector.Configure(camera, controller, controller.ArenaOrigin);

            var gameRoot = new GameObject(
                "Stage9Game",
                typeof(PlayerInput),
                typeof(PenaltyReplayRecorderV1),
                typeof(Stage7PenaltyGameV1),
                typeof(Stage9PenaltyAudioV1));
            var input = gameRoot.GetComponent<PlayerInput>();
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                Stage7ProjectBuilder.RuntimeInputActionsPath);
            input.actions = actions;
            input.defaultActionMap = "Gameplay";
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            var replay = gameRoot.GetComponent<PenaltyReplayRecorderV1>();
            replay.Configure(
                controller,
                AssetDatabase.LoadAssetAtPath<Stage7RuntimeManifestV1>(
                    Stage7ProjectBuilder.RuntimeManifestPath));

            var hudObject = PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath)) as GameObject;
            var hud = hudObject.GetComponent<Stage7PenaltyHudV1>();
            hud.GameplayCamera = camera;

            var sources = CreateAudioSources(gameRoot.transform, out var uiSource, out var ambienceSource);
            var audio = gameRoot.GetComponent<Stage9PenaltyAudioV1>();
            audio.Configure(controller, audioLibrary, sources, uiSource, ambienceSource);
            ConfigureFinalUi(hudObject, audio, manifest);

            gameRoot.GetComponent<Stage7PenaltyGameV1>().Configure(
                controller,
                input,
                actions,
                AssetDatabase.LoadAssetAtPath<PlayerPenaltyInputConfigV1>(
                    Stage7ProjectBuilder.InputConfigPath),
                hud,
                cameraDirector,
                null,
                replay);

            var eventSystem = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var lightObject = new GameObject("DirectionalLight", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.87f, 1f);
            light.intensity = 1.18f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.72f;
            light.transform.rotation = Quaternion.Euler(43f, -34f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.68f, 0.76f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.39f, 0.43f, 0.43f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.22f, 0.19f, 1f);
            RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static AudioSource[] CreateAudioSources(
            Transform parent,
            out AudioSource ui,
            out AudioSource ambience)
        {
            var sources = new AudioSource[4];
            for (var index = 0; index < sources.Length; index++)
            {
                var child = new GameObject($"WorldAudio{index + 1:00}", typeof(AudioSource));
                child.transform.SetParent(parent, false);
                sources[index] = child.GetComponent<AudioSource>();
                sources[index].playOnAwake = false;
                sources[index].spatialBlend = 1f;
                sources[index].rolloffMode = AudioRolloffMode.Linear;
                sources[index].minDistance = 2f;
                sources[index].maxDistance = 28f;
            }
            var uiObject = new GameObject("UiAudio", typeof(AudioSource));
            uiObject.transform.SetParent(parent, false);
            ui = uiObject.GetComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;
            var ambienceObject = new GameObject("AmbienceAudio", typeof(AudioSource));
            ambienceObject.transform.SetParent(parent, false);
            ambience = ambienceObject.GetComponent<AudioSource>();
            ambience.playOnAwake = false;
            ambience.spatialBlend = 0f;
            return sources;
        }

        private static void ConfigureFinalUi(
            GameObject hudObject,
            Stage9PenaltyAudioV1 audio,
            Stage9RuntimeManifestV1 manifest)
        {
            var root = hudObject.transform;
            var analysisButtons = root.GetComponentsInChildren<Button>(true)
                .Where(button => button.name == "Analysis")
                .ToArray();
            var aboutButton = Find(root, "AudioAbout")?.GetComponent<Button>();
            var aboutPanel = Find(root, "Stage9AboutPanel")?.gameObject;
            var back = Find(root, "Stage9AboutPanel/Body/Back")?.GetComponent<Button>();
            var text = Find(root, "Stage9AboutPanel/Body/AboutText")?.GetComponent<Text>();
            var master = Find(root, "Stage9AboutPanel/Body/MasterVolume/Track")?.GetComponent<Slider>();
            var effects = Find(root, "Stage9AboutPanel/Body/EffectsVolume/Track")?.GetComponent<Slider>();
            var ambience = Find(root, "Stage9AboutPanel/Body/AmbienceVolume/Track")?.GetComponent<Slider>();
            hudObject.GetComponent<Stage9FinalUiV1>().Configure(
                audio,
                analysisButtons,
                aboutButton == null ? Array.Empty<Button>() : new[] { aboutButton },
                back,
                aboutPanel,
                text,
                master,
                effects,
                ambience,
                manifest.BuildId);
        }

        private static void CopyStage8Analysis()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            var source = Path.Combine(root, "web/stage8-analysis/dist");
            var destination = Path.Combine(Application.dataPath, "StreamingAssets/Stage8Analysis");
            if (!File.Exists(Path.Combine(source, "index.html")))
            {
                throw new InvalidOperationException(
                    "Build the approved Stage 8 analysis artifact before Stage 9.");
            }
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }
            CopyDirectory(source, destination);
            InlineStage8Analysis(destination);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void InlineStage8Analysis(string destination)
        {
            var indexPath = Path.Combine(destination, "index.html");
            var html = File.ReadAllText(indexPath);
            html = InlineHtmlAsset(
                html,
                destination,
                "<script type=\"module\" crossorigin src=\"",
                "\"></script>",
                "<script>",
                "</script>");
            html = InlineHtmlAsset(
                html,
                destination,
                "<link rel=\"stylesheet\" crossorigin href=\"",
                "\">",
                "<style>",
                "</style>");
            if (html.Contains("src=\"./assets/") ||
                html.Contains("href=\"./assets/"))
            {
                throw new InvalidOperationException(
                    "The packaged Stage 8 analysis must be a self-contained offline page.");
            }
            File.WriteAllText(indexPath, html);
        }

        private static string InlineHtmlAsset(
            string html,
            string root,
            string tagPrefix,
            string tagSuffix,
            string inlinePrefix,
            string inlineSuffix)
        {
            var start = html.IndexOf(tagPrefix, StringComparison.Ordinal);
            if (start < 0)
            {
                throw new InvalidOperationException(
                    $"Stage 8 output is missing expected tag '{tagPrefix}'.");
            }
            var pathStart = start + tagPrefix.Length;
            var pathEnd = html.IndexOf(tagSuffix, pathStart, StringComparison.Ordinal);
            if (pathEnd < 0)
            {
                throw new InvalidOperationException("Stage 8 output contains a malformed asset tag.");
            }
            var relative = html.Substring(pathStart, pathEnd - pathStart)
                .TrimStart('.', '/');
            var assetPath = Path.GetFullPath(Path.Combine(root, relative));
            var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!assetPath.StartsWith(fullRoot, StringComparison.Ordinal) ||
                !File.Exists(assetPath))
            {
                throw new InvalidOperationException(
                    $"Stage 8 packaged asset is missing or outside its root: {relative}");
            }
            var contents = File.ReadAllText(assetPath)
                .Replace("</script", "<\\/script");
            var tagEnd = pathEnd + tagSuffix.Length;
            return html.Substring(0, start) + inlinePrefix + contents + inlineSuffix +
                html.Substring(tagEnd);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }
            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(
                    directory,
                    Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        private static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(scene => scene.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{MaterialDirectory}/{name}.mat");

        private static void AssignByToken(Transform root, string token, Material material)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.sharedMaterial = material;
                }
            }
        }

        private static void AssignMaterial(Transform target, Material material)
        {
            var renderer = target == null ? null : target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static Transform Find(Transform root, string nameOrPath)
        {
            if (root == null)
            {
                return null;
            }
            var direct = root.Find(nameOrPath);
            if (direct != null)
            {
                return direct;
            }
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == nameOrPath || RelativePath(root, child) == nameOrPath)
                {
                    return child;
                }
            }
            return null;
        }

        private static GameObject CreateMenu(
            Transform parent,
            string name,
            string title,
            out RectTransform body)
        {
            var panel = ImageObject(parent, name, Charcoal);
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 650f));
            var heading = Text(panel, "Title", title, 34, TextAnchor.MiddleCenter);
            Anchor((RectTransform)heading.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(440f, 64f));
            body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            body.SetParent(panel, false);
            Anchor(body, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f),
                new Vector2(0f, -8f), new Vector2(430f, 520f));
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return panel.gameObject;
        }

        private static Button Button(Transform parent, string name, string label)
        {
            var rect = ImageObject(parent, name, TealDark);
            rect.sizeDelta = new Vector2(400f, 46f);
            SetPreferredHeight(rect, 46f);
            var button = rect.gameObject.AddComponent<Button>();
            var text = Text(rect, "Label", label, 19, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            return button;
        }

        private static Slider Slider(Transform parent, string name, string label)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            root.transform.SetParent(parent, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(400f, 54f);
            SetPreferredHeight(root.transform, 58f);
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childForceExpandHeight = false;
            var text = Text(root.transform, "Label", label, 14, TextAnchor.MiddleLeft);
            ((RectTransform)text.transform).sizeDelta = new Vector2(400f, 18f);
            SetPreferredHeight(text.transform, 20f);
            var track = ImageObject(root.transform, "Track", new Color(0.11f, 0.14f, 0.15f, 1f));
            track.sizeDelta = new Vector2(400f, 16f);
            SetPreferredHeight(track, 20f);
            var fill = ImageObject(track, "Fill", Cyan);
            Stretch(fill);
            var handle = CircleImageObject(track, "Handle", Amber);
            handle.sizeDelta = new Vector2(18f, 24f);
            var slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            return slider;
        }

        private static RectTransform ImageObject(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            return (RectTransform)gameObject.transform;
        }

        private static RectTransform CircleImageObject(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.preserveAspect = true;
            return (RectTransform)gameObject.transform;
        }

        private static Text Text(
            Transform parent,
            string name,
            string value,
            float size,
            TextAnchor alignment)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            gameObject.transform.SetParent(parent, false);
            var text = gameObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = Mathf.RoundToInt(size);
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = SoftWhite;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static bool CompareMesh(Transform baseline, Transform candidate)
        {
            var a = baseline.GetComponent<MeshFilter>();
            var b = candidate.GetComponent<MeshFilter>();
            return (a == null && b == null) ||
                (a != null && b != null && a.sharedMesh == b.sharedMesh);
        }

        private static bool CompareCollider(Transform baseline, Transform candidate)
        {
            var a = baseline.GetComponent<Collider>();
            var b = candidate.GetComponent<Collider>();
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            if (a.GetType() != b.GetType() || a.isTrigger != b.isTrigger ||
                a.sharedMaterial != b.sharedMaterial)
            {
                return false;
            }
            if (a is SphereCollider sphereA && b is SphereCollider sphereB)
            {
                return Mathf.Approximately(sphereA.radius, sphereB.radius) &&
                    Approximately(sphereA.center, sphereB.center);
            }
            if (a is CapsuleCollider capsuleA && b is CapsuleCollider capsuleB)
            {
                return Mathf.Approximately(capsuleA.radius, capsuleB.radius) &&
                    Mathf.Approximately(capsuleA.height, capsuleB.height) &&
                    capsuleA.direction == capsuleB.direction &&
                    Approximately(capsuleA.center, capsuleB.center);
            }
            if (a is BoxCollider boxA && b is BoxCollider boxB)
            {
                return Approximately(boxA.center, boxB.center) &&
                    Approximately(boxA.size, boxB.size);
            }
            return true;
        }

        private static bool CompareRigidbody(Transform baseline, Transform candidate)
        {
            var a = baseline.GetComponent<Rigidbody>();
            var b = candidate.GetComponent<Rigidbody>();
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            return Mathf.Approximately(a.mass, b.mass) &&
                Mathf.Approximately(a.linearDamping, b.linearDamping) &&
                Mathf.Approximately(a.angularDamping, b.angularDamping) &&
                a.isKinematic == b.isKinematic &&
                a.useGravity == b.useGravity &&
                a.constraints == b.constraints &&
                a.collisionDetectionMode == b.collisionDetectionMode &&
                a.interpolation == b.interpolation;
        }

        private static bool IsUnderPresentation(Transform transform, Transform root)
        {
            var current = transform;
            while (current != null && current != root)
            {
                if (current.name == "Stage9Presentation")
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names);
        }

        private static bool Approximately(Vector3 a, Vector3 b) =>
            (a - b).sqrMagnitude <= 1e-10f;

        private static bool Approximately(Quaternion a, Quaternion b) =>
            1f - Mathf.Abs(Quaternion.Dot(a, b)) <= 1e-6f;

        private static string ReadGitCommit()
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process.WaitForExit(3000);
                return process.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                return "unknown";
            }
        }

        private static string ShortHash(string value) =>
            string.IsNullOrWhiteSpace(value) || value.Length < 8
                ? "development"
                : value.Substring(0, 8);

        private static string Sha256File(string path, bool projectRelative = false)
        {
            var fullPath = projectRelative
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", path))
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (!File.Exists(fullPath))
            {
                return "missing";
            }
            using (var stream = File.OpenRead(fullPath))
            using (var hash = SHA256.Create())
            {
                return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }
    }
}
