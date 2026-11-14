using System;
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace MateSoul.Runtime
{
    // Presentation only. Never feeds constrained bones back into gesture recognition.
    [DisallowMultipleComponent]
    public sealed class PicoHandContactVisual : MonoBehaviour
    {
        private sealed class Finger
        {
            public readonly Transform[] Joints = new Transform[4];
            public readonly Quaternion[] Raw = new Quaternion[3];
            public readonly Quaternion[] Locked = new Quaternion[3];
        }

        // Anatomical bend budget per joint (metacarpal/proximal, intermediate,
        // distal), in degrees. A grip never rotates a joint further than this, so
        // a finger can never fold back over itself.
        private static readonly float[] FingerLimits = { 65f, 90f, 60f };
        private static readonly float[] ThumbLimits = { 25f, 45f, 55f };

        private PicoTrackedHand hand;
        private PXR_Hand model;
        private Transform wrist, palm;
        private readonly Finger[] fingers = new Finger[5];
        private readonly Collider[] overlaps = new Collider[48];
        private readonly List<Collider> nearby = new List<Collider>(48);
        private PhysicsBody held;
        private Pose heldWrist;
        private bool pinch;
        private ItemGripAnchor stableGrip;
        private bool lockedPoseReady;
        private float grabbedAt, nextFit;
        private GameObject probes;
        private AudioSource feedback;
        private AudioClip grabSound, releaseSound;
        private string Side => hand != null && hand.IsLeft ? "Left" : "Right";

        public void Configure(PicoTrackedHand source, PXR_Hand visual)
        {
            hand = source;
            model = visual;
            wrist = Bone(HandJoint.JointWrist);
            palm = Bone(HandJoint.JointPalm);
            AddFinger(0, HandJoint.JointThumbMetacarpal, HandJoint.JointThumbProximal,
                HandJoint.JointThumbDistal, HandJoint.JointThumbTip);
            AddFinger(1, HandJoint.JointIndexProximal, HandJoint.JointIndexIntermediate,
                HandJoint.JointIndexDistal, HandJoint.JointIndexTip);
            AddFinger(2, HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate,
                HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip);
            AddFinger(3, HandJoint.JointRingProximal, HandJoint.JointRingIntermediate,
                HandJoint.JointRingDistal, HandJoint.JointRingTip);
            AddFinger(4, HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate,
                HandJoint.JointLittleDistal, HandJoint.JointLittleTip);
            probes = new GameObject("Hand Contact Feedback");
            probes.transform.SetParent(hand.transform, false);
            feedback = probes.AddComponent<AudioSource>();
            feedback.playOnAwake = false;
            feedback.spatialBlend = 1;
            feedback.minDistance = .15f;
            feedback.maxDistance = 1.5f;
            feedback.volume = .12f;
            grabSound = MakeSound("Hand grasp", 540);
            releaseSound = MakeSound("Hand release", 370);
        }

        private Transform Bone(HandJoint joint)
        {
            var bone = model.handJoints[(int)joint];
            if (bone == null) throw new InvalidOperationException("Hand visual bone is missing: " + joint);
            return bone;
        }

        private void AddFinger(int index, HandJoint a, HandJoint b, HandJoint c,
            HandJoint d)
        {
            var finger = fingers[index] = new Finger();
            finger.Joints[0] = Bone(a);
            finger.Joints[1] = Bone(b);
            finger.Joints[2] = Bone(c);
            finger.Joints[3] = Bone(d);
        }

        public void SetHeldBody(PhysicsBody body, Vector3 correction, bool isPinch,
            ItemGripAnchor anchor = null, Pose gripPose = default)
        {
            if (held == body) return;
            var wasHeld = held != null;
            held = body;
            stableGrip = anchor;
            lockedPoseReady = false;
            pinch = isPinch;
            nextFit = 0;
            if (body != null)
            {
                var raw = hand.TryGetJoint(HandJoint.JointWrist, out var pose, out _)
                    ? pose : new Pose(wrist.position, wrist.rotation);
                heldWrist = new Pose(body.transform.InverseTransformPoint(raw.position + correction),
                    Quaternion.Inverse(body.transform.rotation) * raw.rotation);
                if (stableGrip != null)
                {
                    var localWrist = Quaternion.Inverse(gripPose.rotation) *
                                     (raw.position - gripPose.position);
                    var grip = stableGrip.Profile.Grip;
                    heldWrist = new Pose(grip.position + grip.rotation * localWrist,
                        grip.rotation * Quaternion.Inverse(gripPose.rotation) * raw.rotation);
                }
                grabbedAt = Time.unscaledTime;
                Debug.Log($"MATESOUL_GRAB_GRIP_BEGIN side={Side} item={body.name} " +
                          $"pinch={(isPinch ? 1 : 0)} anchor={(stableGrip != null ? 1 : 0)} " +
                          $"following={(body.IsFollowing ? 1 : 0)} " +
                          $"palm={palm.position.ToString("F3")}");
            }
            if (feedback != null && (body != null || wasHeld))
            {
                feedback.transform.position = hand.Palm.position;
                feedback.PlayOneShot(body != null ? grabSound : releaseSound);
            }
        }

        private void OnEnable() => Application.onBeforeRender += ApplyContactPose;
        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyContactPose;
            held = null;
            stableGrip = null;
            nearby.Clear();
        }

        // SDK PXR_Hand restores the raw pose at order 0. This is its final presentation pass.
        [BeforeRenderOrder(100)]
        private void ApplyContactPose()
        {
            if (hand == null || !hand.Tracked || model == null || !model.enabled) return;
            // Only anchored grips snap the wrist onto the item. Otherwise the visual
            // hand stays with the real hand and the item follows it, so the two never
            // chase each other frame by frame.
            if (held != null && held.IsFollowing && stableGrip != null)
            {
                var targetPosition = held.transform.TransformPoint(heldWrist.position);
                var targetRotation = held.transform.rotation * heldWrist.rotation;
                var blend = Mathf.Clamp01((Time.unscaledTime - grabbedAt) / .055f);
                wrist.SetPositionAndRotation(Vector3.Lerp(wrist.position, targetPosition, blend),
                    Quaternion.Slerp(wrist.rotation, targetRotation, blend));
            }
            var refit = Time.unscaledTime >= nextFit;
            if (refit)
            {
                nextFit = Time.unscaledTime + 1f / 30f;
                nearby.Clear();
                var count = Physics.OverlapSphereNonAlloc(palm.position, .19f, overlaps,
                    InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++) nearby.Add(overlaps[i]);
                if (held != null)
                    foreach (var shape in held.Shapes)
                        if (!nearby.Contains(shape)) nearby.Add(shape);
            }
            // While holding, the palm stays with the real hand. Pushing it out of the
            // item fights the tracked pose and reads as the whole hand twitching.
            if (held == null) ResolvePalm();
            for (var index = 0; index < fingers.Length; index++)
            {
                var finger = fingers[index];
                // A free hand is left exactly as the SDK posed it this frame: any
                // second opinion here is what made the fingers twitch.
                if (held == null) continue;
                // Every held item locks the fingers once: fit them onto the item on
                // the grab frame, then hold that pose. Re-fitting every frame is what
                // made the fingers twitch while carrying something.
                if (!lockedPoseReady)
                {
                    FitFinger(finger, index);
                    for (var j = 0; j < 3; j++)
                        finger.Locked[j] = finger.Joints[j].localRotation;
                }
                for (var j = 0; j < 3; j++)
                    finger.Joints[j].localRotation = finger.Locked[j];
            }
            // One fit is enough: the grip comes from the item's size, so there is
            // nothing left to solve while the item is held.
            if (held != null) lockedPoseReady = true;
        }

        private void ResolvePalm()
        {
            var palmProbe = hand.PhysicsProxy?.PalmCollider;
            if (palmProbe == null || !palmProbe.enabled) return;
            var correction = Vector3.zero;
            for (var pass = 0; pass < 2; pass++)
            {
                var strongest = Vector3.zero;
                foreach (var shape in nearby)
                {
                    if (shape == null || !shape.enabled || shape.isTrigger ||
                        held != null && shape.attachedRigidbody == held.Rigidbody) continue;
                    if (Physics.ComputePenetration(palmProbe, palm.position + correction, palm.rotation,
                        shape, shape.transform.position, shape.transform.rotation,
                        out var direction, out var depth) && depth < .045f &&
                        depth > strongest.magnitude)
                        strongest = direction * (depth + .001f);
                }
                correction = Vector3.ClampMagnitude(correction + strongest, .035f);
            }
            wrist.position += correction;
        }

        private void FitFinger(Finger finger, int index)
        {
            // The SDK writes absolute local rotations onto every bone, so the only
            // valid base for a grip is this frame's tracked pose. Deriving the grip
            // from the prefab's rest pose (captured while the model was disabled) or
            // from a guessed hinge axis put the bones into arbitrary orientations and
            // folded the fingers backwards over the back of the hand.
            for (var j = 0; j < 3; j++)
                finger.Raw[j] = finger.Joints[j].localRotation;
            if (held == null || !held.IsFollowing) return;
            var target = palm.position;
            var onItem = HandGripEvaluator.TryClosestPoint(palm.position, held.Shapes, out target);
            var limits = index == 0 ? ThumbLimits : FingerLimits;
            var scale = stableGrip != null ? Mathf.Clamp01(stableGrip.Profile.fingerCurl) : 1f;
            if (index == 0 && pinch) scale *= .6f;
            var axis = CurlAxis();
            // Bending about the knuckle line curls a finger into the palm, but the
            // sign of that line flips with the hand model's handedness. Probe both
            // directions and keep the one that moves the fingertip toward the palm
            // instead of trusting the model's orientation.
            var sign = CurlSign(finger, limits, scale, axis);
            var reach = Vector3.Distance(finger.Joints[3].position, target);
            if (FingerPenetrates(finger, index))
            {
                // The tracked pose already sits inside the item. Keeping it is the
                // only answer that cannot invent an inverted pose.
                RestoreTracked(finger);
                LogGrip(index, target, onItem, 0f, false, reach, sign);
                return;
            }
            var low = 0f;
            var high = 1f;
            var contact = false;
            // Close in six steps and stop on the first contact with the item, which
            // makes the final grip follow the item's size instead of a fixed curl.
            for (var step = 1; step <= 6; step++)
            {
                high = step / 6f;
                Bend(finger, limits, high, sign, axis, scale);
                if (FingerPenetrates(finger, index)) { contact = true; break; }
                low = high;
            }
            if (contact)
            {
                for (var step = 0; step < 5; step++)
                {
                    var mid = (low + high) * .5f;
                    Bend(finger, limits, mid, sign, axis, scale);
                    if (FingerPenetrates(finger, index)) high = mid;
                    else low = mid;
                }
                Bend(finger, limits, low, sign, axis, scale);
            }
            else
            {
                // Nothing to close onto: rest on the joint limits, never past them.
                Bend(finger, limits, 1f, sign, axis, scale);
            }
            var finalFraction = contact ? low : 1f;
            var closing = reach - Vector3.Distance(finger.Joints[3].position, target);
            if (closing < -.001f)
            {
                // Last-resort guard. A correct grip always brings the fingertip
                // nearer the item, so a negative value means this pose is inverted:
                // keep the tracked pose and leave a marker instead of showing it.
                Debug.LogWarning($"MATESOUL_GRAB_GRIP_INVERTED side={Side} finger={index} " +
                                 $"item={(held == null ? "none" : held.name)} " +
                                 $"closing={closing:F3} total={TotalBend(finger):F0} " +
                                 $"sign={sign:F0} fraction={finalFraction:F2}");
                RestoreTracked(finger);
            }
            LogGrip(index, target, onItem, finalFraction, contact, reach, sign);
        }

        // The knuckle line. Bending every joint about this one axis reproduces how
        // real fingers close, so the segments can never fold in mixed directions.
        private Vector3 CurlAxis()
        {
            var index = fingers[1];
            var little = fingers[4];
            var axis = index != null && little != null
                ? little.Joints[0].position - index.Joints[0].position
                : Vector3.zero;
            return axis.sqrMagnitude > .000001f ? axis.normalized : palm.right;
        }

        // Applies a bounded anatomical bend to each joint from the tracked pose.
        private void Bend(Finger finger, float[] limits, float fraction, float sign,
            Vector3 axis, float scale)
        {
            for (var j = 0; j < 3; j++)
            {
                var joint = finger.Joints[j];
                var degrees = limits[j] * scale * fraction * sign;
                if (Mathf.Abs(degrees) < .01f)
                {
                    joint.localRotation = finger.Raw[j];
                    continue;
                }
                // Restore the tracked local rotation first so a partially bent pose
                // never accumulates across samples.
                joint.localRotation = finger.Raw[j];
                joint.rotation = Quaternion.AngleAxis(degrees, axis) * joint.rotation;
            }
        }

        private float CurlSign(Finger finger, float[] limits, float scale, Vector3 axis)
        {
            var probe = 8f / Mathf.Max(limits[0] * scale, .01f);
            Bend(finger, limits, probe, 1f, axis, scale);
            var positive = Vector3.Distance(finger.Joints[3].position, palm.position);
            Bend(finger, limits, probe, -1f, axis, scale);
            var negative = Vector3.Distance(finger.Joints[3].position, palm.position);
            Bend(finger, limits, 0f, 1f, axis, scale);
            return positive <= negative ? 1f : -1f;
        }

        private static void RestoreTracked(Finger finger)
        {
            for (var j = 0; j < 3; j++)
                finger.Joints[j].localRotation = finger.Raw[j];
        }

        private static float TotalBend(Finger finger)
        {
            var total = 0f;
            for (var j = 0; j < 3; j++)
                total += Quaternion.Angle(finger.Raw[j], finger.Joints[j].localRotation);
            return total;
        }

        private void LogGrip(int index, Vector3 target, bool onItem, float fraction,
            bool contact, float reach, float sign)
        {
            var finger = fingers[index];
            var proximal = Quaternion.Angle(finger.Raw[0], finger.Joints[0].localRotation);
            var middle = Quaternion.Angle(finger.Raw[1], finger.Joints[1].localRotation);
            var distal = Quaternion.Angle(finger.Raw[2], finger.Joints[2].localRotation);
            // A correct closing arc always brings the fingertip nearer the grip
            // point; a negative value means the finger bent away from it.
            var closing = reach - Vector3.Distance(finger.Joints[3].position, target);
            Debug.Log($"MATESOUL_GRAB_GRIP_FINGER side={Side} finger={index} " +
                      $"item={(held == null ? "none" : held.name)} onItem={(onItem ? 1 : 0)} " +
                      $"fraction={fraction:F2} contact={(contact ? 1 : 0)} sign={sign:F0} " +
                      $"bend={proximal:F0}/{middle:F0}/{distal:F0} " +
                      $"total={proximal + middle + distal:F0} closing={closing:F3} " +
                      $"target={target.ToString("F3")}");
        }

        private bool FingerPenetrates(Finger finger, int index)
        {
            if (hand.PhysicsProxy == null) return false;
            for (var segment = 0; segment < 3; segment++)
            {
                var start = finger.Joints[segment].position;
                var end = finger.Joints[segment + 1].position;
                var probe = hand.PhysicsProxy.FingerCollider(index, segment);
                if (!probe.enabled) continue;
                var center = (start + end) * .5f;
                var rotation = Quaternion.FromToRotation(Vector3.forward, end - start);
                foreach (var shape in nearby)
                {
                    if (shape == null || !shape.enabled || shape.isTrigger ||
                        shape.bounds.SqrDistance(center) > .0025f) continue;
                    if (Physics.ComputePenetration(probe, center, rotation,
                        shape, shape.transform.position, shape.transform.rotation,
                        out _, out var depth) && depth > .001f) return true;
                }
            }
            return false;
        }

        private static AudioClip MakeSound(string name, float frequency)
        {
            const int rate = 24000, count = 960;
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / rate;
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) *
                    Mathf.Sin(Mathf.PI * i / count) * Mathf.Exp(-t * 100);
            }
            var clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            Release(probes);
            Release(grabSound);
            Release(releaseSound);
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
