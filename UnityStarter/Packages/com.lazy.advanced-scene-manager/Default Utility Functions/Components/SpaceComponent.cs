using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.UtilityFunctions.Components
{
    public static class SpaceComponent
    {
        public static VisualElement Create(float height = 0) => new () { style = { height = height } };    }
}