using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace AdvancedSceneManager.Utility
{

    /// <summary>Represents a persistent reference to the <see cref="GameObject"/> that this is attached to, see also <see cref="GuidReferenceUtility"/> .</summary>
    [ExecuteAlways, ExecuteInEditMode]
    public class GuidReference : MonoBehaviour
    {

        public string guid = GuidReferenceUtility.GenerateID();

        void OnValidate()
        {

            if (!enabled)
                enabled = true;

            //Why is this the only reliable callback outside of playmode? Start() or Awake() does not seem to be called after domain reload?
            //Constructor cannot be used, too many unity apis are called when registering
            Register();

        }

        void Start() => Register();
        void Awake() => Register();

        void Register()
        {
            //Debug.Log("registered: " + guid);
            GuidReferenceUtility.RegisterRuntime(this);
        }

        void OnDestroy()
        {
            //Debug.Log("unregistered: " + guid);
            GuidReferenceUtility.UnregisterRuntime(this);
        }

#if UNITY_EDITOR

        [CustomEditor(typeof(GuidReference))]
        public class Editor : UnityEditor.Editor
        {

            GuidReference reference;
            private void OnEnable()
            {
                reference = (GuidReference)target;
            }

            public override void OnInspectorGUI()
            {

                EditorGUILayout.BeginVertical(new GUIStyle() { padding = new(8, 8, 8, 8) });
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                GUILayout.Label(reference.guid);

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();

                // Check if the component is part of a prefab asset or instance
                var assetType = PrefabUtility.GetPrefabAssetType(reference);
                var instanceStatus = PrefabUtility.GetPrefabInstanceStatus(reference);

                // Display the prefab status in the inspector
                if (assetType != PrefabAssetType.NotAPrefab)
                    EditorGUILayout.HelpBox("This component is part of a prefab asset, this is not supported.", MessageType.Error);

                else if (PrefabUtility.IsPartOfAnyPrefab(reference))
                    EditorGUILayout.HelpBox("This component is part of a prefab asset, this is not supported.", MessageType.Error);

                else
                {

                    var references = GuidReferenceUtility.references.Where(r => r.guid == reference.guid);
                    if (references.Count() > 1)
                    {

                        EditorGUILayout.HelpBox("This reference is duplicated.", MessageType.Error);

                        if (GUILayout.Button("Regenerate id"))
                        {
                            reference.guid = GuidReferenceUtility.GenerateID();
                            EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
                        }

                        EditorGUILayout.Space();
                        foreach (var reference in references)
                            if (reference)
                            {
                                GUI.enabled = false;
                                EditorGUILayout.ObjectField(reference, typeof(GuidReference), allowSceneObjects: false);
                                GUI.enabled = true;
                            }

                    }

                }

                if (!SceneManager.settings.project.enableGUIDReferences)
                    EditorGUILayout.HelpBox("GUID references are disabled.", MessageType.Error);

                EditorGUILayout.EndVertical();

            }

            public override bool UseDefaultMargins() =>
                false;

        }

#endif

    }

}
