#if UNITY_EDITOR
using AdvancedSceneManager.DependencyInjection.Editor;
using AdvancedSceneManager.Editor.Utility;
using UnityEngine;

namespace AdvancedSceneManager.DependencyInjection
{

    public static partial class DependencyInjectionUtility
    {


        sealed class HierarchyGUIService : IHierarchyGUI
        {

            private HierarchyGUIService()
            { }

            public static HierarchyGUIService instance { get; } = new();
            public GUIStyle defaultStyle { get; }

            public void AddGameObjectGUI(HierarchyGUIUtility.HierarchyGameObjectGUI onGUI, int index = 0) => HierarchyGUIUtility.AddGameObjectGUI(onGUI, index);
            public void AddSceneGUI(HierarchyGUIUtility.HierarchySceneGUI onGUI, int index = 0) => HierarchyGUIUtility.AddSceneGUI(onGUI, index);
            public void RemoveGameObjectGUI(HierarchyGUIUtility.HierarchyGameObjectGUI onGUI) => HierarchyGUIUtility.RemoveGameObjectGUI(onGUI);
            public void RemoveSceneGUI(HierarchyGUIUtility.HierarchySceneGUI onGUI) => HierarchyGUIUtility.RemoveSceneGUI(onGUI);
            public void Repaint() => HierarchyGUIUtility.Repaint();

        }


    }

}

#endif