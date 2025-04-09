using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Utility.Runtime
{
    /*
        IMPORTANT USAGE NOTE:
        
        This script by default only works in Edit Mode. For runtime functionality:
        
        Option 1 - Manual control:
        Call UpdateSize() manually when the screen/canvas changes 
        
        Option 2 - Automatic runtime updates:
        Remove the #if UNITY_EDITOR preprocessor directive for the Update() method 
        
        Note: Automatic updates will have a small performance impact as it checks 
        for screen size changes every frame.
    */
    [ExecuteInEditMode]
    public class CustomAspectRatioFitter : MonoBehaviour
    {
        public enum EFitMode
        {
            FitInCanvas,
            Envelope
        };

        private const string DEBUG_FLAG = "[CustomAspectRatioFitter]";

        // Serialized fields 
        [SerializeField] private Canvas canvas;
        [SerializeField] private float TargetAspectRatio = 1.777778f; // 16:9 aspect ratio 
        [SerializeField] private Vector3 Offset = Vector3.zero;
        [SerializeField] private EFitMode FitMode = EFitMode.FitInCanvas;

        // Cached components and reusable arrays to prevent GC 
        private RectTransform selfRTF;
        private RectTransform canvasRTF;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly Vector3[] canvasCorners = new Vector3[4];

        // Optimization: Cache screen dimensions to avoid repeated calls 
        private int lastScreenWidth;
        private int lastScreenHeight;
        private float lastCanvasScale;

#if UNITY_EDITOR
        void Update()
        {

            if (!Application.isPlaying)
            {
                UpdateSize();
            }
        }
#endif

        void OnEnable()
        {
            // Cache components on enable 
            if (selfRTF == null)
            {
                selfRTF = GetComponent<RectTransform>();
            }

            if (canvas != null && canvasRTF == null)
            {
                canvasRTF = canvas.GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// Determines if we should use screen height for FitInCanvas mode 
        /// </summary>
        private bool UseCanvasHeightForFitIn()
        {
            return (float)Screen.width / Screen.height >= TargetAspectRatio;
        }

        /// <summary>
        /// Determines if we should use canvas height for Envelope mode 
        /// </summary>
        private bool UseCanvasHeightForEnvelope()
        {
            return (float)Screen.width / Screen.height <= TargetAspectRatio;
        }

        /// <summary>
        /// Main method to update the size and position of the RectTransform 
        /// </summary>
        public void UpdateSize()
        {
            if (selfRTF == null) return;

            // Early exit if canvas is not set 
            if (canvas == null)
            {
                if (Application.isPlaying)
                {
                    Debug.LogError($"{DEBUG_FLAG} Canvas reference is not set.");
                }
                return;
            }

            // Validate aspect ratio 
            if (TargetAspectRatio <= 0.0001f)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid SourceRatio: {TargetAspectRatio}");
                return;
            }

            // Cache canvas scale factor 
            float currentCanvasScale = canvas.scaleFactor;
            if (currentCanvasScale <= 0.0001f)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid canvas scale: {currentCanvasScale}");
                return;
            }

            // Check if we need to update (screen or canvas changed)
            bool needsUpdate = Screen.width != lastScreenWidth ||
                             Screen.height != lastScreenHeight ||
                             !Mathf.Approximately(currentCanvasScale, lastCanvasScale);

            if (!needsUpdate) return;

            // Update cached values 
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            lastCanvasScale = currentCanvasScale;

            // Get canvas RTF if not cached 
            if (canvasRTF == null)
            {
                canvasRTF = canvas.GetComponent<RectTransform>();
                if (canvasRTF == null) return;
            }

            // Calculate size based on fit mode 
            if (FitMode == EFitMode.FitInCanvas)
            {
                if (UseCanvasHeightForFitIn())
                {
                    selfRTF.sizeDelta = new Vector2(
                        Screen.height * TargetAspectRatio / currentCanvasScale,
                        Screen.height / currentCanvasScale);
                }
                else
                {
                    selfRTF.sizeDelta = new Vector2(
                        Screen.width / currentCanvasScale,
                        Screen.width / (TargetAspectRatio * currentCanvasScale));
                }
                FitAspect_FitInCanvas();
            }
            else if (FitMode == EFitMode.Envelope)
            {
                if (UseCanvasHeightForEnvelope())
                {
                    selfRTF.sizeDelta = new Vector2(
                        Screen.height * TargetAspectRatio / currentCanvasScale,
                        Screen.height / currentCanvasScale);
                }
                else
                {
                    selfRTF.sizeDelta = new Vector2(
                        Screen.width / currentCanvasScale,
                        Screen.width / (TargetAspectRatio * currentCanvasScale));
                }
                FitAspect_Envelope();
            }

            // Apply offset 
            selfRTF.localPosition += Offset;
        }

        /// <summary>
        /// Adjusts position to fit within canvas bounds (FitInCanvas mode)
        /// </summary>
        private void FitAspect_FitInCanvas()
        {
            selfRTF.GetWorldCorners(corners);
            canvasRTF.GetWorldCorners(canvasCorners);

            float minX = canvasCorners[0].x;
            float maxX = canvasCorners[2].x;
            float minY = canvasCorners[0].y;
            float maxY = canvasCorners[2].y;

            Vector3 position = selfRTF.position;

            // Adjust X position if out of bounds 
            if (corners[0].x < minX)
            {
                position.x += minX - corners[0].x;
            }
            else if (corners[2].x > maxX)
            {
                position.x -= corners[2].x - maxX;
            }

            // Adjust Y position if out of bounds 
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

        /// <summary>
        /// Scales and centers content to envelope the canvas (Envelope mode)
        /// </summary>
        private void FitAspect_Envelope()
        {
            selfRTF.GetWorldCorners(corners);
            canvasRTF.GetWorldCorners(canvasCorners);

            float canvasWidth = canvasCorners[2].x - canvasCorners[0].x;
            float canvasHeight = canvasCorners[2].y - canvasCorners[0].y;
            float contentWidth = corners[2].x - corners[0].x;
            float contentHeight = corners[2].y - corners[0].y;

            // Center the content 
            Vector3 position = new Vector3(
                (canvasCorners[0].x + canvasCorners[2].x) * 0.5f,
                (canvasCorners[0].y + canvasCorners[2].y) * 0.5f,
                selfRTF.position.z);

            // Calculate scale to cover the canvas 
            float scale = Mathf.Max(canvasWidth / contentWidth, canvasHeight / contentHeight);

            selfRTF.localScale = new Vector3(scale, scale, 1f);
            selfRTF.position = position;
        }
    }
}