using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Gameplay;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Networking;
using UnityEngine.XR.Interaction.Toolkit;

namespace MateSoul.Runtime
{
    public sealed class GameItem : MonoBehaviour
    {
        private ItemRegistry registry;
        private GameSession session;
        private ItemDefinition definition;
        private readonly Dictionary<Renderer, Material[]> baseMaterials =
            new Dictionary<Renderer, Material[]>();
        private PhysicsGrabInteractable grab;
        private NavMeshObstacle obstacle;
        private Pose stablePose;
        public PhysicsBody Body { get; private set; }
        public ItemInstance Instance { get; private set; }
        public Vector3 ApproachPosition => transform.TransformPoint(definition.ApproachOffset);
        public ItemDefinition Definition => definition;
        public bool IsHeld { get; private set; }
        internal GameSession Session => session;

        public void Bind(ItemDefinition content, ItemRegistry owner, GameSession gameSession,
            string instanceId, string stateId = "default")
        {
            definition = content;
            registry = owner;
            session = gameSession;
            Instance = new ItemInstance(instanceId, content.Id, content.DisplayName, content.Capabilities, stateId);
            registry.Register(Instance);
            stablePose = new Pose(transform.position, transform.rotation);
            obstacle = GetComponent<NavMeshObstacle>();

            Body = GetComponent<PhysicsBody>() ?? gameObject.AddComponent<PhysicsBody>();
            Body.Configure(content.Physics);
            Body.Place(new Pose(transform.position, transform.rotation));
            grab = GetComponent<PhysicsGrabInteractable>() ?? gameObject.AddComponent<PhysicsGrabInteractable>();
            grab.useDynamicAttach = true;
            grab.throwOnDetach = false;
            grab.colliders.Clear();
            foreach (var itemCollider in Body.Shapes) grab.colliders.Add(itemCollider);
            grab.hoverEntered.AddListener(OnHoverEntered);
            grab.hoverExited.AddListener(OnHoverExited);
            grab.selectEntered.AddListener(OnSelected);
            grab.selectExited.AddListener(OnReleased);
            if (content.Breakable.enabled)
            {
                var breakable = GetComponent<BreakableItem>() ??
                                gameObject.AddComponent<BreakableItem>();
                breakable.Configure(this, content.Breakable);
            }

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                baseMaterials.Add(renderer, renderer.sharedMaterials);
#region debug-point C:grab-bind
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var bindPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                              $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                              $"\"location\":\"GameItem.Bind\",\"msg\":\"[DEBUG] grab interactable bound\"," +
                              $"\"data\":{{\"id\":\"{Instance.Id}\",\"definition\":\"{definition.Id}\"," +
                              $"\"grabEnabled\":{grab.isActiveAndEnabled.ToString().ToLowerInvariant()}," +
                              $"\"manager\":{(grab.interactionManager != null).ToString().ToLowerInvariant()}," +
                              $"\"colliders\":{GetComponentsInChildren<Collider>(true).Length}," +
                              $"\"renderers\":{baseMaterials.Count}" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", bindPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        public void SetFocused(bool value)
        {
            if (definition == null || definition.FocusMaterial == null || IsHeld) value = false;
#region debug-point B:focus-material
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var shaderName = (definition?.FocusMaterial?.shader == null
                ? "none"
                : definition.FocusMaterial.shader.name).Replace("\"", "'");
            var focusPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"B\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.SetFocused\",\"msg\":\"[DEBUG] focus material apply\"," +
                               $"\"data\":{{\"id\":\"{Instance?.Id ?? "none"}\"," +
                               $"\"requested\":{value.ToString().ToLowerInvariant()}," +
                               $"\"held\":{IsHeld.ToString().ToLowerInvariant()}," +
                               $"\"renderers\":{baseMaterials.Count},\"shader\":\"{shaderName}\"" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", focusPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
            foreach (var entry in baseMaterials)
            {
                if (entry.Key == null) continue;
                entry.Key.sharedMaterials = value
                    ? entry.Value.Concat(new[] { definition.FocusMaterial }).ToArray()
                    : entry.Value;
            }
        }

        public ItemSaveData Capture(Transform anchor)
        {
            // Holding uses the last settled checkpoint. Free bodies retain their actual
            // pose; interrupted falls resume under gravity after a validated restore.
            var pose = IsHeld ? stablePose : Body.Pose;
            var position = anchor.InverseTransformPoint(pose.position);
            var rotation = Quaternion.Inverse(anchor.rotation) * pose.rotation;
            return new ItemSaveData
            {
                instanceId = Instance.Id, definitionId = Instance.DefinitionId, stateId = Instance.StateId,
                needsGravitySettling = !Body.HasSupport(),
                x = position.x, y = position.y, z = position.z,
                qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w
            };
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
#region debug-point C:grab-hover-enter
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var hoverPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.OnHoverEntered\",\"msg\":\"[DEBUG] grab hover entered\"," +
                               $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                               $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", hoverPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
#region debug-point C:grab-hover-exit
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var hoverPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                               $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                               $"\"location\":\"GameItem.OnHoverExited\",\"msg\":\"[DEBUG] grab hover exited\"," +
                               $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                               $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", hoverPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
#region debug-point C:grab-select
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var rayInteractor = args.interactorObject as XRRayInteractor;
            var cameraTransform = Camera.main == null ? null : Camera.main.transform;
            var selectPayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                $"\"location\":\"GameItem.OnSelected\",\"msg\":\"[DEBUG] grab select entered\"," +
                                $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                                $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"," +
                                $"\"forceGrab\":{(rayInteractor != null && rayInteractor.useForceGrab).ToString().ToLowerInvariant()}," +
                                $"\"cameraDistance\":{(cameraTransform == null ? -1f : Vector3.Distance(transform.position, cameraTransform.position))}" +
                                "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", selectPayload, "application/json")
                .SendWebRequest();
#endif
#endregion
            IsHeld = true;
            SetFocused(false);
            if (obstacle != null) obstacle.enabled = false;
        }

        private void OnReleased(SelectExitEventArgs args)
        {
#region debug-point C:grab-release
#if MATESOUL_ITEM_FOCUS_DIAGNOSTICS && DEVELOPMENT_BUILD && !UNITY_EDITOR
            var releasePayload = $"{{\"sessionId\":\"item-focus-interaction\",\"runId\":\"post-fix\"," +
                                 $"\"hypothesisId\":\"C\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                                 $"\"location\":\"GameItem.OnReleased\",\"msg\":\"[DEBUG] grab select exited\"," +
                                 $"\"data\":{{\"id\":\"{Instance.Id}\"," +
                                 $"\"interactor\":\"{args.interactorObject.transform.name.Replace("\"", "'")}\"" + "}}";
            UnityWebRequest.Post("http://10.0.2.2:7782/event", releasePayload, "application/json")
                .SendWebRequest();
#endif
#endregion
            IsHeld = false;
        }

        private void Update()
        {
            if (Body == null || Body.IsPaused) return;
            var settled = Body.IsSettled && !IsHeld;
            if (obstacle != null) obstacle.enabled = settled;
            if (!settled) return;
            var pose = Body.Pose;
            if (Vector3.Distance(pose.position, stablePose.position) < .005f &&
                Quaternion.Angle(pose.rotation, stablePose.rotation) < 1f) return;
            if (!Body.HasSupport()) return;
            stablePose = pose;
            session?.RequestPhysicsSave();
        }

        public void SetPaused(bool paused)
        {
            if (paused) grab.CancelGrab();
            Body.SetPaused(paused);
            grab.enabled = !paused;
        }

        public void Retire()
        {
            grab.CancelGrab();
            gameObject.SetActive(false);
        }

        internal bool TryBreak(BreakSeverity severity)
            => session != null && Instance != null && session.BreakItem(Instance.Id, severity);

        private void OnDestroy()
        {
            if (grab != null)
            {
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
                grab.selectEntered.RemoveListener(OnSelected);
                grab.selectExited.RemoveListener(OnReleased);
            }
            if (Instance != null && registry != null) registry.Remove(Instance.Id);
        }
    }
}
