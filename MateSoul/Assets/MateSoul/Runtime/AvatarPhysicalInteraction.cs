using System;
using System.Collections.Generic;
using System.Linq;
using MateSoul.Core;
using UnityEngine;

namespace MateSoul.Runtime
{
    public enum AvatarBodyRegion { Head, Torso, UpperArm, Forearm, Hand, UpperLeg, LowerLeg, Foot }
    public enum AvatarBodySide { Center, Left, Right }

    [DisallowMultipleComponent]
    public sealed class AvatarBodyPartCollider : MonoBehaviour
    {
        public AvatarBodyRegion Region { get; private set; }
        public AvatarBodySide Side { get; private set; }
        public Collider Shape { get; private set; }

        internal void Configure(AvatarBodyRegion region, AvatarBodySide side, Collider shape)
        {
            Region = region;
            Side = side;
            Shape = shape;
            shape.contactOffset = .002f;
        }
    }

    // Animation establishes the pose. Each registered reaction owns only its affected channel.
    [DefaultExecutionOrder(11050), DisallowMultipleComponent]
    public sealed class AvatarPhysicalResponse : MonoBehaviour
    {
        private readonly List<AvatarBodyPartCollider> bodyParts = new List<AvatarBodyPartCollider>();
        private readonly Dictionary<Transform, List<Vector3>> skin = new Dictionary<Transform, List<Vector3>>();
        private readonly List<JointSpring>[] groups =
            { new List<JointSpring>(), new List<JointSpring>(), new List<JointSpring>() };
        private readonly ActionHandle[] reactions = new ActionHandle[3];
        private readonly bool[] owned = new bool[3];
        private readonly float[] lastContact = new float[3];
        private static readonly ControlChannels[] Channels =
            { ControlChannels.Body, ControlChannels.LeftArm, ControlChannels.RightArm };
        private static readonly string[] Ids = { "physical_body", "physical_left_arm", "physical_right_arm" };
        private GameSession session;
        private AvatarMotor motor;
        private Dictionary<string, Transform> bones;
        private bool initialized;
        private float strongContactSeconds, lastStrongContact, nextStepTime;
        private ActionHandle stepAction;
        private Pose leftFootAnchor, rightFootAnchor;
        private Vector3 stepOrigin;

        public IReadOnlyList<AvatarBodyPartCollider> BodyParts => bodyParts;

        public static AvatarPhysicalResponse Install(GameObject avatar)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            var response = avatar.GetComponent<AvatarPhysicalResponse>() ??
                avatar.AddComponent<AvatarPhysicalResponse>();
            response.Initialize();
            return response;
        }

        public void Bind(AvatarMotor owner) => motor = owner ?? throw new ArgumentNullException(nameof(owner));

        public void BindSession(GameSession owner)
        {
            session = owner ?? throw new ArgumentNullException(nameof(owner));
            for (var group = 0; group < 3; group++)
            {
                var captured = group;
                session.Catalog.Register(new ActionDefinition(
                    Ids[group], "Respond to local hand contact", Channels[group],
                    _ => new ReactionAction(this, captured), timeoutSeconds: 3600,
                    exposeToAI: false, exitTimeoutSeconds: 1));
            }
            session.Catalog.Register(new ActionDefinition("physical_balance_step",
                "Take one small balance step", ControlChannels.Locomotion,
                intent => new BalanceStepAction(this, (Vector3)intent.Payload),
                timeoutSeconds: 2, exposeToAI: false, exitTimeoutSeconds: 1));
        }

        private void Initialize()
        {
            if (initialized) return;
            InteractionPhysicsLayers.Configure();
            var animator = GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException("Physical response requires an Animator.");
            bones = animator.GetComponentsInChildren<Transform>(true)
                .GroupBy(item => item.name).ToDictionary(group => group.Key, group => group.First());
            // Face is a renderer at the character root; Head 1 is the actual moving head bone.
            foreach (var name in new[] { "lengyue_hips", "lengyue_spine", "lengyue_chest", "lengyue_neck", "lengyue_head_01",
                "lengyue_upper_arm_l", "lengyue_forearm_l", "lengyue_hand_l", "lengyue_thigh_l", "lengyue_shin_l", "lengyue_foot_l", "lengyue_toe_l",
                "lengyue_upper_arm_r", "lengyue_forearm_r", "lengyue_hand_r", "lengyue_thigh_r", "lengyue_shin_r", "lengyue_foot_r", "lengyue_toe_r" })
                if (!bones.ContainsKey(name))
                    throw new InvalidOperationException($"Physical response bone is missing: {name}.");
            var locomotion = GetComponent<CapsuleCollider>();
            if (locomotion == null) throw new InvalidOperationException("Physical response requires a locomotion capsule.");
            locomotion.gameObject.layer = InteractionPhysicsLayers.AvatarLocomotion;
            // A previous attempt may have failed midway; reused parts are reconfigured below.
            bodyParts.Clear();
            foreach (var group in groups) group.Clear();
            CacheSkin();
            AddTorso(bones["lengyue_hips"], bones["lengyue_spine"]);
            AddTorso(bones["lengyue_spine"], bones["lengyue_chest"]);
            AddTorso(bones["lengyue_chest"], bones["lengyue_neck"]);
            AddFittedBox(bones["lengyue_head_01"], AvatarBodyRegion.Head, AvatarBodySide.Center,
                new Vector3(.13f, .18f, .14f));
            AddSide(AvatarBodySide.Left, "_l", 1);
            AddSide(AvatarBodySide.Right, "_r", 2);
            AddSpring(0, "lengyue_spine", 5f);
            AddSpring(0, "lengyue_chest", 5f);
            AddSpring(0, "lengyue_neck", 4f);
            AddSpring(0, "lengyue_head_01", 4f);
            foreach (var socket in GetComponentsInChildren<HandHoldInteractable>(true))
                socket.Configure(socket.Side, GetComponent<HandIKRig>());
            var body = GetComponent<PhysicsBody>();
            if (body == null) throw new InvalidOperationException("Physical response requires PhysicsBody.");
            body.RefreshColliders();
            skin.Clear();
            initialized = true;
        }

        private void CacheSkin()
        {
            skin.Clear();
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Fit anatomical contact to the skin, avoiding hair, skirt and loose sleeves.
                if (renderer.name != "lengyue_body_root" && renderer.name != "lengyue_face_root") continue;
                var mesh = new Mesh();
                try
                {
                    renderer.BakeMesh(mesh);
                    var vertices = mesh.vertices;
                    var weights = renderer.sharedMesh.boneWeights;
                    var joints = renderer.bones;
                    for (var index = 0; index < vertices.Length; index++)
                    {
                        var weight = weights[index];
                        var point = renderer.transform.TransformPoint(vertices[index]);
                        Add(weight.boneIndex0, weight.weight0, point);
                        Add(weight.boneIndex1, weight.weight1, point);
                        Add(weight.boneIndex2, weight.weight2, point);
                        Add(weight.boneIndex3, weight.weight3, point);
                    }
                    void Add(int index, float weight, Vector3 point)
                    {
                        if (weight < .22f || index >= joints.Length || joints[index] == null) return;
                        if (!skin.TryGetValue(joints[index], out var points))
                            skin.Add(joints[index], points = new List<Vector3>());
                        points.Add(point);
                    }
                }
                finally
                {
                    if (Application.isPlaying) Destroy(mesh);
                    else DestroyImmediate(mesh);
                }
            }
        }

        private void AddSide(AvatarBodySide side, string suffix, int group)
        {
            AddLimb(bones["lengyue_upper_arm" + suffix], bones["lengyue_forearm" + suffix], .03f,
                AvatarBodyRegion.UpperArm, side);
            AddLimb(bones["lengyue_forearm" + suffix], bones["lengyue_hand" + suffix], .025f,
                AvatarBodyRegion.Forearm, side);
            AddFittedBox(bones["lengyue_hand" + suffix], AvatarBodyRegion.Hand, side,
                new Vector3(.055f, .023f, .075f));
            foreach (var finger in new[] { "lengyue_thumb", "lengyue_index", "lengyue_middle", "lengyue_ring", "lengyue_little" })
            {
                for (var segment = 1; segment < 3; segment++)
                {
                    var start = bones[finger + "_0" + segment + suffix];
                    var end = bones[finger + "_0" + (segment + 1) + suffix];
                    var delta = end.position - start.position;
                    var shape = AddCollider<CapsuleCollider>(Part(start, (start.position + end.position) * .5f,
                        Quaternion.FromToRotation(Vector3.forward, delta.normalized),
                        AvatarBodyRegion.Hand, side));
                    shape.direction = 2;
                    shape.radius = .0055f;
                    shape.height = Mathf.Max(.011f, delta.magnitude);
                    Finish(shape, AvatarBodyRegion.Hand, side);
                }
                AddFittedBox(bones[finger + "_03" + suffix], AvatarBodyRegion.Hand,
                    side, new Vector3(.01f, .012f, .014f));
            }
            AddLimb(bones["lengyue_thigh" + suffix], bones["lengyue_shin" + suffix], .055f,
                AvatarBodyRegion.UpperLeg, side);
            AddLimb(bones["lengyue_shin" + suffix], bones["lengyue_foot" + suffix], .035f,
                AvatarBodyRegion.LowerLeg, side);
            AddFittedBox(bones["lengyue_foot" + suffix], AvatarBodyRegion.Foot, side,
                new Vector3(.075f, .05f, .16f));
            AddSpring(group, "lengyue_upper_arm" + suffix, 9f);
            AddSpring(group, "lengyue_forearm" + suffix, 12f);
            AddSpring(group, "lengyue_hand" + suffix, 10f);
            AddSpring(0, "lengyue_thigh" + suffix, 4f);
            AddSpring(0, "lengyue_shin" + suffix, 6f);
        }

        private void AddSpring(int group, string bone, float limit)
        {
            var joint = bones[bone];
            var spring = new JointSpring(joint, limit);
            if (bone.StartsWith("lengyue_forearm") || bone.StartsWith("lengyue_shin"))
            {
                var suffix = bone.EndsWith("_l") ? "_l" : "_r";
                var upper = bones[(bone.StartsWith("lengyue_forearm") ? "lengyue_upper_arm" : "lengyue_thigh") + suffix];
                var end = bones[(bone.StartsWith("lengyue_forearm") ? "lengyue_hand" : "lengyue_foot") + suffix];
                var first = joint.position - upper.position;
                var second = end.position - joint.position;
                var axis = Vector3.Cross(first.normalized, second.normalized);
                if (axis.sqrMagnitude < .0001f) axis = transform.right;
                spring.HingeAxis = joint.InverseTransformDirection(axis.normalized);
                spring.ExtensionLimit = Mathf.Min(4f, Vector3.Angle(first, second));
            }
            groups[group].Add(spring);
        }

        private void AddTorso(Transform start, Transform end)
        {
            var height = Vector3.Distance(start.position, end.position);
            // Narrow horizontal rounded bands follow the varying waist/chest silhouette.
            for (var band = 0; band < Mathf.Max(1, Mathf.CeilToInt(height / .05f)); band++)
            {
                var count = Mathf.Max(1, Mathf.CeilToInt(height / .05f));
                var center = Vector3.Lerp(start.position, end.position, (band + .5f) / count);
                var points = Points(start, end).Where(p =>
                    Mathf.Abs(Vector3.Dot(p - center, transform.up)) < height / count * .7f).ToList();
                var local = points.Select(p => transform.InverseTransformDirection(p - center)).ToList();
                var width = Percentile(local.Select(p => Mathf.Abs(p.x)), .9f, .10f);
                var depth = Percentile(local.Select(p => Mathf.Abs(p.z)), .8f, .065f);
                var thickness = Mathf.Clamp(depth - .004f, .035f, .10f);
                // A flattened box inset into each skin band avoids the old 14–17 cm radii.
                var shape = AddCollider<BoxCollider>(Part(start, center, transform.rotation,
                    AvatarBodyRegion.Torso, AvatarBodySide.Center, "Band" + band));
                shape.size = new Vector3(Mathf.Clamp(width * 2 - .008f, .09f, .32f),
                    height / count * .96f, thickness * 2);
                Finish(shape, AvatarBodyRegion.Torso, AvatarBodySide.Center);
            }
        }

        private void AddLimb(Transform start, Transform end, float fallbackRadius,
            AvatarBodyRegion region, AvatarBodySide side)
        {
            var delta = end.position - start.position;
            var axis = delta.normalized;
            var rotation = Quaternion.FromToRotation(Vector3.forward, axis);
            var points = Points(start, end).ToArray();
            for (var band = 0; band < 3; band++)
            {
                var center = Vector3.Lerp(start.position, end.position, (band + .5f) / 3f);
                var radii = points.Where(p =>
                    Mathf.Abs(Vector3.Dot(p - center, axis)) < delta.magnitude / 4f)
                    .Select(p => Vector3.ProjectOnPlane(p - center, axis).magnitude);
                var radius = Mathf.Clamp(Percentile(radii, .7f, fallbackRadius) - .003f,
                    .012f, fallbackRadius * 1.3f);
                var shape = AddCollider<CapsuleCollider>(Part(start, center, rotation,
                    region, side, "Band" + band));
                shape.direction = 2;
                shape.radius = radius;
                shape.height = Mathf.Max(radius * 2, delta.magnitude / 3f + radius * .5f);
                Finish(shape, region, side);
            }
        }

        private void AddFittedBox(Transform bone, AvatarBodyRegion region,
            AvatarBodySide side, Vector3 fallback)
        {
            var points = Points(bone).Select(bone.InverseTransformPoint).ToList();
            var bounds = new Bounds(Vector3.zero, fallback);
            if (points.Count > 8)
            {
                var minimum = new Vector3(Quantile(points.Select(p => p.x), .06f),
                    Quantile(points.Select(p => p.y), .06f), Quantile(points.Select(p => p.z), .06f));
                var maximum = new Vector3(Quantile(points.Select(p => p.x), .94f),
                    Quantile(points.Select(p => p.y), .94f), Quantile(points.Select(p => p.z), .94f));
                bounds = new Bounds((minimum + maximum) * .5f, maximum - minimum);
            }
            var shape = AddCollider<BoxCollider>(Part(bone, bone.TransformPoint(bounds.center), bone.rotation,
                region, side));
            shape.size = Vector3.Max(bounds.size - Vector3.one * .006f, Vector3.one * .012f);
            Finish(shape, region, side);
        }

        private IEnumerable<Vector3> Points(params Transform[] targets)
        {
            foreach (var target in targets)
                if (skin.TryGetValue(target, out var points))
                    foreach (var point in points) yield return point;
        }

        private static float Percentile(IEnumerable<float> source, float percentile, float fallback)
        {
            var values = source.OrderBy(value => value).ToArray();
            return values.Length < 8 ? fallback : values[Mathf.RoundToInt((values.Length - 1) * percentile)];
        }

        private static float Quantile(IEnumerable<float> source, float percentile)
            => Percentile(source, percentile, 0);

        private GameObject Part(Transform bone, Vector3 position, Quaternion rotation,
            AvatarBodyRegion region, AvatarBodySide side, string variant = null)
        {
            var partName = $"{region} Contact {side}" +
                           (string.IsNullOrEmpty(variant) ? "" : " " + variant);
            // Initialisation can run twice (enable/re-init). Reuse the existing part
            // so the skeleton never carries duplicate bone names, which breaks the
            // outfit swap that maps bones by name.
            var existing = bone.Find(partName);
            if (existing != null)
            {
                existing.SetPositionAndRotation(position, rotation);
                return existing.gameObject;
            }
            var root = new GameObject(partName);
            root.layer = InteractionPhysicsLayers.AvatarDetail;
            root.transform.SetParent(bone, false);
            root.transform.SetPositionAndRotation(position, rotation);
            return root;
        }

        private static T AddCollider<T>(GameObject part) where T : Collider
        {
            var collider = part.GetComponent<T>();
            return collider != null ? collider : part.AddComponent<T>();
        }

        private void Finish(Collider shape, AvatarBodyRegion region, AvatarBodySide side)
        {
            if (shape == null) return;
            var marker = shape.GetComponent<AvatarBodyPartCollider>();
            if (marker == null)
                marker = shape.gameObject.AddComponent<AvatarBodyPartCollider>();
            marker.Configure(region, side, shape);
            if (!bodyParts.Contains(marker)) bodyParts.Add(marker);
        }

        public void ReceiveContact(AvatarBodyPartCollider part, Vector3 direction,
            float approachSpeed, float penetration, Vector3 point)
        {
            if (part == null || session == null || session.Paused || motor == null ||
                motor.Body.IsPaused || !isActiveAndEnabled || approachSpeed < .035f ||
                penetration < .001f)
                return;
            var arm = part.Region == AvatarBodyRegion.UpperArm ||
                part.Region == AvatarBodyRegion.Forearm || part.Region == AvatarBodyRegion.Hand;
            var group = arm ? (part.Side == AvatarBodySide.Left ? 1 : 2) : 0;
            if (!EnsureReaction(group)) return;
            lastContact[group] = Time.time;
            var strength = Mathf.Lerp(1.2f, 6f, Mathf.InverseLerp(.035f, 1.1f, approachSpeed));
            foreach (var spring in groups[group])
            {
                var influence = 1f;
                if (arm)
                    influence = spring.Bone == part.transform.parent ||
                        part.Region == AvatarBodyRegion.Hand &&
                        spring.Bone == bones[part.Side == AvatarBodySide.Left ? "lengyue_hand_l" : "lengyue_hand_r"]
                        ? 1 : .35f;
                else if (spring.Bone.name.StartsWith("lengyue_thigh") || spring.Bone.name.StartsWith("lengyue_shin"))
                    influence = (part.Region == AvatarBodyRegion.UpperLeg ||
                        part.Region == AvatarBodyRegion.LowerLeg || part.Region == AvatarBodyRegion.Foot) &&
                        spring.Bone.name.EndsWith(part.Side == AvatarBodySide.Left ? "_l" : "_r") ? .8f : 0;
                else if (part.Region == AvatarBodyRegion.Head)
                    influence = spring.Bone == bones["lengyue_head_01"] ? 1 : .3f;
                else if (spring.Bone == bones["lengyue_head_01"] || spring.Bone == bones["lengyue_neck"])
                    influence = .2f;
                var lever = point - spring.Bone.position;
                if (lever.sqrMagnitude < .0001f) lever = transform.up * .1f;
                var torque = Vector3.Cross(lever.normalized, direction);
                spring.Target = Vector3.ClampMagnitude(
                    spring.Bone.InverseTransformDirection(torque) * (strength * influence), spring.Limit);
            }
            if (arm && EnsureReaction(0))
            {
                lastContact[0] = Time.time;
                foreach (var spring in groups[0])
                    if (spring.Bone == bones["lengyue_chest"])
                        spring.Target = spring.Bone.InverseTransformDirection(
                            Vector3.Cross(transform.up, direction)) * (strength * .18f);
            }
            if (part.Region != AvatarBodyRegion.Torso || approachSpeed < .65f || penetration < .012f)
                return;
            if (Time.time - lastStrongContact > Time.fixedDeltaTime * 2.5f)
                strongContactSeconds = 0;
            lastStrongContact = Time.time;
            strongContactSeconds += Time.fixedDeltaTime;
            if (strongContactSeconds < .14f || Time.time < nextStepTime ||
                !owned[0] ||
                session.Avatar.HasClaim(ControlChannels.Locomotion))
                return;
            var horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude < .01f) return;
            var distance = Mathf.Lerp(.04f, .09f, Mathf.InverseLerp(.65f, 1.8f, approachSpeed));
            stepAction = session.Submit(new AvatarIntent("physical_balance_step", IntentSource.Contact,
                payload: horizontal.normalized * distance));
            if (stepAction.State != ActionState.Rejected)
            {
                nextStepTime = Time.time + 1.1f;
                strongContactSeconds = 0;
            }
        }

        private bool EnsureReaction(int group)
        {
            if (reactions[group] != null && !reactions[group].IsFinished) return true;
            if (session.Avatar.HasClaim(Channels[group]))
                foreach (var action in session.Avatar.Capture())
                    if (session.Catalog.TryGet(action.action_id, out var definition) &&
                        (definition.Channels & Channels[group]) != 0 &&
                        action.source != "ambient" && action.source != "autonomous")
                        return false;
            // Physical contact can interrupt idle social attention. Explicit gameplay
            // and the selected arm's hand-hold action retain their resources.
            reactions[group] = session.Submit(new AvatarIntent(Ids[group], IntentSource.Contact));
            return !reactions[group].IsFinished;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            for (var group = 0; group < 3; group++)
                if (owned[group])
                    foreach (var spring in groups[group]) spring.Tick(Time.deltaTime);
            if (owned[0] && motor != null && motor.IsPhysicalStepActive)
                ApplyBalanceFeet();
        }

        private void CaptureBalanceFeet()
        {
            stepOrigin = motor.Position;
            leftFootAnchor = new Pose(bones["lengyue_foot_l"].position, bones["lengyue_foot_l"].rotation);
            rightFootAnchor = new Pose(bones["lengyue_foot_r"].position, bones["lengyue_foot_r"].rotation);
        }

        private void ApplyBalanceFeet()
        {
            var progress = motor.PhysicalStepProgress;
            var displacement = motor.Position - stepOrigin;
            // Transfer support once per foot; the opposite foot stays planted in world space.
            ApplyFoot("_l", leftFootAnchor, Mathf.Clamp01(progress * 2), displacement);
            ApplyFoot("_r", rightFootAnchor, Mathf.Clamp01(progress * 2 - 1), displacement);
        }

        private void ApplyFoot(string suffix, Pose anchor, float progress, Vector3 displacement)
        {
            var target = anchor.position + displacement * Mathf.SmoothStep(0, 1, progress);
            target.y += Mathf.Sin(progress * Mathf.PI) * .025f;
            AvatarSeatingRig.Solve(bones["lengyue_thigh" + suffix], bones["lengyue_shin" + suffix],
                bones["lengyue_foot" + suffix], target, transform.forward);
            bones["lengyue_foot" + suffix].rotation = anchor.rotation;
        }

        private sealed class ReactionAction : IAvatarAction
        {
            private readonly AvatarPhysicalResponse owner;
            private readonly int group;
            private bool ending;
            public ReactionAction(AvatarPhysicalResponse owner, int group) { this.owner = owner; this.group = group; }
            public string Check() => owner.isActiveAndEnabled ? null : "Physical response is disabled.";
            public void Start() { owner.owned[group] = true; owner.lastContact[group] = Time.time; }
            public ActionProgress Tick(float deltaSeconds)
            {
                var stepping = group == 0 && owner.motor.IsPhysicalStepActive;
                return ending || !stepping && Time.time - owner.lastContact[group] > .85f
                    ? ActionProgress.Succeeded : ActionProgress.Running;
            }
            public void Cancel(ActionStopReason reason) => ending = true;
            public void Dispose()
            {
                owner.owned[group] = false;
                foreach (var spring in owner.groups[group]) spring.Reset();
                if (group == 0) owner.motor?.EndPhysicalStep();
            }
        }

        private sealed class BalanceStepAction : IAvatarAction
        {
            private readonly AvatarPhysicalResponse owner;
            private readonly Vector3 offset;
            private bool started, ending;
            public BalanceStepAction(AvatarPhysicalResponse owner, Vector3 offset)
            { this.owner = owner; this.offset = offset; }
            public string Check() => owner.owned[0] && owner.motor.ContactPosture == null &&
                !owner.motor.Body.IsPaused ? null : "Balance step is unavailable.";
            public void Start()
            {
                owner.CaptureBalanceFeet();
                owner.motor.BeginPhysicalStep(offset);
                started = true;
            }
            public ActionProgress Tick(float deltaSeconds)
                => ending || !owner.motor.IsPhysicalStepActive ? ActionProgress.Succeeded : ActionProgress.Running;
            public void Cancel(ActionStopReason reason) => ending = true;
            public void Dispose() { if (started) owner.motor.EndPhysicalStep(); }
        }

        private sealed class JointSpring
        {
            public readonly Transform Bone;
            public readonly float Limit;
            public Vector3 Target;
            public Vector3 HingeAxis;
            public float ExtensionLimit;
            private Vector3 angle, velocity;
            private Quaternion baseline, result;
            private bool applied;
            public JointSpring(Transform bone, float limit) { Bone = bone; Limit = limit; }
            public void Tick(float delta)
            {
                var remaining = Mathf.Min(delta, .05f);
                while (remaining > 0)
                {
                    var dt = Mathf.Min(remaining, 1f / 120f);
                    if (HingeAxis.sqrMagnitude > .5f)
                        Target = HingeAxis * Mathf.Clamp(
                            Vector3.Dot(Target, HingeAxis), -ExtensionLimit, Limit);
                    velocity += (Target * 169f - angle * 169f - velocity * 23f) * dt;
                    angle = Vector3.ClampMagnitude(angle + velocity * dt, Limit);
                    if (HingeAxis.sqrMagnitude > .5f)
                    {
                        var value = Vector3.Dot(angle, HingeAxis);
                        var bounded = Mathf.Clamp(value, -ExtensionLimit, Limit);
                        angle = HingeAxis * bounded;
                        if (value != bounded) velocity = Vector3.zero;
                    }
                    Target *= Mathf.Exp(-10f * dt);
                    remaining -= dt;
                }
                var observed = Bone.localRotation;
                baseline = applied && Quaternion.Angle(observed, result) < .01f ? baseline : observed;
                result = baseline * Quaternion.Euler(angle);
                Bone.localRotation = result;
                applied = true;
            }
            public void Reset()
            {
                if (applied && Quaternion.Angle(Bone.localRotation, result) < .01f)
                    Bone.localRotation = baseline;
                applied = false;
                Target = angle = velocity = Vector3.zero;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                foreach (var reaction in reactions) session.Avatar.Cancel(reaction);
                session.Avatar.Cancel(stepAction);
            }
            foreach (var springs in groups)
                foreach (var spring in springs) spring.Reset();
            Array.Clear(owned, 0, owned.Length);
            strongContactSeconds = 0;
            motor?.EndPhysicalStep();
        }
    }
}
