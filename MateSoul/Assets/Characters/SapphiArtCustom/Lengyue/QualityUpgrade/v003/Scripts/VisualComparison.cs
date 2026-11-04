using UnityEngine;

namespace MateSoul.CharacterVisual
{
    /// <summary>Presentation-only A/B viewer; gameplay uses the separate game scene.</summary>
    public sealed class VisualComparison : MonoBehaviour
    {
        public GameObject before, after;
        public Camera reviewCamera;
        public Light key;
        bool closeup;
        int angle, lighting;
        readonly float[] angles={0,45,90,180};

        void Start() => ApplyView();
        void ApplyView()
        {
            float offset=closeup?.24f:.58f;
            before.transform.position=new Vector3(-offset,0,0);
            after.transform.position=new Vector3(offset,0,0);
            before.transform.rotation=after.transform.rotation=Quaternion.Euler(0,angles[angle],0);
            var center=new Vector3(0,closeup?1.56f:.86f,0);
            reviewCamera.orthographicSize=closeup?.28f:.96f;
            reviewCamera.transform.position=center+Vector3.forward*4;
            reviewCamera.transform.LookAt(center);
            key.transform.rotation=Quaternion.Euler(lighting==0?new Vector3(35,155,0):
                lighting==1?new Vector3(15,90,0):new Vector3(35,0,0));
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16,16,460,110),GUI.skin.box);
            GUILayout.Label("LENGYUE  |  LEFT: original   RIGHT: visual v003");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(closeup?"Full body":"Face close-up"))
            { closeup=!closeup; ApplyView(); }
            if(GUILayout.Button("View "+angles[angle]+" deg"))
            { angle=(angle+1)%angles.Length; ApplyView(); }
            if(GUILayout.Button("Light "+(lighting+1)))
            { lighting=(lighting+1)%3; ApplyView(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Matched camera, lighting and animation. No outline.");
            GUILayout.EndArea();
        }
    }
}
