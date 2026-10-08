using System;
using System.Threading;
using System.Threading.Tasks;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.RoomNavigation
{
    public enum RoomNavigationState
    {
        Idle,
        LoadingRoom,
        BuildingNavigation,
        Ready,
        Failed
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RuntimeRoomNavigation))]
    public sealed class RoomNavigationBootstrap : MonoBehaviour
    {
        [SerializeField]
        private TextAsset simulatedSceneData;

        [SerializeField]
        private GameObject characterRoot;

        [SerializeField]
        private bool requestRoomCaptureWhenMissing = true;

        [SerializeField]
        private bool useNativeSpatialMeshOnPicoEmulator = true;

        [SerializeField]
        private bool showDebugGeometry = true;

        [SerializeField]
        private int randomSeed = 1701;

        private RuntimeRoomNavigation navigation;
        private CancellationTokenSource lifetimeCancellation;

        public RoomNavigationState State { get; private set; } = RoomNavigationState.Idle;
        public string LastError { get; private set; } = string.Empty;
        public RoomRandomWalker Walker { get; private set; }
        public RoomMapSnapshot Snapshot => navigation != null ? navigation.Snapshot : null;

        public void Configure(
            TextAsset mockData,
            GameObject character,
            bool debugGeometry,
            int seed)
        {
            simulatedSceneData = mockData;
            characterRoot = character;
            showDebugGeometry = debugGeometry;
            randomSeed = seed;
        }

        private async void Start()
        {
            navigation = GetComponent<RuntimeRoomNavigation>();
            lifetimeCancellation = new CancellationTokenSource();
            if (characterRoot != null)
            {
                characterRoot.SetActive(false);
            }

            try
            {
                await InitializeAsync(lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                State = RoomNavigationState.Failed;
                LastError = exception.Message;
                Debug.LogError($"[RoomNavigation] Initialization failed: {exception}");
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            State = RoomNavigationState.LoadingRoom;
            RoomMapSnapshot snapshot;
            if (Application.isEditor)
            {
                snapshot = await LoadSimulationAsync(cancellationToken);
            }
            else if (useNativeSpatialMeshOnPicoEmulator && IsPicoEmulator())
            {
                var provider = new PicoSpatialMeshRoomMapProvider(TimeSpan.FromSeconds(15));
                snapshot = await provider.LoadAsync(cancellationToken);
            }
            else
            {
#if UNITY_ANDROID
                PXR_Manager.EnableVideoSeeThrough = true;
#endif
                var provider = new PicoSceneCaptureRoomMapProvider(requestRoomCaptureWhenMissing);
                snapshot = await provider.LoadAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var validation = snapshot.Validate();
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Error);
            }

            State = RoomNavigationState.BuildingNavigation;
            navigation.Build(snapshot, showDebugGeometry && Application.isEditor);
            cancellationToken.ThrowIfCancellationRequested();

            if (characterRoot == null)
            {
                throw new InvalidOperationException("The production character reference is missing.");
            }

            Walker = characterRoot.GetComponent<RoomRandomWalker>();
            if (Walker == null)
            {
                Walker = characterRoot.AddComponent<RoomRandomWalker>();
            }

            characterRoot.SetActive(true);
            Walker.Initialize(navigation, GetInitialSpawnPoint(snapshot), randomSeed);

            State = RoomNavigationState.Ready;
            Debug.Log(
                $"[RoomNavigation] Ready source={snapshot.Source} " +
                $"floorArea={validation.FloorArea:F2}m2 obstacles={snapshot.Obstacles.Count} " +
                $"navMeshBuildMs={navigation.LastBuildMilliseconds}");
        }

        private static Vector3 GetInitialSpawnPoint(RoomMapSnapshot snapshot)
        {
            var fallback = snapshot.MainFloor.Centroid;
            var mainCamera = Camera.main;
            if (Application.isEditor || mainCamera == null)
            {
                return fallback;
            }

            var forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
            {
                return fallback;
            }

            var desired = mainCamera.transform.position + forward.normalized * 2.2f;
            desired.y = fallback.y;
            return desired;
        }

        private Task<RoomMapSnapshot> LoadSimulationAsync(CancellationToken cancellationToken)
        {
            var provider = new PicoMockRoomMapProvider(simulatedSceneData);
            return provider.LoadAsync(cancellationToken);
        }

        private static bool IsPicoEmulator()
        {
            return ContainsIgnoreCase(SystemInfo.deviceModel, "swan_arm64") ||
                   ContainsIgnoreCase(SystemInfo.deviceName, "pico_spaceos_emulator");
        }

        private static bool ContainsIgnoreCase(string value, string expected)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnDestroy()
        {
            if (lifetimeCancellation == null)
            {
                return;
            }

            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
            lifetimeCancellation = null;
        }
    }
}
