using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class FooterView : ExtendableViewModel, IView
    {

        public override bool IsExtendableButtonsEnabled => true;
        public override ElementLocation Location => ElementLocation.Footer;
        public override VisualElement ExtendableButtonContainer => view.Q("extendable-buttons-container");
        public override bool IsOverflow => false;

        public override void OnAdded()
        {

            base.OnAdded();

            SetupPlayButton(view);
            SetupProfile(view);
            SetupCollectionButton(view);

            OnProfileChanged();
            Profile.onProfileChanged += OnProfileChanged;

        }

        public override void OnRemoved()
        {
            base.OnRemoved();

            Profile.onProfileChanged -= OnProfileChanged;
        }

        void OnProfileChanged()
        {
            view.Q<Button>("button-profile").text = Profile.current ? Profile.current.name : "none";
            UpdateProfileVisibility(view);
        }

        void SetupPlayButton(VisualElement element) =>
            element.Q<Button>("button-play").BindEnabled(SceneManager.settings.user, nameof(SceneManager.settings.user.activeProfile));

        void SetupProfile(VisualElement element)
        {
            view.Q<Button>("button-profile").clicked += OpenPopup<ProfilePopup>;
        }

        void SetupCollectionButton(VisualElement element)
        {

            var button = element.Q("split-button-add-collection");
            profileBindingService.BindEnabledToProfile(button);

            button.Q<Button>("button-add-collection-menu").clicked += OpenPopup<ExtraCollectionPopup>;
            button.Q<Button>("button-add-collection").clicked += () => Profile.current.CreateCollection();

        }

        public override void ApplyAppearanceSettings(VisualElement element)
        {
            UpdateProfileVisibility(element);
        }

        void UpdateProfileVisibility(VisualElement element)
        {
            element.Q("active-profile").SetVisible(SceneManager.settings.user.displayProfileButton || !SceneManager.profile);
        }

        #region Extendable buttons

        [ASMWindowElement(ElementLocation.Footer, isVisibleByDefault: true)]
        static VisualElement SceneHelper()
        {

            var button = new Button() { text = "", tooltip = "Scene helper (try drag me to a Unity UI Button click UnityEvent...)" };
            button.UseFontAwesome();

            button.RegisterCallback<PointerDownEvent>(e =>
            {

#if !UNITY_2023_1_OR_NEWER
                e.PreventDefault();
#endif
                e.StopPropagation();
                e.StopImmediatePropagation();

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new[] { ASMSceneHelper.instance };
                DragAndDrop.StartDrag("Drag: Scene helper");

            }, TrickleDown.TrickleDown);

            return button;

        }

        #endregion

    }

}
