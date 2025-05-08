using UnityEngine;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Add this component to any object and it'll display the frame rate.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        public enum Modes { Instant, MovingAverage, InstantAndMovingAverage }
        public enum ScreenPosition
        {
            TopLeft,    // Top left corner
            TopCenter,  // Top center
            TopRight,   // Top right corner
            MiddleLeft, // Middle left
            MiddleRight, // Middle right
            BottomLeft, // Bottom left corner
            BottomCenter, // Bottom center
            BottomRight, // Bottom right corner
            Custom       // Custom position
        }

        // Dictionary to hold FPS thresholds and corresponding colors
        [System.Serializable]
        public struct FPSColor
        {
            [Tooltip("The FPS threshold. When the current FPS is below this value, the text color will change to the associated color.")]
            public int FPSValue; // The FPS threshold

            [Tooltip("The color that will be used when the current FPS is below the threshold.")]
            public Color Color; // The corresponding color
        }

        public static FPSCounter Instance { get; private set; }
        public bool IsVisible = true;   // sometimes, you dont want to show the fps by default, uncheck this to hide the FPSCounter
        [SerializeField] private bool _singleton = true;
        [SerializeField] private bool AdjustForSafeArea = true; // Option to adjust position for safe area
        [SerializeField] private float UpdateInterval = 0.3f;
        [SerializeField] private Modes Mode = Modes.Instant;
        [SerializeField] private Color DefaultForegroundColor = Color.green; // Default foreground color
        [SerializeField] private Color OutlineColor = Color.black; // Background color
        [SerializeField] private Vector2 OutlineOffset = new Vector2(1, 1); // Offset for simulating text outline
        [SerializeField] private ScreenPosition PositionPreset = ScreenPosition.TopLeft; // Preset for screen position
        [SerializeField] private int PresetPositionMargin = 10; // Margin
        [SerializeField] private Vector2 CustomPosition = new Vector2(0, 0); // Coordinates for a custom position
        [Tooltip("A list of FPS thresholds and their corresponding colors. When the current FPS is below a threshold, the text color will change to the associated color.")]
        [SerializeField] private List<FPSColor> FPSColors = new List<FPSColor>(); // List of FPS thresholds and colors

        private Color _foregroundColor;
        private float _framesAccumulated = 0f;
        private float _framesDrawnInTheInterval = 0f;
        private float _timeLeft;
        private int _currentFPS;
        private int _totalFrames = 0;
        private int _average;
        private StringBuilder _displayedTextSB = new StringBuilder();
        private Vector2 _labelPosition;
        private GUIStyle _style = new GUIStyle();
        private GUIContent _content = new GUIContent();
        private float _fontSizeRatio = 0.04f;
        private static int _fpsMax = 300;
        private static string[] _fpsStrings = GenerateFPSTextArray(_fpsMax);

        private static string[] GenerateFPSTextArray(int maxFPS)
        {
            string[] array = new string[maxFPS + 1];
            for (int i = 0; i <= maxFPS; i++)
            {
                if (i < 100)
                {
                    array[i] = i.ToString("00");
                }
                else
                {
                    array[i] = i.ToString();
                }
            }
            return array;
        }

        void Awake()
        {
            if (_singleton)
            {
                if (Instance == null)
                {
                    Instance = this;
                    DontDestroyOnLoad(gameObject);
                    return;
                }

                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Initialize the counter on Start()
        /// </summary>
        protected virtual void Start()
        {
            FPSColors.Sort((a, b) => b.FPSValue.CompareTo(a.FPSValue));
            _timeLeft = UpdateInterval;
            int shortestScreenSide = Mathf.Min(Screen.width, Screen.height);
            _style.normal.textColor = DefaultForegroundColor;
            // _style.alignment  = TextAnchor.MiddleLeft;
            _style.fontSize = Mathf.RoundToInt(shortestScreenSide * _fontSizeRatio);
            _foregroundColor = DefaultForegroundColor;
        }

        /// <summary>
        /// Calculate FPS in Update
        /// </summary>
        protected virtual void Update()
        {
            if (!IsVisible) return;

            _framesDrawnInTheInterval++;
            _framesAccumulated += Time.timeScale / Time.deltaTime;
            _timeLeft -= Time.deltaTime;

            if (_timeLeft <= 0.0f)
            {
                _currentFPS = Mathf.RoundToInt(Mathf.Clamp(_framesAccumulated / _framesDrawnInTheInterval, 0, _fpsMax));
                _framesDrawnInTheInterval = 0;
                _framesAccumulated = 0f;
                _timeLeft = UpdateInterval;
                _totalFrames++;
                _average += (_currentFPS - _average) / _totalFrames;

                if (_currentFPS >= 0 && _currentFPS <= 300)
                {
                    _displayedTextSB.Clear();
                    switch (Mode)
                    {
                        case Modes.Instant:
                            _displayedTextSB.Append(_fpsStrings[_currentFPS]);
                            break;
                        case Modes.MovingAverage:
                            _displayedTextSB.Append(_fpsStrings[_average]);
                            break;
                        case Modes.InstantAndMovingAverage:
                            _displayedTextSB.Append(_fpsStrings[_currentFPS] + " / " + _fpsStrings[_average]);
                            break;
                    }
                }

                UpdateForegroundColor();
            }
        }

        private void UpdateForegroundColor()
        {
            _foregroundColor = DefaultForegroundColor;
            if (FPSColors == null || FPSColors.Count == 0) return;

            int left = 0, right = FPSColors.Count - 1;
            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (_currentFPS < FPSColors[mid].FPSValue)
                {
                    _foregroundColor = FPSColors[mid].Color;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
        }

        /// <summary>
        /// Render FPS with outline effect in OnGUI
        /// </summary>
        private void OnGUI()
        {
            if (!IsVisible) return;

            if (_displayedTextSB.Length == 0)
            {
                return;
            }

            int shortestScreenSide = Mathf.Min(Screen.width, Screen.height);
            _style.fontSize = Mathf.RoundToInt(shortestScreenSide * _fontSizeRatio);
            _content.text = _displayedTextSB.ToString();
            Vector2 labelSize = _style.CalcSize(_content); // Calculate the actual width and height of the text

            // Set position
            _labelPosition = GetLabelPosition(labelSize);

            // Draw background (outline)
            _style.normal.textColor = OutlineColor;
            DrawOutlineText(_style, labelSize, -OutlineOffset.x, -OutlineOffset.y);
            DrawOutlineText(_style, labelSize, -OutlineOffset.x, OutlineOffset.y);
            DrawOutlineText(_style, labelSize, OutlineOffset.x, -OutlineOffset.y);
            DrawOutlineText(_style, labelSize, OutlineOffset.x, OutlineOffset.y);

            // Draw foreground (main text)
            _style.normal.textColor = _foregroundColor;
            GUI.Label(new Rect(_labelPosition.x, _labelPosition.y, labelSize.x, labelSize.y), _content, _style);
        }

        private void DrawOutlineText(GUIStyle style, Vector2 labelSize, float offsetX, float offsetY)
        {
            GUI.Label(
                new Rect(
                    _labelPosition.x + offsetX,
                    _labelPosition.y + offsetY,
                    labelSize.x,  // Use dynamic width
                    labelSize.y   // Use dynamic height
                ),
                _content,
                style
            );
        }

        /// <summary>
        /// Calculate the screen coordinates of the text based on the position preset
        /// </summary>
        private Vector2 GetLabelPosition(Vector2 labelSize)
        {
            Rect safeArea = AdjustForSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);
            float safeAreaXMin = safeArea.xMin;
            float safeAreaXMax = safeArea.xMax;
            float safeAreaYMin = safeArea.yMin;
            float safeAreaYMax = safeArea.yMax;

            switch (PositionPreset)
            {
                case ScreenPosition.TopLeft:
                    return new Vector2(safeAreaXMin + PresetPositionMargin, safeAreaYMin + PresetPositionMargin); // Top left with safe area
                case ScreenPosition.TopCenter:
                    return new Vector2((safeAreaXMin + safeAreaXMax) / 2 - (labelSize.x / 2), safeAreaYMin + PresetPositionMargin); // Top center with safe area
                case ScreenPosition.TopRight:
                    return new Vector2(safeAreaXMax - labelSize.x - PresetPositionMargin, safeAreaYMin + PresetPositionMargin); // Top right with safe area
                case ScreenPosition.MiddleLeft:
                    return new Vector2(safeAreaXMin + PresetPositionMargin, (safeAreaYMin + safeAreaYMax) / 2 - (labelSize.y / 2)); // Middle left with safe area
                case ScreenPosition.MiddleRight:
                    return new Vector2(safeAreaXMax - labelSize.x - PresetPositionMargin, (safeAreaYMin + safeAreaYMax) / 2 - (labelSize.y / 2)); // Middle right with safe area
                case ScreenPosition.BottomLeft:
                    return new Vector2(safeAreaXMin + PresetPositionMargin, safeAreaYMax - labelSize.y - PresetPositionMargin); // Bottom left with safe area
                case ScreenPosition.BottomCenter:
                    return new Vector2((safeAreaXMin + safeAreaXMax) / 2 - (labelSize.x / 2), safeAreaYMax - labelSize.y - PresetPositionMargin); // Bottom center with safe area
                case ScreenPosition.BottomRight:
                    return new Vector2(safeAreaXMax - labelSize.x - PresetPositionMargin, safeAreaYMax - labelSize.y - PresetPositionMargin); // Bottom right with safe area
                case ScreenPosition.Custom:
                    return CustomPosition; // Custom position (no safe area adjustment)
                default:
                    return new Vector2(safeAreaXMin + PresetPositionMargin, safeAreaYMin + PresetPositionMargin); // Default to top left with safe area
            }
        }
    }
}