using System;
using System.Collections;
using MateSoul.Core;
using MateSoul.Runtime;
using UnityEngine;

namespace MateSoul.CharacterVisual
{
    public sealed class FacialPoseRequest
    {
        public VisualExpression Expression { get; }
        public VisualViseme Viseme { get; }
        public float Strength { get; }
        public float Seconds { get; }
        public Vector2 Blink { get; }
        public Vector3? Gaze { get; }
        public FacialPoseRequest(VisualExpression expression, float seconds = 2,
            float strength = 1, VisualViseme viseme = VisualViseme.None,
            Vector2 blink = default, Vector3? gaze = null)
        {
            if (!Enum.IsDefined(typeof(VisualExpression), expression) ||
                !Enum.IsDefined(typeof(VisualViseme), viseme) ||
                float.IsNaN(seconds) || seconds <= 0 || seconds > 20 ||
                float.IsNaN(strength) || strength < 0 || strength > 1 ||
                !float.IsFinite(blink.x) || !float.IsFinite(blink.y) ||
                blink.x < 0 || blink.x > 1 || blink.y < 0 || blink.y > 1 ||
                (gaze.HasValue && (!float.IsFinite(gaze.Value.x) || !float.IsFinite(gaze.Value.y) || !float.IsFinite(gaze.Value.z))))
                throw new ArgumentOutOfRangeException(nameof(expression), "Invalid facial pose request.");
            Expression=expression; Viseme=viseme; Strength=strength; Seconds=seconds;
            Blink=blink; Gaze=gaze;
        }
    }

    /// <summary>Registers the candidate's facial capability with the shared action owner.</summary>
    public sealed class VisualExpressionRegistration : MonoBehaviour
    {
        [SerializeField] GameBootstrap bootstrap;
        [SerializeField] VisualFaceControl face;

        public void Configure(GameBootstrap game, VisualFaceControl control)
        {
            bootstrap=game;
            face=control;
        }

        IEnumerator Start()
        {
            if (!bootstrap || !face)
                throw new InvalidOperationException("Visual expression registration needs game and face bindings.");
            while (bootstrap.Session==null) yield return null;
            bootstrap.Session.Catalog.Register(new ActionDefinition(
                "expression", "Show a facial expression", ControlChannels.Face | ControlChannels.Gaze,
                intent=>new ExpressionAction(face,intent.Payload as FacialPoseRequest),
                timeoutSeconds:22, exposeToAI:false));
        }

        sealed class ExpressionAction : IAvatarAction
        {
            readonly VisualFaceControl face;
            readonly FacialPoseRequest request;
            float elapsed;
            bool started, cancelled;
            public ExpressionAction(VisualFaceControl face, FacialPoseRequest request)
            { this.face=face; this.request=request; }
            public string Check()=>!face || !face.isActiveAndEnabled
                ? "Facial control is unavailable." : request==null ? "FacialPoseRequest is required." : null;
            public void Start()
            {
                started=true;
                face.SetExpression(request.Expression,request.Strength);
                face.SetViseme(request.Viseme,request.Strength);
                face.SetBlink(request.Blink.x,request.Blink.y);
                face.SetGaze(request.Gaze ?? Vector3.zero,request.Gaze.HasValue ? request.Strength : 0);
            }
            public ActionProgress Tick(float deltaSeconds)
            {
                elapsed+=deltaSeconds;
                return cancelled || elapsed>=request.Seconds ? ActionProgress.Succeeded : ActionProgress.Running;
            }
            public void Cancel(ActionStopReason reason) { cancelled=true; }
            public void Dispose()
            {
                if (!started || !face) return;
                face.SetExpression(VisualExpression.Neutral);
                face.SetViseme(VisualViseme.None);
                face.SetBlink(0,0);
                face.SetGaze(Vector3.zero,0);
            }
        }
    }
}
