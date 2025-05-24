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

        // Struct to define FPS thresholds and corresponding colors
        [System.Serializable]
        public struct FPSColor
        {
            [Tooltip("The FPS threshold. When the current FPS is below this value, the text color will change to the associated color.")]
            public int FPSValue; // The FPS threshold

            [Tooltip("The color that will be used when the current FPS is below the threshold.")]
            public Color Color; // The corresponding color
        }

        public static FPSCounter Instance { get; private set; }

        [Tooltip("If true, the FPS counter will be visible by default.")]
        public bool IsVisible = true;

        [Tooltip("If true, this component will enforce a singleton pattern and persist across scene loads.")]
        [SerializeField] private bool _singleton = true;

        [Tooltip("If true, the position of the FPS counter will be adjusted to fit within the screen's safe area.")]
        [SerializeField] private bool AdjustForSafeArea = true;

        [Tooltip("The interval (in seconds) at which the FPS display is updated.")]
        [SerializeField] private float UpdateInterval = 0.3f;

        [Tooltip("Determines what FPS value is displayed: instantaneous, a moving average, or both.")]
        [SerializeField] private Modes Mode = Modes.Instant;

        [Tooltip("The default color for the FPS text.")]
        [SerializeField] private Color DefaultForegroundColor = Color.green;

        [Tooltip("The color used for the text outline effect.")]
        [SerializeField] private Color OutlineColor = Color.black;

        [Tooltip("The offset for the text outline effect. (1,1) usually looks good.")]
        [SerializeField] private Vector2 OutlineOffset = new Vector2(1, 1);

        [Tooltip("Predefined screen position for the FPS counter.")]
        [SerializeField] private ScreenPosition PositionPreset = ScreenPosition.TopLeft;

        [Tooltip("Margin from the screen edges or safe area boundaries (in pixels).")]
        [SerializeField] private int PresetPositionMargin = 10;

        [Tooltip("Custom screen position (in pixels) if PositionPreset is set to Custom. (0,0) is top-left.")]
        [SerializeField] private Vector2 CustomPosition = new Vector2(0, 0);

        [Tooltip("A list of FPS thresholds and their corresponding colors. Sorted by FPSValue in descending order. When the current FPS is below a threshold, the text color will change to the associated color.")]
        [SerializeField] private List<FPSColor> FPSColors = new List<FPSColor>();

        private Color _foregroundColor;
        private float _framesAccumulated = 0f;
        private float _framesDrawnInTheInterval = 0f;
        private float _timeLeft;
        private int _currentFPS;
        private int _totalFrames = 0; // For calculating the cumulative average FPS
        private int _averageFPS;      // Cumulative average FPS
        private readonly StringBuilder _displayedTextSB = new StringBuilder(16); // Initial capacity for "XXX / XXX"
        private Vector2 _labelPosition;
        private GUIStyle _style = new GUIStyle();
        private GUIContent _content = new GUIContent();
        private float _fontSizeRatio = 0.04f; // Relative to the shortest screen side

        private const int FPS_MAX_CACHED = 300; // Maximum FPS value for which strings are pre-cached
        private static string[] _fpsStrings = GenerateFPSTextArray(FPS_MAX_CACHED);

        // Pre-generates an array of formatted FPS strings for performance
        private static string[] GenerateFPSTextArray(int maxFPS)
        {
            string[] array = new string[maxFPS + 1];
            for (int i = 0; i <= maxFPS; i++)
            {
                if (i < 10)
                {
                    array[i] = "0" + i.ToString(); // Ensures two digits for 0-9, e.g., "07"
                }
                else if (i < 100)
                {
                    array[i] = i.ToString("00"); // Ensures two digits, e.g., "99"
                }
                else
                {
                    array[i] = i.ToString(); // Three digits or more, e.g., "120"
                }
            }
            return array;
        }

        void Awake()
        {
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void Start()
        {
            // Sort FPSColors by FPSValue in descending order to ensure correct color selection logic
            FPSColors.Sort((a, b) => b.FPSValue.CompareTo(a.FPSValue));

            _timeLeft = UpdateInterval;
            UpdateFontDetails(); // Initialize font size
            _foregroundColor = DefaultForegroundColor;
        }

        protected virtual void Update()
        {
            if (!IsVisible) return;

            _framesDrawnInTheInterval++;
            _framesAccumulated += Time.timeScale / Time.deltaTime; // Using unscaledDeltaTime would be better if timeScale can be 0
            _timeLeft -= Time.deltaTime;

            if (_timeLeft <= 0.0f)
            {
                // Calculate current FPS, clamped between 0 and FPS_MAX_CACHED
                _currentFPS = Mathf.RoundToInt(Mathf.Clamp(_framesAccumulated / _framesDrawnInTheInterval, 0, FPS_MAX_CACHED));

                // Reset counters for the next interval
                _framesDrawnInTheInterval = 0;
                _framesAccumulated = 0f;
                _timeLeft += UpdateInterval; // Add UpdateInterval to maintain timing accuracy

                // Update cumulative average FPS
                _totalFrames++;
                _averageFPS += (_currentFPS - _averageFPS) / _totalFrames; // Standard cumulative moving average
                _averageFPS = Mathf.Clamp(_averageFPS, 0, FPS_MAX_CACHED); // Ensure average is also clamped

                // Build the display string
                _displayedTextSB.Clear();
                switch (Mode)
                {
                    case Modes.Instant:
                        _displayedTextSB.Append(_fpsStrings[_currentFPS]);
                        break;
                    case Modes.MovingAverage:
                        _displayedTextSB.Append(_fpsStrings[_averageFPS]);
                        break;
                    case Modes.InstantAndMovingAverage:
                        _displayedTextSB.Append(_fpsStrings[_currentFPS]);
                        _displayedTextSB.Append(" / "); // Optimized: Use multiple appends
                        _displayedTextSB.Append(_fpsStrings[_averageFPS]);
                        break;
                }
                UpdateForegroundColor();
            }
        }

        // Updates the font size based on screen dimensions
        private void UpdateFontDetails()
        {
            int shortestScreenSide = Mathf.Min(Screen.width, Screen.height);
            _style.fontSize = Mathf.Max(10, Mathf.RoundToInt(shortestScreenSide * _fontSizeRatio)); // Ensure a minimum font size
            // _style.alignment can be set here if needed, e.g., TextAnchor.UpperLeft
        }

        // Determines the appropriate text color based on the current FPS and defined thresholds
        private void UpdateForegroundColor()
        {
            _foregroundColor = DefaultForegroundColor; // Default to green
            if (FPSColors == null || FPSColors.Count == 0) return;

            // Binary search for the correct color (list is sorted descending by FPSValue)
            // We want the color for the highest threshold that _currentFPS is below.
            int selectedColorIndex = -1;
            for(int i = 0; i < FPSColors.Count; ++i)
            {
                if (_currentFPS < FPSColors[i].FPSValue)
                {
                    selectedColorIndex = i; // This threshold applies
                }
                else
                {
                    // Since the list is sorted descending, if currentFPS is not below this threshold,
                    // it won't be below any subsequent (lower value) thresholds either.
                    // However, we want the "last" applicable threshold if multiple apply.
                    // The original binary search was slightly more complex to read.
                    // A linear scan from highest threshold (lowest FPSValue in sorted list) to lowest is simpler.
                    // Let's stick to the provided binary search as it was correct, just re-verify.

                    // Original logic was:
                    // if _currentFPS < FPSColors[mid].FPSValue -> _foregroundColor = FPSColors[mid].Color; left = mid + 1; (Means: this color applies, try even lower FPS thresholds)
                    // else -> right = mid - 1; (Means: this threshold is too high or FPS is good, try higher FPS thresholds)
                    // This will find the "lowest" FPSValue in the sorted list (which is highest actual FPS value) that _currentFPS is still less than.
                    // Correct logic should be: find the color for the *first* (i.e., highest value) threshold that currentFPS is *less than*.
                    // The list is sorted descending: e.g., [ (50, Yellow), (30, Orange), (15, Red) ]
                    // If FPS = 25:
                    //  - It's < 50 (Yellow applies).
                    //  - It's < 30 (Orange applies). We want Orange.
                    // The provided binary search actually correctly finds the "last" match in the sorted (descending) list that `_currentFPS < FPSValue` holds, which is what we want.
                }
            }

            // Re-implementing the binary search as it was correct and efficient.
            // FPSColors is sorted descending by FPSValue.
            // We want the color for the highest FPSValue threshold that _currentFPS is *strictly less than*.
            // If _currentFPS is 25, and thresholds are 50(Yellow), 20(Red).
            // _currentFPS < 50 (Yellow)
            // _currentFPS is not < 20.
            // The binary search correctly finds the tightest bound.
            int left = 0, right = FPSColors.Count - 1;
            // int bestMatchIndex = -1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2; // Avoid potential overflow with (left + right) / 2
                if (_currentFPS < FPSColors[mid].FPSValue)
                {
                    // This color is a candidate. Store it and try to find an even "tighter" (lower FPSValue) threshold.
                    _foregroundColor = FPSColors[mid].Color;
                    // Because FPSColors is sorted descending, a lower FPSValue is at a higher index.
                    // So we search in the right part of the array for a potentially more specific (lower) threshold.
                    left = mid + 1;
                }
                else
                {
                    // _currentFPS is not below this threshold, so this threshold (and any lower FPSValues / higher indices) are too strict.
                    // We need to look for higher FPSValues (lower indices).
                    right = mid - 1;
                }
            }
            // If no threshold was met, _foregroundColor remains DefaultForegroundColor.
        }


        private void OnGUI()
        {
            if (!IsVisible) return;
            if (_displayedTextSB == null || _displayedTextSB.Length == 0) return; // Should not happen after first update

            // Update font size dynamically if screen resolution changes (e.g., window resize)
            // This check can be more sophisticated if needed (e.g., cache Screen.width/height and only update on change)
            UpdateFontDetails();

            _content.text = _displayedTextSB.ToString(); // This allocates a string, but only when text changes
            Vector2 labelSize = _style.CalcSize(_content);

            _labelPosition = GetLabelPosition(labelSize);

            // Draw outline (background)
            _style.normal.textColor = OutlineColor;
            DrawOutlineText(_style, labelSize, -OutlineOffset.x, -OutlineOffset.y);
            DrawOutlineText(_style, labelSize, -OutlineOffset.x, OutlineOffset.y);
            DrawOutlineText(_style, labelSize, OutlineOffset.x, -OutlineOffset.y);
            DrawOutlineText(_style, labelSize, OutlineOffset.x, OutlineOffset.y);

            // Draw foreground (main text)
            _style.normal.textColor = _foregroundColor;
            GUI.Label(new Rect(_labelPosition.x, _labelPosition.y, labelSize.x, labelSize.y), _content, _style);
        }

        // Helper method to draw text with an offset, used for the outline effect
        private void DrawOutlineText(GUIStyle style, Vector2 labelSize, float offsetX, float offsetY)
        {
            GUI.Label(
                new Rect(
                    _labelPosition.x + offsetX,
                    _labelPosition.y + offsetY,
                    labelSize.x,
                    labelSize.y
                ),
                _content,
                style
            );
        }

        // Calculates the screen position for the FPS counter label
        private Vector2 GetLabelPosition(Vector2 labelSize)
        {
            Rect safeArea = AdjustForSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);

            // Use properties of safeArea for calculations
            float xPos = 0, yPos = 0;

            switch (PositionPreset)
            {
                case ScreenPosition.TopLeft:
                    xPos = safeArea.xMin + PresetPositionMargin;
                    yPos = safeArea.yMin + PresetPositionMargin;
                    break;
                case ScreenPosition.TopCenter:
                    xPos = safeArea.xMin + (safeArea.width - labelSize.x) / 2;
                    yPos = safeArea.yMin + PresetPositionMargin;
                    break;
                case ScreenPosition.TopRight:
                    xPos = safeArea.xMax - labelSize.x - PresetPositionMargin;
                    yPos = safeArea.yMin + PresetPositionMargin;
                    break;
                case ScreenPosition.MiddleLeft:
                    xPos = safeArea.xMin + PresetPositionMargin;
                    yPos = safeArea.yMin + (safeArea.height - labelSize.y) / 2;
                    break;
                case ScreenPosition.MiddleRight:
                    xPos = safeArea.xMax - labelSize.x - PresetPositionMargin;
                    yPos = safeArea.yMin + (safeArea.height - labelSize.y) / 2;
                    break;
                case ScreenPosition.BottomLeft:
                    xPos = safeArea.xMin + PresetPositionMargin;
                    yPos = safeArea.yMax - labelSize.y - PresetPositionMargin;
                    break;
                case ScreenPosition.BottomCenter:
                    xPos = safeArea.xMin + (safeArea.width - labelSize.x) / 2;
                    yPos = safeArea.yMax - labelSize.y - PresetPositionMargin;
                    break;
                case ScreenPosition.BottomRight:
                    xPos = safeArea.xMax - labelSize.x - PresetPositionMargin;
                    yPos = safeArea.yMax - labelSize.y - PresetPositionMargin;
                    break;
                case ScreenPosition.Custom:
                    // For custom position, safe area is not automatically applied.
                    // User is responsible for ensuring it's visible.
                    return CustomPosition;
            }
            return new Vector2(xPos, yPos);
        }
    }
}