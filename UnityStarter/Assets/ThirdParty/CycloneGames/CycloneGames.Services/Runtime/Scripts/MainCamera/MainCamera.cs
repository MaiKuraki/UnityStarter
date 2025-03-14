using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CycloneGames.Service
{
    public class MainCamera : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[MainCamera]";
        [SerializeField] private Camera _camera;

        public Camera Inst => _camera;
        private UniversalAdditionalCameraData _urpCameraData;

        public void AddCameraToStack(Camera inCamera)
        {
            if (_urpCameraData == null) _urpCameraData = Inst?.GetUniversalAdditionalCameraData();

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
            if (_urpCameraData == null) _urpCameraData = Inst?.GetUniversalAdditionalCameraData();

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

