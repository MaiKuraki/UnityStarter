using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>Represents a resolved <see cref="ObjectReference"/>.</summary>
    public struct ResolvedReference
    {

        public ResolveStatus result;
        public Scene? scene;
        public GameObject gameObject;
        public Component component;
        public FieldInfo field;

        public int index;
        public bool isTargetingUnityEvent;
        public bool isTargetingArray;

        public Object resolvedTarget;

        public bool hasBeenRemoved;

        public ResolvedReference(ResolveStatus result, Scene? scene = null, GameObject gameObject = null, Component component = null, FieldInfo field = null, int index = 0, bool isTargetingArray = false, bool isTargetingUnityEvent = false, Object resolvedTarget = null, bool hasBeenRemoved = false)
        {
            this.scene = scene;
            this.result = result;
            this.gameObject = gameObject;
            this.component = component;
            this.field = field;
            this.index = index;
            this.isTargetingArray = isTargetingArray;
            this.isTargetingUnityEvent = isTargetingUnityEvent;
            this.resolvedTarget = resolvedTarget;
            this.hasBeenRemoved = hasBeenRemoved;
        }

        public override string ToString() =>
            ToString(includeScene: true, includeGameObject: false);

        public string ToString(bool includeScene = true, bool includeGameObject = true)
        {

            var str = "";
            if (scene.HasValue && includeScene)
                str += scene.Value.name;

            if (gameObject != null && includeGameObject)
                str += (includeScene ? "." : "") + gameObject.name;

            if (!includeScene || !includeGameObject)
                str = "::" + str;

            if (component)
            {
                if (includeGameObject)
                    str += ".";
                str += GetComponentName();
            }

            if (field != null)
                str += "." + field.Name;

            if (isTargetingArray || isTargetingUnityEvent)
                str += $"[{index}]";

            if (result != ResolveStatus.Succeeded)
                str += "\nError: " + result.ToString();

            return str;

        }

        string GetComponentName() =>
            component ? (component.GetType().Name) : null;

    }

}
