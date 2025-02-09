using System;
using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AdvancedSceneManager.Editor
{

    [CustomPropertyDrawer(typeof(Scene))]
    public class ScenePropertyDrawer : PropertyDrawer
    {

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) =>
            property.objectReferenceValue = SceneField(position, label, property.objectReferenceValue as Scene);

        #region IMGUI scene field

        /// <summary>Makes a <see cref="Scene"/> field. You can assign <see cref="Scene"/> either by drag and drop objects or by selecting a <see cref="Scene"/> using the <see cref="Scene"/> Picker.</summary>
        static Scene SceneField(Rect position, GUIContent label, Scene obj) =>
            ObjectField(position, label, obj, SceneAssetToScene, typeof(SceneAsset));

        static (Scene obj, bool didConvert) SceneAssetToScene(Object obj)
        {
            if (obj is SceneAsset sceneAsset && sceneAsset && sceneAsset.ASMScene(out var scene))
                return (scene, true);
            else
                return (null, false);
        }

        /// <summary>Makes a object field. You can assign objects either by drag and drop objects or by selecting a object using the Object Picker, allows other types to be dragged onto the field and be converted to the target object..</summary>
        public static T ObjectField<T>(Rect position, GUIContent label, T obj, Func<Object, (T obj, bool didConvert)> convertAllowedType = null, params Type[] extraAllowedTypes) where T : Object
        {

            var type = DragAndDrop.objectReferences.Where(o => o && extraAllowedTypes.Contains(o.GetType())).Any()
                ? DragAndDrop.objectReferences.FirstOrDefault(o => o && extraAllowedTypes.Contains(o.GetType())).GetType()
                : typeof(T);

            var newObj = EditorGUI.ObjectField(position, label, obj, type, false);

            //If object is null then return null,
            //if object is of the correct type, then return it
            //If object is of a convertible type, then lets convert it and return
            //If none of the above, then nothing has changed
            if (newObj == null)
                return default;
            else if (newObj is T t)
                return t;
            else if (extraAllowedTypes.Contains(newObj.GetType()) && convertAllowedType != null)
            {

                (T o, bool didConvert) = convertAllowedType.Invoke(newObj);
                if (didConvert)
                    return o;

            }

            return obj;

        }

        #endregion

    }

}
