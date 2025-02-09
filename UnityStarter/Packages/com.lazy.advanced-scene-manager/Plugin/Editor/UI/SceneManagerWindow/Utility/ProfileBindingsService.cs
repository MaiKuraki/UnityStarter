using System.Collections.Generic;
using AdvancedSceneManager.Models;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Utility
{

    public interface IProfileBindingsService
    {
        void BindEnabledToProfile(VisualElement element);
    }

    class ProfileBindingsService : ViewModel, IProfileBindingsService
    {

        public override void OnAdded()
        {

            OnProfileChanged();
            Profile.onProfileChanged += OnProfileChanged;
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(e => UpdateProfileElements());

            void OnProfileChanged()
            {

                if (Profile.current && Profile.serializedObject is not null)
                    rootVisualElement.Bind(Profile.serializedObject);
                else
                    rootVisualElement.Unbind();

                UpdateProfileElements();

            }

        }

        readonly List<VisualElement> profileElements = new();
        public void BindEnabledToProfile(VisualElement element) =>
            profileElements.Add(element);

        void UpdateProfileElements() =>
            profileElements.ForEach(e => e.SetEnabled(Profile.current));

    }

}