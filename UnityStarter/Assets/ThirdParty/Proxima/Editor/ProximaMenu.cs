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
        private static readonly string _flexalon = "https://www.flexalon.com?utm_source=pxmenu";

        private static readonly string _showOnStartKey = "ProximaMenu_ShowOnStart";
        private static readonly string _versionKey = "ProximaMenu_Version";

        private bool _isProInstalled = false;

        private const string ProximaStartScreenTag = "ProximaStartScreen";

        private GUIStyle _errorStyle;
        private GUIStyle _bgStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _versionStyle;
        private GUIStyle _boldStyle;
        private GUIStyle _boldStyleNoWrap;
        private GUIStyle _semiboldStyle;
        private GUIStyle _proStyle;
        private GUIStyle _buildalonStyle;
        private GUIStyle _discordButtonStyle;

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
            window.minSize = new Vector2(850, 600);
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

            _bgStyle = ProximaGUI.CreateStyle(Color.white, ProximaGUI.HexColor("#222222"));

            _bodyStyle = ProximaGUI.CreateStyle(Color.white);

            _boldStyle = ProximaGUI.CreateStyle(Color.white);
            _boldStyle.fontStyle = FontStyle.Bold;
            _boldStyle.fontSize = 16;

            _boldStyleNoWrap = new GUIStyle(_boldStyle);
            _boldStyleNoWrap.wordWrap = false;

            _semiboldStyle = ProximaGUI.CreateStyle(Color.white);
            _semiboldStyle.fontStyle = FontStyle.Bold;

            _errorStyle = ProximaGUI.CreateStyle(new Color(1, 0.2f, 0));
            _errorStyle.fontStyle = FontStyle.Bold;
            _errorStyle.margin.top = 10;

            _buttonStyle = ProximaGUI.CreateStyle(Color.white, ProximaGUI.HexColor("#515151"));
            _buttonStyle.padding.top = 6;
            _buttonStyle.padding.left = 10;
            _buttonStyle.padding.right = 11;
            _buttonStyle.padding.bottom = 5;
            _buttonStyle.fontStyle = FontStyle.Bold;

            _versionStyle = new GUIStyle(EditorStyles.label);
            _versionStyle.padding.right = 10;

            _proStyle = new GUIStyle(_buttonStyle);
            ProximaGUI.SetBackgroundColor(_proStyle, new Color(.94f, .42f, .13f));
            _proStyle.wordWrap = false;

            _buildalonStyle = ProximaGUI.CreateStyle(Color.white, ProximaGUI.HexColor("#FF1E6F"));
            _buildalonStyle.fontStyle = FontStyle.Bold;

            _discordButtonStyle = new GUIStyle(_buttonStyle);
            ProximaGUI.SetBackgroundColor(_discordButtonStyle, ProximaGUI.HexColor("#5865f2"));

            WindowUtil.CenterOnEditor(this);

            ReadChangeLog();

            _isProInstalled = ProximaFeatures.IsProInstalled();
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

        private void Bullet(string text)
        {
            var ws = 1 + text.IndexOf('-');
            ProximaGUI.Horizontal(() =>
            {
                for (int i = 0; i < ws; i++)
                {
                    GUILayout.Space(10);
                }
                GUILayout.Label("•", _bodyStyle);
                GUILayout.Space(10);
                GUILayout.Label(text.Substring(ws + 1), _bodyStyle, GUILayout.ExpandWidth(true));
            });
        }

        private void WhatsNew()
        {
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

            ProximaGUI.Vertical(_bgStyle, () =>
            {
                ProximaGUI.HorizontalExpanded(() =>
                {
                    EditorGUILayout.Space(8);

                    ProximaGUI.Vertical(150, () =>
                    {
                        GUILayout.Space(12);
                        ProximaGUI.Horizontal(() =>
                        {
                            GUILayout.Space(11);
                            ProximaGUI.Image("834e6e3f5b2f6fd479051cdddf01f4b1", 128);
                        });

                        GUILayout.Space(20);
                        ProximaGUI.LinkButton("Discord Server", _discord, _discordButtonStyle, 150, 30);
                        GUILayout.Space(8);
                        ProximaGUI.LinkButton("Documentation", _docs, _buttonStyle, 150, 30);
                        GUILayout.Space(8);
                        ProximaGUI.LinkButton("Write a Review", _review, _buttonStyle, 150, 30);
                        GUILayout.Space(8);

                        if (!_isProInstalled)
                        {
                            ProximaGUI.LinkButton("Upgrade to Pro", _website, _proStyle, 150, 30);
                            GUILayout.Space(8);
                        }

                        if (!ProximaSurvey.Completed)
                        {
                            if (ProximaGUI.Button("Feedback", _buttonStyle, 150, 30))
                            {
                                ProximaSurvey.ShowSurvey();
                            }
                        }

                        GUILayout.FlexibleSpace();

                        ProximaGUI.HorizontalCentered(() => GUILayout.Label("More Tools", _boldStyleNoWrap));
                        GUILayout.Space(20);
                        ProximaGUI.HorizontalCentered(() =>
                        {
                            if (ProximaGUI.ImageButton("3a8828df6dcaca540b6fee70da9c4697", GUIStyle.none, 100, (int)(165 * 0.3483f)))
                            {
                                Application.OpenURL(_buildalon);
                            }
                        });

                        ProximaGUI.HorizontalCentered(() =>
                        {
                            if (ProximaGUI.ImageButton("9c4086f38f8e37949978f4861eee5e47", GUIStyle.none, 100, (int)(148 * 0.525f)))
                            {
                                Application.OpenURL(_flexalon);
                            }
                        });

                        ProximaGUI.HorizontalCentered(() => GUILayout.Label("  Version: " + WindowUtil.GetVersion(), _versionStyle));
                    });

                    EditorGUILayout.Space();
                    ProximaGUI.VerticalLine(ProximaGUI.HexColor("#515151"));
                    GUILayout.Space(24);

                    ProximaGUI.Vertical(() =>
                    {
                        _scrollPosition = ProximaGUI.Scroll(_scrollPosition, () =>
                        {
                            GUILayout.Space(24);

                            GUILayout.Label("Introducing Proxima Remote Access Beta!", _boldStyle);

                            GUILayout.Space(24);

                            GUILayout.Label("Hello Proxima users! We're excited to announce an upcoming feature called Proxima Remote Access. Get ready to debug your game from anywhere in the world!", _bodyStyle);

                            GUILayout.Space(24);

                            ProximaGUI.HorizontalCentered(() => ProximaGUI.Image("020f2dd7432168a4eb4d830a209e54e5", 630));

                            GUILayout.Space(24);

                            GUILayout.Label("Today, to connect to a device running Proxima Inspector, you need to be on the same local network. With Proxima Remote Access, you can connect to users anywhere over the internet through a Proxima server.", _bodyStyle);

                            GUILayout.Space(24);

                            ProximaGUI.LinkButton("Join the Beta", "https://www.unityproxima.com/beta?utm_source=pxmenu", _proStyle);

                            GUILayout.Space(24);

                            GUILayout.Label("By the way, if you're enjoying Proxima, please consider writing a review. It helps a ton!", _semiboldStyle);

                            GUILayout.Space(24);

                            ProximaGUI.HorizontalLine();

                            WhatsNew();
                        });
                    });
                    EditorGUILayout.Space();
                });

                ProximaGUI.HorizontalExpanded(() =>
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Show On Start: ");
                    var newShowOnStart = (ShowOnStart)EditorGUILayout.Popup((int)_showOnStart, _showOnStartOptions);
                    if (_showOnStart != newShowOnStart)
                    {
                        _showOnStart = newShowOnStart;
                        EditorPrefs.SetInt(_showOnStartKey, (int)_showOnStart);
                    }
                });
            });
        }
    }
}