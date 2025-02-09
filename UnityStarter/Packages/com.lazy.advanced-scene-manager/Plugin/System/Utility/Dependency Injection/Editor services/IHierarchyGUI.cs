#if UNITY_EDITOR
using AdvancedSceneManager.Editor.Utility;
using UnityEngine;

namespace AdvancedSceneManager.DependencyInjection.Editor
{

    /// <inheritdoc cref="HierarchyGUIUtility"/>
    public interface IHierarchyGUI : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="HierarchyGUIUtility.AddSceneGUI"/>
        void AddSceneGUI(HierarchyGUIUtility.HierarchySceneGUI onGUI, int index = 0);

        /// <inheritdoc cref="HierarchyGUIUtility.AddGameObjectGUI"/>
        void AddGameObjectGUI(HierarchyGUIUtility.HierarchyGameObjectGUI onGUI, int index = 0);

        /// <inheritdoc cref="HierarchyGUIUtility.RemoveSceneGUI"/>
        void RemoveSceneGUI(HierarchyGUIUtility.HierarchySceneGUI onGUI);

        /// <inheritdoc cref="HierarchyGUIUtility.RemoveGameObjectGUI"/>
        void RemoveGameObjectGUI(HierarchyGUIUtility.HierarchyGameObjectGUI onGUI);

        /// <inheritdoc cref="HierarchyGUIUtility.defaultStyle"/>
        GUIStyle defaultStyle { get; }

        /// <inheritdoc cref="HierarchyGUIUtility.Repaint"/>
        void Repaint();

    }

}

#endif