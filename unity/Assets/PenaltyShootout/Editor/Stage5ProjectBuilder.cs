using System;
using System.IO;
using PenaltyShootout.Kernel;
using PenaltyShootout.MLAgents;
using Unity.MLAgents;
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
    public static class Stage5ProjectBuilder
    {
        public const string MotorLabScenePath =
            "Assets/PenaltyShootout/Scenes/GoalkeeperControlLab.unity";
        public const string TrainingScenePath =
            "Assets/PenaltyShootout/Scenes/ControlTraining.unity";
        public const string PrefabPath =
            "Assets/PenaltyShootout/Prefabs/Stage5ControlArena.prefab";
        public const string MotorConfigPath =
            "Assets/PenaltyShootout/Config/GoalkeeperControlMotorProfile.asset";

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
            ExportGoalkeeperControlManifest(motor);
            CreateControlArenaPrefab(motor);
            CreateMotorLabScene();
            CreateTrainingScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"Prepared {PrefabPath}, {MotorLabScenePath}, and " +
                $"{TrainingScenePath} for Stage 5.");
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

        private static void ExportGoalkeeperControlManifest(
            GoalkeeperControlMotorConfig motor)
        {
            var output = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "../../configs/environment/goalkeeper-control-v1.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(
                output,
                KernelManifestUtility.CreateGoalkeeperControlJson(motor));
        }

        private static void CreateControlArenaPrefab(
            GoalkeeperControlMotorConfig motorConfiguration)
        {
            if (!AssetDatabase.CopyAsset(
                    Stage2ProjectBuilder.PrefabPath,
                    PrefabPath) &&
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
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

            RemoveV0Agent(controller);
            var motor = ConfigureControlGoalkeeper(
                controller,
                motorConfiguration);
            var agentObject = new GameObject("GoalkeeperControlAgent");
            agentObject.transform.SetParent(controller.transform, false);
            var behavior = agentObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName =
                KernelConstants.GoalkeeperControlBehaviorName;
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize =
                KernelConstants.GoalkeeperControlObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = new ActionSpec(
                GoalkeeperControlSpace.ContinuousActionCount,
                new[] { GoalkeeperControlSpace.CommitBranchSize });

            var agent = agentObject.AddComponent<GoalkeeperControlAgent>();
            agent.Controller = controller;
            agent.MaxStep = 0;
            var requester = agentObject.AddComponent<DecisionRequester>();
            requester.DecisionPeriod = 2;
            requester.DecisionStep = 0;
            requester.TakeActionsBetweenDecisions = false;

            controller.ControlMode = GoalkeeperControlMode.HybridV1;
            controller.ControlMotorConfiguration = motorConfiguration;
            controller.GoalkeeperControlMotor = motor;
            controller.ActionSource = agent;
        }

        private static void RemoveV0Agent(PenaltyAreaController controller)
        {
            var agents =
                controller.GetComponentsInChildren<GoalkeeperKernelAgent>(true);
            foreach (var agent in agents)
            {
                UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }
        }

        private static GoalkeeperMotorV1 ConfigureControlGoalkeeper(
            PenaltyAreaController controller,
            GoalkeeperControlMotorConfig configuration)
        {
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

        private static void CreateMotorLabScene()
        {
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

        private static void CreateTrainingScene()
        {
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
    }
}
