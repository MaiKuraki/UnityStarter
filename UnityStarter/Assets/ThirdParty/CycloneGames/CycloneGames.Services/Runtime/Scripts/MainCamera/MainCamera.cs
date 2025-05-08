using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CycloneGames.Service
{
    public class MainCamera : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[MainCamera]";
        [SerializeField] private Camera _camera;
        [SerializeField] private bool _isSingleton = true;

        public static MainCamera Instance { get; private set; }
        public Camera CameraInst => _camera;
        private UniversalAdditionalCameraData _urpCameraData;

        void Awake()
        {
            if (_isSingleton)
            {
                MakeSingleton();
            }
        }

        void MakeSingleton()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                return;
            }

            //  TODO: mabye merge the stack to SingletonCamera?

            Destroy(gameObject);
        }

        public void AddCameraToStack(Camera inCamera)
        {
            if (_urpCameraData == null) _urpCameraData = CameraInst?.GetUniversalAdditionalCameraData();

            if (_urpCameraData == null)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} invlaid URP Camera Data");
                return;
            }

            if (!_urpCameraData.cameraStack.Contains(inCamera))
            {
                _urpCameraData.cameraStack.Add(inCamera);
            }
        }

        public void RemoveCameraFromStack(Camera inCamera)
        {
            if (_urpCameraData == null) _urpCameraData = CameraInst?.GetUniversalAdditionalCameraData();

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