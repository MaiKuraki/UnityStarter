#if UNITY_TMPRO

using TMPro;
using UnityEngine;

namespace Proxima
{
    [HelpURL("https://www.unityproxima.com/docs")]
    public class ProximaStatusUI : MonoBehaviour
    {
        // The inspector to monitor status from.
        [SerializeField]
        private ProximaInspector _proximaInspector;
        public ProximaInspector ProximaInspector
        {
            get => _proximaInspector;
            set => _proximaInspector = value;
        }

        // GameObject to show when Proxima is enabled.
        [SerializeField]
        private GameObject _uiRoot;
        public GameObject UIRoot
        {
            get => _uiRoot;
            set => _uiRoot = value;
        }

        // Label to show address to connect to Proxima.
        [SerializeField]
        private TMP_InputField _connectInfoLabel;
        public TMP_InputField ConnectInfoLabel
        {
            get => _connectInfoLabel;
            set => _connectInfoLabel = value;
        }

        // Label to show the current status and errors.
        [SerializeField]
        private TMP_Text _statusLabel;
        public TMP_Text StatusLabel
        {
            get => _statusLabel;
            set => _statusLabel = value;
        }

        private bool _isPortrait;

        void OnEnable()
        {
            _proximaInspector.Status.Changed += UpdateUI;
            UpdateUI();
        }

        void OnDisable()
        {
            if (_proximaInspector)
            {
                _proximaInspector.Status.Changed -= UpdateUI;
            }
        }

        private void UpdateUI()
        {
            if (_uiRoot)
            {
                _uiRoot.SetActive(_proximaInspector.Status.Listening);
            }

            if (_connectInfoLabel)
            {
                _connectInfoLabel.text = _proximaInspector.Status.ConnectInfo;
            }

            if (_statusLabel)
            {
                _statusLabel.text = _proximaInspector.Status.GetStatusMessage(_isPortrait);
            }
        }

        void Update()
        {
            bool isPortrait = (float)Screen.width / (float)Screen.height < 0.75f;
            if (_isPortrait != isPortrait)
            {
                _isPortrait = isPortrait;
                UpdateUI();
            }
        }
    }
}

#else

namespace Proxima
{
    public class ProximaStatusUI : UnityEngine.MonoBehaviour
    {
        public ProximaInspector ProximaInspector;
    }
}

#endif