using System;
using System.IO;
using PenaltyShootout.Kernel;
using PenaltyShootout.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PenaltyShootout.Stage0.Editor
{
    public static class Stage2ProjectBuilder
    {
        public const string ScenePath = "Assets/PenaltyShootout/Scenes/Training.unity";
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage2TrainingArena.prefab";
        private const int TrainingArenaCount = 16;
        private const float TrainingArenaSpacing = 30f;

        [MenuItem("Penalty Shootout/Stage 2/Prepare Training")]
        public static void PrepareProject()
        {
            Stage1ProjectBuilder.PrepareProject();
            ExportGoalkeeperStateManifest();
            CreateStage2ArenaPrefab();
            CreateTrainingScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Prepared {ScenePath} and {PrefabPath} for Stage 2.");
        }

        [MenuItem("Penalty Shootout/Stage 2/Build macOS Headless")]
        public static void BuildMacHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../builds/macos/PenaltyShootoutStage2.app"));
            BuildHeadless(BuildTarget.StandaloneOSX, output);
        }

        [MenuItem("Penalty Shootout/Stage 2/Build Linux Headless")]
        public static void BuildLinuxHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../builds/linux/PenaltyShootoutStage2.x86_64"));
            BuildHeadless(BuildTarget.StandaloneLinux64, output);
        }

        private static void ExportGoalkeeperStateManifest()
        {
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../configs/environment/goalkeeper-state-v0.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, KernelManifestUtility.CreateGoalkeeperStateJson());
        }

        private static void CreateStage2ArenaPrefab()
        {
            if (!AssetDatabase.CopyAsset(Stage1ProjectBuilder.PrefabPath, PrefabPath) &&
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                throw new InvalidOperationException($"Failed to create {PrefabPath}.");
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ConfigureStage2Arena(root.GetComponent<PenaltyAreaController>());
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateTrainingScene()
        {
            var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (arenaPrefab == null)
            {
                throw new InvalidOperationException($"Failed to load {PrefabPath}.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var columns = Mathf.CeilToInt(Mathf.Sqrt(TrainingArenaCount));
            for (var index = 0; index < TrainingArenaCount; index++)
            {
                var row = index / columns;
                var column = index % columns;
                var instance = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException("Failed to instantiate Stage 2 arena.");
                }

                instance.name = $"Stage2TrainingArena_{index:000}";
                instance.transform.position =
                    new Vector3(column * TrainingArenaSpacing, 0f, row * TrainingArenaSpacing);
                var controller = instance.GetComponent<PenaltyAreaController>();
                ConfigureStage2Arena(controller);
                controller.ArenaId = index;
                controller.ShowDebugUi = false;
            }

            CreateCamera();
            CreateLight();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void ConfigureStage2Arena(PenaltyAreaController controller)
        {
            if (controller == null)
            {
                throw new InvalidOperationException("Stage 2 arena is missing PenaltyAreaController.");
            }

            var agent = controller.GetComponentInChildren<GoalkeeperKernelAgent>(true);
            var behavior = agent == null ? null : agent.GetComponent<BehaviorParameters>();
            if (agent == null || behavior == null)
            {
                throw new InvalidOperationException("Stage 2 arena is missing its ML-Agents behavior.");
            }

            agent.Controller = controller;
            agent.ObservationProfile = GoalkeeperObservationProfile.StateV0;
            behavior.BehaviorName = KernelConstants.GoalkeeperStateBehaviorName;
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize =
                KernelConstants.GoalkeeperStateObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(9);
            controller.ActionSource = agent;
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("DebugCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(18f, 12f, 42f);
            camera.transform.LookAt(new Vector3(12f, 1f, 12f));
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("DirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Stage0ProjectBuilder.ScenePath, true),
                new EditorBuildSettingsScene(Stage1ProjectBuilder.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }

        private static void BuildHeadless(BuildTarget target, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var agent in UnityEngine.Object.FindObjectsByType<GoalkeeperKernelAgent>(
                         FindObjectsSortMode.None))
            {
                var behavior = agent.GetComponent<BehaviorParameters>();
                if (behavior != null)
                {
                    behavior.BehaviorType = BehaviorType.Default;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = output,
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = BuildOptions.Development,
                };
                var report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        $"Headless {target} build failed: {report.summary.result}, " +
                        $"{report.summary.totalErrors} errors.");
                }
            }
            finally
            {
                var restoredScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                foreach (var agent in UnityEngine.Object.FindObjectsByType<GoalkeeperKernelAgent>(
                             FindObjectsSortMode.None))
                {
                    var behavior = agent.GetComponent<BehaviorParameters>();
                    if (behavior != null)
                    {
                        behavior.BehaviorType = BehaviorType.HeuristicOnly;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);
                    }
                }

                EditorSceneManager.MarkSceneDirty(restoredScene);
                EditorSceneManager.SaveScene(restoredScene);
            }
        }
    }
}
