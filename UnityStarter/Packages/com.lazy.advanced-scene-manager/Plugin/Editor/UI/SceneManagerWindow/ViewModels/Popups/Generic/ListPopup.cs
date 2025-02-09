using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Models;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    abstract class ListPopup<T> : ExtendableViewModel, IPopup where T : ASMModel
    {

        public override VisualElement ExtendableButtonContainer => null;
        public override bool IsOverflow => false;
        public override ElementLocation Location => ElementLocation.Header;

        VisualTreeAsset listItem => viewLocator.items.list;

        public abstract void OnAdd();
        public abstract void OnSelected(T item);

        public virtual void OnRemove(T item) { }
        public virtual void OnRename(T item) { }
        public virtual void OnDuplicate(T item) { }

        public virtual bool displayRenameButton { get; }
        public virtual bool displayRemoveButton { get; }
        public virtual bool displayDuplicateButton { get; }

        public abstract string noItemsText { get; }
        public abstract string headerText { get; }

        public abstract IEnumerable<T> items { get; }

        T[] list;

        VisualElement container;
        public override void OnAdded()
        {

            base.OnAdded();

            this.container = view;
            this.list = items.Where(o => o).ToArray();

            container.BindToSettings();

            container.Q<Label>("text-header").text = headerText;
            container.Q<Label>("text-no-items").text = noItemsText;

            container.Q<Button>("button-add").clicked += OnAdd;

            var list = container.Q<ListView>();

            list.makeItem = () => Instantiate(listItem);

            list.unbindItem = Unbind;
            list.bindItem = Bind;
            Reload();

            view.Q<ScrollView>().PersistScrollPosition();

        }

        public void Reload()
        {
            list = items.Where(o => o).ToArray();
            container.Q("text-no-items").SetVisible(!list.Any());
            container.Q<ListView>().itemsSource = list;
            container.Q<ListView>().Rebuild();
        }

        void Unbind(VisualElement element, int index)
        {

            var nameButton = element.Q<Button>("button-name");
            var menuButton = element.Q<Button>("button-menu");
            nameButton.userData = null;

            nameButton.UnregisterCallback<ClickEvent>(OnSelect);

        }

        void OnSelect(ClickEvent e)
        {
            if (e.target is Button button && button.userData is T t)
                OnSelected(t);
        }

        void Bind(VisualElement element, int index)
        {

            var item = list.ElementAt(index);
            var nameButton = element.Q<Button>("button-name");
            var menuButton = element.Q<Button>("button-menu");

            nameButton.text = item.name;
            nameButton.RegisterCallback<ClickEvent>(OnSelect);
            menuButton.SetupMenuButton(
                ("Rename", () => OnRename(item), displayRenameButton),
                ("Duplicate", () => OnDuplicate(item), displayDuplicateButton),
                ("Remove", () => OnRemove(item), displayRemoveButton));

            menuButton.SetVisible(displayRenameButton || displayDuplicateButton || displayRemoveButton);
            menuButton.style.opacity = 0;

            nameButton.userData = item;

        }

    }

}
