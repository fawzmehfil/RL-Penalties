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
    public static class Stage1ProjectBuilder
    {
        public const string ScenePath = "Assets/PenaltyShootout/Scenes/KernelLab.unity";
        public const string PrefabPath = "Assets/PenaltyShootout/Prefabs/TrainingArena.prefab";
        public const string EnvironmentConfigPath =
            "Assets/PenaltyShootout/Config/EnvironmentKernelConfig.asset";
        public const string ShotConfigPath =
            "Assets/PenaltyShootout/Config/OnTargetShotDistribution.asset";
        public const string MotorConfigPath =
            "Assets/PenaltyShootout/Config/GoalkeeperMotorProfile.asset";

        private const string MaterialDirectory = "Assets/PenaltyShootout/Materials";

        [MenuItem("Penalty Shootout/Stage 1/Prepare Kernel")]
        public static void PrepareProject()
        {
            EnsureDirectories();
            var environment = GetOrCreateConfig<EnvironmentKernelConfig>(
                EnvironmentConfigPath,
                "EnvironmentKernelConfig");
            var shots = GetOrCreateConfig<ShotDistributionConfig>(
                ShotConfigPath,
                "OnTargetShotDistribution");
            var motor = GetOrCreateConfig<GoalkeeperMotorConfig>(
                MotorConfigPath,
                "GoalkeeperMotorProfile");
            ApplyAuthoritativeHandProfile(motor);

            ExportManifest(environment, shots, motor);
            CreateTrainingArenaPrefab(environment, shots, motor);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateKernelLabScene();
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"Prepared {ScenePath}, {PrefabPath}, and the " +
                $"{KernelConstants.EnvironmentId} manifest.");
        }

        [MenuItem("Penalty Shootout/Stage 1/Build macOS Headless")]
        public static void BuildMacHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../builds/macos/PenaltyShootoutStage1.app"));
            BuildHeadless(BuildTarget.StandaloneOSX, output);
        }

        [MenuItem("Penalty Shootout/Stage 1/Build Linux Headless")]
        public static void BuildLinuxHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../builds/linux/PenaltyShootoutStage1.x86_64"));
            BuildHeadless(BuildTarget.StandaloneLinux64, output);
        }

        public static void ExportManifest(
            EnvironmentKernelConfig environment,
            ShotDistributionConfig shots,
            GoalkeeperMotorConfig motor)
        {
            var json = KernelManifestUtility.CreateJson(environment, shots, motor);
            var output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../configs/environment/kernel-v1.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, json);
            Debug.Log(
                $"Exported kernel manifest {KernelManifestUtility.Sha256(json)} to {output}.");
        }

        private static void CreateTrainingArenaPrefab(
            EnvironmentKernelConfig environment,
            ShotDistributionConfig shots,
            GoalkeeperMotorConfig motor)
        {
            var white = GetOrCreateMaterial("GoalFrame", new Color(0.95f, 0.95f, 0.95f));
            var pitch = GetOrCreateMaterial("Pitch", new Color(0.08f, 0.34f, 0.12f));
            var ballMaterial = GetOrCreateMaterial("Ball", new Color(0.95f, 0.95f, 0.95f));
            var targetMaterial = GetOrCreateMaterial("Target", new Color(1f, 0.25f, 0.1f));
            var lineMaterial = GetOrCreateMaterial(
                "Trajectory",
                new Color(1f, 0.75f, 0.08f),
                true);
            var keeperMaterial = GetOrCreateMaterial(
                "GoalkeeperProxy",
                new Color(0.08f, 0.30f, 0.85f));
            var gloveMaterial = GetOrCreateMaterial(
                "GoalkeeperGloves",
                new Color(0.95f, 0.72f, 0.12f));

            var root = new GameObject("TrainingArena");
            var arenaOrigin = root.transform;

            CreateGround(root.transform, pitch);
            CreateGoal(root.transform, white);
            CreateGoalLineMarker(root.transform, white);

            var ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballObject.name = "Ball";
            ballObject.transform.SetParent(root.transform, false);
            ballObject.transform.localPosition = KernelConstants.CanonicalLaunch;
            ballObject.transform.localScale = Vector3.one * (KernelConstants.BallRadius * 2f);
            ballObject.GetComponent<Renderer>().sharedMaterial = ballMaterial;
            var ballBody = ballObject.AddComponent<Rigidbody>();
            ballBody.mass = KernelConstants.BallMass;
            ballBody.linearDamping = 0f;
            ballBody.angularDamping = 0.05f;
            ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ballBody.interpolation = RigidbodyInterpolation.None;
            var contactSensor = ballObject.AddComponent<BallContactSensor>();

            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.name = "RequestedTarget";
            target.transform.SetParent(root.transform, false);
            target.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            target.transform.localScale = Vector3.one * 0.14f;
            target.GetComponent<Renderer>().sharedMaterial = targetMaterial;
            UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());

            var trajectoryObject = new GameObject("Trajectory");
            trajectoryObject.transform.SetParent(root.transform, false);
            var trajectory = trajectoryObject.AddComponent<LineRenderer>();
            trajectory.sharedMaterial = lineMaterial;
            trajectory.startWidth = 0.035f;
            trajectory.endWidth = 0.015f;
            trajectory.positionCount = 0;
            trajectory.useWorldSpace = true;

            var keeper = CreateGoalkeeper(
                root.transform,
                arenaOrigin,
                motor,
                keeperMaterial,
                gloveMaterial);

            var scenarioController = root.AddComponent<ScenarioController>();
            scenarioController.Configuration = shots;
            scenarioController.ArenaId = 0;
            scenarioController.MasterSeed = 20260723UL;
            var controller = root.AddComponent<PenaltyAreaController>();
            controller.EnvironmentConfiguration = environment;
            controller.ShotConfiguration = shots;
            controller.MotorConfiguration = motor;
            controller.ArenaOrigin = arenaOrigin;
            controller.Ball = ballBody;
            controller.BallCollider = ballObject.GetComponent<Collider>();
            controller.BallContactSensor = contactSensor;
            controller.GoalkeeperMotor = keeper;
            controller.ScenarioController = scenarioController;
            controller.TargetMarker = target.transform;
            controller.Trajectory = trajectory;
            controller.ArenaId = 0;
            controller.MasterSeed = 20260723UL;
            controller.AutoRun = true;
            controller.ManualSimulationMode = false;
            controller.ShowDebugUi = true;

            var agentObject = new GameObject("GoalkeeperKernelAgent");
            agentObject.transform.SetParent(root.transform, false);
            var behavior = agentObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = KernelConstants.BehaviorName;
            behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize = 1;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(9);
            var agent = agentObject.AddComponent<GoalkeeperKernelAgent>();
            agent.MaxStep = 0;
            var decisionRequester = agentObject.AddComponent<DecisionRequester>();
            decisionRequester.DecisionPeriod = 2;
            decisionRequester.DecisionStep = 0;
            decisionRequester.TakeActionsBetweenDecisions = false;
            controller.ActionSource = agent;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (saved == null)
            {
                throw new InvalidOperationException($"Failed to create {PrefabPath}.");
            }
        }

        private static GoalkeeperMotor CreateGoalkeeper(
            Transform arena,
            Transform arenaOrigin,
            GoalkeeperMotorConfig configuration,
            Material bodyMaterial,
            Material gloveMaterial)
        {
            var root = new GameObject("GoalkeeperProxy");
            root.transform.SetParent(arena, false);
            root.transform.localPosition = new Vector3(0f, 0f, configuration.StandingZ);
            var marker = root.AddComponent<ContactMarker>();
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = GoalkeeperContactPart.TorsoOrHead;
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var motor = root.AddComponent<GoalkeeperMotor>();
            motor.Configuration = configuration;
            motor.ArenaOrigin = arenaOrigin;

            var torso = CreateKeeperPart(
                root.transform,
                PrimitiveType.Capsule,
                "Torso",
                new Vector3(0f, 0.98f, 0f),
                new Vector3(0.52f, 0.72f, 0.36f),
                bodyMaterial);
            MarkKeeperPart(torso, GoalkeeperContactPart.TorsoOrHead);
            var head = CreateKeeperPart(
                root.transform,
                PrimitiveType.Sphere,
                "Head",
                new Vector3(0f, 1.73f, 0f),
                Vector3.one * 0.32f,
                bodyMaterial);
            MarkKeeperPart(head, GoalkeeperContactPart.TorsoOrHead);

            var leftShoulder = new GameObject("LeftShoulder").transform;
            leftShoulder.SetParent(root.transform, false);
            leftShoulder.localPosition = new Vector3(
                -configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);
            var rightShoulder = new GameObject("RightShoulder").transform;
            rightShoulder.SetParent(root.transform, false);
            rightShoulder.localPosition = new Vector3(
                configuration.ShoulderLateral,
                configuration.ShoulderHeight,
                configuration.ShoulderForward);

            var leftArm = CreateKeeperPart(
                root.transform,
                PrimitiveType.Capsule,
                "LeftArm",
                new Vector3(-0.38f, 1.10f, 0f),
                new Vector3(0.18f, 0.42f, 0.18f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, -32f));
            MarkKeeperPart(leftArm, GoalkeeperContactPart.Arm);
            var rightArm = CreateKeeperPart(
                root.transform,
                PrimitiveType.Capsule,
                "RightArm",
                new Vector3(0.38f, 1.10f, 0f),
                new Vector3(0.18f, 0.42f, 0.18f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, 32f));
            MarkKeeperPart(rightArm, GoalkeeperContactPart.Arm);
            var leftGlove = CreateKeeperPart(
                root.transform,
                PrimitiveType.Sphere,
                "LeftGlove",
                new Vector3(
                    -configuration.ReadyGloveLateral,
                    configuration.ReadyGloveHeight,
                    configuration.ReadyGloveForward),
                Vector3.one * (configuration.GloveRadius * 2f),
                gloveMaterial);
            MarkKeeperPart(leftGlove, GoalkeeperContactPart.LeftGlove);
            var rightGlove = CreateKeeperPart(
                root.transform,
                PrimitiveType.Sphere,
                "RightGlove",
                new Vector3(
                    configuration.ReadyGloveLateral,
                    configuration.ReadyGloveHeight,
                    configuration.ReadyGloveForward),
                Vector3.one * (configuration.GloveRadius * 2f),
                gloveMaterial);
            MarkKeeperPart(rightGlove, GoalkeeperContactPart.RightGlove);
            var leftLeg = CreateKeeperPart(
                root.transform,
                PrimitiveType.Capsule,
                "LeftLeg",
                new Vector3(-0.18f, 0.36f, 0f),
                new Vector3(0.20f, 0.34f, 0.20f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, -8f));
            MarkKeeperPart(leftLeg, GoalkeeperContactPart.Leg);
            var rightLeg = CreateKeeperPart(
                root.transform,
                PrimitiveType.Capsule,
                "RightLeg",
                new Vector3(0.18f, 0.36f, 0f),
                new Vector3(0.20f, 0.34f, 0.20f),
                bodyMaterial,
                Quaternion.Euler(0f, 0f, 8f));
            MarkKeeperPart(rightLeg, GoalkeeperContactPart.Leg);

            var reachRig = root.AddComponent<GoalkeeperReachRig>();
            reachRig.Configure(
                configuration,
                arenaOrigin,
                leftShoulder,
                rightShoulder,
                leftArm.transform,
                rightArm.transform,
                leftGlove.transform,
                rightGlove.transform);
            motor.ReachRig = reachRig;
            return motor;
        }

        private static void MarkKeeperPart(
            GameObject part,
            GoalkeeperContactPart goalkeeperPart)
        {
            var marker = part.AddComponent<ContactMarker>();
            marker.Kind = ContactKind.Goalkeeper;
            marker.GoalkeeperPart = goalkeeperPart;
        }

        private static void CreateGround(Transform parent, Material material)
        {
            var ground = CreateCube(
                "Ground",
                parent,
                new Vector3(0f, -0.05f, 5.5f),
                new Vector3(14f, 0.1f, 25f),
                material);
            ground.AddComponent<ContactMarker>().Kind = ContactKind.Ground;
            ground.isStatic = true;
        }

        private static void CreateGoal(Transform parent, Material material)
        {
            var goal = new GameObject("GoalFrame");
            goal.transform.SetParent(parent, false);
            goal.AddComponent<ContactMarker>().Kind = ContactKind.GoalFrame;
            var postX =
                KernelConstants.GoalHalfWidth + KernelConstants.FrameThickness * 0.5f;
            CreateCube(
                "LeftPost",
                goal.transform,
                new Vector3(-postX, KernelConstants.CrossbarLowerEdge * 0.5f, 0f),
                new Vector3(
                    KernelConstants.FrameThickness,
                    KernelConstants.CrossbarLowerEdge,
                    KernelConstants.FrameThickness),
                material);
            CreateCube(
                "RightPost",
                goal.transform,
                new Vector3(postX, KernelConstants.CrossbarLowerEdge * 0.5f, 0f),
                new Vector3(
                    KernelConstants.FrameThickness,
                    KernelConstants.CrossbarLowerEdge,
                    KernelConstants.FrameThickness),
                material);
            CreateCube(
                "Crossbar",
                goal.transform,
                new Vector3(
                    0f,
                    KernelConstants.CrossbarLowerEdge +
                    KernelConstants.FrameThickness * 0.5f,
                    0f),
                new Vector3(
                    KernelConstants.GoalInsideWidth +
                    KernelConstants.FrameThickness * 2f,
                    KernelConstants.FrameThickness,
                    KernelConstants.FrameThickness),
                material);
        }

        private static void CreateGoalLineMarker(Transform parent, Material material)
        {
            var marker = CreateCube(
                "GoalLineMarker",
                parent,
                new Vector3(0f, 0.006f, 0f),
                new Vector3(KernelConstants.GoalInsideWidth, 0.012f, 0.04f),
                material);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
        }

        private static GameObject CreateKeeperPart(
            Transform parent,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Quaternion? localRotation = null)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation ?? Quaternion.identity;
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        private static void CreateKernelLabScene()
        {
            var arenaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (arenaPrefab == null)
            {
                throw new InvalidOperationException($"Failed to load {PrefabPath}.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = PrefabUtility.InstantiatePrefab(arenaPrefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Failed to instantiate the training arena prefab.");
            }

            instance.name = "TrainingArena";
            CreateCamera();
            CreateLight();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("DebugCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(8.5f, 4.5f, 13.5f);
            camera.transform.LookAt(new Vector3(0f, 1.1f, 0f));
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("DirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = localScale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static T GetOrCreateConfig<T>(string path, string name)
            where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var configuration = ScriptableObject.CreateInstance<T>();
            configuration.name = name;
            AssetDatabase.CreateAsset(configuration, path);
            return configuration;
        }

        private static void ApplyAuthoritativeHandProfile(
            GoalkeeperMotorConfig motor)
        {
            motor.MotorProfileId = KernelConstants.MotorProfileId;
            motor.ReachStartNormalized = 0.08f;
            motor.FullExtensionNormalized = 0.55f;
            motor.LeadingLowLateralReach = 0.55f;
            motor.TrailingLowLateralReach = 0.28f;
            motor.LeadingMiddleLateralReach = 0.65f;
            motor.TrailingMiddleLateralReach = 0.36f;
            motor.LeadingHighLateralReach = 0.76f;
            motor.TrailingHighLateralReach = 0.46f;
            motor.LeadingLowHeight = 0.22f;
            motor.TrailingLowHeight = 0.34f;
            motor.LeadingMiddleHeight = 0.58f;
            motor.TrailingMiddleHeight = 0.48f;
            motor.LeadingHighHeight = 0.92f;
            motor.TrailingHighHeight = 0.74f;
            motor.LeadingForwardReach = 0.18f;
            motor.TrailingForwardReach = 0.12f;
            motor.GloveRadius = 0.125f;
            motor.ArmRadius = 0.09f;
            motor.MaximumArmLength = 0.95f;
            motor.ReadyGloveLateral = 0.58f;
            motor.ReadyGloveHeight = 0.92f;
            motor.ReadyGloveForward = 0f;
            motor.ShoulderLateral = 0.25f;
            motor.ShoulderHeight = 1.30f;
            motor.ShoulderForward = 0f;
            EditorUtility.SetDirty(motor);
        }

        private static Material GetOrCreateMaterial(
            string name,
            Color color,
            bool unlit = false)
        {
            var path = $"{MaterialDirectory}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shaderName = unlit
                ? "Universal Render Pipeline/Unlit"
                : "Universal Render Pipeline/Lit";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required shader not found: {shaderName}");
            }

            var material = new Material(shader)
            {
                name = name,
                color = color,
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory(Path.Combine(
                Application.dataPath,
                "PenaltyShootout/Config"));
            Directory.CreateDirectory(Path.Combine(
                Application.dataPath,
                "PenaltyShootout/Prefabs"));
            Directory.CreateDirectory(Path.Combine(
                Application.dataPath,
                "PenaltyShootout/Scenes"));
            Directory.CreateDirectory(Path.Combine(
                Application.dataPath,
                "PenaltyShootout/Materials"));
        }

        private static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(Stage0ProjectBuilder.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }

        private static void BuildHeadless(BuildTarget target, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<PenaltyAreaController>();
            var agent = UnityEngine.Object.FindFirstObjectByType<GoalkeeperKernelAgent>();
            var behavior = agent == null ? null : agent.GetComponent<BehaviorParameters>();
            if (controller == null || agent == null || behavior == null)
            {
                throw new BuildFailedException("KernelLab is missing its controller or agent.");
            }

            behavior.BehaviorType = BehaviorType.Default;
            PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);
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
                var restoredScene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
                var restoredController =
                    UnityEngine.Object.FindFirstObjectByType<PenaltyAreaController>();
                var restoredAgent =
                    UnityEngine.Object.FindFirstObjectByType<GoalkeeperKernelAgent>();
                var restoredBehavior =
                    restoredAgent == null
                        ? null
                        : restoredAgent.GetComponent<BehaviorParameters>();
                if (restoredController != null &&
                    restoredBehavior != null &&
                    restoredAgent != null)
                {
                    restoredController.ActionSource = restoredAgent;
                    restoredBehavior.BehaviorType = BehaviorType.HeuristicOnly;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        restoredBehavior);
                    EditorSceneManager.MarkSceneDirty(restoredScene);
                    EditorSceneManager.SaveScene(restoredScene);
                }
            }
        }
    }
}
