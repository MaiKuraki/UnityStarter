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
        public Camera MainCameraInst => mainCamera.CameraInst;
        private MainCamera mainCamera => MainCamera.Instance ?? UnityEngine.GameObject.FindFirstObjectByType<MainCamera>();

        public MainCameraService()
        {
            Initialize();
        }

        public void Initialize()
        {

        }

        private MainCamera TryGetMainCamera()
        {
            return MainCamera.Instance ?? UnityEngine.GameObject.FindFirstObjectByType<MainCamera>();
        }

        public void AddCameraToStack(Camera camera)
        {
            mainCamera?.AddCameraToStack(camera);
        }

        public void RemoveCameraFromStack(Camera camera)
        {
            mainCamera?.RemoveCameraFromStack(camera);
        }
    }
}