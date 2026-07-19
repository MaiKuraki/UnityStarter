using System;
using System.Globalization;
using System.Text;

using UnityEngine;
using UnityEngine.Serialization;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Explicitly owned IMGUI frame-rate diagnostic overlay.
    /// </summary>
    /// <remarks>
    /// Sampling performs no recurring collection growth. IMGUI and combined text mode can still allocate;
    /// use this component as a diagnostic tool rather than production presentation UI.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FPSCounter : MonoBehaviour
    {
        public enum Modes
        {
            Instant = 0,
            MovingAverage = 1,
            InstantAndMovingAverage = 2
        }

        public enum ScreenPosition
        {
            TopLeft = 0,
            TopCenter = 1,
            TopRight = 2,
            MiddleLeft = 3,
            MiddleRight = 4,
            BottomLeft = 5,
            BottomCenter = 6,
            BottomRight = 7,
            Custom = 8
        }

        [Serializable]
        public struct FPSColor
        {
            [Min(0)]
            [Tooltip("The color is used when the sampled FPS is below this value.")]
            public int FPSValue;

            public Color Color;
        }

        private const int MinimumMovingAverageSamples = 1;
        private const int MaximumMovingAverageSamples = 120;
        private const int MaximumDisplayedFps = 9999;
        private const int CachedFpsLimit = 999;

        private static readonly string[] FpsStringCache = new string[CachedFpsLimit + 1];

        [FormerlySerializedAs("IsVisible")]
        [SerializeField]
        private bool _isVisible = true;

        [FormerlySerializedAs("_singleton")]
        [SerializeField]
        [Tooltip("Moves this component's entire GameObject to DontDestroyOnLoad. Use a dedicated owner GameObject.")]
        private bool _persistAcrossScenes;

        [FormerlySerializedAs("AdjustForSafeArea")]
        [SerializeField]
        private bool _adjustForSafeArea = true;

        [FormerlySerializedAs("extendIntoBottomSafeArea")]
        [SerializeField]
        private bool _extendIntoBottomSafeArea = true;

        [FormerlySerializedAs("enforceVerticalSymmetry")]
        [SerializeField]
        private bool _enforceVerticalSymmetry = true;

        [FormerlySerializedAs("enforceHorizontalSymmetry")]
        [SerializeField]
        private bool _enforceHorizontalSymmetry = true;

        [FormerlySerializedAs("UpdateInterval")]
        [SerializeField, Min(0.05f)]
        private float _updateInterval = 0.3f;

        [SerializeField, Range(MinimumMovingAverageSamples, MaximumMovingAverageSamples)]
        private int _movingAverageSampleCount = 30;

        [FormerlySerializedAs("Mode")]
        [SerializeField]
        private Modes _mode = Modes.Instant;

        [FormerlySerializedAs("DefaultForegroundColor")]
        [SerializeField]
        private Color _defaultForegroundColor = Color.green;

        [FormerlySerializedAs("OutlineColor")]
        [SerializeField]
        private Color _outlineColor = Color.black;

        [FormerlySerializedAs("OutlineOffset")]
        [SerializeField]
        private Vector2 _outlineOffset = new Vector2(1f, 1f);

        [FormerlySerializedAs("PositionPreset")]
        [SerializeField]
        private ScreenPosition _positionPreset = ScreenPosition.TopLeft;

        [FormerlySerializedAs("PresetPositionMargin")]
        [SerializeField, Min(0)]
        private int _presetPositionMargin = 10;

        [FormerlySerializedAs("CustomPosition")]
        [SerializeField]
        private Vector2 _customPosition;

        [SerializeField, Range(0.01f, 0.1f)]
        private float _fontSizeRatio = 0.04f;

        [FormerlySerializedAs("FPSColors")]
        [SerializeField]
        private FPSColor[] _fpsColors = Array.Empty<FPSColor>();

        private readonly StringBuilder _displayBuilder = new StringBuilder(24);
        private readonly GUIContent _content = new GUIContent();
        private SortedFpsColor[] _sortedFpsColors = Array.Empty<SortedFpsColor>();
        private int[] _movingAverageSamples = Array.Empty<int>();
        private GUIStyle _style;
        private Color _foregroundColor;
        private string _cachedDisplayText = string.Empty;
        private int _cachedInstantValue = -1;
        private string _cachedInstantText;
        private int _cachedAverageValue = -1;
        private string _cachedAverageText;
        private bool _displayDirty = true;
        private bool _configurationDirty = true;

        private double _intervalElapsed;
        private int _framesInInterval;
        private long _movingAverageSum;
        private int _movingAverageCount;
        private int _movingAverageWriteIndex;
        private int _currentFps;
        private int _averageFps;

        private int _cachedScreenWidth;
        private int _cachedScreenHeight;
        private Rect _cachedSafeArea;
        private Rect _computedGuiArea;
        private bool _layoutCacheDirty = true;

        /// <summary>
        /// Gets or sets whether the overlay is visible and sampling is active.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public int CurrentFPS => _currentFps;
        public int AverageFPS => _averageFps;

        [Obsolete("Use the Inspector safe-area settings or a dedicated presentation adapter.")]
        public bool enforceVerticalSymmetry
        {
            get => _enforceVerticalSymmetry;
            set
            {
                _enforceVerticalSymmetry = value;
                InvalidateLayoutCache();
            }
        }

        [Obsolete("Use the Inspector safe-area settings or a dedicated presentation adapter.")]
        public bool enforceHorizontalSymmetry
        {
            get => _enforceHorizontalSymmetry;
            set
            {
                _enforceHorizontalSymmetry = value;
                InvalidateLayoutCache();
            }
        }

        private void Awake()
        {
            if (_persistAcrossScenes && Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            if (_style == null)
            {
                _style = new GUIStyle();
            }

            ConfigureCaches();
            ResetSampling();
            _foregroundColor = _defaultForegroundColor;
            _content.text = string.Empty;
            InvalidateLayoutCache();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _updateInterval = SanitizeUpdateInterval(_updateInterval);
            _movingAverageSampleCount = Mathf.Clamp(
                _movingAverageSampleCount,
                MinimumMovingAverageSamples,
                MaximumMovingAverageSamples);
            _presetPositionMargin = Mathf.Max(0, _presetPositionMargin);
            _fontSizeRatio = Mathf.Clamp(_fontSizeRatio, 0.01f, 0.1f);

            _configurationDirty = true;
            InvalidateLayoutCache();
        }
#endif

        private void Update()
        {
            if (_configurationDirty)
            {
                ConfigureCaches();
            }

            if (!_isVisible)
            {
                return;
            }

            SampleFrame(Time.unscaledDeltaTime);
        }

        internal void SampleFrame(float unscaledDeltaTime)
        {
            if (!(unscaledDeltaTime > 0f) || float.IsNaN(unscaledDeltaTime) || float.IsInfinity(unscaledDeltaTime))
            {
                return;
            }

            _framesInInterval++;
            _intervalElapsed += unscaledDeltaTime;
            double interval = SanitizeUpdateInterval(_updateInterval);
            if (_intervalElapsed < interval)
            {
                return;
            }

            double sampledFps = _framesInInterval / _intervalElapsed;
            _currentFps = sampledFps >= MaximumDisplayedFps
                ? MaximumDisplayedFps
                : (int)Math.Max(0d, Math.Round(sampledFps));
            _framesInInterval = 0;
            _intervalElapsed = 0d;

            AddMovingAverageSample(_currentFps);
            UpdateForegroundColor();
            RebuildDisplayText();
        }

        private void OnGUI()
        {
            if (!_isVisible || _style == null)
            {
                return;
            }

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

            if (_displayDirty)
            {
                _content.text = _cachedDisplayText;
                _displayDirty = false;
            }

            Vector2 labelSize = _style.CalcSize(_content);
            Vector2 labelPosition = GetLabelPosition(labelSize);
            float outlineX = _outlineOffset.x;
            float outlineY = _outlineOffset.y;

            _style.normal.textColor = _outlineColor;
            GUI.Label(new Rect(labelPosition.x - outlineX, labelPosition.y - outlineY, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPosition.x - outlineX, labelPosition.y + outlineY, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPosition.x + outlineX, labelPosition.y - outlineY, labelSize.x, labelSize.y), _content, _style);
            GUI.Label(new Rect(labelPosition.x + outlineX, labelPosition.y + outlineY, labelSize.x, labelSize.y), _content, _style);

            _style.normal.textColor = _foregroundColor;
            GUI.Label(new Rect(labelPosition.x, labelPosition.y, labelSize.x, labelSize.y), _content, _style);
        }

        /// <summary>
        /// Enables or disables visibility and sampling.
        /// </summary>
        public void SetVisible(bool visible)
        {
            _isVisible = visible;
        }

        /// <summary>
        /// Clears the bounded moving-average window without changing the current sample.
        /// </summary>
        public void ResetAverage()
        {
            if (_movingAverageSamples.Length > 0)
            {
                Array.Clear(_movingAverageSamples, 0, _movingAverageSamples.Length);
            }

            _movingAverageSum = 0L;
            _movingAverageCount = 0;
            _movingAverageWriteIndex = 0;
            _averageFps = 0;
            RebuildDisplayText();
        }

        private void ConfigureCaches()
        {
            int sampleCount = Mathf.Clamp(
                _movingAverageSampleCount,
                MinimumMovingAverageSamples,
                MaximumMovingAverageSamples);
            if (_movingAverageSamples.Length != sampleCount)
            {
                _movingAverageSamples = new int[sampleCount];
                _movingAverageSum = 0L;
                _movingAverageCount = 0;
                _movingAverageWriteIndex = 0;
                _averageFps = 0;
            }

            int colorCount = _fpsColors == null ? 0 : _fpsColors.Length;
            if (_sortedFpsColors.Length != colorCount)
            {
                _sortedFpsColors = colorCount == 0
                    ? Array.Empty<SortedFpsColor>()
                    : new SortedFpsColor[colorCount];
            }

            if (colorCount > 0)
            {
                for (int i = 0; i < colorCount; i++)
                {
                    _sortedFpsColors[i] = new SortedFpsColor(_fpsColors[i], i);
                }

                Array.Sort(_sortedFpsColors, SortedFpsColorComparer.Instance);
            }

            UpdateForegroundColor();
            _configurationDirty = false;
        }

        private void ResetSampling()
        {
            _intervalElapsed = 0d;
            _framesInInterval = 0;
            _currentFps = 0;
            ResetAverage();
        }

        private void AddMovingAverageSample(int sample)
        {
            if (_movingAverageSamples.Length == 0)
            {
                _averageFps = sample;
                return;
            }

            if (_movingAverageCount == _movingAverageSamples.Length)
            {
                _movingAverageSum -= _movingAverageSamples[_movingAverageWriteIndex];
            }
            else
            {
                _movingAverageCount++;
            }

            _movingAverageSamples[_movingAverageWriteIndex] = sample;
            _movingAverageSum += sample;
            _movingAverageWriteIndex++;
            if (_movingAverageWriteIndex == _movingAverageSamples.Length)
            {
                _movingAverageWriteIndex = 0;
            }

            _averageFps = _movingAverageCount == 0
                ? 0
                : (int)Math.Round((double)_movingAverageSum / _movingAverageCount);
        }

        private void RebuildDisplayText()
        {
            string instantText = GetValueText(_currentFps, ref _cachedInstantValue, ref _cachedInstantText);
            string averageText = GetValueText(_averageFps, ref _cachedAverageValue, ref _cachedAverageText);

            switch (_mode)
            {
                case Modes.Instant:
                    _cachedDisplayText = instantText;
                    break;
                case Modes.MovingAverage:
                    _cachedDisplayText = averageText;
                    break;
                case Modes.InstantAndMovingAverage:
                    _displayBuilder.Clear();
                    _displayBuilder.Append(instantText);
                    _displayBuilder.Append(" / ");
                    _displayBuilder.Append(averageText);
                    _cachedDisplayText = _displayBuilder.ToString();
                    break;
                default:
                    _cachedDisplayText = instantText;
                    break;
            }

            _displayDirty = true;
        }

        private static string GetValueText(int value, ref int cachedValue, ref string cachedText)
        {
            if (cachedValue == value && cachedText != null)
            {
                return cachedText;
            }

            cachedValue = value;
            if ((uint)value <= CachedFpsLimit)
            {
                string shared = FpsStringCache[value];
                if (shared == null)
                {
                    shared = value.ToString(CultureInfo.InvariantCulture);
                    FpsStringCache[value] = shared;
                }

                cachedText = shared;
                return shared;
            }

            cachedText = value.ToString(CultureInfo.InvariantCulture);
            return cachedText;
        }

        private void UpdateForegroundColor()
        {
            _foregroundColor = _defaultForegroundColor;
            for (int i = 0; i < _sortedFpsColors.Length; i++)
            {
                FPSColor threshold = _sortedFpsColors[i].Value;
                if (_currentFps < threshold.FPSValue)
                {
                    _foregroundColor = threshold.Color;
                }
            }
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

            int shortSide = Mathf.Min(_cachedScreenWidth, _cachedScreenHeight);
            _style.fontSize = Mathf.Max(10, Mathf.RoundToInt(shortSide * _fontSizeRatio));

            Rect pixelArea;
            if (_adjustForSafeArea && _cachedScreenWidth > 0 && _cachedScreenHeight > 0)
            {
                SafeAreaPolicy policy = new SafeAreaPolicy(
                    _extendIntoBottomSafeArea,
                    _enforceVerticalSymmetry,
                    _enforceHorizontalSymmetry);
                pixelArea = SafeAreaUtility.CalculatePixelRect(
                    _cachedSafeArea,
                    _cachedScreenWidth,
                    _cachedScreenHeight,
                    in policy);
            }
            else
            {
                pixelArea = new Rect(0f, 0f, _cachedScreenWidth, _cachedScreenHeight);
            }

            _computedGuiArea = SafeAreaUtility.ToGuiRect(pixelArea, _cachedScreenHeight);
            _layoutCacheDirty = false;
        }

        private Vector2 GetLabelPosition(Vector2 labelSize)
        {
            Rect area = _computedGuiArea;
            int margin = _presetPositionMargin;
            switch (_positionPreset)
            {
                case ScreenPosition.TopLeft:
                    return new Vector2(area.xMin + margin, area.yMin + margin);
                case ScreenPosition.TopCenter:
                    return new Vector2(area.xMin + (area.width - labelSize.x) * 0.5f, area.yMin + margin);
                case ScreenPosition.TopRight:
                    return new Vector2(area.xMax - labelSize.x - margin, area.yMin + margin);
                case ScreenPosition.MiddleLeft:
                    return new Vector2(area.xMin + margin, area.yMin + (area.height - labelSize.y) * 0.5f);
                case ScreenPosition.MiddleRight:
                    return new Vector2(area.xMax - labelSize.x - margin, area.yMin + (area.height - labelSize.y) * 0.5f);
                case ScreenPosition.BottomLeft:
                    return new Vector2(area.xMin + margin, area.yMax - labelSize.y - margin);
                case ScreenPosition.BottomCenter:
                    return new Vector2(area.xMin + (area.width - labelSize.x) * 0.5f, area.yMax - labelSize.y - margin);
                case ScreenPosition.BottomRight:
                    return new Vector2(area.xMax - labelSize.x - margin, area.yMax - labelSize.y - margin);
                case ScreenPosition.Custom:
                    return _customPosition;
                default:
                    return new Vector2(area.xMin + margin, area.yMin + margin);
            }
        }

        private readonly struct SortedFpsColor
        {
            public readonly FPSColor Value;
            public readonly int SourceIndex;

            public SortedFpsColor(FPSColor value, int sourceIndex)
            {
                Value = value;
                SourceIndex = sourceIndex;
            }
        }

        private sealed class SortedFpsColorComparer : System.Collections.Generic.IComparer<SortedFpsColor>
        {
            public static readonly SortedFpsColorComparer Instance = new SortedFpsColorComparer();

            private SortedFpsColorComparer()
            {
            }

            public int Compare(SortedFpsColor x, SortedFpsColor y)
            {
                int thresholdComparison = y.Value.FPSValue.CompareTo(x.Value.FPSValue);
                return thresholdComparison != 0
                    ? thresholdComparison
                    : x.SourceIndex.CompareTo(y.SourceIndex);
            }
        }

        private static float SanitizeUpdateInterval(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0.3f : Mathf.Max(0.05f, value);
        }
    }
}
