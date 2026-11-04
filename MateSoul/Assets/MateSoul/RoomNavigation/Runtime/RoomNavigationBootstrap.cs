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
            else if (useNativeSpatialMeshOnPicoEmulator && PicoDeviceEnvironment.IsEmulator)
            {
                var provider = new PicoSpatialMeshRoomMapProvider(TimeSpan.FromSeconds(15));
                snapshot = await provider.LoadAsync(cancellationToken);
            }
            else
            {
#if UNITY_ANDROID
                PXR_Manager.EnableVideoSeeThrough = true;
#endif
                var provider = new PicoSceneCaptureRoomMapProvider();
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
            Physics.SyncTransforms();
            Walker.Initialize(
                navigation, FindStandableSpawn(snapshot), randomSeed);

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

        private Vector3 FindStandableSpawn(RoomMapSnapshot snapshot)
        {
            var requested = GetInitialSpawnPoint(snapshot);
            var capsule = characterRoot.GetComponent<CapsuleCollider>();
            if (capsule == null)
                throw new InvalidOperationException(
                    "The production character requires a CapsuleCollider.");
            for (var ring = 0; ring < 8; ring++)
            {
                var radius = ring == 0 ? 0f : .45f * ring;
                var samples = ring == 0 ? 1 : 16;
                for (var index = 0; index < samples; index++)
                {
                    var direction = Quaternion.Euler(
                        0, index * 360f / samples, 0) * Vector3.forward;
                    var candidate = requested + direction * radius;
                    candidate.y = snapshot.MainFloor.Centroid.y;
                    if (!navigation.TrySamplePosition(
                            candidate, .3f, out var floor) ||
                        Mathf.Abs(floor.y - snapshot.MainFloor.Centroid.y) > .1f ||
                        Vector3.ProjectOnPlane(
                            floor - candidate, Vector3.up).magnitude > .3f)
                        continue;
                    var pose = new Pose(
                        floor + Vector3.up * .025f,
                        characterRoot.transform.rotation);
                    if (CanOccupySpawn(capsule, pose))
                        return floor;
                }
            }
            throw new InvalidOperationException(
                "No collision-free character spawn point exists on the room floor.");
        }

        private static bool CanOccupySpawn(
            CapsuleCollider capsule, Pose pose)
        {
            var scale = capsule.transform.lossyScale;
            var radiusScale = Mathf.Max(
                Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var axisScale = Mathf.Abs(scale.y);
            var radius = capsule.radius * radiusScale;
            var halfSegment = Mathf.Max(
                0, capsule.height * axisScale * .5f - radius);
            var center = pose.position + pose.rotation *
                         Vector3.Scale(capsule.center, scale);
            var axis = pose.rotation * Vector3.up;
            var overlaps = new Collider[32];
            var count = Physics.OverlapCapsuleNonAlloc(
                center - axis * halfSegment,
                center + axis * halfSegment,
                Mathf.Max(.01f, radius - .01f),
                overlaps,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (var index = 0; index < count; index++)
            {
                var other = overlaps[index];
                if (other != null &&
                    !other.transform.IsChildOf(capsule.transform))
                    return false;
            }
            return true;
        }

        private Task<RoomMapSnapshot> LoadSimulationAsync(CancellationToken cancellationToken)
        {
            var provider = new PicoMockRoomMapProvider(simulatedSceneData);
            return provider.LoadAsync(cancellationToken);
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
