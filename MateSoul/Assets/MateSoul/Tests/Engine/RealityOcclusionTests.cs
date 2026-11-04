using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ByteDance.PICO.XR;
using MateSoul.Editor;
using MateSoul.RoomNavigation;
using MateSoul.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace MateSoul.Tests
{
    public sealed class RealityOcclusionTests
    {
        private GameObject host;
        private RealityOcclusionMeshRenderer renderer;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Reality Occlusion Test");
            renderer = host.AddComponent<RealityOcclusionMeshRenderer>();
            typeof(RealityOcclusionMeshRenderer).GetMethod(
                    "Awake",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(renderer, null);
        }

        [TearDown]
        public void TearDown()
        {
            Shader.SetGlobalFloat("_EnvironmentOcclusionEnabled", 0f);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void SnapshotWithoutMeasuredSurfaceBuildsNoRealityCollisionProxy()
        {
            renderer.ApplySnapshot(new RoomMapSnapshot(
                "room",
                "test",
                new[]
                {
                    new RoomFloorRegion("floor", new[]
                    {
                        new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 1),
                        new Vector3(1, 0, 1),
                        new Vector3(1, 0, -1)
                    })
                },
                new[]
                {
                    new RoomObstacle(
                        "table",
                        RoomObstacleKind.Table,
                        new Vector3(0, .4f, 0),
                        Quaternion.identity,
                        new Vector3(1, .8f, .6f))
                }));

            Assert.That(renderer.SnapshotMeshCount, Is.EqualTo(0));
            Assert.That(renderer.DepthMaterial, Is.Not.Null);
            Assert.That(renderer.DepthMaterial.shader.name,
                Is.EqualTo("Hidden/MateSoul/Reality Spatial Mesh Depth"));
            Assert.That(host.GetComponentsInChildren<MeshCollider>(), Is.Empty);
        }

        [Test]
        public void CapturedSurfaceMeshTakesPriorityOverCoarseRoomVolumes()
        {
            renderer.ApplySnapshot(new RoomMapSnapshot(
                "room",
                "test",
                new[]
                {
                    new RoomFloorRegion("floor", new[]
                    {
                        new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 1),
                        new Vector3(1, 0, 1),
                        new Vector3(1, 0, -1)
                    })
                },
                new[]
                {
                    new RoomObstacle(
                        "table",
                        RoomObstacleKind.Table,
                        Vector3.zero,
                        Quaternion.identity,
                        Vector3.one)
                },
                new[]
                {
                    new RoomSurfaceMesh(
                        "mesh",
                        new[]
                        {
                            Vector3.zero,
                            Vector3.right,
                            Vector3.up
                        },
                        new[] { 0, 1, 2 })
                }));

            Assert.That(renderer.SnapshotMeshCount, Is.EqualTo(1));
            Assert.That(host.GetComponentInChildren<MeshRenderer>().enabled, Is.True,
                "Measured snapshot surfaces provide depth before native meshes arrive.");
        }

        [Test]
        public void MeasuredMeshRendersWhenEnvironmentDepthIsUnavailable()
        {
            AddMeasuredMesh();

            Assert.That(renderer.NativeMeshCount, Is.EqualTo(1));
            Assert.That(renderer.MeshDepthEnabled, Is.True);
            Assert.That(host.GetComponentInChildren<MeshRenderer>().enabled, Is.True,
                "Measured geometry must remain the visual fallback without live depth.");
        }

        [Test]
        public void MeasuredOcclusionRemainsEnabledWithAvailableEnvironmentDepth()
        {
            var manager = host.AddComponent<PXR_EnvironmentDepthManager>();
            typeof(PXR_EnvironmentDepthManager).GetProperty("IsDepthAvailable")
                .GetSetMethod(true).Invoke(manager, new object[] { true });
            Shader.SetGlobalFloat("_EnvironmentOcclusionEnabled", 1f);
            AddMeasuredMesh();

            Assert.That(manager.IsDepthAvailable, Is.True);
            Assert.That(renderer.MeshDepthEnabled, Is.True);
            Assert.That(host.GetComponentInChildren<MeshRenderer>().enabled, Is.True);
            Assert.That(host.GetComponentInChildren<MeshCollider>().enabled, Is.True);
        }

        [Test]
        public void MeasuredDepthHidesVirtualObjectButHudStillDraws()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "This regression requires actual GPU rendering.");
            Shader.SetGlobalFloat("_EnvironmentOcclusionEnabled", 1f);
            AddMeasuredMesh();
            var measured = host.GetComponentInChildren<MeshRenderer>();
            var mesh = measured.GetComponent<MeshFilter>().sharedMesh;
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            var worldMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var hudMaterial = AlwaysOnTopUi.CreateMaterial();
            var cameraHost = new GameObject("Occlusion Pixel Camera");
            var camera = cameraHost.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.aspect = 1;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << 31;
            measured.gameObject.layer = 31;
            measured.transform.position = Vector3.forward;
            var worldObject = new GameObject("Virtual Pixel Object",
                typeof(MeshFilter), typeof(MeshRenderer));
            worldObject.layer = 31;
            worldObject.transform.position = Vector3.forward * 2;
            worldObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            worldObject.GetComponent<MeshRenderer>().sharedMaterial = worldMaterial;
            var hudObject = new GameObject("HUD Pixel Object",
                typeof(MeshFilter), typeof(MeshRenderer));
            hudObject.layer = 31;
            hudObject.transform.position = Vector3.forward * 3;
            hudObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            hudObject.GetComponent<MeshRenderer>().sharedMaterial = hudMaterial;
            hudObject.SetActive(false);
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            var previousTarget = RenderTexture.active;
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                target.Create();
                camera.targetTexture = target;
                worldMaterial.SetColor("_BaseColor", Color.red);
                worldMaterial.SetFloat("_Cull", 0);
                hudMaterial.SetColor("_Color", Color.green);
                measured.enabled = false;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                var visible = pixels.GetPixel(32, 32);
                Assert.That(visible.r, Is.GreaterThan(.9f),
                    "The virtual object must be visible before real-world depth is drawn.");

                typeof(RealityOcclusionMeshRenderer).GetMethod("UpdateDepthRendering",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(renderer, new object[] { true });
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                var hidden = pixels.GetPixel(32, 32);
                Assert.That(hidden.r, Is.LessThan(.05f),
                    "Measured real-world depth must hide the virtual object behind it.");

                hudObject.SetActive(true);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                var hudVisible = pixels.GetPixel(32, 32);
                Assert.That(hudVisible.g, Is.GreaterThan(.9f),
                    "HUD must render despite the nearer real-world depth surface.");
                Debug.Log($"MATESOUL_OCCLUSION_PIXELS before={visible} " +
                    $"worldOccluded={hidden} hudVisible={hudVisible}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(hudObject);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraHost);
                Object.DestroyImmediate(worldMaterial);
                Object.DestroyImmediate(hudMaterial);
            }
        }

        [TestCase("Assets/MateSoul/Runtime/Shaders/UiAlwaysOnTop.shader")]
        [TestCase("Assets/MateSoul/Runtime/Shaders/SpatialUiSdf.shader")]
        public void HudShaderAlwaysRendersWithoutRealityOcclusion(string path)
        {
            var source = File.ReadAllText(path);

            Assert.That(source, Does.Contain("ZTest Always"));
            Assert.That(source, Does.Not.Contain("MateSoulEnvironmentOcclusion"));
            Assert.That(source, Does.Not.Contain("_EnvironmentOcclusionEnabled"));
        }

        [UnityTest]
        public IEnumerator CharacterSpawnWaitsForMeasuredFloorAfterFirstWallMesh()
        {
            var room = host.AddComponent<RuntimeRoomNavigation>();
            var floor = new RoomFloorRegion("floor", new[]
            {
                new Vector3(-2, .529f, -2), new Vector3(-2, .529f, 2),
                new Vector3(2, .529f, 2), new Vector3(2, .529f, -2)
            });
            var snapshot = new RoomMapSnapshot("spawn-replay", "test",
                new[] { floor }, Array.Empty<RoomObstacle>());
            room.Build(snapshot, false);
            renderer.ApplySnapshot(snapshot, room);
            var game = host.AddComponent<GameBootstrap>();
            game.enabled = false;
            typeof(GameBootstrap).GetField("realityOcclusion",
                BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, renderer);
            var update = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "OnSpatialMeshDataUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            var apply = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "ApplyPendingMeshes", BindingFlags.NonPublic | BindingFlags.Instance);
            Submit(new[]
            {
                new Vector3(1.8f, .529f, -2), new Vector3(1.8f, 2.2f, -2),
                new Vector3(1.8f, 2.2f, 2), new Vector3(1.8f, .529f, 2)
            });
            Assert.That(room.TryFindSpawnPosition(Vector3.zero, out _), Is.False,
                "A wall does not provide measured floor support.");
            var ready = (System.Threading.Tasks.Task<Vector3?>)typeof(GameBootstrap)
                .GetMethod("WaitForCharacterSpawnAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(game, new object[] { Vector3.zero, TimeSpan.FromSeconds(2),
                    System.Threading.CancellationToken.None });
            yield return null;
            Assert.That(ready.IsCompleted, Is.False,
                "The first wall must not prematurely end character startup.");
            Debug.Log("MATESOUL_SPAWN_REPLAY firstWall=True startupWaiting=True");
            Submit(floor.Vertices.ToArray());
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 3;
            while (!ready.IsCompleted && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(ready.Status, Is.EqualTo(System.Threading.Tasks.TaskStatus.RanToCompletion));
            Assert.That(ready.Result.HasValue, Is.True);
            var spawn = ready.Result.Value;
            Assert.That(spawn.y, Is.EqualTo(.529f).Within(.001f));
            Debug.Log($"MATESOUL_SPAWN_REPLAY measuredFloorAdded=True spawn=True position={spawn}");

            void Submit(Vector3[] vertices)
            {
                update.Invoke(renderer, new object[] { new List<PxrSpatialMeshInfo>
                {
                    new PxrSpatialMeshInfo
                    {
                        uuid = Guid.NewGuid(), state = MeshChangeState.Added,
                        position = Vector3.zero, rotation = Quaternion.identity,
                        vertices = vertices, indices = new ushort[] { 0, 1, 2, 0, 2, 3 }
                    }
                } });
                apply.Invoke(renderer, null);
            }
        }

        [UnityTest]
        public IEnumerator MissingMeasuredMeshTimesOutAndCancellationStopsSpawnWait()
        {
            var game = host.AddComponent<GameBootstrap>();
            game.enabled = false;
            typeof(GameBootstrap).GetField("realityOcclusion",
                BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, renderer);
            var wait = typeof(GameBootstrap).GetMethod("WaitForCharacterSpawnAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var pending = (System.Threading.Tasks.Task<Vector3?>)wait.Invoke(game,
                new object[] { Vector3.zero, TimeSpan.FromMilliseconds(100),
                    System.Threading.CancellationToken.None });
            var deadline = UnityEditor.EditorApplication.timeSinceStartup + 2;
            while (!pending.IsCompleted && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(pending.Status, Is.EqualTo(System.Threading.Tasks.TaskStatus.RanToCompletion));
            Assert.That(pending.Result.HasValue, Is.False,
                "A missing measured floor must report no spawn so startup can select the measured room.");
            using (var cancellation = new System.Threading.CancellationTokenSource())
            {
                pending = (System.Threading.Tasks.Task<Vector3?>)wait.Invoke(game,
                    new object[] { Vector3.zero, TimeSpan.FromSeconds(8), cancellation.Token });
                cancellation.Cancel();
                deadline = UnityEditor.EditorApplication.timeSinceStartup + 2;
                while (!pending.IsCompleted && UnityEditor.EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.That(pending.IsCanceled, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator CapturedFloorMismatchRequiresRebuildingFromMeasuredRoom()
        {
            var room = host.AddComponent<RuntimeRoomNavigation>();
            var game = host.AddComponent<GameBootstrap>();
            game.enabled = false;
            typeof(GameBootstrap).GetField("realityOcclusion",
                BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, renderer);
            var build = typeof(GameBootstrap).GetMethod("BuildRoomAndFindSpawnAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var update = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "OnSpatialMeshDataUpdated", BindingFlags.NonPublic | BindingFlags.Instance);
            var apply = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "ApplyPendingMeshes", BindingFlags.NonPublic | BindingFlags.Instance);
            var block = new PxrSpatialMeshInfo
            {
                uuid = Guid.NewGuid(), state = MeshChangeState.Added,
                position = Vector3.zero, rotation = Quaternion.identity,
                vertices = new[]
                {
                    new Vector3(-2, 1.14f, -2), new Vector3(-2, 1.14f, 2),
                    new Vector3(2, 1.18f, 2), new Vector3(2, 1.18f, -2)
                },
                indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
                labels = Enumerable.Repeat(PxrSemanticLabel.Floor, 4).ToArray()
            };
            var captured = new RoomMapSnapshot("stale-capture", "PICO Room Capture",
                new[] { new RoomFloorRegion("floor", block.vertices
                    .Select(point => new Vector3(point.x, .64f, point.z)).ToArray()) },
                Array.Empty<RoomObstacle>());
            var pending = (System.Threading.Tasks.Task<Vector3?>)build.Invoke(game,
                new object[] { captured, System.Threading.CancellationToken.None });
            update.Invoke(renderer, new object[] { new List<PxrSpatialMeshInfo> { block } });
            apply.Invoke(renderer, null);
            var deadline = EditorApplication.timeSinceStartup + 10;
            while (!pending.IsCompleted && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(pending.Status, Is.EqualTo(System.Threading.Tasks.TaskStatus.RanToCompletion));
            Assert.That(pending.Result.HasValue, Is.False,
                "The 52cm height mismatch must still fail measured support checks.");

            var provider = new PicoSpatialMeshRoomMapProvider(TimeSpan.FromSeconds(1));
            var measured = (RoomMapSnapshot)typeof(PicoSpatialMeshRoomMapProvider)
                .GetMethod("BuildSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(provider, new object[] { new[] { block } });
            pending = (System.Threading.Tasks.Task<Vector3?>)build.Invoke(game,
                new object[] { measured, System.Threading.CancellationToken.None });
            Assert.That(renderer.HasLiveCollision, Is.False,
                "Replacing the room must retire its old native entries.");
            block.state = MeshChangeState.Unchanged;
            update.Invoke(renderer, new object[] { new List<PxrSpatialMeshInfo> { block } });
            apply.Invoke(renderer, null);
            deadline = EditorApplication.timeSinceStartup + 3;
            while (!pending.IsCompleted && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(pending.Status, Is.EqualTo(System.Threading.Tasks.TaskStatus.RanToCompletion));
            Assert.That(pending.Result.HasValue, Is.True);
            Assert.That(pending.Result.Value.y, Is.EqualTo(1.16f).Within(.03f));
            Assert.That(room.Snapshot.Source, Is.EqualTo(PicoSpatialMeshRoomMapProvider.ProviderName));
            Assert.That(renderer.HasLiveCollision && renderer.MeshDepthEnabled, Is.True);

            var validCapture = new RoomMapSnapshot(measured.Id, "PICO Room Capture",
                measured.Floors, measured.Obstacles);
            pending = (System.Threading.Tasks.Task<Vector3?>)build.Invoke(game,
                new object[] { validCapture, System.Threading.CancellationToken.None });
            deadline = EditorApplication.timeSinceStartup + 3;
            while (!pending.IsCompleted && EditorApplication.timeSinceStartup < deadline)
                yield return null;
            Assert.That(pending.Status, Is.EqualTo(System.Threading.Tasks.TaskStatus.RanToCompletion));
            Assert.That(pending.Result.HasValue, Is.True);
            Assert.That(room.Snapshot.Source, Is.EqualTo("PICO Room Capture"),
                "A capture compatible with measured support must remain usable.");
            Debug.Log($"MATESOUL_CAPTURE_REPLAY staleFloorY=.640 measuredFloorY={measured.MainFloor.Centroid.y:F3} " +
                      $"spawnY={pending.Result.Value.y:F3} validCapturePreserved=True");
        }

        [Test]
        public void HudVisibleCenterIncludesTwentyCentimeterDownwardOffset()
        {
            var calculate = typeof(GameHud).GetMethod(
                "CalculateVisibleCenter",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(calculate, Is.Not.Null);

            var center = (Vector3)calculate.Invoke(
                null,
                new object[] { new Vector3(1, 2, 3), Quaternion.identity });

            Assert.That(center.x, Is.EqualTo(1f).Within(.0001f));
            Assert.That(center.y, Is.EqualTo(1.8f).Within(.0001f));
            Assert.That(center.z, Is.EqualTo(3.45f).Within(.0001f));
        }

        [Test]
        public void EveryQualityRendererContainsEnvironmentOcclusionFeature()
        {
            var paths = new[]
            {
                "Assets/Settings/URP-Performant-Renderer.asset",
                "Assets/Settings/URP-Balanced-Renderer.asset",
                "Assets/Settings/URP-HighFidelity-Renderer.asset"
            };

            foreach (var path in paths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                Assert.That(RealityOcclusionSetup.HasFeature(data), Is.True, path);
            }
        }

        [TestCase("swan_arm64", false)]
        [TestCase("Pico-emulator", true)]
        [TestCase("Pico/swan/pico_spaceos_emulator_arm64:16/build", true)]
        public void EmulatorIdentityRequiresExplicitEmulatorMarker(
            string identity,
            bool expected)
        {
            var method = typeof(PicoDeviceEnvironment).GetMethod(
                "HasEmulatorMarker",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { identity }), Is.EqualTo(expected));
        }

        private void AddMeasuredMesh()
        {
            var update = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "OnSpatialMeshDataUpdated",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var apply = typeof(RealityOcclusionMeshRenderer).GetMethod(
                "ApplyPendingMeshes",
                BindingFlags.NonPublic | BindingFlags.Instance);
            update.Invoke(renderer, new object[]
            {
                new List<PxrSpatialMeshInfo>
                {
                    new PxrSpatialMeshInfo
                    {
                        uuid = Guid.NewGuid(),
                        state = MeshChangeState.Added,
                        position = Vector3.zero,
                        rotation = Quaternion.identity,
                        vertices = new[]
                        {
                            new Vector3(-.6f, -.6f, 0),
                            new Vector3(-.6f, .6f, 0),
                            new Vector3(.6f, .6f, 0),
                            new Vector3(.6f, -.6f, 0)
                        },
                        indices = new ushort[] { 0, 1, 2, 0, 2, 3 }
                    }
                }
            });
            apply.Invoke(renderer, null);
        }
    }
}
