using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdvancedSceneManager.Utility
{

    /// <summary>Contains utility methods for <see cref="ScriptableObject"/>.</summary>
    public static class ScriptableObjectUtility
    {

        /// <summary>Saves the <see cref="ScriptableObject"/>.</summary>
        /// <remarks>Safe to call from outside editor, but has no effect.</remarks>
        public static void Save(this ScriptableObject obj)
        {

#if UNITY_EDITOR
            if (obj && EditorUtility.IsPersistent(obj))
            {
                if (EditorApplication.isUpdating)
                    EditorApplication.delayCall += () => Save(obj);
                else
                {
                    EditorUtility.SetDirty(obj);
                    AssetDatabase.SaveAssetIfDirty(obj);
                }
            }
#endif

        }

    }

}
