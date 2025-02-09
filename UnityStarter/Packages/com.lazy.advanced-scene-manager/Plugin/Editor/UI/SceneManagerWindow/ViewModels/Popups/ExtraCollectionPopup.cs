using System.Collections.Generic;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Utility;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ExtraCollectionPopup : ListPopup<SceneCollectionTemplate>
    {

        public override VisualElement ExtendableButtonContainer => view.Q("extendable-button-container");
        public override bool IsOverflow => true;
        public override ElementLocation Location => ElementLocation.Footer;

        public override string noItemsText { get; } = "No templates, you can create one using + button.";
        public override string headerText { get; } = "Collection templates";
        public override IEnumerable<SceneCollectionTemplate> items => SceneManager.assets.templates;

        public override bool displayRenameButton => true;
        public override bool displayRemoveButton => true;

        public override void OnAdded()
        {

            var extendableButtonContainer = new VisualElement();
            extendableButtonContainer.name = "extendable-button-container";
            extendableButtonContainer.style.flexDirection = FlexDirection.RowReverse;
            extendableButtonContainer.style.flexGrow = 1;
            extendableButtonContainer.style.flexShrink = 0;

            view.Insert(0, extendableButtonContainer);

            base.OnAdded();

            var group = new GroupBox();
            var button = new Button(CreateDynamicCollection) { text = "Create dynamic collection" };
            group.Add(button);
            view.Q<ScrollView>().Insert(0, group);

        }

        void CreateDynamicCollection() =>
            Profile.current.CreateDynamicCollection();

        public override void OnAdd()
        {

            pickNamePopup.Prompt(value =>
            {
                SceneCollectionTemplate.CreateTemplate(value);
                OpenPopup<ExtraCollectionPopup>();
            },
            onCancel: OpenPopup<ProfilePopup>);

        }

        public override void OnSelected(SceneCollectionTemplate template)
        {
            Profile.current.CreateCollection(template);
            ClosePopup();
        }

        public override void OnRename(SceneCollectionTemplate template)
        {

            pickNamePopup.Prompt(
                value: template.title,
                onContinue: value =>
                {
                    template.m_title = value;
                    template.Rename(value);
                    OpenPopup<ExtraCollectionPopup>();
                },
                onCancel: OpenPopup<ExtraCollectionPopup>);

        }

        public override void OnRemove(SceneCollectionTemplate template)
        {

            confirmPopup.Prompt(() =>
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(template));
                OpenPopup<ExtraCollectionPopup>();
            },
            onCancel: OpenPopup<ExtraCollectionPopup>,
            message: $"Are you sure you wish to remove '{template.name}'?");


        }

    }

}
