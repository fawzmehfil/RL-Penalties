using System;
using System.IO;
using PenaltyShootout.Kernel;
using PenaltyShootout.MLAgents;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PenaltyShootout.Stage0.Editor
{
    public static class Stage5ProjectBuilder
    {
        public const string MotorLabScenePath =
            "Assets/PenaltyShootout/Scenes/GoalkeeperControlLab.unity";
        public const string TrainingScenePath =
            "Assets/PenaltyShootout/Scenes/ControlTraining.unity";
        public const string DemonstrationScenePath =
            "Assets/PenaltyShootout/Scenes/ControlDemonstration.unity";
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage5ControlArena.prefab";
        public const string MotorConfigPath =
            "Assets/PenaltyShootout/Config/GoalkeeperControlMotorProfile.asset";
        public const string SplitInterceptionModelPath =
            "Assets/PenaltyShootout/Models/Stage5Split/" +
            "goalkeeper-interception-v2.onnx";
        public const string SplitTimingModelPath =
            "Assets/PenaltyShootout/Models/Stage5Split/" +
            "goalkeeper-commit-timing-v1.onnx";

        private const int TrainingArenaCount = 16;
        private const float TrainingArenaSpacing = 30f;
        private const string KeeperMaterialPath =
            "Assets/PenaltyShootout/Materials/GoalkeeperProxy.mat";
        private const string GloveMaterialPath =
            "Assets/PenaltyShootout/Materials/GoalkeeperGloves.mat";

        [MenuItem("Penalty Shootout/Stage 5/Prepare Control Prototype")]
        public static void PrepareProject()
        {
            EnsureStage2ArenaPrefab();
            var motor = GetOrCreateMotorConfig();
            ExportGoalkeeperControlV2Manifest(motor);
            CreateControlArenaPrefab(motor);
            EnsureMotorLabScene();
            EnsureTrainingScene();
            EnsureDemonstrationScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"Prepared {PrefabPath}, {MotorLabScenePath}, " +
                $"{TrainingScenePath}, and {DemonstrationScenePath} " +
                "for Stage 5.");
        }

        [MenuItem("Penalty Shootout/Stage 5/Build macOS Headless")]
        public static void BuildMacHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../builds/macos/PenaltyShootoutStage5.app"));
            BuildHeadless(BuildTarget.StandaloneOSX, output);
        }

        [MenuItem("Penalty Shootout/Stage 5/Build macOS Native Inference")]
        public static void BuildMacNativeInference()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../builds/macos/" +
                    "PenaltyShootoutStage5Native.app"));
            BuildNativeInference(BuildTarget.StandaloneOSX, output);
        }

        [MenuItem("Penalty Shootout/Stage 5/Build Linux Headless")]
        public static void BuildLinuxHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../builds/linux/PenaltyShootoutStage5.x86_64"));
            BuildHeadless(BuildTarget.StandaloneLinux64, output);
        }

        [MenuItem(
            "Penalty Shootout/Stage 5/Build macOS Reactive Demonstration")]
        public static void BuildMacReactiveDemonstration()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../builds/macos/" +
                    "PenaltyShootoutStage5Demo.app"));
            BuildDemonstrationHeadless(BuildTarget.StandaloneOSX, output);
        }

        private static void EnsureStage2ArenaPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    Stage2ProjectBuilder.PrefabPath) == null)
            {
                Stage2ProjectBuilder.PrepareProject();
            }
        }

        private static GoalkeeperControlMotorConfig GetOrCreateMotorConfig()
        {
            var configuration =
                AssetDatabase.LoadAssetAtPath<GoalkeeperControlMotorConfig>(
                    MotorConfigPath);
            if (configuration == null)
            {
                configuration =
                    ScriptableObject.CreateInstance<GoalkeeperControlMotorConfig>();
                configuration.name = "GoalkeeperControlMotorProfile";
                AssetDatabase.CreateAsset(configuration, MotorConfigPath);
            }

            configuration.MotorProfileId =
                KernelConstants.GoalkeeperControlMotorProfileId;
            EditorUtility.SetDirty(configuration);
            if (!configuration.Validate(out var error))
            {
                throw new InvalidOperationException(error);
            }

            return configuration;
        }

        private static void ExportGoalkeeperControlV2Manifest(
            GoalkeeperControlMotorConfig motor)
        {
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../configs/environment/goalkeeper-control-v2.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(
                output,
                KernelManifestUtility.CreateGoalkeeperControlV2Json(motor));
        }

        private static void CreateControlArenaPrefab(
            GoalkeeperControlMotorConfig motorConfiguration)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null &&
                !AssetDatabase.CopyAsset(
                    Stage2ProjectBuilder.PrefabPath,
                    PrefabPath))
            {
                throw new InvalidOperationException(
                    $"Failed to create {PrefabPath}.");
            }

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ConfigureControlArena(
                    root.GetComponent<PenaltyAreaController>(),
                    motorConfiguration);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureControlArena(
            PenaltyAreaController controller,
            GoalkeeperControlMotorConfig motorConfiguration)
        {
            if (controller == null)
            {
                throw new InvalidOperationException(
                    "Stage 5 arena is missing PenaltyAreaController.");
            }

            RemoveKernelAgents(controller);
            var motor = ConfigureControlGoalkeeper(
                controller,
                motorConfiguration);
            var agent = GetOrCreateControlAgent(controller);
            var agentObject = agent.gameObject;
            var behavior = agentObject.GetComponent<BehaviorParameters>();
            if (behavior == null)
            {
                behavior = agentObject.AddComponent<BehaviorParameters>();
            }

            behavior.BehaviorName =
                KernelConstants.GoalkeeperControlV2BehaviorName;
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize =
                KernelConstants.GoalkeeperControlV2ObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = new ActionSpec(
                GoalkeeperControlSpace.ContinuousActionCount,
                new[] { GoalkeeperControlSpace.CommitBranchSize });

            var interceptionModel =
                AssetDatabase.LoadAssetAtPath<ModelAsset>(
                    SplitInterceptionModelPath);
            var timingModel =
                AssetDatabase.LoadAssetAtPath<ModelAsset>(
                    SplitTimingModelPath);
            if (interceptionModel == null || timingModel == null)
            {
                throw new InvalidOperationException(
                    "Stage 5.6B selected ONNX models failed to import.");
            }

            var nativePolicy =
                agentObject.GetComponent<
                    GoalkeeperSplitInferencePolicyV1>() ??
                agentObject.AddComponent<
                    GoalkeeperSplitInferencePolicyV1>();
            nativePolicy.Configure(
                interceptionModel,
                timingModel,
                GoalkeeperSplitInferencePolicyV1.DefaultCommitThreshold);

            agent.Controller = controller;
            agent.NativeSplitPolicy = nativePolicy;
            agent.NativeSplitInferenceByDefault = false;
            agent.MaxStep = 0;
            foreach (var requester in
                     agentObject.GetComponents<DecisionRequester>())
            {
                UnityEngine.Object.DestroyImmediate(requester);
            }

            controller.ControlMode = GoalkeeperControlMode.HybridV1;
            controller.ControlMotorConfiguration = motorConfiguration;
            controller.GoalkeeperControlMotor = motor;
            controller.ActionSource = agent;
        }

        private static void RemoveKernelAgents(
            PenaltyAreaController controller)
        {
            var kernelAgents =
                controller.GetComponentsInChildren<GoalkeeperKernelAgent>(true);
            foreach (var agent in kernelAgents)
            {
                UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }
        }

        private static GoalkeeperControlAgent GetOrCreateControlAgent(
            PenaltyAreaController controller)
        {
            var controlAgents =
                controller.GetComponentsInChildren<GoalkeeperControlAgent>(true);
            GoalkeeperControlAgent selected = null;
            foreach (var agent in controlAgents)
            {
                if (selected == null)
                {
                    selected = agent;
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }

            if (selected != null)
            {
                return selected;
            }

            var agentObject = new GameObject("GoalkeeperControlAgent");
            agentObject.transform.SetParent(controller.transform, false);
            return agentObject.AddComponent<GoalkeeperControlAgent>();
        }

        private static GoalkeeperMotorV1 ConfigureControlGoalkeeper(
            PenaltyAreaController controller,
            GoalkeeperControlMotorConfig configuration)
        {
            var existingMotor = controller.GoalkeeperControlMotor;
            if (existingMotor != null)
            {
                existingMotor.Configuration = configuration;
                existingMotor.ArenaOrigin = controller.ArenaOrigin;
                return existingMotor;
            }

            var oldMotor = controller.GoalkeeperMotor;
            if (oldMotor == null)
            {
                throw new InvalidOperationException(
                    "Copied Stage 2 arena has no goalkeeper motor.");
            }

            var keeper = oldMotor.gameObject;
            var oldReach = keeper.GetComponent<GoalkeeperReachRig>();
            if (oldReach != null)
            {
                UnityEngine.Object.DestroyImmediate(oldReach);
            }

            UnityEngine.Object.DestroyImmediate(oldMotor);
            DestroyChild(keeper.transform, "LeftShoulder");
            DestroyChild(keeper.transform, "RightShoulder");
            DestroyChild(keeper.transform, "LeftArm");
            DestroyChild(keeper.transform, "RightArm");
            DestroyChild(keeper.transform, "LeftGlove");
            DestroyChild(keeper.transform, "RightGlove");

            var bodyMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(KeeperMaterialPath);
            var gloveMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(GloveMaterialPath);
            if (bodyMaterial == null || gloveMaterial == null)
            {
                throw new InvalidOperationException(
                    "Stage 5 goalkeeper materials are missing.");
            }

            var leftShoulder = CreateShoulder(
                keeper.transform,
                "LeftShoulder",
                -configuration.ShoulderLateral,
                configuration);
            var rightShoulder = CreateShoulder(
                keeper.transform,
                "RightShoulder",
                configuration.ShoulderLateral,
                configuration);
            var leftUpperArm = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Capsule,
                "LeftUpperArm",
                bodyMaterial,
                GoalkeeperContactPart.Arm);
            var rightUpperArm = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Capsule,
                "RightUpperArm",
                bodyMaterial,
                GoalkeeperContactPart.Arm);
            var leftForearm = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Capsule,
                "LeftForearm",
                bodyMaterial,
                GoalkeeperContactPart.Arm);
            var rightForearm = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Capsule,
                "RightForearm",
                bodyMaterial,
                GoalkeeperContactPart.Arm);
            var leftGlove = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Sphere,
                "LeftGlove",
                gloveMaterial,
                GoalkeeperContactPart.LeftGlove);
            var rightGlove = CreateKeeperPart(
                keeper.transform,
                PrimitiveType.Sphere,
                "RightGlove",
                gloveMaterial,
                GoalkeeperContactPart.RightGlove);

            var armRig = keeper.AddComponent<GoalkeeperArmRigV1>();
            armRig.Configure(
                configuration,
                controller.ArenaOrigin,
                leftShoulder,
                rightShoulder,
                leftUpperArm.transform,
                rightUpperArm.transform,
                leftForearm.transform,
                rightForearm.transform,
                leftGlove.transform,
                rightGlove.transform);
            var motor = keeper.AddComponent<GoalkeeperMotorV1>();
            motor.Configuration = configuration;
            motor.ArenaOrigin = controller.ArenaOrigin;
            motor.ArmRig = armRig;
            motor.ResetForAttempt(0, 0UL);
            controller.GoalkeeperMotor = null;
            return motor;
        }

        private static Transform CreateShoulder(
            Transform parent,
            string name,
            float localX,
            GoalkeeperControlMotorConfig configuration)
        {
            var shoulder = new GameObject(name).transform;
            shoulder.SetParent(parent, false);
            shoulder.localPosition = new Vector3(
                localX,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
            return shoulder;
        }

        private static GameObject CreateKeeperPart(
            Transform parent,
            PrimitiveType primitive,
            string name,
            Material material,
            GoalkeeperContactPart contactPart)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.GetComponent<Renderer>().sharedMaterial = material;
            var marker = part.AddComponent<ContactMarker>();
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = contactPart;
            return part;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void EnsureMotorLabScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    MotorLabScenePath) != null)
            {
                return;
            }

            var scene =
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            var instance = InstantiateArena("Stage5ControlLabArena", Vector3.zero);
            var controller = instance.GetComponent<PenaltyAreaController>();
            controller.ArenaId = 0;
            controller.ShowDebugUi = true;
            ConfigureAgentBehavior(instance, BehaviorType.HeuristicOnly);
            CreateCamera(
                new Vector3(8.5f, 4.5f, 13.5f),
                new Vector3(0f, 1.1f, 0f),
                48f);
            CreateLight();
            EditorSceneManager.SaveScene(scene, MotorLabScenePath);
        }

        private static void EnsureTrainingScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    TrainingScenePath) != null)
            {
                return;
            }

            var scene =
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            var columns = Mathf.CeilToInt(Mathf.Sqrt(TrainingArenaCount));
            for (var index = 0; index < TrainingArenaCount; index++)
            {
                var row = index / columns;
                var column = index % columns;
                var instance = InstantiateArena(
                    $"Stage5ControlTrainingArena_{index:000}",
                    new Vector3(
                        column * TrainingArenaSpacing,
                        0f,
                        row * TrainingArenaSpacing));
                var controller = instance.GetComponent<PenaltyAreaController>();
                controller.ArenaId = index;
                controller.ShowDebugUi = false;
                ConfigureAgentBehavior(instance, BehaviorType.HeuristicOnly);
            }

            CreateCamera(
                new Vector3(18f, 12f, 42f),
                new Vector3(12f, 1f, 12f),
                45f);
            CreateLight();
            EditorSceneManager.SaveScene(scene, TrainingScenePath);
        }

        private static void EnsureDemonstrationScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    DemonstrationScenePath) != null)
            {
                return;
            }

            var scene =
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            var columns = Mathf.CeilToInt(Mathf.Sqrt(TrainingArenaCount));
            for (var index = 0; index < TrainingArenaCount; index++)
            {
                var row = index / columns;
                var column = index % columns;
                var instance = InstantiateArena(
                    $"Stage5DemonstrationArena_{index:000}",
                    new Vector3(
                        column * TrainingArenaSpacing,
                        0f,
                        row * TrainingArenaSpacing));
                var controller =
                    instance.GetComponent<PenaltyAreaController>();
                controller.ArenaId = index;
                controller.AutoRun = false;
                controller.ShowDebugUi = false;
                ConfigureAgentBehavior(
                    instance,
                    BehaviorType.HeuristicOnly);
                var agent =
                    instance.GetComponentInChildren<
                        GoalkeeperControlAgent>(true);
                agent.HeuristicMode =
                    GoalkeeperControlHeuristicMode.ReactiveTeacher;
                var recorder =
                    agent.GetComponent<DemonstrationRecorder>() ??
                    agent.gameObject.AddComponent<DemonstrationRecorder>();
                recorder.Record = false;
                recorder.NumStepsToRecord = 0;
                recorder.DemonstrationName =
                    $"GKCtrlV2A{index:000}";
            }

            var coordinatorObject =
                new GameObject("Stage5ReactiveDemonstrationCoordinator");
            var coordinator =
                coordinatorObject.AddComponent<
                    Stage5ReactiveDemonstrationCoordinator>();
            coordinator.AttemptsPerArena = 1250;
            coordinator.MasterSeed = 20260723UL;
            coordinator.QuitWhenComplete = true;
            CreateCamera(
                new Vector3(18f, 12f, 42f),
                new Vector3(12f, 1f, 12f),
                45f);
            CreateLight();
            EditorSceneManager.SaveScene(
                scene,
                DemonstrationScenePath);
        }

        private static GameObject InstantiateArena(
            string name,
            Vector3 position)
        {
            var arenaPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (arenaPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Failed to load {PrefabPath}.");
            }

            var instance =
                PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Failed to instantiate Stage 5 arena.");
            }

            instance.name = name;
            instance.transform.position = position;
            return instance;
        }

        private static void ConfigureAgentBehavior(
            GameObject arena,
            BehaviorType behaviorType)
        {
            var agent = arena.GetComponentInChildren<GoalkeeperControlAgent>(true);
            var behavior =
                agent == null ? null : agent.GetComponent<BehaviorParameters>();
            if (agent == null || behavior == null)
            {
                throw new InvalidOperationException(
                    "Stage 5 arena is missing its hybrid ML-Agents behavior.");
            }

            behavior.BehaviorType = behaviorType;
        }

        private static void CreateCamera(
            Vector3 position,
            Vector3 lookAt,
            float fieldOfView)
        {
            var cameraObject = new GameObject("DebugCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 200f;
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("DirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(
                    Stage0ProjectBuilder.ScenePath,
                    true),
                new EditorBuildSettingsScene(
                    Stage1ProjectBuilder.ScenePath,
                    true),
                new EditorBuildSettingsScene(
                    Stage2ProjectBuilder.ScenePath,
                    true),
                new EditorBuildSettingsScene(
                    Stage4ProjectBuilder.ScenePath,
                    true),
                new EditorBuildSettingsScene(MotorLabScenePath, true),
                new EditorBuildSettingsScene(TrainingScenePath, true),
                new EditorBuildSettingsScene(
                    DemonstrationScenePath,
                    true),
            };
        }

        private static void BuildHeadless(BuildTarget target, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var scene = EditorSceneManager.OpenScene(
                TrainingScenePath,
                OpenSceneMode.Single);
            SetSceneBehaviorType(BehaviorType.Default);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { TrainingScenePath },
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
                        $"Headless {target} build failed: " +
                        $"{report.summary.result}, " +
                        $"{report.summary.totalErrors} errors.");
                }
            }
            finally
            {
                var restored = EditorSceneManager.OpenScene(
                    TrainingScenePath,
                    OpenSceneMode.Single);
                SetSceneBehaviorType(BehaviorType.HeuristicOnly);
                EditorSceneManager.MarkSceneDirty(restored);
                EditorSceneManager.SaveScene(restored);
            }
        }

        private static void BuildDemonstrationHeadless(
            BuildTarget target,
            string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { DemonstrationScenePath },
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
                    $"Reactive demonstration {target} build failed: " +
                    $"{report.summary.result}, " +
                    $"{report.summary.totalErrors} errors.");
            }
        }

        private static void BuildNativeInference(
            BuildTarget target,
            string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var sceneFile = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    TrainingScenePath));
            var originalSceneBytes = File.ReadAllBytes(sceneFile);
            var scene = EditorSceneManager.OpenScene(
                TrainingScenePath,
                OpenSceneMode.Single);
            SetSceneBehaviorType(BehaviorType.HeuristicOnly);
            SetNativeInferenceDefault(true);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { TrainingScenePath },
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
                        $"Native inference {target} build failed: " +
                        $"{report.summary.result}, " +
                        $"{report.summary.totalErrors} errors.");
                }
            }
            finally
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
                File.WriteAllBytes(sceneFile, originalSceneBytes);
                AssetDatabase.ImportAsset(
                    TrainingScenePath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);
            }
        }

        private static void SetSceneBehaviorType(BehaviorType behaviorType)
        {
            foreach (var agent in
                     UnityEngine.Object.FindObjectsByType<GoalkeeperControlAgent>(
                         FindObjectsSortMode.None))
            {
                var behavior = agent.GetComponent<BehaviorParameters>();
                if (behavior != null)
                {
                    behavior.BehaviorType = behaviorType;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        behavior);
                }
            }
        }

        private static void SetNativeInferenceDefault(bool enabled)
        {
            foreach (var agent in
                     UnityEngine.Object.FindObjectsByType<
                         GoalkeeperControlAgent>(FindObjectsSortMode.None))
            {
                agent.NativeSplitInferenceByDefault = enabled;
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    agent);
            }
        }
    }
}
