using UnityEngine;

namespace MateSoul.Runtime
{
    public enum ItemGripKind
    {
        Generic,
        Handle,
        LoopHandle,
        LargeTwoHand,
        ThinEdge
    }

    public enum CharacterHandPose
    {
        Support,
        Power,
        Hook,
        TwoHandSupport,
        Pinch
    }

    // Local coordinates are metres in the item root, independent of its imported model.
    [CreateAssetMenu(menuName = "MateSoul/Item interaction profile")]
    public sealed class ItemInteractionProfile : ScriptableObject
    {
        public string capability;
        public ItemGripKind gripKind;
        public CharacterHandPose handPose;
        public Vector3 gripPosition;
        public Vector3 gripEuler;
        public Vector3 gripSize;
        public Vector3 gripShapeOffset;
        [Min(0)] public float fingerClearance = .002f;
        [Range(0, 1)] public float fingerCurl = .75f;
        public Vector3 secondaryGripPosition;
        public Vector3 secondaryGripEuler;
        public Vector3 carryEuler;
        public Vector3 bladeStart, bladeEnd;
        public float receivingHeight;
        public Vector3 assemblyOffset;
        public ItemDefinition cutResult;
        public Vector3 workPosition;
        public Vector3 platePosition;
        public Vector3 toolPosition;
        public Vector3 approachPosition;
        // Authored right-hand gesture that replaces the procedural finger pose
        // while this item is held. Empty keeps the procedural grip.
        public AnimationClip handGesture;
        [Min(0)] public float handGestureLeadTime = .05f;
        // The item's use clip already animates the fingers around the item. The
        // procedural grip would fight that authored pose and push the hand
        // through the model, so it is skipped while the use gesture plays.
        public bool authoredFingerPose;
        // Per-frame authored item pose expressed in the right-hand bone's local
        // space. A single rigid grip offset cannot follow a two-hand hug or a
        // re-gripped handle, so the use gesture samples this track instead.
        public Vector3[] authoredHoldPositions;
        public Quaternion[] authoredHoldRotations;
        public float authoredHoldFrameRate;

        public Pose Grip => new Pose(gripPosition, Quaternion.Euler(gripEuler));

        public bool TryReadAuthoredHold(float seconds, out Pose local)
        {
            local = default;
            var positions = authoredHoldPositions;
            var rotations = authoredHoldRotations;
            if (positions == null || rotations == null || positions.Length == 0 ||
                positions.Length != rotations.Length || authoredHoldFrameRate <= 0f)
                return false;
            float frame = Mathf.Clamp(
                seconds * authoredHoldFrameRate, 0, positions.Length - 1);
            int index = Mathf.FloorToInt(frame);
            int next = Mathf.Min(index + 1, positions.Length - 1);
            float blend = frame - index;
            local = new Pose(
                Vector3.Lerp(positions[index], positions[next], blend),
                Quaternion.Slerp(rotations[index], rotations[next], blend));
            return true;
        }
        public Pose SecondaryGrip =>
            new Pose(secondaryGripPosition, Quaternion.Euler(secondaryGripEuler));
        public bool RequiresTwoHands => gripKind == ItemGripKind.LargeTwoHand;
        public float GripRadius => Mathf.Max(.003f,
            Mathf.Max(gripSize.x, gripSize.y) * .5f);

        public Pose WorldGrip(Pose item)
            => new Pose(item.position + item.rotation * gripPosition,
                item.rotation * Quaternion.Euler(gripEuler));

        public Pose WorldSecondaryGrip(Pose item)
            => new Pose(item.position + item.rotation * secondaryGripPosition,
                item.rotation * Quaternion.Euler(secondaryGripEuler));

        public Vector3 WorldGripShapeCenter(Pose grip)
            => grip.position + grip.rotation * gripShapeOffset;

        public Pose ItemFromGrip(Pose grip)
        {
            var rotation = grip.rotation * Quaternion.Inverse(Quaternion.Euler(gripEuler));
            return new Pose(grip.position - rotation * gripPosition, rotation);
        }

        public Pose ItemFromHands(Pose primary, Pose secondary)
        {
            if (!RequiresTwoHands)
                throw new System.InvalidOperationException(
                    "The item does not define a two-hand grip.");
            var localSpan = secondaryGripPosition - gripPosition;
            var worldSpan = secondary.position - primary.position;
            if (localSpan.sqrMagnitude < .0001f || worldSpan.sqrMagnitude < .0001f)
                throw new System.InvalidOperationException(
                    "Two-hand grip anchors must have a non-zero span.");
            var primaryRotation = primary.rotation *
                                  Quaternion.Inverse(Quaternion.Euler(gripEuler));
            var rotation = Quaternion.FromToRotation(
                primaryRotation * localSpan, worldSpan) * primaryRotation;
            return new Pose(primary.position - rotation * gripPosition, rotation);
        }

        public void ConfigureGrip(ItemGripKind kind, CharacterHandPose pose,
            Pose primary, Vector3 size, Vector3 shapeOffset,
            float clearance, float curl,
            Pose? secondary = null)
        {
            gripKind = kind;
            handPose = pose;
            gripPosition = primary.position;
            gripEuler = primary.rotation.eulerAngles;
            gripSize = size;
            gripShapeOffset = shapeOffset;
            fingerClearance = clearance;
            fingerCurl = curl;
            if (secondary.HasValue)
            {
                secondaryGripPosition = secondary.Value.position;
                secondaryGripEuler = secondary.Value.rotation.eulerAngles;
            }
            else
            {
                secondaryGripPosition = Vector3.zero;
                secondaryGripEuler = Vector3.zero;
            }
            ValidateGrip();
        }

        public void ValidateGrip()
        {
            if (!PhysicsBody.IsFinite(gripPosition) ||
                !PhysicsBody.IsFinite(gripEuler) ||
                !PhysicsBody.IsFinite(gripSize) ||
                !PhysicsBody.IsFinite(gripShapeOffset) ||
                gripSize.x <= 0 || gripSize.y <= 0 || gripSize.z <= 0 ||
                !PhysicsBody.IsFinite(fingerClearance) ||
                fingerClearance < 0 || fingerClearance > .03f ||
                !PhysicsBody.IsFinite(fingerCurl) ||
                fingerCurl < 0 || fingerCurl > 1)
                throw new System.InvalidOperationException(
                    name + " has invalid grip geometry.");
            if (RequiresTwoHands &&
                (!PhysicsBody.IsFinite(secondaryGripPosition) ||
                 !PhysicsBody.IsFinite(secondaryGripEuler) ||
                 Vector3.Distance(gripPosition, secondaryGripPosition) < .15f))
                throw new System.InvalidOperationException(
                    name + " requires separated two-hand grip anchors.");
        }
    }
}
