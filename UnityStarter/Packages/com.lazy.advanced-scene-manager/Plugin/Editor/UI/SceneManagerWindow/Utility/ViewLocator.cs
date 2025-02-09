using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Utility
{

    public class ViewLocator : ScriptableObject
    {

        public Main main;
        public Layouts layouts;
        public Popups popups;
        public Settings settings;
        public Items items;
        public Misc misc;

        [Serializable]
        public struct Main
        {
            public VisualTreeAsset progressSpinner;
            public VisualTreeAsset header;
            public VisualTreeAsset search;
            public VisualTreeAsset collection;
            public VisualTreeAsset selection;
            public VisualTreeAsset notification;
            public VisualTreeAsset undo;
            public VisualTreeAsset footer;
        }

        [Serializable]
        public struct Layouts
        {
            public VisualTreeAsset popups;
            public VisualTreeAsset settings;
        }

        [Serializable]
        public struct Popups
        {
            public VisualTreeAsset pickName;
            public VisualTreeAsset confirm;
            public VisualTreeAsset importScene;
            public VisualTreeAsset collection;
            public VisualTreeAsset dynamicCollection;
            public VisualTreeAsset menu;
            public VisualTreeAsset overview;
            public VisualTreeAsset scene;
            public VisualTreeAsset list;
            public VisualTreeAsset legacy;
            public VisualTreeAsset update;
        }

        [Serializable]
        public struct Settings
        {
            public VisualTreeAsset appearance;
            public VisualTreeAsset assets;
            public VisualTreeAsset editor;
            public VisualTreeAsset network;
            public VisualTreeAsset sceneLoading;
            public VisualTreeAsset startup;
            public VisualTreeAsset experimental;
            public VisualTreeAsset updates;
        }

        [Serializable]
        public struct Items
        {
            public VisualTreeAsset collection;
            public VisualTreeAsset scene;
            public VisualTreeAsset undo;
            public VisualTreeAsset list;
            public VisualTreeAsset importScene;
        }

        [Serializable]
        public struct Misc
        {
            public VisualTreeAsset utilityFunctionsWindow;
            public VisualTreeAsset settingsButton;
        }

    }

}
