using UnityEngine;
using System.Text;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Lightweight, 0GC (after initialization) FPS counter with IMGUI overlay.
    /// Supports safe area, outline text, color thresholds, and multiple display modes.
    /// Attach to any GameObject; optionally enable singleton + DontDestroyOnLoad.
    /// </summary>
    public class FPSCounter : MonoBehaviour
    {
        public enum Modes { Instant, MovingAverage, InstantAndMovingAverage }
        public enum ScreenPosition
        {
            TopLeft,
            TopCenter,
            TopRight,
            MiddleLeft,
            MiddleRight,
            BottomLeft,
            BottomCenter,
            BottomRight,
            Custom
        }

        [System.Serializable]
        public struct FPSColor
        {
            [Tooltip("When FPS falls below this value, the text changes to the associated color.")]
            public int FPSValue;

            [Tooltip("The color applied when FPS is below the threshold.")]
            public Color Color;
        }

        public static FPSCounter Instance { get; private set; }

        [Tooltip("If true, the FPS counter will be visible.")]
        public bool IsVisible = true;

        [Tooltip("If true, enforces singleton pattern and persists across scene loads.")]
        [SerializeField] private bool _singleton = true;

        [Header("Safe Area Settings")]
        [Tooltip("Adjust position to fit within the screen's safe area (notch, rounded corners, etc.).")]
        [SerializeField] private bool AdjustForSafeArea = true;
        [Tooltip("If true, the UI extends into the bottom safe area (behind Home Indicator on iOS).")]
        private bool extendIntoBottomSafeArea = true;
        [Tooltip("If true, the bottom inset is increased to match the top inset. Balances a top notch in portrait mode.")]
        public bool enforceVerticalSymmetry = true;
        [Tooltip("If true, left/right insets are matched to the larger of the two. Balances a notch in landscape mode.")]
        public bool enforceHorizontalSymmetry = true;

        [Space(10)]
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

        [Tooltip("FPS thresholds and colors. Sorted descending at runtime. When FPS < threshold, the corresponding color is used.")]
        [SerializeField] private FPSColor[] FPSColors = new FPSColor[0];

        private Color _foregroundColor;
        private float _framesAccumulated;
        private int _framesDrawnInTheInterval;
        private float _timeLeft;
        private int _currentFPS;
        private int _totalFrames;
        private float _averageAccumulator; // Float accumulator for precision
        private int _averageFPS;
        private readonly StringBuilder _displayedTextSB = new StringBuilder(16);
        private string _cachedDisplayText = string.Empty;
        private bool _displayDirty = true;
        private bool _useDirectString; // True when display string can be a pre-cached FpsString (0GC)
        private GUIStyle _style;
        private readonly GUIContent _content = new GUIContent();
        private float _fontSizeRatio = 0.04f;

        // Cached screen/safe-area state to avoid per-frame recalculation
        private int _cachedScreenWidth;
        private int _cachedScreenHeight;
        private Rect _cachedSafeArea;
        private Rect _computedSafeArea;
        private int _cachedFontSize;
        private bool _layoutCacheDirty = true;

        private const int FPS_MAX_CACHED = 999;
        private static readonly string[] FpsStrings = GenerateFpsStrings(FPS_MAX_CACHED);

        private static string[] GenerateFpsStrings(int max)
        {
            string[] arr = new string[max + 1];
            for (int i = 0; i <= max; i++)
            {
                arr[i] = i.ToString();
            }
            return arr;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        protected virtual void Start()
        {
            // Sort FPSColors descending by FPSValue for binary search
            System.Array.Sort(FPSColors, CompareFPSColorsDescending);
            _timeLeft = UpdateInterval;
            _foregroundColor = DefaultForegroundColor;
            _style = new GUIStyle();
            InvalidateLayoutCache();
        }

        private static int CompareFPSColorsDescending(FPSColor a, FPSColor b)
        {
            return b.FPSValue.CompareTo(a.FPSValue);
        }

        protected virtual void Update()
        {
            if (!IsVisible) return;

            _framesDrawnInTheInterval++;
            _framesAccumulated += 1f / Time.unscaledDeltaTime;
            _timeLeft -= Time.unscaledDeltaTime;

            if (_timeLeft <= 0f)
            {
                _currentFPS = _framesDrawnInTheInterval > 0
                    ? Mathf.Clamp(Mathf.RoundToInt(_framesAccumulated / _framesDrawnInTheInterval), 0, FPS_MAX_CACHED)
                    : 0;

                _framesDrawnInTheInterval = 0;
                _framesAccumulated = 0f;
                _timeLeft += UpdateInterval;

                // Cumulative moving average with float precision
                _totalFrames++;
                _averageAccumulator += (_currentFPS - _averageAccumulator) / _totalFrames;
                _averageFPS = Mathf.Clamp(Mathf.RoundToInt(_averageAccumulator), 0, FPS_MAX_CACHED);

                // Build display string (only on interval tick, not every frame)
                switch (Mode)
                {
                    case Modes.Instant:
                        _cachedDisplayText = FpsStrings[_currentFPS];
                        _useDirectString = true;
                        break;
                    case Modes.MovingAverage:
                        _cachedDisplayText = FpsStrings[_averageFPS];
                        _useDirectString = true;
                        break;
                    case Modes.InstantAndMovingAverage:
                        _displayedTextSB.Clear();
                        _displayedTextSB.Append(FpsStrings[_currentFPS]);
                        _displayedTextSB.Append(" / ");
                        _displayedTextSB.Append(FpsStrings[_averageFPS]);
                        _useDirectString = false;
                        break;
                }

                UpdateForegroundColor();
                _displayDirty = true;
            }
        }

        private void OnGUI()
        {
            if (!IsVisible) return;
            if (_style == null) return;

            // Detect screen/safe-area changes (rotation, resize, etc.)
            if (_cachedScreenWidth != Screen.width ||
                _cachedScreenHeight != Screen.height ||
                _cachedSafeArea != Screen.safeArea)
            {
                InvalidateLayoutCache();
            }

            if (_layoutCacheDirty)
            {
                RebuildLayoutCache();
            }

            // Only allocate a new string when the display content actually changed
            if (_displayDirty)
            {
                if (!_useDirectString)
                {
                    _cachedDisplayText = _displayedTextSB.ToString();
                }
                // _useDirectString: _cachedDisplayText already points to a pre-cached string (0GC)
                _content.text = _cachedDisplayText;
                _displayDirty = false;
            }

            Vector2 labelSize = _style.CalcSize(_content);
            Vector2 labelPos = GetLabelPosition(labelSize);

            // Draw outline (4 offset copies)
            _style.normal.textColor = OutlineColor;
            float ox = OutlineOffset.x, oy = OutlineOffset.y;
            GUI.Label(new Rect(labelPos.x - ox, labelPos.y - oy, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPos.x - ox, labelPos.y + oy, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPos.x + ox, labelPos.y - oy, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPos.x + ox, labelPos.y + oy, labelSize.x, labelSize.y), _content, _style);

            // Draw foreground
            _style.normal.textColor = _foregroundColor;
            GUI.Label(new Rect(labelPos.x, labelPos.y, labelSize.x, labelSize.y), _content, _style);
        }

        private void InvalidateLayoutCache()
        {
            _layoutCacheDirty = true;
        }

        private void RebuildLayoutCache()
        {
            _cachedScreenWidth = Screen.width;
            _cachedScreenHeight = Screen.height;
            _cachedSafeArea = Screen.safeArea;

            // Font size
            int shortSide = _cachedScreenWidth < _cachedScreenHeight ? _cachedScreenWidth : _cachedScreenHeight;
            _cachedFontSize = Mathf.Max(10, Mathf.RoundToInt(shortSide * _fontSizeRatio));
            _style.fontSize = _cachedFontSize;

            // Safe area
            _computedSafeArea = AdjustForSafeArea ? ComputeAdaptiveSafeArea() : new Rect(0, 0, _cachedScreenWidth, _cachedScreenHeight);

            _layoutCacheDirty = false;
        }

        /// <summary>
        /// Determines text color based on current FPS and thresholds.
        /// Uses binary search on descending-sorted FPSColors array.
        /// </summary>
        private void UpdateForegroundColor()
        {
            _foregroundColor = DefaultForegroundColor;
            if (FPSColors == null || FPSColors.Length == 0) return;

            // Binary search: find the tightest (lowest FPSValue) threshold that currentFPS is below.
            // FPSColors is sorted descending by FPSValue.
            int left = 0, right = FPSColors.Length - 1;
            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);
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

        private Vector2 GetLabelPosition(Vector2 labelSize)
        {
            Rect sa = _computedSafeArea;
            int m = PresetPositionMargin;

            switch (PositionPreset)
            {
                case ScreenPosition.TopLeft:
                    return new Vector2(sa.xMin + m, sa.yMin + m);
                case ScreenPosition.TopCenter:
                    return new Vector2(sa.xMin + (sa.width - labelSize.x) * 0.5f, sa.yMin + m);
                case ScreenPosition.TopRight:
                    return new Vector2(sa.xMax - labelSize.x - m, sa.yMin + m);
                case ScreenPosition.MiddleLeft:
                    return new Vector2(sa.xMin + m, sa.yMin + (sa.height - labelSize.y) * 0.5f);
                case ScreenPosition.MiddleRight:
                    return new Vector2(sa.xMax - labelSize.x - m, sa.yMin + (sa.height - labelSize.y) * 0.5f);
                case ScreenPosition.BottomLeft:
                    return new Vector2(sa.xMin + m, sa.yMax - labelSize.y - m);
                case ScreenPosition.BottomCenter:
                    return new Vector2(sa.xMin + (sa.width - labelSize.x) * 0.5f, sa.yMax - labelSize.y - m);
                case ScreenPosition.BottomRight:
                    return new Vector2(sa.xMax - labelSize.x - m, sa.yMax - labelSize.y - m);
                case ScreenPosition.Custom:
                    return CustomPosition;
                default:
                    return new Vector2(sa.xMin + m, sa.yMin + m);
            }
        }

        private Rect ComputeAdaptiveSafeArea()
        {
            Rect safeArea = _cachedSafeArea;
            float sw = _cachedScreenWidth;
            float sh = _cachedScreenHeight;

            float topInset = sh - safeArea.yMax;
            float bottomInset = safeArea.yMin;
            float leftInset = safeArea.xMin;
            float rightInset = sw - safeArea.xMax;

            // Step 1: Optionally reclaim bottom area (e.g. draw behind iOS Home Indicator)
            if (extendIntoBottomSafeArea)
            {
                bottomInset = 0;
            }

            // Step 2: Symmetry overrides (applied after step 1 so notch balance takes priority)
            if (enforceVerticalSymmetry)
            {
                float maxVertical = bottomInset > topInset ? bottomInset : topInset;
                bottomInset = maxVertical;
                // topInset remains unchanged — it's derived from safeArea.yMax
            }

            if (enforceHorizontalSymmetry)
            {
                float maxHorizontal = leftInset > rightInset ? leftInset : rightInset;
                leftInset = maxHorizontal;
                rightInset = maxHorizontal;
            }

            return new Rect(leftInset, bottomInset, sw - leftInset - rightInset, sh - bottomInset - topInset);
        }

        // --- Public API ---

        /// <summary>
        /// Toggles FPS counter visibility. Safe to call from anywhere.
        /// </summary>
        public void SetVisible(bool visible)
        {
            IsVisible = visible;
        }

        /// <summary>
        /// Resets the cumulative average FPS counter.
        /// </summary>
        public void ResetAverage()
        {
            _totalFrames = 0;
            _averageAccumulator = 0f;
            _averageFPS = 0;
        }

        /// <summary>
        /// Gets the current instantaneous FPS value.
        /// </summary>
        public int CurrentFPS => _currentFPS;

        /// <summary>
        /// Gets the cumulative average FPS value.
        /// </summary>
        public int AverageFPS => _averageFPS;
    }
}