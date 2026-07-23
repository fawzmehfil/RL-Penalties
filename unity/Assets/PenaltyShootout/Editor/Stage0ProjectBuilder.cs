using System;
using System.IO;
using PenaltyShootout.Stage0;
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
    public static class Stage0ProjectBuilder
    {
        public const string ScenePath = "Assets/PenaltyShootout/Scenes/PhysicsLab.unity";
        private const string MaterialDirectory = "Assets/PenaltyShootout/Materials";

        [MenuItem("Penalty Shootout/Stage 0/Prepare PhysicsLab")]
        public static void PrepareProject()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Application.dataPath, "PenaltyShootout/Scenes"))!);
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "PenaltyShootout/Scenes"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "PenaltyShootout/Materials"));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var white = GetOrCreateMaterial("GoalFrame", new Color(0.95f, 0.95f, 0.95f));
            var pitch = GetOrCreateMaterial("Pitch", new Color(0.08f, 0.34f, 0.12f));
            var ballMaterial = GetOrCreateMaterial("Ball", new Color(0.95f, 0.95f, 0.95f));
            var targetMaterial = GetOrCreateMaterial("Target", new Color(1f, 0.25f, 0.1f));
            var lineMaterial = GetOrCreateMaterial("Trajectory", new Color(1f, 0.75f, 0.08f), true);

            CreateGround(pitch);
            CreateGoal(white);
            CreateGoalLineMarker(white);
            CreatePlaceholderGoalkeeper(white);

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.position = Stage0Constants.CanonicalLaunch;
            ball.transform.localScale = Vector3.one * (Stage0Constants.BallRadius * 2f);
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            var ballBody = ball.AddComponent<Rigidbody>();
            ballBody.mass = Stage0Constants.BallMass;
            ballBody.linearDamping = 0f;
            ballBody.angularDamping = 0.05f;
            ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            ballBody.interpolation = RigidbodyInterpolation.None;

            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.name = "CanonicalTarget";
            target.transform.position = Stage0Constants.CanonicalTarget;
            target.transform.localScale = Vector3.one * 0.14f;
            target.GetComponent<Renderer>().sharedMaterial = targetMaterial;
            UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());

            var trajectoryObject = new GameObject("Trajectory");
            var trajectory = trajectoryObject.AddComponent<LineRenderer>();
            trajectory.sharedMaterial = lineMaterial;
            trajectory.startWidth = 0.035f;
            trajectory.endWidth = 0.015f;
            trajectory.positionCount = 0;
            trajectory.useWorldSpace = true;

            var controllerObject = new GameObject("PhysicsLabController");
            var controller = controllerObject.AddComponent<PhysicsLabController>();
            controller.Ball = ballBody;
            controller.TargetMarker = target.transform;
            controller.Trajectory = trajectory;
            controller.LaunchPosition = Stage0Constants.CanonicalLaunch;
            controller.TargetPosition = Stage0Constants.CanonicalTarget;
            controller.FlightTime = Stage0Constants.CanonicalFlightTime;
            controller.Timeout = Stage0Constants.AttemptTimeout;
            controller.AutoLaunch = true;

            var agentObject = new GameObject("Stage0ConnectionProbeAgent");
            var behavior = agentObject.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = Stage0Constants.BehaviorName;
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = 8;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(1);

            var probe = agentObject.AddComponent<ConnectionProbeAgent>();
            probe.Controller = controller;
            probe.MaxStep = 150;

            var decisionRequester = agentObject.AddComponent<DecisionRequester>();
            decisionRequester.DecisionPeriod = 1;
            decisionRequester.DecisionStep = 0;
            decisionRequester.TakeActionsBetweenDecisions = true;

            controller.ProbeAgent = probe;

            CreateCamera();
            CreateLight();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Prepared {ScenePath} for {Stage0Constants.EnvironmentId}.");
        }

        [MenuItem("Penalty Shootout/Stage 0/Build macOS Headless")]
        public static void BuildMacHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../builds/macos/PenaltyShootoutStage0.app"));
            BuildHeadless(BuildTarget.StandaloneOSX, output);
        }

        [MenuItem("Penalty Shootout/Stage 0/Build Linux Headless")]
        public static void BuildLinuxHeadless()
        {
            PrepareProject();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../builds/linux/PenaltyShootoutStage0.x86_64"));
            BuildHeadless(BuildTarget.StandaloneLinux64, output);
        }

        private static void BuildHeadless(BuildTarget target, string output)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                // A regular standalone player is used for portability. The
                // probe runs it headlessly with -batchmode -nographics, so a
                // separate Dedicated Server module is not required.
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

            Debug.Log(
                $"Headless {target} build succeeded at {output} " +
                $"({report.summary.totalSize} bytes).");
        }

        private static void CreateGround(Material material)
        {
            var ground = CreateCube(
                "Ground",
                new Vector3(0f, -0.05f, 5.5f),
                new Vector3(14f, 0.1f, 25f),
                material);
            ground.isStatic = true;
        }

        private static void CreateGoal(Material material)
        {
            var goal = new GameObject("GoalFrame");
            var postCentreX = Stage0Constants.GoalHalfWidth + Stage0Constants.FrameThickness * 0.5f;

            var left = CreateCube(
                "LeftPost",
                new Vector3(-postCentreX, Stage0Constants.CrossbarLowerEdge * 0.5f, 0f),
                new Vector3(
                    Stage0Constants.FrameThickness,
                    Stage0Constants.CrossbarLowerEdge,
                    Stage0Constants.FrameThickness),
                material);
            left.transform.SetParent(goal.transform);

            var right = CreateCube(
                "RightPost",
                new Vector3(postCentreX, Stage0Constants.CrossbarLowerEdge * 0.5f, 0f),
                new Vector3(
                    Stage0Constants.FrameThickness,
                    Stage0Constants.CrossbarLowerEdge,
                    Stage0Constants.FrameThickness),
                material);
            right.transform.SetParent(goal.transform);

            var crossbar = CreateCube(
                "Crossbar",
                new Vector3(
                    0f,
                    Stage0Constants.CrossbarLowerEdge + Stage0Constants.FrameThickness * 0.5f,
                    0f),
                new Vector3(
                    Stage0Constants.GoalInsideWidth + Stage0Constants.FrameThickness * 2f,
                    Stage0Constants.FrameThickness,
                    Stage0Constants.FrameThickness),
                material);
            crossbar.transform.SetParent(goal.transform);
        }

        private static void CreateGoalLineMarker(Material material)
        {
            var marker = CreateCube(
                "GoalLineMarker",
                new Vector3(0f, 0.006f, 0f),
                new Vector3(Stage0Constants.GoalInsideWidth, 0.012f, 0.04f),
                material);
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
        }

        private static void CreatePlaceholderGoalkeeper(Material material)
        {
            var keeper = CreateCube(
                "PlaceholderGoalkeeper_Stage1",
                new Vector3(-4.4f, 1f, 0.35f),
                new Vector3(0.7f, 2f, 0.35f),
                material);
            keeper.isStatic = true;
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
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gameObject.name = name;
            gameObject.transform.position = position;
            gameObject.transform.localScale = scale;
            gameObject.GetComponent<Renderer>().sharedMaterial = material;
            return gameObject;
        }

        private static Material GetOrCreateMaterial(string name, Color color, bool unlit = false)
        {
            var path = $"{MaterialDirectory}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            var shaderName = unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit";
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
    }
}
