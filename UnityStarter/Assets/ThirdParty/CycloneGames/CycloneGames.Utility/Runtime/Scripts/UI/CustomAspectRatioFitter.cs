using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// NOTE: This script required the parent canvas or root canvas to get the correct scale factor. Now the fit mode is fit-in-canvas, envelope mode not implemented.
namespace CycloneGames.Utility.Runtime
{
    [ExecuteInEditMode]
    public class CustomAspectRatioFitter : MonoBehaviour
    {
        public enum EFitMode
        {
            FitInCanvas,
            Envelope
        };
        private const string DEBUG_FLAG = "[CustomAspectRatioFitter] ";
        [SerializeField] private Canvas canvas;

        [SerializeField] private float TargetAspectRatio = 1.777778f; // Horizontal 16:9 
        [SerializeField] private EFitMode FitMode = EFitMode.FitInCanvas;

        private RectTransform selfRTF;

        void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UpdateSize();
#endif
        }

        void OnEnable()
        {
            if (selfRTF == null) selfRTF = GetComponent<RectTransform>();
        }

        private bool UseScreenHeight()
        {
            return (float)Screen.width / (float)Screen.height >= TargetAspectRatio;
        }

        private void UpdateSize()
        {
            if (selfRTF == null) return;
            if (canvas == null)
            {
                if (Application.isPlaying)
                {
                    // Editor mode should not print message
                    Debug.LogError($"{DEBUG_FLAG} Canvas reference is not set.");
                }

                return;
            }

            if (TargetAspectRatio <= 0.0001f)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid SourceRatio: {TargetAspectRatio}");
                return;
            }

            float canvasScale = canvas.scaleFactor;
            if (canvasScale <= 0.0001f)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid canvas scale: {canvasScale}");
                return;
            }

            if (UseScreenHeight())
            {
                selfRTF.sizeDelta = new Vector2(Screen.height * TargetAspectRatio / canvasScale, Screen.height / canvasScale);
            }
            else
            {
                selfRTF.sizeDelta = new Vector2(Screen.width / canvasScale, Screen.width / (TargetAspectRatio * canvasScale));
            }

            // Make sure the UI is inside the screen
            ClampToScreen();
        }

        private void ClampToScreen()
        {
            Vector3[] corners = new Vector3[4];
            selfRTF.GetWorldCorners(corners);

            RectTransform canvasRTF = canvas.GetComponent<RectTransform>();
            Vector3[] canvasCorners = new Vector3[4];
            canvasRTF.GetWorldCorners(canvasCorners);

            float minX = canvasCorners[0].x;
            float maxX = canvasCorners[2].x;
            float minY = canvasCorners[0].y;
            float maxY = canvasCorners[2].y;

            Vector3 position = selfRTF.position;

            if (corners[0].x < minX)
            {
                position.x += minX - corners[0].x;
            }
            else if (corners[2].x > maxX)
            {
                position.x -= corners[2].x - maxX;
            }

            if (corners[0].y < minY)
            {
                position.y += minY - corners[0].y;
            }
            else if (corners[2].y > maxY)
            {
                position.y -= corners[2].y - maxY;
            }

            selfRTF.position = position;
        }
    }
}