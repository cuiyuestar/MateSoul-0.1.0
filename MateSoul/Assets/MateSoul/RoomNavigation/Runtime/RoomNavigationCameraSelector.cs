using UnityEngine;

namespace MateSoul.RoomNavigation
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class RoomNavigationCameraSelector : MonoBehaviour
    {
        [SerializeField]
        private GameObject xrCamera;

        [SerializeField]
        private GameObject editorCamera;

        public void Configure(GameObject xrCameraObject, GameObject editorCameraObject)
        {
            xrCamera = xrCameraObject;
            editorCamera = editorCameraObject;
        }

        private void Awake()
        {
            var useEditorCamera = Application.isEditor;
            SetCameraState(editorCamera, useEditorCamera);
            SetCameraState(xrCamera, !useEditorCamera);
        }

        private static void SetCameraState(GameObject cameraObject, bool active)
        {
            if (cameraObject == null)
            {
                return;
            }

            cameraObject.tag = active ? "MainCamera" : "Untagged";
            cameraObject.SetActive(active);
        }
    }
}
