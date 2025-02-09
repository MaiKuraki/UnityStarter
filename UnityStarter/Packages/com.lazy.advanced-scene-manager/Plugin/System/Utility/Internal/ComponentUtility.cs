using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    static class ComponentUtility
    {

        #region Ensure component

        public static void CreateIfNotExists<T>(this Component c) where T : Component =>
            CreateIfNotExists<T>(c, out _);

        public static bool CreateIfNotExists<T>(this Component c, out T createdComponent) where T : Component
        {

            if (!UnityCompatibiltyHelper.FindFirstObjectByType<T>())
            {
                Debug.Log($"ASM: No component of type '{typeof(T).Name}' found, when opening pause screen, creating one temporarily.", c);
                createdComponent = c.gameObject.AddComponent<T>();
                createdComponent.hideFlags = HideFlags.DontSaveInEditor;
                return true;
            }

            createdComponent = null;
            return false;

        }

        #endregion

        public static void EnsureCameraExists(this Component c)
        {
            if (c.CreateIfNotExists<Camera>(out var camera))
                camera.backgroundColor = Color.black;
        }

    }

}
