using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.Service.Runtime
{
    public interface IMainCameraService
    {
        Camera MainCameraInst { get; }
        void AddCameraToStack(Camera camera, int index);
        void RemoveCameraFromStack(Camera camera);
    }

    public class MainCameraService : IMainCameraService
    {
        public Camera MainCameraInst => mainCamera?.CameraInst;
        private MainCamera mainCamera => MainCamera.Instance ?? GameObject.FindFirstObjectByType<MainCamera>();

        public MainCameraService() { }

        public void AddCameraToStack(Camera camera, int index = 0)
        {
            mainCamera?.AddCameraToStack(camera, index);
        }

        public void RemoveCameraFromStack(Camera camera)
        {
            mainCamera?.RemoveCameraFromStack(camera);
        }
    }
}