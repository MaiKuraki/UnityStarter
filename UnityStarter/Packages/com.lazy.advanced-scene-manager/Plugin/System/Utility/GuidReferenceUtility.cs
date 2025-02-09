using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace AdvancedSceneManager.Utility
{

    /// <summary>An utility for referencing objects globally.</summary>
    public class GuidReferenceUtility
    {

        internal static readonly List<GuidReference> references = new();

        internal static void RegisterRuntime(GuidReference reference)
        {

            if (!SceneManager.settings.project.enableGUIDReferences)
                return;

            if (!reference)
                return;

            references.Remove(reference);
            references.Add(reference);

        }

        internal static void UnregisterRuntime(GuidReference reference)
        {
            references.Remove(reference);
        }

        /// <summary>Gets if reference exists.</summary>
        public static bool IsRegistered(GuidReference reference) =>
            references.Contains(reference);

        /// <summary>Gets if reference exists.</summary>
        public static bool HasReference(string id) =>
            Find(id);

        /// <summary>Tries to find the reference.</summary>
        public static bool TryFind(string id, out GuidReference obj)
        {
            obj = Find(id);
            return obj;
        }

        /// <summary>Finds a reference if it exists.</summary>
        public static GuidReference Find(string id) =>
            references.FirstOrDefault(r => r.guid == id);

        /// <summary>Adds a persistent reference to this <see cref="GameObject"/>.</summary>
        /// <remarks>Can only add in editor, returns <see langword="null"/> otherwise.</remarks>
        public static string GetOrAddPersistent(GameObject obj)
        {

            if (!SceneManager.settings.project.enableGUIDReferences)
                return null;

            if (obj.TryGetComponent<GuidReference>(out var guidReference))
                return guidReference.guid;

#if UNITY_EDITOR
            return Add(obj);
#else
            return null;
#endif
        }

#if UNITY_EDITOR

        /// <summary>Adds a persistent reference to this <see cref="GameObject"/>.</summary>
        /// <remarks>Only usable in editor.</remarks>
        public static string Add(GameObject obj)
        {

            if (!SceneManager.settings.project.enableGUIDReferences)
                return null;

            if (obj.TryGetComponent<GuidReference>(out var guidReference))
                return guidReference.guid;

            guidReference = obj.AddComponent<GuidReference>();

            _ = EditorSceneManager.MarkSceneDirty(obj.scene);
            _ = EditorSceneManager.SaveScene(obj.scene);

            return guidReference.guid;

        }

        /// <summary>Removes a persistent reference to this <see cref="GameObject"/>.</summary>
        /// <remarks>Only usable in editor.</remarks>
        public static void Remove(GameObject obj, bool saveScene)
        {

            if (!obj.TryGetComponent<GuidReference>(out var guidReference))
                return;

            Object.DestroyImmediate(guidReference);

            _ = EditorSceneManager.MarkSceneDirty(obj.scene);
            if (saveScene)
                _ = EditorSceneManager.SaveScene(obj.scene);

        }

#endif

        /// <summary>Generates an id.</summary>
        /// <remarks>Uses https://blog.codinghorror.com/equipping-our-ascii-armor.</remarks>
        public static string GenerateID() =>
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=');

    }

}
