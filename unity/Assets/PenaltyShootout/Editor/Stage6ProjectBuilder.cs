using System;
using System.IO;
using System.Linq;
using PenaltyShootout.Kernel;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PenaltyShootout.Stage0.Editor
{
    public static class Stage6ProjectBuilder
    {
        public const string PhysicsPath =
            "Assets/PenaltyShootout/Config/PlayerShotPhysicsV1.asset";
        public const string DistributionPath =
            "Assets/PenaltyShootout/Config/HumanShotDistributionV1.asset";
        public const string GloveHandlingPath =
            "Assets/PenaltyShootout/Config/GoalkeeperGloveHandlingV1.asset";
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage6GameplayArena.prefab";
        public const string LabScenePath =
            "Assets/PenaltyShootout/Scenes/ShotVarietyLab.unity";
        public const string BaselineScenePath =
            "Assets/PenaltyShootout/Scenes/Stage6Baseline.unity";

        [MenuItem("Penalty Shootout/Stage 6/Prepare Gameplay Shots")]
        public static void PrepareProject()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    Stage5ProjectBuilder.PrefabPath) == null)
            {
                Stage5ProjectBuilder.PrepareProject();
            }
            var physics = GetOrCreate<PlayerShotPhysicsConfigV1>(PhysicsPath);
            var distribution =
                GetOrCreate<HumanShotDistributionConfigV1>(DistributionPath);
            var gloveHandling =
                GetOrCreate<GoalkeeperGloveHandlingConfigV1>(GloveHandlingPath);
            Validate(physics, distribution, gloveHandling);
            CreateArenaPrefab(physics, distribution, gloveHandling);
            CreateLabScene(distribution);
            CreateBaselineScene();
            RegisterStage6Scenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void RegisterStage6Scenes()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var path in new[] { LabScenePath, BaselineScenePath })
            {
                if (scenes.All(scene => scene.path != path))
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Penalty Shootout/Stage 6/Build macOS")]
        public static void BuildMac()
        {
            PrepareProject();
            var output = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../../builds/macos/PenaltyShootoutStage6.app"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var scene = EditorSceneManager.OpenScene(
                BaselineScenePath,
                OpenSceneMode.Single);
            SetBehaviorType(scene, BehaviorType.Default);
            EditorSceneManager.SaveScene(scene);
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { BaselineScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneOSX,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        $"Stage 6 build failed with {report.summary.totalErrors} errors.");
                }
            }
            finally
            {
                var restored = EditorSceneManager.OpenScene(
                    BaselineScenePath,
                    OpenSceneMode.Single);
                SetBehaviorType(restored, BehaviorType.HeuristicOnly);
                EditorSceneManager.SaveScene(restored);
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

        private static void Validate(
            PlayerShotPhysicsConfigV1 physics,
            HumanShotDistributionConfigV1 distribution,
            GoalkeeperGloveHandlingConfigV1 gloveHandling)
        {
            if (!physics.Validate(out var physicsError))
            {
                throw new InvalidOperationException(physicsError);
            }
            if (!distribution.Validate(out var distributionError))
            {
                throw new InvalidOperationException(distributionError);
            }
            if (!gloveHandling.Validate(out var gloveError))
            {
                throw new InvalidOperationException(gloveError);
            }
        }

        private static void CreateArenaPrefab(
            PlayerShotPhysicsConfigV1 physics,
            HumanShotDistributionConfigV1 distribution,
            GoalkeeperGloveHandlingConfigV1 gloveHandling)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null &&
                !AssetDatabase.CopyAsset(Stage5ProjectBuilder.PrefabPath, PrefabPath))
            {
                throw new InvalidOperationException("Failed to copy Stage 6 arena.");
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var controller = root.GetComponent<PenaltyAreaController>();
                controller.HumanShotConfiguration = distribution;
                controller.PlayerShotPhysicsConfiguration = physics;
                controller.GameplayObservationDelayTicks = 2;
                controller.ScenarioController.HumanShotConfiguration = distribution;
                controller.ScenarioController.PlayerShotPhysicsConfiguration = physics;
                controller.ScenarioController.UseHumanShots = true;
                var handler = root.GetComponent<GoalkeeperGloveHandlingV1>();
                if (handler == null)
                {
                    handler = root.AddComponent<GoalkeeperGloveHandlingV1>();
                }
                handler.Configure(
                    gloveHandling,
                    controller.GoalkeeperControlMotor,
                    controller.Ball,
                    true);
                controller.GoalkeeperGloveHandling = handler;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateLabScene(
            HumanShotDistributionConfigV1 distribution)
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var arena = InstantiateArena("Stage6ShotVarietyArena", Vector3.zero, 0, true);
            var controller = arena.GetComponent<PenaltyAreaController>();
            controller.AutoRun = false;
            var lab = arena.AddComponent<Stage6ShotVarietyLab>();
            lab.Configure(controller, distribution);
            CreateCamera(new Vector3(8.5f, 4.5f, 13.5f), new Vector3(0f, 1.1f, 0f));
            CreateLight();
            EditorSceneManager.SaveScene(scene, LabScenePath);
        }

        private static void CreateBaselineScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            const int count = 16;
            const float spacing = 30f;
            var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (var index = 0; index < count; index++)
            {
                InstantiateArena(
                    $"Stage6BaselineArena_{index:000}",
                    new Vector3(index % columns * spacing, 0f, index / columns * spacing),
                    index,
                    false);
            }
            CreateCamera(new Vector3(18f, 12f, 42f), new Vector3(12f, 1f, 12f));
            CreateLight();
            EditorSceneManager.SaveScene(scene, BaselineScenePath);
        }

        private static GameObject InstantiateArena(
            string name,
            Vector3 position,
            int arenaId,
            bool showDebug)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Failed to instantiate Stage 6 arena.");
            }
            instance.name = name;
            instance.transform.position = position;
            var controller = instance.GetComponent<PenaltyAreaController>();
            controller.ArenaId = arenaId;
            controller.ShowDebugUi = showDebug;
            var behavior = instance.GetComponentInChildren<BehaviorParameters>(true);
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            return instance;
        }

        private static void SetBehaviorType(Scene scene, BehaviorType type)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behavior in root.GetComponentsInChildren<BehaviorParameters>(true))
                {
                    behavior.BehaviorType = type;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        behavior);
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void CreateCamera(Vector3 position, Vector3 lookAt)
        {
            var gameObject = new GameObject("DebugCamera");
            gameObject.tag = "MainCamera";
            var camera = gameObject.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            camera.fieldOfView = 48f;
            camera.farClipPlane = 200f;
        }

        private static void CreateLight()
        {
            var gameObject = new GameObject("DirectionalLight");
            var light = gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }
    }
}
