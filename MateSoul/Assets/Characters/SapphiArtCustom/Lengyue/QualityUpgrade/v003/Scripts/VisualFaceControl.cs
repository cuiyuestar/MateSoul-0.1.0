using System;
using System.Linq;
using UnityEngine;

namespace MateSoul.CharacterVisual
{
    public enum VisualExpression { Neutral, Smile, Laugh, Shy, Angry, Sad, Surprised }
    public enum VisualViseme { None, A, I, U, E, O }

    /// <summary>Facial inputs. The Animator exclusively owns all facial curves.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(13000)]
    public sealed class VisualFaceControl : MonoBehaviour
    {
        public const float GazeYawLimit = 8;
        public const float GazePitchLimit = 6;
        Animator animator;
        int expressionLayer, mouthLayer, leftBlinkLayer, rightBlinkLayer, gazeLayer;
        Transform head;
        Quaternion headToCharacter;
        SkinnedMeshRenderer[] faceRenderers;
        MaterialPropertyBlock block;
        static readonly int Right = Shader.PropertyToID("_FaceRight");
        static readonly int Forward = Shader.PropertyToID("_FaceForward");
        public Quaternion FaceBasis => head.rotation * headToCharacter;

        void Awake()
        {
            animator = GetComponent<Animator>();
            if (!animator) throw new InvalidOperationException("Facial control requires an Animator.");
            expressionLayer = RequireLayer("Visual Expression");
            mouthLayer = RequireLayer("Visual Mouth");
            leftBlinkLayer = RequireLayer("Visual Blink L");
            rightBlinkLayer = RequireLayer("Visual Blink R");
            gazeLayer = RequireLayer("Visual Gaze");
            head = transform.Find("BindJoints/PelvisRoot/Hips/Spine/Chest/Neck/Head 1");
            if (!head) throw new InvalidOperationException("Missing character head binding.");
            headToCharacter = Quaternion.Inverse(head.rotation) * transform.rotation;
            faceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(
                r => r.sharedMaterials.Any(m => m && m.HasProperty("_FaceMode") &&
                    m.GetFloat("_FaceMode") > .5f)).ToArray();
            block = new MaterialPropertyBlock();
        }

        int RequireLayer(string name)
        {
            int index = animator.GetLayerIndex(name);
            if (index < 0) throw new InvalidOperationException("Missing Animator layer: " + name);
            return index;
        }

        public void SetExpression(VisualExpression expression, float strength = 1)
        {
            if (!Enum.IsDefined(typeof(VisualExpression), expression))
                throw new ArgumentOutOfRangeException(nameof(expression));
            animator.SetInteger("VisualExpression", (int)expression);
            animator.SetLayerWeight(expressionLayer,
                expression == VisualExpression.Neutral ? 0 : Mathf.Clamp01(strength));
        }

        public void SetViseme(VisualViseme viseme, float strength = 1)
        {
            if (!Enum.IsDefined(typeof(VisualViseme), viseme))
                throw new ArgumentOutOfRangeException(nameof(viseme));
            animator.SetInteger("VisualViseme", (int)viseme);
            animator.SetLayerWeight(mouthLayer, viseme == VisualViseme.None ? 0 : Mathf.Clamp01(strength));
        }

        public void SetBlink(float left, float right)
        {
            animator.SetLayerWeight(leftBlinkLayer, Mathf.Clamp01(left));
            animator.SetLayerWeight(rightBlinkLayer, Mathf.Clamp01(right));
        }

        public void SetGaze(Vector3 worldPosition, float strength)
        {
            var direction = Quaternion.Inverse(FaceBasis) * (worldPosition - head.position);
            float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            animator.SetFloat("VisualGazeX",
                Mathf.Clamp(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / GazeYawLimit, -1, 1));
            animator.SetFloat("VisualGazeY",
                Mathf.Clamp(Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg / GazePitchLimit, -1, 1));
            animator.SetLayerWeight(gazeLayer, Mathf.Clamp01(strength));
        }

        void LateUpdate()
        {
            var basis = FaceBasis;
            foreach (var renderer in faceRenderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetVector(Right, basis * Vector3.right);
                block.SetVector(Forward, basis * Vector3.forward);
                renderer.SetPropertyBlock(block);
            }
        }

        void OnDisable()
        {
            if (!animator) return;
            animator.SetLayerWeight(expressionLayer, 0);
            animator.SetLayerWeight(mouthLayer, 0);
            animator.SetLayerWeight(leftBlinkLayer, 0);
            animator.SetLayerWeight(rightBlinkLayer, 0);
            animator.SetLayerWeight(gazeLayer, 0);
        }
    }
}
