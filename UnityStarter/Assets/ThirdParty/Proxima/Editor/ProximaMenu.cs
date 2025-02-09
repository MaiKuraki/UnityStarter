using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Text.RegularExpressions;

namespace Proxima.Editor
{
    [InitializeOnLoad]
    internal class ProximaMenu : EditorWindow
    {
        private static readonly string _website = "https://www.unityproxima.com?utm_source=pxmenu";
        public static readonly string StoreLink = "https://assetstore.unity.com/packages/tools/utilities/proxima-inspector-244788?aid=1101lqSYn";
        private static readonly string _review = "https://assetstore.unity.com/packages/tools/utilities/proxima-inspector-244788#reviews";
        private static readonly string _discord = "https://discord.gg/VM9cWJ9rjH";
        private static readonly string _docs = "https://www.unityproxima.com/docs?utm_source=pxmenu";
        private static readonly string _buildalon = "https://www.buildalon.com?utm_source=pxmenu";

        private static readonly string _showOnStartKey = "ProximaMenu_ShowOnStart";
        private static readonly string _versionKey = "ProximaMenu_Version";

        private bool _allFeaturesInstalled = false;

        private const string ProximaStartScreenTag = "ProximaStartScreen";

        private GUIStyle _errorStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _versionStyle;
        private GUIStyle _boldStyle;
        private GUIStyle _semiboldStyle;
        private GUIStyle _moreToolsButtonStyle;
        private GUIStyle _proStyle;
        private GUIStyle _buildalonStyle;

        private static ShowOnStart _showOnStart;
        private static readonly string[] _showOnStartOptions = {
            "Always", "On Update", "Never"
        };

        private Vector2 _scrollPosition;

        private List<string> _changelog = new List<string>();

        private enum ShowOnStart
        {
            Always,
            OnUpdate,
            Never
        }

        static ProximaMenu()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            EditorApplication.update -= OnEditorUpdate;
            Initialize();
        }

        internal static void Initialize()
        {
            var shownKey = "ProximaMenuShown";
            bool alreadyShown = SessionState.GetBool(shownKey, false);
            SessionState.SetBool(shownKey, true);

            var version = WindowUtil.GetVersion();
            var lastVersion = EditorPrefs.GetString(_versionKey, "0.0.0");
            var newVersion = version.CompareTo(lastVersion) > 0;
            if (newVersion)
            {
                EditorPrefs.SetString(_versionKey, version);
                alreadyShown = false;
            }

            _showOnStart = (ShowOnStart)EditorPrefs.GetInt(_showOnStartKey, 0);
            bool showPref = _showOnStart == ShowOnStart.Always ||
                (_showOnStart == ShowOnStart.OnUpdate && newVersion);
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !alreadyShown && showPref && !Application.isBatchMode)
            {
                StartScreen();
            }

            if (!EditorApplication.isPlayingOrWillChangePlaymode && ProximaSurvey.ShouldAsk())
            {
                ProximaSurvey.ShowSurvey();
            }
        }

        private void OnEnable()
        {
            _bodyStyle = null;
        }

        private void OnDisable()
        {
            ProximaGUI.CleanupBackgroundTextures(ProximaStartScreenTag);
        }

        [MenuItem("Tools/Proxima/Start Screen")]
        public static void StartScreen()
        {
            ProximaMenu window = GetWindow<ProximaMenu>(true, "Proxima Start Screen", true);
            window.minSize = new Vector2(800, 600);
            window.maxSize = window.minSize;
            window.Show();
        }

        [MenuItem("Tools/Proxima/Website")]
        public static void OpenStore()
        {
            Application.OpenURL(_website);
        }

        [MenuItem("Tools/Proxima/Write a Review")]
        public static void OpenReview()
        {
            Application.OpenURL(_review);
        }

        [MenuItem("Tools/Proxima/Support (Discord)")]
        public static void OpenSupport()
        {
            Application.OpenURL(_discord);
        }

        private void InitStyles()
        {
            if (_bodyStyle != null) return;

            ProximaGUI.StyleTag = ProximaStartScreenTag;
            ProximaGUI.StyleFontSize = 14;

            _bodyStyle = new GUIStyle(EditorStyles.label);
            _bodyStyle.wordWrap = true;
            _bodyStyle.fontSize = 14;
            _bodyStyle.margin.left = 10;
            _bodyStyle.margin.top = 10;
            _bodyStyle.stretchWidth = false;
            _bodyStyle.richText = true;

            _boldStyle = new GUIStyle(_bodyStyle);
            _boldStyle.fontStyle = FontStyle.Bold;
            _boldStyle.fontSize = 16;

            _semiboldStyle = new GUIStyle(_bodyStyle);
            _semiboldStyle.fontStyle = FontStyle.Bold;

            _errorStyle = new GUIStyle(_bodyStyle);
            _errorStyle.fontStyle = FontStyle.Bold;
            _errorStyle.margin.top = 10;
            _errorStyle.normal.textColor = new Color(1, 0.2f, 0);

            _buttonStyle = new GUIStyle(_bodyStyle);
            _buttonStyle.fontSize = 14;
            _buttonStyle.margin.bottom = 5;
            _buttonStyle.padding.top = 5;
            _buttonStyle.padding.left = 10;
            _buttonStyle.padding.right = 10;
            _buttonStyle.padding.bottom = 5;
            _buttonStyle.hover.background = Texture2D.grayTexture;
            _buttonStyle.hover.textColor = Color.white;
            _buttonStyle.active.background = Texture2D.grayTexture;
            _buttonStyle.active.textColor = Color.white;
            _buttonStyle.focused.background = Texture2D.grayTexture;
            _buttonStyle.focused.textColor = Color.white;
            _buttonStyle.normal.background = Texture2D.grayTexture;
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.wordWrap = false;
            _buttonStyle.stretchWidth = false;

            _versionStyle = new GUIStyle(EditorStyles.label);
            _versionStyle.padding.right = 10;

            _moreToolsButtonStyle = new GUIStyle(_buttonStyle);
            _moreToolsButtonStyle.normal.background = Texture2D.blackTexture;
            _moreToolsButtonStyle.hover.background = Texture2D.blackTexture;
            _moreToolsButtonStyle.focused.background = Texture2D.blackTexture;
            _moreToolsButtonStyle.active.background = Texture2D.blackTexture;
            _moreToolsButtonStyle.padding.left = 0;
            _moreToolsButtonStyle.padding.right = 0;
            _moreToolsButtonStyle.padding.bottom = 0;
            _moreToolsButtonStyle.padding.top = 0;
            _moreToolsButtonStyle.margin.bottom = 10;

            _proStyle = new GUIStyle(_buttonStyle);
            _proStyle.normal.background = new Texture2D(1, 1);
            _proStyle.normal.background.SetPixel(0, 0, new Color(.94f, .42f, .13f));
            _proStyle.normal.background.Apply();
            _proStyle.hover.background = _proStyle.normal.background;
            _proStyle.focused.background = _proStyle.normal.background;
            _proStyle.active.background = _proStyle.normal.background;
            _proStyle.normal.textColor = Color.white;
            _proStyle.fontStyle = FontStyle.Bold;

            _buildalonStyle = ProximaGUI.CreateStyle(ProximaGUI.HexColor("#FF1E6F"));
            _buildalonStyle.fontStyle = FontStyle.Bold;
            _buildalonStyle.margin.left = 10;
            _buildalonStyle.margin.top = 10;

            WindowUtil.CenterOnEditor(this);

            ReadChangeLog();

            _allFeaturesInstalled = ProximaFeatures.AllFeaturesInstalled();
        }

        private void ReadChangeLog()
        {
            _changelog.Clear();
            var changelogPath = AssetDatabase.GUIDToAssetPath("53c7cf36ddcf17b4da75df27231f866e");
            var changelogAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(changelogPath);
            _changelog = changelogAsset.text.Split('\n')
            .Select(x => Regex.Replace(x.TrimEnd(), @"`(.*?)`", "<b>$1</b>"))
                .Select(x => Regex.Replace(x.TrimEnd(), @"\*\*(.*?)\*\*", "<b>$1</b>"))
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
            var start = _changelog.FindIndex(l => l.StartsWith("## "));
            _changelog = _changelog.GetRange(start, _changelog.Count - start);
        }

        private void LinkButton(string label, string url, GUIStyle style = null, int width = 170)
        {
            if (style == null) style = _buttonStyle;
            var labelContent = new GUIContent(label);
            var position = GUILayoutUtility.GetRect(width, 35, style);
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
            if (GUI.Button(position, labelContent, style))
            {
                Application.OpenURL(url);
            }
        }

        private bool Button(string label, GUIStyle style = null, int width = 170)
        {
            if (style == null) style = _buttonStyle;
            var labelContent = new GUIContent(label);
            var position = GUILayoutUtility.GetRect(width, 35, style);
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
            return GUI.Button(position, labelContent, style);
        }

        private void Bullet(string text)
        {
            var ws = 1 + text.IndexOf('-');
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < ws; i++)
            {
                GUILayout.Space(10);
            }
            GUILayout.Label("•", _bodyStyle);

            GUILayout.Label(text.Substring(ws + 1), _bodyStyle, GUILayout.ExpandWidth(true));

            EditorGUILayout.EndHorizontal();
        }

        private void WhatsNew()
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            GUILayout.Label("What's New in Proxima", _boldStyle, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            for (int i = 0; i < _changelog.Count; i++)
            {
                var line = _changelog[i];
                if (line.StartsWith("###"))
                {
                    EditorGUILayout.Space();
                    GUILayout.Label(line.Substring(4), _semiboldStyle, GUILayout.ExpandWidth(true));
                    EditorGUILayout.Space();
                }
                else if (line.StartsWith("##"))
                {
                    EditorGUILayout.Space();
                    GUILayout.Label(line.Substring(3), _boldStyle, GUILayout.ExpandWidth(true));
                    EditorGUILayout.Space();
                }
                else
                {
                    Bullet(line);
                    EditorGUILayout.Space();
                }
            }

            EditorGUILayout.Space();
        }

        private void OnGUI()
        {
            InitStyles();

            GUILayout.BeginHorizontal("In BigTitle", GUILayout.ExpandWidth(true));
            {
                WindowUtil.DrawProximaIcon(128);
                GUILayout.FlexibleSpace();
                GUILayout.Label("Version: " + WindowUtil.GetVersion(), _versionStyle, GUILayout.ExpandHeight(true));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                GUILayout.BeginVertical();
                {
                    GUILayout.Label("Resources", _boldStyle);
                    LinkButton("Discord Invite", _discord);
                    LinkButton("Documentation", _docs);
                    LinkButton("Write a Review", _review);
                    if (!_allFeaturesInstalled)
                    {
                        LinkButton("Upgrade to Pro", _website, _proStyle);
                    }

                    if (!ProximaSurvey.Completed)
                    {
                        if (Button("Feedback"))
                        {
                            ProximaSurvey.ShowSurvey();
                        }
                    }

                    GUILayout.FlexibleSpace();
                    GUILayout.Label("More Tools", _boldStyle);
                    if (ProximaGUI.ImageButton("3a8828df6dcaca540b6fee70da9c4697", 165, (int)(165 * 0.525f)))
                    {
                        Application.OpenURL(_buildalon);
                    }

                    EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                }
                GUILayout.EndVertical();

                EditorGUILayout.Separator();

                GUILayout.BeginVertical();
                {
                    _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

                    GUILayout.Label("Thank you for using Proxima Inspector!", _boldStyle);

                    EditorGUILayout.Space();

                    GUILayout.Label("You're invited to join the Discord community for support and feedback. Let us know how to make Proxima better for you!", _bodyStyle);

                    EditorGUILayout.Space();
                    EditorGUILayout.Space();

                    ProximaGUI.Vertical(EditorStyles.helpBox, () =>
                    {
                        GUILayout.Label("Unveiling our new tool for Unity developers:", _bodyStyle);
                        EditorGUILayout.Space();
                        if (ProximaGUI.Link("Buildalon: Automate Unity!", _buildalonStyle))
                        {
                            Application.OpenURL(_buildalon);
                        }
                        EditorGUILayout.Space();
                        GUILayout.Label("Buildalon is a comprehensive suite of build, test, and deploy automation solutions for Unity developers.", _bodyStyle);
                        EditorGUILayout.Space();
                    });

                    EditorGUILayout.Space();
                    EditorGUILayout.Space();

                    ProximaGUI.Vertical(EditorStyles.helpBox, () =>
                    {
                        GUILayout.Label("If you're enjoying Proxima, please consider writing a review. It helps a ton!", _bodyStyle);
                        EditorGUILayout.Space();
                    });

                    WhatsNew();

                    EditorGUILayout.EndScrollView();
                }
                GUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal("In BigTitle", GUILayout.ExpandHeight(true));
            {
                GUILayout.Label("Tools/Proxima/Start Screen");
                GUILayout.FlexibleSpace();
                GUILayout.Label("Show On Start: ");
                var newShowOnStart = (ShowOnStart)EditorGUILayout.Popup((int)_showOnStart, _showOnStartOptions);
                if (_showOnStart != newShowOnStart)
                {
                    _showOnStart = newShowOnStart;
                    EditorPrefs.SetInt(_showOnStartKey, (int)_showOnStart);
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}