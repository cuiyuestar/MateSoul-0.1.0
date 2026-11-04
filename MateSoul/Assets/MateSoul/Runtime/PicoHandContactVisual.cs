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
            public readonly Quaternion[] Open = new Quaternion[3];
            public readonly Quaternion[] Raw = new Quaternion[3];
            public readonly Quaternion[] Wanted = new Quaternion[3];
            public readonly Quaternion[] Locked = new Quaternion[3];
            public readonly Quaternion[] Correction = { Quaternion.identity, Quaternion.identity, Quaternion.identity };
            public readonly Vector3[] Hinge = new Vector3[3];
        }

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

        public void Configure(PicoTrackedHand source, PXR_Hand visual)
        {
            hand = source;
            model = visual;
            wrist = Bone(HandJoint.JointWrist);
            palm = Bone(HandJoint.JointPalm);
            var normal = Vector3.Cross(Bone(HandJoint.JointIndexProximal).position - wrist.position,
                Bone(HandJoint.JointLittleProximal).position - wrist.position).normalized;
            if (source.IsLeft) normal = -normal;
            AddFinger(0, HandJoint.JointThumbMetacarpal, HandJoint.JointThumbProximal,
                HandJoint.JointThumbDistal, HandJoint.JointThumbTip, normal);
            AddFinger(1, HandJoint.JointIndexProximal, HandJoint.JointIndexIntermediate,
                HandJoint.JointIndexDistal, HandJoint.JointIndexTip, normal);
            AddFinger(2, HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate,
                HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip, normal);
            AddFinger(3, HandJoint.JointRingProximal, HandJoint.JointRingIntermediate,
                HandJoint.JointRingDistal, HandJoint.JointRingTip, normal);
            AddFinger(4, HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate,
                HandJoint.JointLittleDistal, HandJoint.JointLittleTip, normal);
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
            HandJoint d, Vector3 normal)
        {
            var finger = fingers[index] = new Finger();
            finger.Joints[0] = Bone(a);
            finger.Joints[1] = Bone(b);
            finger.Joints[2] = Bone(c);
            finger.Joints[3] = Bone(d);
            for (var j = 0; j < 3; j++)
            {
                var joint = finger.Joints[j];
                finger.Open[j] = joint.localRotation;
                var forward = finger.Joints[j + 1].position - joint.position;
                finger.Hinge[j] = joint.InverseTransformDirection(
                    Vector3.Cross(forward.normalized, normal).normalized);
            }
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
            }
            if (feedback != null && (body != null || wasHeld))
            {
                feedback.transform.position = hand.Palm.position;
                feedback.PlayOneShot(body != null ? grabSound : releaseSound);
            }
            foreach (var finger in fingers)
                if (finger != null)
                    for (var j = 0; j < 3; j++) finger.Correction[j] = Quaternion.identity;
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
            if (held != null && held.IsFollowing)
            {
                var targetPosition = held.transform.TransformPoint(heldWrist.position);
                var targetRotation = held.transform.rotation * heldWrist.rotation;
                var blend = Mathf.Clamp01((Time.unscaledTime - grabbedAt) / .055f);
                if (stableGrip != null)
                    wrist.SetPositionAndRotation(Vector3.Lerp(wrist.position, targetPosition, blend),
                        Quaternion.Slerp(wrist.rotation, targetRotation, blend));
                else
                {
                    wrist.position += Vector3.ClampMagnitude(targetPosition - wrist.position, .075f) * blend;
                    wrist.rotation = Quaternion.Slerp(wrist.rotation,
                        Quaternion.RotateTowards(wrist.rotation, targetRotation, 35), blend);
                }
            }
            var refit = Time.unscaledTime >= nextFit;
            if (refit)
            {
                nextFit = Time.unscaledTime + 1f / 30f;
                nearby.Clear();
                var count = Physics.OverlapSphereNonAlloc(palm.position, .19f, overlaps,
                    InteractionPhysicsLayers.DirectSelectionMask, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++) nearby.Add(overlaps[i]);
                if (stableGrip != null)
                    foreach (var shape in held.Shapes)
                        if (!nearby.Contains(shape)) nearby.Add(shape);
            }
            if (stableGrip == null) ResolvePalm();
            for (var index = 0; index < fingers.Length; index++)
            {
                var finger = fingers[index];
                for (var j = 0; j < 3; j++) finger.Raw[j] = finger.Joints[j].localRotation;
                if (stableGrip != null)
                {
                    if (!lockedPoseReady)
                    {
                        FitFinger(finger, index);
                        for (var j = 0; j < 3; j++) finger.Locked[j] = finger.Joints[j].localRotation;
                    }
                    for (var j = 0; j < 3; j++)
                        finger.Joints[j].localRotation = finger.Locked[j];
                    continue;
                }
                if (refit) FitFinger(finger, index);
                for (var j = 0; j < 3; j++)
                    finger.Joints[j].localRotation = finger.Raw[j] * finger.Correction[j];
            }
            if (stableGrip != null)
                lockedPoseReady = Time.unscaledTime - grabbedAt >= .055f;
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
            var wrap = held != null && held.IsFollowing && (!pinch || index < 2);
            for (var j = 0; j < 3; j++)
            {
                // A small extra curl supplies contact on a relaxed grip; raw spread and
                // opposition remain. Only the first collision-free closing arc is used.
                var extra = stableGrip != null
                    ? (index == 0 ? 35f : 85f) * stableGrip.Profile.fingerCurl
                    : wrap ? (index == 0 ? 12f : 25f) : 0;
                finger.Wanted[j] = (stableGrip != null ? finger.Open[j] : finger.Raw[j]) *
                                  Quaternion.AngleAxis(extra, finger.Hinge[j]);
            }
            PoseFinger(finger, 0);
            if (FingerPenetrates(finger, index))
            {
                // No safe closing arc when even the open pose is embedded.
                for (var j = 0; j < 3; j++)
                    finger.Joints[j].localRotation = finger.Raw[j];
            }
            else
            {
                var low = 0f;
                var high = 1f;
                var hit = false;
                // Stop at the first contact along the arc, including when a finger
                // would pass through a thin object and end outside it.
                for (var step = 1; step <= 6; step++)
                {
                    high = step / 6f;
                    PoseFinger(finger, high);
                    if (FingerPenetrates(finger, index)) { hit = true; break; }
                    low = high;
                }
                if (hit)
                {
                    for (var step = 0; step < 5; step++)
                    {
                        var mid = (low + high) * .5f;
                        PoseFinger(finger, mid);
                        if (FingerPenetrates(finger, index)) high = mid;
                        else low = mid;
                    }
                    PoseFinger(finger, low);
                }
                else if (stableGrip == null && (!wrap || !NearHeld(finger.Joints[3].position)))
                    for (var j = 0; j < 3; j++)
                        finger.Joints[j].localRotation = finger.Raw[j];
            }
            for (var j = 0; j < 3; j++)
                finger.Correction[j] = Quaternion.Inverse(finger.Raw[j]) * finger.Joints[j].localRotation;
        }

        private bool NearHeld(Vector3 point)
            => held != null && HandGripEvaluator.Distance(point, held.Shapes) < .03f;

        private static void PoseFinger(Finger finger, float fraction)
        {
            for (var j = 0; j < 3; j++)
                finger.Joints[j].localRotation = Quaternion.Slerp(finger.Open[j], finger.Wanted[j], fraction);
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
