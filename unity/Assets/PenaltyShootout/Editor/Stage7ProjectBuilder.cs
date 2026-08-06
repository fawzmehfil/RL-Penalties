using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PenaltyShootout.Gameplay;
using PenaltyShootout.Kernel;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PenaltyShootout.Stage0.Editor
{
    public static class Stage7ProjectBuilder
    {
        public const string InputConfigPath =
            "Assets/PenaltyShootout/Config/PlayerPenaltyInputV1.asset";
        public const string InputActionsPath =
            "Assets/PenaltyShootout/Config/PenaltyGameplay.inputactions";
        public const string RuntimeInputActionsPath =
            "Assets/PenaltyShootout/Config/PenaltyGameplayActions.asset";
        public const string RuntimeManifestPath =
            "Assets/PenaltyShootout/Config/Stage7RuntimeManifestV1.asset";
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage7PlayableArena.prefab";
        public const string HudPrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage7GameplayHud.prefab";
        public const string ScenePath =
            "Assets/PenaltyShootout/Scenes/PenaltyGame.unity";

        [MenuItem("Penalty Shootout/Stage 7/Prepare Playable Vertical Slice")]
        public static void PrepareProject()
        {
            RequireStage6Assets();
            var inputConfig = GetOrCreate<PlayerPenaltyInputConfigV1>(InputConfigPath);
            if (!inputConfig.Validate(out var inputError))
            {
                throw new InvalidOperationException(inputError);
            }
            var actions = CreateInputActionsIfMissing();
            var manifest = GetOrCreate<Stage7RuntimeManifestV1>(RuntimeManifestPath);
            UpdateRuntimeManifest(manifest, inputConfig);
            CreateArenaPrefabIfMissing();
            CreateHudPrefabIfMissing();
            CreateSceneIfMissing(inputConfig, actions, manifest);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);
            if (!scenes.Exists(candidate => candidate.path == ScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        [MenuItem("Penalty Shootout/Stage 7/Rebuild Generated Playable Assets")]
        public static void RebuildGeneratedAssets()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(HudPrefabPath);
            PrepareProject();
        }

        [MenuItem("Penalty Shootout/Stage 7/Build macOS")]
        public static void BuildMac()
        {
            PrepareProject();
            var output = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../builds/macos/PenaltyShootoutStage7.app"));
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
                    $"Stage 7 build failed with {report.summary.totalErrors} errors.");
            }
        }

        private static void RequireStage6Assets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    Stage6ProjectBuilder.PrefabPath) == null ||
                AssetDatabase.LoadAssetAtPath<PlayerShotPhysicsConfigV1>(
                    Stage6ProjectBuilder.PhysicsPath) == null)
            {
                throw new InvalidOperationException(
                    "Stage 6 selected assets must exist before preparing Stage 7.");
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static InputActionAsset CreateInputActionsIfMissing()
        {
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                RuntimeInputActionsPath);
            if (existing != null)
            {
                return existing;
            }

            var asset = PlayerPenaltyInputActionsV1.Create();
            AssetDatabase.CreateAsset(asset, RuntimeInputActionsPath);
            var fullPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                InputActionsPath));
            File.WriteAllText(fullPath, asset.ToJson());
            AssetDatabase.ImportAsset(
                InputActionsPath,
                ImportAssetOptions.ForceSynchronousImport);
            return asset;
        }

        private static void CreateArenaPrefabIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                return;
            }
            if (!AssetDatabase.CopyAsset(Stage6ProjectBuilder.PrefabPath, PrefabPath))
            {
                throw new InvalidOperationException("Failed to copy Stage 7 arena.");
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var controller = root.GetComponent<PenaltyAreaController>();
                controller.AutoRun = false;
                controller.ShowDebugUi = false;
                controller.GameplayObservationDelayTicks = 2;
                controller.TargetMarker?.gameObject.SetActive(false);
                controller.Trajectory?.gameObject.SetActive(false);
                controller.GoalkeeperGloveHandling?.SetHandlingVersion(1);
                var agent = root.GetComponentInChildren<GoalkeeperControlAgent>(true);
                agent.NativeSplitInferenceByDefault = true;
                var behavior = agent.GetComponent<BehaviorParameters>();
                behavior.BehaviorType = BehaviorType.HeuristicOnly;

                var trail = controller.Ball.GetComponent<TrailRenderer>();
                if (trail == null)
                {
                    trail = controller.Ball.gameObject.AddComponent<TrailRenderer>();
                }
                trail.time = 0.16f;
                trail.startWidth = 0.055f;
                trail.endWidth = 0.008f;
                trail.minVertexDistance = 0.025f;
                trail.emitting = true;
                trail.material = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/PenaltyShootout/Materials/Trajectory.mat");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateHudPrefabIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath) != null)
            {
                return;
            }
            var root = new GameObject(
                "Stage7GameplayHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(Stage7PenaltyHudV1));
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 20;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                var topBand = Panel(root.transform, "ScoreBand", new Color(0.03f, 0.05f, 0.06f, 0.72f));
                Anchor(topBand, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -42f), new Vector2(720f, 62f));
                var shot = Text(topBand, "Shot", "SHOT 1 / 5", 28, TextAnchor.MiddleLeft);
                Anchor((RectTransform)shot.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(105f, 0f), new Vector2(190f, 48f));
                var score = Text(topBand, "Score", "GOALS  0     SAVES  0     MISSES  0", 25, TextAnchor.MiddleRight);
                Anchor((RectTransform)score.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-250f, 0f), new Vector2(480f, 48f));

                var aimingRoot = new GameObject("Aiming", typeof(RectTransform), typeof(CanvasGroup));
                aimingRoot.transform.SetParent(root.transform, false);
                Stretch((RectTransform)aimingRoot.transform);
                var aimGroup = aimingRoot.GetComponent<CanvasGroup>();
                var ring = CircleImageObject(aimingRoot.transform, "ComposureRing", new Color(1f, 1f, 1f, 0.24f));
                ring.sizeDelta = new Vector2(64f, 64f);
                var reticle = CircleImageObject(aimingRoot.transform, "Reticle", new Color(1f, 0.78f, 0.12f, 0.95f));
                reticle.sizeDelta = new Vector2(14f, 14f);

                var powerBack = ImageObject(root.transform, "PowerBack", new Color(0.02f, 0.03f, 0.03f, 0.82f));
                Anchor(powerBack, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 72f), new Vector2(360f, 20f));
                var power = ImageObject(powerBack, "PowerFill", new Color(0.96f, 0.67f, 0.12f, 1f));
                Stretch(power);
                var powerImage = power.GetComponent<Image>();
                powerImage.type = Image.Type.Filled;
                powerImage.fillMethod = Image.FillMethod.Horizontal;
                powerImage.fillAmount = 0f;
                var curveBack = ImageObject(root.transform, "CurveBack", new Color(0.02f, 0.03f, 0.03f, 0.72f));
                Anchor(curveBack, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 42f), new Vector2(180f, 6f));
                var curveMarker = CircleImageObject(curveBack, "CurveMarker", new Color(0.20f, 0.80f, 0.72f, 1f));
                curveMarker.sizeDelta = new Vector2(8f, 20f);

                var outcome = Text(root.transform, "Outcome", "GOAL", 66, TextAnchor.MiddleCenter);
                Anchor((RectTransform)outcome.transform, new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f),
                    Vector2.zero, new Vector2(620f, 100f));
                outcome.gameObject.SetActive(false);
                var technical = Text(root.transform, "Technical", "SHOT RESET", 34, TextAnchor.MiddleCenter);
                Anchor((RectTransform)technical.transform, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f),
                    Vector2.zero, new Vector2(520f, 70f));
                technical.gameObject.SetActive(false);

                var pause = CreateMenu(root.transform, "PausePanel", "PAUSED", out var pauseBody);
                var resume = Button(pauseBody, "Resume", "RESUME");
                var restart = Button(pauseBody, "Restart", "RESTART SET");
                var fullscreen = Button(pauseBody, "Fullscreen", "FULLSCREEN");
                var quit = Button(pauseBody, "Quit", "QUIT");
                var pointerSlider = Slider(pauseBody, "PointerSensitivity", "POINTER SENSITIVITY");
                var keyboardSlider = Slider(pauseBody, "KeyboardAimSpeed", "KEYBOARD AIM SPEED");
                var contrastSlider = Slider(pauseBody, "ReticleContrast", "RETICLE CONTRAST");
                pause.SetActive(false);

                var complete = CreateMenu(root.transform, "CompletePanel", "SET COMPLETE", out var completeBody);
                var completeScore = Text(completeBody, "CompleteScore", "0 / 5 SCORED", 31, TextAnchor.MiddleCenter);
                ((RectTransform)completeScore.transform).sizeDelta = new Vector2(370f, 96f);
                var playAgain = Button(completeBody, "PlayAgain", "PLAY AGAIN");
                complete.SetActive(false);

                var fade = new GameObject("ScreenFade", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                fade.transform.SetParent(root.transform, false);
                Stretch((RectTransform)fade.transform);
                fade.GetComponent<Image>().color = Color.black;
                var fadeGroup = fade.GetComponent<CanvasGroup>();
                fadeGroup.alpha = 1f;

                root.GetComponent<Stage7PenaltyHudV1>().Configure(
                    null,
                    reticle,
                    ring,
                    aimGroup,
                    powerImage,
                    curveMarker,
                    shot,
                    score,
                    outcome,
                    technical,
                    fadeGroup,
                    pause,
                    complete,
                    completeScore,
                    resume,
                    restart,
                    fullscreen,
                    quit,
                    playAgain,
                    pointerSlider,
                    keyboardSlider,
                    contrastSlider);
                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateSceneIfMissing(
            PlayerPenaltyInputConfigV1 inputConfig,
            InputActionAsset actions,
            Stage7RuntimeManifestV1 manifest)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var arena = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
            arena.name = "Stage7PlayableArena";
            var controller = arena.GetComponent<PenaltyAreaController>();

            var cameraObject = new GameObject(
                "GameplayCamera",
                typeof(Camera),
                typeof(AudioListener),
                typeof(Stage7PenaltyCameraDirectorV1));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.farClipPlane = 200f;
            camera.transform.position = new Vector3(0f, 1.35f, 14.6f);
            camera.transform.LookAt(new Vector3(0f, 1.25f, 0f));
            var cameraDirector = cameraObject.GetComponent<Stage7PenaltyCameraDirectorV1>();
            cameraDirector.Configure(camera, controller, controller.ArenaOrigin);

            var gameRoot = new GameObject(
                "Stage7Game",
                typeof(PlayerInput),
                typeof(AudioSource),
                typeof(Stage7PenaltyAudioV1),
                typeof(PenaltyReplayRecorderV1),
                typeof(Stage7PenaltyGameV1));
            var input = gameRoot.GetComponent<PlayerInput>();
            input.actions = actions;
            input.defaultActionMap = "Gameplay";
            input.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            var replay = gameRoot.GetComponent<PenaltyReplayRecorderV1>();
            replay.Configure(controller, manifest);

            var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            var hudObject = PrefabUtility.InstantiatePrefab(hudPrefab) as GameObject;
            var hud = hudObject.GetComponent<Stage7PenaltyHudV1>();
            hud.GameplayCamera = camera;

            var audio = gameRoot.GetComponent<Stage7PenaltyAudioV1>();
            gameRoot.GetComponent<Stage7PenaltyGameV1>().Configure(
                controller,
                input,
                actions,
                inputConfig,
                hud,
                cameraDirector,
                audio,
                replay);

            var eventSystem = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var lightObject = new GameObject("DirectionalLight", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static GameObject CreateMenu(
            Transform parent,
            string name,
            string title,
            out RectTransform body)
        {
            var panel = Panel(parent, name, new Color(0.025f, 0.035f, 0.04f, 0.94f));
            Anchor(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(440f, 590f));
            var heading = Text(panel, "Title", title, 40, TextAnchor.MiddleCenter);
            ((RectTransform)heading.transform).sizeDelta = new Vector2(390f, 62f);
            body = new GameObject("Body", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            body.SetParent(panel, false);
            Anchor(body, new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(0f, -12f), new Vector2(380f, 440f));
            var layout = body.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            return panel.gameObject;
        }

        private static Button Button(Transform parent, string name, string label)
        {
            var objectRect = ImageObject(parent, name, new Color(0.13f, 0.17f, 0.18f, 1f));
            objectRect.sizeDelta = new Vector2(360f, 48f);
            var button = objectRect.gameObject.AddComponent<Button>();
            var text = Text(objectRect, "Label", label, 21, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            return button;
        }

        private static Slider Slider(Transform parent, string name, string label)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            root.transform.SetParent(parent, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(360f, 58f);
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3f;
            layout.childForceExpandHeight = false;
            var text = Text(root.transform, "Label", label, 15, TextAnchor.MiddleLeft);
            ((RectTransform)text.transform).sizeDelta = new Vector2(360f, 20f);
            var track = ImageObject(root.transform, "Track", new Color(0.11f, 0.13f, 0.14f, 1f));
            track.sizeDelta = new Vector2(360f, 16f);
            var fill = ImageObject(track, "Fill", new Color(0.20f, 0.80f, 0.72f, 1f));
            Stretch(fill);
            var handle = CircleImageObject(track, "Handle", new Color(0.96f, 0.67f, 0.12f, 1f));
            handle.sizeDelta = new Vector2(18f, 24f);
            var slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            return slider;
        }

        private static RectTransform Panel(Transform parent, string name, Color color)
        {
            return ImageObject(parent, name, color);
        }

        private static RectTransform ImageObject(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            return (RectTransform)gameObject.transform;
        }

        private static RectTransform CircleImageObject(
            Transform parent,
            string name,
            Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = color;
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(
                "UI/Skin/Knob.psd");
            image.type = Image.Type.Simple;
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
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
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

        private static void UpdateRuntimeManifest(
            Stage7RuntimeManifestV1 manifest,
            PlayerPenaltyInputConfigV1 inputConfig)
        {
            var commit = ReadGitCommit();
            manifest.GitCommit = commit;
            manifest.BuildId = "stage7-" +
                (commit.Length >= 8 ? commit.Substring(0, 8) : commit);
            manifest.InputConfigHash = Sha256(EditorJsonUtility.ToJson(inputConfig));
            manifest.InterceptionModelHash = Sha256File(
                Stage5ProjectBuilder.SplitInterceptionModelPath);
            manifest.TimingModelHash = Sha256File(
                Stage5ProjectBuilder.SplitTimingModelPath);
            EditorUtility.SetDirty(manifest);
        }

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
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 && output.Length > 0
                    ? output
                    : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string Sha256File(string assetPath)
        {
            var path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                assetPath));
            return File.Exists(path)
                ? Sha256Bytes(File.ReadAllBytes(path))
                : "missing";
        }

        private static string Sha256(string value) =>
            Sha256Bytes(System.Text.Encoding.UTF8.GetBytes(value));

        private static string Sha256Bytes(byte[] value)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(value))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
