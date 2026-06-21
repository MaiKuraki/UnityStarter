using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Networking.Platform;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    public sealed class NetworkHostPermissionWindow : EditorWindow
    {
        private const string MENU_PATH = "Tools/CycloneGames/Networking/LAN Host Permission";
        private const float CHIP_HEIGHT = 42.0f;
        private const float CHIP_GAP = 4.0f;

        private static readonly Color HeaderColor = new Color(0.34f, 0.55f, 0.76f);
        private static readonly Color StatusReadyColor = new Color(0.25f, 0.58f, 0.34f);
        private static readonly Color StatusActionColor = new Color(0.86f, 0.60f, 0.22f);
        private static readonly Color StatusUnsupportedColor = new Color(0.72f, 0.28f, 0.25f);
        private static readonly Color AddressColor = new Color(0.28f, 0.52f, 0.62f);
        private static readonly Color ActionColor = new Color(0.42f, 0.46f, 0.66f);

        [SerializeField] private int Port = 7777;
        [SerializeField] private NetworkTransportProtocol Protocol = NetworkTransportProtocol.Udp;
        [SerializeField] private string RuleDisplayNamePrefix = NetworkHostPermissionServiceFactory.DEFAULT_RULE_DISPLAY_NAME_PREFIX;
        [SerializeField] private bool RequireGatewayForLocalAddresses = true;

        private readonly List<string> _localAddresses = new List<string>(4);
        private INetworkHostPermissionService _permissionService;
        private NetworkHostPermissionCheckResult _lastStatus;
        private string _lastRequestMessage = string.Empty;
        private Vector2 _scroll;
        private bool _configurationFoldout = true;
        private bool _addressesFoldout = true;
        private bool _actionsFoldout = true;
        private GUIStyle _chipTitleStyle;
        private GUIStyle _chipValueStyle;
        private GUIStyle _miniRightStyle;
        private CancellationTokenSource _cts;
        private bool _isVerifying;

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            NetworkHostPermissionWindow window = GetWindow<NetworkHostPermissionWindow>();
            window.titleContent = new GUIContent("LAN Host");
            window.minSize = new Vector2(620.0f, 420.0f);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            _cts = new CancellationTokenSource();
            _permissionService = NetworkHostPermissionServiceFactory.CreateDefault(RuleDisplayNamePrefix);
            Refresh();
        }

        private void OnDisable()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(6.0f);
            InspectorUiUtility.DrawSectionHeader(
                "LAN Host Permission",
                "Checks whether this machine is ready to host local-network sessions and helps request Windows Firewall rules for the selected port.",
                HeaderColor);

            EditorGUILayout.Space(6.0f);
            DrawSummaryBar();

            EditorGUILayout.Space(6.0f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawConfiguration();
            EditorGUILayout.Space(6.0f);
            DrawAddresses();
            EditorGUILayout.Space(6.0f);
            DrawActions();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSummaryBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect row = EditorGUILayout.GetControlRect(false, CHIP_HEIGHT);
            float width = (row.width - CHIP_GAP * 3.0f) * 0.25f;

            Rect rect = new Rect(row.x, row.y, width, CHIP_HEIGHT);
            DrawChip(rect, "Status", _lastStatus.Status.ToString(), GetStatusColor(_lastStatus));

            rect.x += width + CHIP_GAP;
            DrawChip(rect, "Platform", string.IsNullOrWhiteSpace(_lastStatus.PlatformName) ? "Unknown" : _lastStatus.PlatformName, HeaderColor);

            rect.x += width + CHIP_GAP;
            DrawChip(rect, "Endpoint", $"{Port}/{Protocol}", AddressColor);

            rect.x += width + CHIP_GAP;
            rect.width = row.xMax - rect.x;
            DrawChip(rect, "LAN IPv4", _localAddresses.Count.ToString(), _localAddresses.Count > 0 ? StatusReadyColor : StatusUnsupportedColor);

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.HelpBox(_lastStatus.DeveloperMessage, GetMessageType(_lastStatus.Status));
            EditorGUILayout.HelpBox(
                "This reflects the editor host platform. A built player on a different platform may behave differently; verify on the target build.",
                MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawConfiguration()
        {
            _configurationFoldout = InspectorUiUtility.DrawFoldoutHeader("Configuration", _configurationFoldout, HeaderColor);
            if (!_configurationFoldout)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            Port = EditorGUILayout.IntField("Port", Port);
            Protocol = (NetworkTransportProtocol)EditorGUILayout.EnumPopup("Protocol", Protocol);
            RuleDisplayNamePrefix = EditorGUILayout.TextField("Rule Name Prefix", RuleDisplayNamePrefix);
            RequireGatewayForLocalAddresses = EditorGUILayout.Toggle("Require Gateway", RequireGatewayForLocalAddresses);
            if (EditorGUI.EndChangeCheck())
            {
                _permissionService = NetworkHostPermissionServiceFactory.CreateDefault(RuleDisplayNamePrefix);
                Refresh();
            }

            EditorGUILayout.Space(2.0f);
            EditorGUILayout.LabelField("Rule Name Preview", $"{RuleDisplayNamePrefix} {Protocol.ToString().ToUpperInvariant()} {Port}", _miniRightStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawAddresses()
        {
            _addressesFoldout = InspectorUiUtility.DrawFoldoutHeader("Local IPv4 Addresses", _addressesFoldout, AddressColor);
            if (!_addressesFoldout)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (_localAddresses.Count == 0)
            {
                EditorGUILayout.HelpBox("No LAN IPv4 address was detected.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            for (int i = 0; i < _localAddresses.Count; i++)
            {
                DrawAddressRow(_localAddresses[i]);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            _actionsFoldout = InspectorUiUtility.DrawFoldoutHeader("Actions", _actionsFoldout, ActionColor);
            if (!_actionsFoldout)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                {
                    Refresh();
                }

                using (new EditorGUI.DisabledScope(!_lastStatus.CanRequestAutomatically))
                {
                    if (GUILayout.Button(GetRequestButtonLabel()))
                    {
                        RequestPermission();
                    }
                }

                using (new EditorGUI.DisabledScope(!_lastStatus.CanRequestAutomatically || _isVerifying))
                {
                    if (GUILayout.Button(_isVerifying ? "Verifying.." : "Verify Firewall Rule"))
                    {
                        VerifyAsync();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_lastRequestMessage))
            {
                EditorGUILayout.HelpBox(_lastRequestMessage, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAddressRow(string address)
        {
            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4.0f);
            Rect markerRect = new Rect(row.x, row.y + 4.0f, 3.0f, row.height - 8.0f);
            Rect labelRect = new Rect(markerRect.xMax + 6.0f, row.y + 2.0f, row.width - 98.0f, EditorGUIUtility.singleLineHeight);
            Rect copyRect = new Rect(row.xMax - 82.0f, row.y, 82.0f, row.height);

            EditorGUI.DrawRect(markerRect, AddressColor);
            EditorGUI.SelectableLabel(labelRect, $"{address}:{Port}", EditorStyles.label);
            if (GUI.Button(copyRect, "Copy", EditorStyles.miniButton))
            {
                EditorGUIUtility.systemCopyBuffer = $"{address}:{Port}";
                _lastRequestMessage = $"Copied {address}:{Port}.";
            }
        }

        private void DrawChip(Rect rect, string title, string value, Color color)
        {
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3.0f, rect.height), color);

            Rect titleRect = new Rect(rect.x + 8.0f, rect.y + 4.0f, rect.width - 12.0f, 15.0f);
            Rect valueRect = new Rect(rect.x + 8.0f, rect.y + 20.0f, rect.width - 12.0f, 18.0f);
            GUI.Label(titleRect, title, _chipTitleStyle);
            GUI.Label(valueRect, value, _chipValueStyle);
        }

        private void Refresh()
        {
            if (_permissionService == null)
            {
                _permissionService = NetworkHostPermissionServiceFactory.CreateDefault(RuleDisplayNamePrefix);
            }

            _lastStatus = _permissionService.GetStatus(Port, Protocol);
            NetworkLocalAddressUtility.GetLanIPv4Addresses(_localAddresses, RequireGatewayForLocalAddresses);
            Repaint();
        }

        private void RequestPermission()
        {
            NetworkHostPermissionRequestResult result = _permissionService.RequestSystemConfiguration(Port, Protocol);
            _lastRequestMessage = result.DeveloperMessage;
            Repaint();
        }

        private async void VerifyAsync()
        {
            if (_isVerifying || _permissionService == null)
            {
                return;
            }

            _isVerifying = true;
            Repaint();
            try
            {
                CancellationToken token = _cts != null ? _cts.Token : CancellationToken.None;
                _lastStatus = await _permissionService.RefreshStatusAsync(Port, Protocol, token);
                _lastRequestMessage = _lastStatus.DeveloperMessage;
            }
            catch (OperationCanceledException)
            {
                // The window was closed or verification was cancelled; nothing to report.
            }
            catch (Exception exception)
            {
                _lastRequestMessage = exception.Message;
            }
            finally
            {
                _isVerifying = false;
                Repaint();
            }
        }

        private void EnsureStyles()
        {
            if (_chipTitleStyle != null)
            {
                return;
            }

            _chipTitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };

            _chipValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft
            };

            _miniRightStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
        }

        private string GetRequestButtonLabel()
        {
            return _lastStatus.CanRequestAutomatically
                ? "Request Firewall Rule"
                : "Manual System Configuration Required";
        }

        private static MessageType GetMessageType(NetworkHostPermissionStatus status)
        {
            return status switch
            {
                NetworkHostPermissionStatus.Failed => MessageType.Error,
                NetworkHostPermissionStatus.Unsupported => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private static Color GetStatusColor(in NetworkHostPermissionCheckResult result)
        {
            switch (result.Status)
            {
                case NetworkHostPermissionStatus.CanHost:
                    return result.RequiresSystemConfiguration ? StatusActionColor : StatusReadyColor;
                case NetworkHostPermissionStatus.Unsupported:
                case NetworkHostPermissionStatus.Failed:
                    return StatusUnsupportedColor;
                default:
                    return HeaderColor;
            }
        }
    }
}
