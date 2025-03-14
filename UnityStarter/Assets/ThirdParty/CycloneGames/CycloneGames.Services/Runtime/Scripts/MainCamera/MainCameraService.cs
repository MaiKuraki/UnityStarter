using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.Service
{
    public interface IMainCameraService
    {
        Camera MainCameraInst { get; }
        void AddCameraToStack(Camera camera);
        void RemoveCameraFromStack(Camera camera);
    }
    public class MainCameraService : IMainCameraService
    {
        private const string DEBUG_FLAG = "[MainCameraService]";
        public Camera MainCameraInst => mainCamera.Inst;
        private MainCamera mainCamera;

        public MainCameraService()
        {
            Initialize();
        }

        public void Initialize()
        {
            mainCamera = UnityEngine.GameObject.FindFirstObjectByType<MainCamera>();
            UnityEngine.MonoBehaviour.DontDestroyOnLoad(mainCamera.gameObject);
        }

        public void AddCameraToStack(Camera camera)
        {
            if (mainCamera == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid MainCamera");
                return;
            }

            mainCamera.AddCameraToStack(camera);
        }

        public void RemoveCameraFromStack(Camera camera)
        {
            if (mainCamera == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid MainCamera");
                return;
            }

            mainCamera.RemoveCameraFromStack(camera);
        }
    }
}

