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
        public bool IsVisible = true;   // sometimes, you dont want to show the fps by default, uncheck this to hide the FPSCounter
        public bool AdjustForSafeArea = true; // Option to adjust position for safe area
        public float UpdateInterval = 0.3f;
        public Modes Mode = Modes.Instant;
        public Color DefaultForegroundColor = Color.green; // Default foreground color
        private Color ForegroundColor;
        public Color OutlineColor = Color.black; // Background color
        public Vector2 OutlineOffset = new Vector2(1, 1); // Offset for simulating text outline
        public ScreenPosition PositionPreset = ScreenPosition.TopLeft; // Preset for screen position
        public int PresetPositionMargin = 10; // Margin
        public Vector2 CustomPosition = new Vector2(0, 0); // Coordinates for a custom position

        // Dictionary to hold FPS thresholds and corresponding colors
        [System.Serializable]
        public struct FPSColor
        {
            [Tooltip("The FPS threshold. When the current FPS is below this value, the text color will change to the associated color.")]
            public int FPSValue; // The FPS threshold

            [Tooltip("The color that will be used when the current FPS is below the threshold.")]
            public Color Color; // The corresponding color
        }

        [Tooltip("A list of FPS thresholds and their corresponding colors. When the current FPS is below a threshold, the text color will change to the associated color.")]
        public List<FPSColor> FPSColors = new List<FPSColor>(); // List of FPS thresholds and colors

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
        private Color? foundColor = null; // Color in the 'FPSColors' list
        private float _fontSizeRatio = 0.04f;

        static string[] _stringsFrom00To300 = {
            "00", "01", "02", "03", "04", "05", "06", "07", "08", "09",
            "10", "11", "12", "13", "14", "15", "16", "17", "18", "19",
            "20", "21", "22", "23", "24", "25", "26", "27", "28", "29",
            "30", "31", "32", "33", "34", "35", "36", "37", "38", "39",
            "40", "41", "42", "43", "44", "45", "46", "47", "48", "49",
            "50", "51", "52", "53", "54", "55", "56", "57", "58", "59",
            "60", "61", "62", "63", "64", "65", "66", "67", "68", "69",
            "70", "71", "72", "73", "74", "75", "76", "77", "78", "79",
            "80", "81", "82", "83", "84", "85", "86", "87", "88", "89",
            "90", "91", "92", "93", "94", "95", "96", "97", "98", "99",
            "100", "101", "102", "103", "104", "105", "106", "107", "108", "109",
            "110", "111", "112", "113", "114", "115", "116", "117", "118", "119",
            "120", "121", "122", "123", "124", "125", "126", "127", "128", "129",
            "130", "131", "132", "133", "134", "135", "136", "137", "138", "139",
            "140", "141", "142", "143", "144", "145", "146", "147", "148", "149",
            "150", "151", "152", "153", "154", "155", "156", "157", "158", "159",
            "160", "161", "162", "163", "164", "165", "166", "167", "168", "169",
            "170", "171", "172", "173", "174", "175", "176", "177", "178", "179",
            "180", "181", "182", "183", "184", "185", "186", "187", "188", "189",
            "190", "191", "192", "193", "194", "195", "196", "197", "198", "199",
            "200", "201", "202", "203", "204", "205", "206", "207", "208", "209",
            "210", "211", "212", "213", "214", "215", "216", "217", "218", "219",
            "220", "221", "222", "223", "224", "225", "226", "227", "228", "229",
            "230", "231", "232", "233", "234", "235", "236", "237", "238", "239",
            "240", "241", "242", "243", "244", "245", "246", "247", "248", "249",
            "250", "251", "252", "253", "254", "255", "256", "257", "258", "259",
            "260", "261", "262", "263", "264", "265", "266", "267", "268", "269",
            "270", "271", "272", "273", "274", "275", "276", "277", "278", "279",
            "280", "281", "282", "283", "284", "285", "286", "287", "288", "289",
            "290", "291", "292", "293", "294", "295", "296", "297", "298", "299",
            "300"
        };

        void Awake()
        {
            MakeUnique();
        }

        /// <summary>
        /// Initialize the counter on Start()
        /// </summary>
        protected virtual void Start()
        {
            MakeUnique();

            _timeLeft = UpdateInterval;
            int shortestScreenSide = Mathf.Min(Screen.width, Screen.height);
            _style.fontSize = Mathf.RoundToInt(shortestScreenSide * _fontSizeRatio);
            ForegroundColor = DefaultForegroundColor;
        }

        void OnEnable()
        {
            MakeUnique();
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
                _currentFPS = Mathf.RoundToInt(Mathf.Clamp(_framesAccumulated / _framesDrawnInTheInterval, 0, 300));
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
                            _displayedTextSB.Append(_stringsFrom00To300[_currentFPS]);
                            break;
                        case Modes.MovingAverage:
                            _displayedTextSB.Append(_stringsFrom00To300[_average]);
                            break;
                        case Modes.InstantAndMovingAverage:
                            _displayedTextSB.Append(_stringsFrom00To300[_currentFPS] + " / " + _stringsFrom00To300[_average]);
                            break;
                    }
                }

                UpdateForegroundColor();
            }
        }

        private void MakeUnique()
        {
            var fpsCounters = GameObject.FindObjectsByType<FPSCounter>(FindObjectsSortMode.None);
            int amount = 0;
            if (fpsCounters != null) amount = fpsCounters.Length;
            if (amount > 1) GameObject.Destroy(gameObject);
        }

        private void UpdateForegroundColor()
        {
            if (FPSColors == null || FPSColors.Count == 0)
            {
                ForegroundColor = DefaultForegroundColor;
                return;
            }

            // Initialize ForegroundColor to the default color
            ForegroundColor = DefaultForegroundColor;

            foundColor = null;
            foreach (var fpsColor in FPSColors)
            {
                if (_currentFPS < fpsColor.FPSValue)
                {
                    foundColor = fpsColor.Color;
                }
            }

            if (foundColor.HasValue) ForegroundColor = foundColor.Value;
            else ForegroundColor = DefaultForegroundColor;
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
            _style.normal.textColor = ForegroundColor;
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