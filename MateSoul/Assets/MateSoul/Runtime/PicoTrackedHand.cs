using System;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    /// <summary>One validated tracking sample shared by grabbing, poking and hand visibility.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class PicoTrackedHand : MonoBehaviour
    {
        private HandType handType;
        private HandJointLocations joints;
        private Renderer[] renderers;
        private Material material;
        private PXR_Hand visualHand;
        private bool visible;
        private bool suppressGraspUntilOpen;
        private readonly Pose[] jointPoses = new Pose[(int)HandJoint.JointMax];
        private readonly float[] jointRadii = new float[(int)HandJoint.JointMax];
        private readonly bool[] jointValid = new bool[(int)HandJoint.JointMax];
        private Vector3 previousPalmPosition;
        private Quaternion previousPalmRotation;
        private bool hadPalm;
        private readonly float[] fingerCurls = new float[3];
        private int validCurlMask;
#region debug-point A-B:eyehand-pointer-stall
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private float debugNextHandTrace;
        private int debugHandMask = -1;
#endif
#endregion
        public bool Tracked { get; private set; }
        public bool TipTracked { get; private set; }
        public Pose Palm { get; private set; }
        public Pose Tip { get; private set; }
        public float Grip { get; private set; }
        public float WorldGrip { get; private set; }
        public float PinchStrength { get; private set; }
        public bool Closed { get; private set; }
        public bool IsPinching { get; private set; }
        public bool NativeTracked { get; private set; }
        public bool NativePinching { get; private set; }
        public bool NativeGrasping { get; private set; }
        public PicoDirectHandInteractor Direct { get; private set; }
        public PicoHandPoke Poke { get; private set; }
        public TrackedHandPhysics PhysicsProxy { get; private set; }
        public Vector3 PalmVelocity { get; private set; }
        public Vector3 PalmAngularVelocity { get; private set; }
        public bool MotionValid { get; private set; }
        public PicoHandContactVisual ContactVisual { get; private set; }
        public bool IsLeft => handType == HandType.HandLeft;

        public void Configure(bool left, UnityEngine.XR.Interaction.Toolkit.XRInteractionManager manager,
            PlayerInteractionModeController mode)
        {
            handType = left ? HandType.HandLeft : HandType.HandRight;
            var prefab = Resources.Load<GameObject>(left ? "Prefabs/HandLeft" : "Prefabs/HandRight");
            if (prefab == null) throw new InvalidOperationException("PICO official hand prefab is missing.");
            var visual = Instantiate(prefab, transform);
            visualHand = visual.GetComponent<PXR_Hand>();
            visualHand.enabled = false;
            // Strip the ray only on our instance; the SDK prefab remains untouched.
            var ray = visual.transform.Find("RayPose");
            if (ray != null)
            {
                ray.gameObject.SetActive(false);
                Destroy(ray.gameObject);
            }
            renderers = visual.GetComponentsInChildren<Renderer>(true);
            var shader = Resources.Load<Shader>("MateSoulHands/TrackedHand");
            if (shader == null) throw new InvalidOperationException("Tracked hand shader is missing.");
            material = new Material(shader) { name = left ? "Left Virtual Hand" : "Right Virtual Hand" };
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial = material;
                renderer.enabled = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var grab = new GameObject("Palm Grab");
            grab.transform.SetParent(transform, false);
            Direct = grab.AddComponent<PicoDirectHandInteractor>();
            Direct.Configure(this, manager, mode);
            var poke = new GameObject("Index Touch");
            poke.transform.SetParent(transform, false);
            Poke = poke.AddComponent<PicoHandPoke>();
            Poke.Configure(this);
            PhysicsProxy = gameObject.AddComponent<TrackedHandPhysics>();
            PhysicsProxy.Configure(this);
            ContactVisual = visual.AddComponent<PicoHandContactVisual>();
            ContactVisual.Configure(this, visualHand);
        }

        private void Update()
        {
            Tracked = TipTracked = false;
            NativeTracked = NativePinching = NativeGrasping = false;
            Grip = 0;
            PalmVelocity = Vector3.zero;
            PalmAngularVelocity = Vector3.zero;
            MotionValid = false;
            Array.Clear(jointValid, 0, jointValid.Length);
            var pinch = 0f;
#if UNITY_ANDROID && !UNITY_EDITOR
            var device = handType == HandType.HandLeft ? PicoHandInteraction.left : PicoHandInteraction.right;
            NativeTracked = device != null && device.isTracked.isPressed;
            NativePinching = NativeTracked && device.pinchTouched.isPressed;
            NativeGrasping = NativeTracked && device.graspFirm.isPressed;
            if (NativeTracked &&
                PXR_HandTracking.GetJointLocations(handType, ref joints) && joints.isActive != 0 &&
                CacheJoints() &&
                TryGetJoint(HandJoint.JointPalm, out var palm, out _))
            {
                Tracked = true;
                Palm = palm;
                UpdatePalmMotion(Time.unscaledDeltaTime);
                var value = device.graspValue.ReadValue();
                Grip = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
                value = device.pinchValue.ReadValue();
                pinch = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
                if (TryGetJoint(HandJoint.JointIndexTip, out var tip, out _) &&
                    TryGetJoint(HandJoint.JointIndexDistal, out var distal, out _))
                {
                    var direction = tip.position - distal.position;
                    if (direction.sqrMagnitude > .000001f)
                    {
                        tip.rotation = Quaternion.LookRotation(direction, tip.rotation * Vector3.up);
                        Tip = tip;
                        TipTracked = true;
                    }
                }
            }
#endif
            if (!Tracked) hadPalm = false;
            UpdateClosedState();
            UpdateWorldGrip();
            PinchStrength = pinch;
            IsPinching = Tracked && (IsPinching ? pinch > .55f : pinch >= .75f);
#region debug-point A-B:eyehand-pointer-stall
#if DEVELOPMENT_BUILD && UNITY_ANDROID && !UNITY_EDITOR
            var debugNativeTracked = device != null && device.isTracked.isPressed;
            var debugNativePinch = device != null && device.pinchTouched.isPressed;
            var debugNativeGrasp = device != null && device.graspFirm.isPressed;
            var debugMask = (Tracked ? 1 : 0) | (TipTracked ? 2 : 0) | (Closed ? 4 : 0) |
                            (IsPinching ? 8 : 0) | (debugNativeTracked ? 16 : 0) |
                            (debugNativePinch ? 32 : 0) | (debugNativeGrasp ? 64 : 0) |
                            (suppressGraspUntilOpen ? 128 : 0);
            if (debugMask != debugHandMask || Time.unscaledTime >= debugNextHandTrace)
            {
                debugHandMask = debugMask;
                debugNextHandTrace = Time.unscaledTime + 1f;
                var debugPalmIndex = (int)HandJoint.JointPalm;
                var debugPalmFlags = joints.jointLocations != null &&
                    debugPalmIndex < joints.jointLocations.Length
                    ? (int)joints.jointLocations[debugPalmIndex].locationStatus : 0;
                var debugRawPinch = device == null ? 0 : device.pinchValue.ReadValue();
                var debugRawGrasp = device == null ? 0 : device.graspValue.ReadValue();
                var debugPoke = device?.pokePose.ReadValue();
                var debugPokeLength = debugPoke.HasValue
                    ? Quaternion.Dot(debugPoke.Value.rotation, debugPoke.Value.rotation) : 0;
                GameHud.ReportInputTrace($"{{\"sessionId\":\"eyehand-pointer-stall\",\"runId\":\"post-fix-2\"," +
                    $"\"hypothesisId\":\"A\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}," +
                    $"\"location\":\"PicoTrackedHand.Update\",\"msg\":\"[DEBUG] raw hand and UI gates\"," +
                    $"\"data\":{{\"frame\":{Time.frameCount},\"side\":\"{handType}\"," +
                    $"\"nativeTracked\":{(debugNativeTracked ? 1 : 0)},\"nativePinch\":{(debugNativePinch ? 1 : 0)}," +
                    $"\"nativeGrasp\":{(debugNativeGrasp ? 1 : 0)}," +
                    $"\"nativeTrackingState\":{(device == null ? 0 : device.trackingState.ReadValue())}," +
                    $"\"nativePokeQuatLength\":{GameHud.InputTraceString(debugPokeLength.ToString("R", System.Globalization.CultureInfo.InvariantCulture))}," +
                    $"\"rawPinch\":{GameHud.InputTraceString(debugRawPinch.ToString("R", System.Globalization.CultureInfo.InvariantCulture))}," +
                    $"\"rawGrasp\":{GameHud.InputTraceString(debugRawGrasp.ToString("R", System.Globalization.CultureInfo.InvariantCulture))}," +
                    $"\"tracked\":{(Tracked ? 1 : 0)},\"tipTracked\":{(TipTracked ? 1 : 0)}," +
                    $"\"customPinch\":{(IsPinching ? 1 : 0)},\"closed\":{(Closed ? 1 : 0)}," +
                    $"\"graspSuppressed\":{(suppressGraspUntilOpen ? 1 : 0)}," +
                    $"\"lastPalmFlags\":{debugPalmFlags},\"held\":{(Direct.hasSelection ? 1 : 0)}," +
                    $"\"nearItem\":{(Direct.HasNearbyTarget ? 1 : 0)}}}}}");
            }
#endif
#endregion
            if (visible == Tracked) return;
            visible = Tracked;
            if (visualHand != null) visualHand.enabled = visible;
            foreach (var renderer in renderers)
                if (renderer != null) renderer.enabled = visible;
            Debug.Log($"MATESOUL_HAND_TRACKING side={handType} tracked={Tracked}");
        }

        // Bound sample speed rather than per-frame distance so a legitimate
        // fast gesture remains continuous when the render frame rate drops.
        internal static bool IsContinuousMotion(Pose previous, Pose current, float delta)
            => delta > .0001f && delta < .1f &&
               Vector3.Distance(previous.position, current.position) <= .02f + 6f * delta &&
               Quaternion.Angle(previous.rotation, current.rotation) <=
               10f + 20f * Mathf.Rad2Deg * delta;

        private void UpdatePalmMotion(float delta)
        {
            PalmVelocity = PalmAngularVelocity = Vector3.zero;
            MotionValid = hadPalm && IsContinuousMotion(
                new Pose(previousPalmPosition, previousPalmRotation), Palm, delta);
            if (MotionValid)
            {
                PalmVelocity = Vector3.ClampMagnitude(
                    (Palm.position - previousPalmPosition) / delta, 4f);
                var turn = Palm.rotation * Quaternion.Inverse(previousPalmRotation);
                turn.ToAngleAxis(out var angle, out var axis);
                if (angle > 180) angle -= 360;
                PalmAngularVelocity = PhysicsBody.IsFinite(axis)
                    ? Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / delta), 12f)
                    : Vector3.zero;
            }
            previousPalmPosition = Palm.position;
            previousPalmRotation = Palm.rotation;
            hadPalm = true;
        }

        private void UpdateClosedState()
        {
            if (NativePinching)
                suppressGraspUntilOpen = true;
            else if (!NativeGrasping)
                suppressGraspUntilOpen = false;
            Closed = Tracked && NativeGrasping && !suppressGraspUntilOpen;
        }

        private void UpdateWorldGrip()
        {
            WorldGrip = 0;
            validCurlMask = 0;
            if (!Tracked) return;
            // Index/thumb pinch remains UI input. Curling two of the other fingers
            // also recognizes a relaxed side grip without demanding a firm SDK fist.
            var middle = Curl(HandJoint.JointMiddleProximal,
                HandJoint.JointMiddleIntermediate, HandJoint.JointMiddleTip);
            var ring = Curl(HandJoint.JointRingProximal,
                HandJoint.JointRingIntermediate, HandJoint.JointRingTip);
            var little = Curl(HandJoint.JointLittleProximal,
                HandJoint.JointLittleIntermediate, HandJoint.JointLittleTip);
            var curl = Mathf.Max(Mathf.Min(middle, ring),
                Mathf.Max(Mathf.Min(middle, little), Mathf.Min(ring, little)));
            fingerCurls[0] = middle;
            fingerCurls[1] = ring;
            fingerCurls[2] = little;
            // Measured open fingers take precedence over a stale SDK firm-grasp flag.
            var twoValid = (validCurlMask & (validCurlMask - 1)) != 0;
            WorldGrip = twoValid || NativePinching ? curl : (NativeGrasping ? 1f : Grip);
        }

        private float Curl(HandJoint proximal, HandJoint intermediate, HandJoint tip)
        {
            if (!TryGetJoint(proximal, out var first, out _) ||
                !TryGetJoint(intermediate, out var second, out _) ||
                !TryGetJoint(tip, out var last, out _))
                return 0;
            validCurlMask |= proximal == HandJoint.JointMiddleProximal ? 1 :
                proximal == HandJoint.JointRingProximal ? 2 : 4;
            return Mathf.InverseLerp(18f, 95f,
                Vector3.Angle(second.position - first.position,
                    last.position - second.position));
        }

        public int CaptureGripFingers()
        {
            var mask = 0;
            for (var i = 0; i < 3; i++)
                if ((validCurlMask & (1 << i)) != 0 && fingerCurls[i] >= .3f)
                    mask |= 1 << i;
            return mask;
        }

        public bool TryGetGripStrength(int fingers, out float strength)
        {
            strength = 0;
            if (fingers == 0) { strength = WorldGrip; return Tracked; }
            if ((validCurlMask & fingers) != fingers) return false;
            var strongest = 0f;
            var second = 0f;
            var count = 0;
            for (var i = 0; i < 3; i++)
                if ((fingers & (1 << i)) != 0)
                {
                    count++;
                    var curl = fingerCurls[i];
                    if (curl >= strongest) { second = strongest; strongest = curl; }
                    else second = Mathf.Max(second, curl);
                }
            // A multi-finger grasp needs two curled fingers. One trailing finger
            // must not hold the item after the player has opened their hand.
            strength = count > 1 ? second : strongest;
            return true;
        }

        public bool TryGetGraspPose(bool pinch, out Pose pose)
        {
            pose = Palm;
            if (!Tracked) return false;
            if (pinch)
            {
                if (!TryGetJoint(HandJoint.JointThumbTip, out var thumb, out _) ||
                    !TryGetJoint(HandJoint.JointIndexTip, out var index, out _)) return false;
                pose.position = (thumb.position + index.position) * .5f;
            }
            else if (TryGetJoint(HandJoint.JointThumbProximal, out var thumb, out _) &&
                     TryGetJoint(HandJoint.JointIndexProximal, out var index, out _))
                pose.position = Vector3.Lerp(Palm.position, (thumb.position + index.position) * .5f, .65f);
            return true;
        }

        public bool TryGetStableGripPose(out Pose pose)
        {
            pose = Palm;
            if (!Tracked) return false;
            // A palm frame does not rotate with finger curl. Z is along the
            // fingers and Y points away from the grasped surface.
            if (TryGetJoint(HandJoint.JointWrist, out var wrist, out _) &&
                TryGetJoint(HandJoint.JointMiddleProximal, out var middle, out _) &&
                TryGetJoint(HandJoint.JointIndexProximal, out var index, out _) &&
                TryGetJoint(HandJoint.JointLittleProximal, out var little, out _))
            {
                var forward = middle.position - wrist.position;
                var normal = Vector3.Cross(index.position - wrist.position,
                    little.position - wrist.position);
                if (IsLeft) normal = -normal;
                if (forward.sqrMagnitude > .000001f && normal.sqrMagnitude > .00000001f)
                    pose.rotation = Quaternion.LookRotation(forward, normal);
            }
            return true;
        }

        public bool TryGetJoint(HandJoint joint, out Pose pose, out float radius)
        {
            var index = (int)joint;
            if (index < 0 || index >= jointValid.Length || !jointValid[index])
            {
                pose = default;
                radius = 0;
                return false;
            }
            pose = jointPoses[index];
            radius = jointRadii[index];
            return true;
        }

        public HandGripContact EvaluateGrip(
            System.Collections.Generic.IReadOnlyList<Collider> target,
            float extraAllowance = 0)
            => HandGripEvaluator.Evaluate(BuildGripSample(), target, extraAllowance);

        private HandGripSample BuildGripSample()
        {
            var palm = ContactPoint(HandJoint.JointPalm, .022f);
            if (!palm.Valid && Tracked)
                palm = new HandContactPoint(Palm.position, .022f);
            var thumbProximal = ContactPoint(HandJoint.JointThumbProximal, .009f);
            var indexProximal = ContactPoint(HandJoint.JointIndexProximal, .009f);
            var web = thumbProximal.Valid && indexProximal.Valid
                ? new HandContactPoint(
                    (thumbProximal.Position + indexProximal.Position) * .5f,
                    Mathf.Max(thumbProximal.Radius, indexProximal.Radius) + .007f)
                : default;
            return new HandGripSample(
                palm,
                web,
                ContactPoint(HandJoint.JointThumbTip, .008f),
                ContactPoint(HandJoint.JointIndexTip, .007f),
                ContactPoint(HandJoint.JointMiddleTip, .008f),
                ContactPoint(HandJoint.JointRingTip, .007f),
                ContactPoint(HandJoint.JointLittleTip, .006f));
        }

        private HandContactPoint ContactPoint(HandJoint joint, float fallbackRadius)
            => TryGetJoint(joint, out var pose, out var radius)
                ? new HandContactPoint(
                    pose.position, Mathf.Max(fallbackRadius, radius))
                : default;

        private bool CacheJoints()
        {
            var cached = false;
            for (var index = 0;
                 joints.jointLocations != null &&
                 index < joints.jointCount &&
                 index < jointValid.Length &&
                 index < joints.jointLocations.Length;
                 index++)
            {
                if (!TryJoint((HandJoint)index, out var local, out var radius))
                    continue;
                jointPoses[index] = ToWorld(local);
                jointRadii[index] = Mathf.Max(0, radius);
                jointValid[index] = true;
                cached = true;
            }
            return cached;
        }

        private bool TryJoint(HandJoint joint, out Pose pose, out float radius)
        {
            pose = default;
            radius = 0;
            var index = (int)joint;
            if (joints.jointLocations == null || index >= joints.jointCount ||
                index >= joints.jointLocations.Length) return false;
            var location = joints.jointLocations[index];
            const HandLocationStatus required = HandLocationStatus.PositionValid |
                                                HandLocationStatus.OrientationValid |
                                                HandLocationStatus.PositionTracked;
            if ((location.locationStatus & required) != required) return false;
            var position = location.pose.Position.ToVector3();
            var rotation = location.pose.Orientation.ToQuat();
            var length = Quaternion.Dot(rotation, rotation);
            if (!PhysicsBody.IsFinite(position) || float.IsNaN(length) || float.IsInfinity(length) ||
                length < .5f || length > 1.5f) return false;
            pose = new Pose(position, rotation.normalized);
            radius = PhysicsBody.IsFinite(location.radius) && location.radius > 0
                ? location.radius : 0;
            return true;
        }

        private Pose ToWorld(Pose pose)
            => new Pose(transform.TransformPoint(pose.position), transform.rotation * pose.rotation);

        private void OnDisable()
        {
            Tracked = TipTracked = Closed = IsPinching = visible = false;
            NativeTracked = NativePinching = NativeGrasping = false;
            suppressGraspUntilOpen = false;
            Grip = WorldGrip = PinchStrength = 0;
            PalmVelocity = PalmAngularVelocity = Vector3.zero;
            MotionValid = false;
            validCurlMask = 0;
            hadPalm = false;
            Array.Clear(jointValid, 0, jointValid.Length);
            if (visualHand != null) visualHand.enabled = false;
            if (renderers != null)
                foreach (var renderer in renderers)
                    if (renderer != null) renderer.enabled = false;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
