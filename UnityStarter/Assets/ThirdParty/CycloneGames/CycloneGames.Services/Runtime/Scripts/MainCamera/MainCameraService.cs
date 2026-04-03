using System;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.Service.Runtime
{
    public interface IMainCameraService
    {
        Camera MainCameraInst { get; }
        bool IsAvailable { get; }
        void AddCameraToStack(Camera camera, int index = 0);
        void RemoveCameraFromStack(Camera camera);
        void ClearCameraStack();
        int CameraStackCount { get; }

        event Action<Camera> OnCameraAddedToStack;
        event Action<Camera> OnCameraRemovedFromStack;
        event Action OnCameraStackCleared;
    }

    public class MainCameraService : IMainCameraService
    {
        private const string DEBUG_FLAG = "[MainCameraService]";

        private MainCamera _cached;

        public event Action<Camera> OnCameraAddedToStack;
        public event Action<Camera> OnCameraRemovedFromStack;
        public event Action OnCameraStackCleared;

        public Camera MainCameraInst => GetMainCamera()?.CameraInst;
        public bool IsAvailable => GetMainCamera() != null;
        public int CameraStackCount => GetMainCamera()?.CameraStackCount ?? 0;

        public MainCameraService() { }

        public void AddCameraToStack(Camera camera, int index = 0)
        {
            var mc = GetMainCamera();
            if (mc == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} MainCamera not found, cannot add to stack");
                return;
            }
            mc.AddCameraToStack(camera, index);
            OnCameraAddedToStack?.Invoke(camera);
        }

        public void RemoveCameraFromStack(Camera camera)
        {
            var mc = GetMainCamera();
            if (mc == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} MainCamera not found, cannot remove from stack");
                return;
            }
            mc.RemoveCameraFromStack(camera);
            OnCameraRemovedFromStack?.Invoke(camera);
        }

        public void ClearCameraStack()
        {
            var mc = GetMainCamera();
            if (mc == null) return;
            mc.ClearCameraStack();
            OnCameraStackCleared?.Invoke();
        }

        // Cached lookup: re-resolves only when cached reference is null or destroyed
        private MainCamera GetMainCamera()
        {
            if (_cached == null)
            {
                _cached = MainCamera.Instance;
                if (_cached == null)
                    _cached = UnityEngine.Object.FindFirstObjectByType<MainCamera>();
            }
            return _cached;
        }
    }
}