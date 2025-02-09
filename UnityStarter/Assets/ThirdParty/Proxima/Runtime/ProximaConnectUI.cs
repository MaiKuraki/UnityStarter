#if UNITY_TMPRO

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Proxima
{
    [HelpURL("https://www.unityproxima.com/docs")]
    public class ProximaConnectUI : MonoBehaviour
    {
        // Insepctor to start and stop.
        [SerializeField]
        private ProximaInspector _proximaInspector;
        public ProximaInspector ProximaInspector
        {
            get => _proximaInspector;
            set => _proximaInspector = value;
        }

        // Input field to enter the display name.
        [SerializeField]
        private TMP_InputField _displayNameInputField;
        public TMP_InputField DisplayNameInputField
        {
            get => _displayNameInputField;
            set => _displayNameInputField = value;
        }

        // Input field to enter the password.
        [SerializeField]
        private TMP_InputField _passwordInputField;
        public TMP_InputField PasswordInputField
        {
            get => _passwordInputField;
            set => _passwordInputField = value;
        }

        // Text to display errors.
        [SerializeField]
        private TMP_Text _errorLabel;
        public TMP_Text ErrorLabel
        {
            get => _errorLabel;
            set => _errorLabel = value;
        }

        // Button to start Proxima.
        [SerializeField]
        private Button _startButton;
        public Button StartButton
        {
            get => _startButton;
            set => _startButton = value;
        }

        // Button to open Proxima in the browser.
        [SerializeField]
        private Button _openButton;
        public Button OpenButton
        {
            get => _openButton;
            set => _openButton = value;
        }

        // Button to stop Proxima.
        [SerializeField]
        private Button _stopButton;
        public Button StopButton
        {
            get => _stopButton;
            set => _stopButton = value;
        }

        // GameObject to show when Proxima is not started.
        [SerializeField]
        private GameObject _connectUIRoot;
        public GameObject ConnectUIRoot
        {
            get => _connectUIRoot;
            set => _connectUIRoot = value;
        }

        // GameObject to show when Proxima is started.
        [SerializeField]
        private GameObject _startedUIRoot;
        public GameObject StartedUIRoot
        {
            get => _startedUIRoot;
            set => _startedUIRoot = value;
        }

        // Button to show/hide the UI.
        [SerializeField]
        private Button  _showHideButton;
        public Button ShowHideButton
        {
            get => _showHideButton;
            set => _showHideButton = value;
        }

        void Start()
        {
            if (EventSystem.current == null)
            {
                gameObject.AddComponent<EventSystem>();
                gameObject.AddComponent<StandaloneInputModule>();
            }

            _proximaInspector.Status.Changed += OnStatusChanged;
            _displayNameInputField.text = _proximaInspector.DisplayName;
            _displayNameInputField.onSubmit.AddListener((s) => _passwordInputField.Select());
            _startButton.onClick.AddListener(OnStartButtonClicked);

            if (_openButton)
            {
                #if UNITY_WEBGL && !UNITY_EDITOR
                    var trigger = _openButton.gameObject.AddComponent<EventTrigger>();
                    var pointerDown = new EventTrigger.Entry();
                    pointerDown.eventID = EventTriggerType.PointerDown;
                    pointerDown.callback.AddListener((e) => {
                        ProximaWebGLConnection.OpenOnMouseUp(_proximaInspector.Status.ConnectInfo);
                    });
                    trigger.triggers.Add(pointerDown);
                #else
                    _openButton.onClick.AddListener(() => {
                        Application.OpenURL(_proximaInspector.Status.ConnectInfo);
                    });
                #endif
            }

            _stopButton.onClick.AddListener(OnStopButtonClicked);
            _showHideButton.onClick.AddListener(TogglePanel);
            _passwordInputField.onSubmit.AddListener((s) => OnStartButtonClicked());

            OnStatusChanged();
        }

        private void OnDestroy()
        {
            if (_proximaInspector)
            {
                _proximaInspector.Status.Changed -= OnStatusChanged;
            }
        }

        private void OnStatusChanged()
        {
            _startButton.interactable = !_proximaInspector.Status.IsRunning;

            if (_proximaInspector.Status.Listening)
            {
                _startedUIRoot.SetActive(true);
                _connectUIRoot.SetActive(false);
            }
            else
            {
                _startedUIRoot.SetActive(false);
                _connectUIRoot.SetActive(true);
            }

            if (_errorLabel != null)
            {
                _errorLabel.text = _proximaInspector.Status.Error;
                _errorLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(_proximaInspector.Status.Error));
            }
        }

        private void OnStartButtonClicked()
        {
            if (_proximaInspector != null &&
                _passwordInputField != null &&
                _displayNameInputField != null &&
                !_proximaInspector.Status.IsRunning)
            {
                _proximaInspector.Password = _passwordInputField.text;
                _proximaInspector.DisplayName = _displayNameInputField.text;
                _proximaInspector.Run();
            }
        }

        private void OnStopButtonClicked()
        {
            if (_proximaInspector != null)
            {
                _proximaInspector.Stop();
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HidePanel();
            }
        }

        public void ShowPanel()
        {
            var panel = transform.GetChild(0).gameObject;
            panel.SetActive(true);
        }

        public void HidePanel()
        {
            var panel = transform.GetChild(0).gameObject;
            panel.SetActive(false);
        }

        public void TogglePanel()
        {
            var panel = transform.GetChild(0).gameObject;
            panel.SetActive(!panel.activeSelf);
        }
    }
}

#else

namespace Proxima
{
    public class ProximaConnectUI : UnityEngine.MonoBehaviour
    {
        public ProximaInspector ProximaInspector;
    }
}

#endif