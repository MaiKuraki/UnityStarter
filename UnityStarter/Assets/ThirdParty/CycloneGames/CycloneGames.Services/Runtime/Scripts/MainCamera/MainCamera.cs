using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CycloneGames.Service
{
    public class MainCamera : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[MainCamera]";
        [SerializeField] private Camera _camera;
        [SerializeField] private bool _singleton = true;

        public static MainCamera Instance { get; private set; }
        public Camera CameraInst => _camera;
        private UniversalAdditionalCameraData _urpCameraData;

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
            _urpCameraData = CameraInst?.GetUniversalAdditionalCameraData();
        }

        public void AddCameraToStack(Camera inCamera, int index = 0)
        {
            if (!inCamera) return;

            _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();

            if (_urpCameraData == null)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} invlaid URP Camera Data");
                return;
            }

            if (!_urpCameraData.cameraStack.Contains(inCamera))
            {
                _urpCameraData.cameraStack.Insert(index, inCamera);
            }
        }

        public void RemoveCameraFromStack(Camera inCamera)
        {
            if (!inCamera) return;

            _urpCameraData ??= CameraInst?.GetUniversalAdditionalCameraData();

            if (_urpCameraData == null)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} invlaid URP Camera Data");
                return;
            }

            if (_urpCameraData.cameraStack.Contains(inCamera))
            {
                _urpCameraData.cameraStack.Remove(inCamera);
            }
        }
    }
}