using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Notifications;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    abstract class ImportPopup<T, TSelf> : ViewModel, IPopup where TSelf : ViewModel, IPopup, new()
    {

        public override void OnAdded()
        {

            allToggle = view.Q<Toggle>("toggle-all");
            allToggle.RegisterValueChangedCallback(e =>
            {

                var value = e.newValue;

                foreach (var item in items)
                    item.isChecked = value;

                //Toggles aren't bound, and currently rendered ones in viewport need to be set directly.
                //When scrolling, new items will retrieve new values automatically.
                view.Query<Toggle>("list-item-toggle").ForEach(toggle => toggle.value = value);

            });

            ReloadItems();
            SetupHeader();
            SetupFooter();
            SetupList();

            ReloadButtons();

            SceneImportUtility.scenesChanged += Reload;

            view.Q<ScrollView>().PersistScrollPosition();
            view.Q<ListView>().Q<ScrollView>().name = "scroll-import-popup-list";
            view.Q<ListView>().Q<ScrollView>().PersistScrollPosition();

        }

        public override void OnRemoved()
        {
            SceneImportUtility.scenesChanged -= Reload;
            view.Q<ListView>().Q<ScrollView>().ClearScrollPosition();
        }

        public override void OnWindowEnable() =>
            Reload();

        public void Reload()
        {
            ReloadItems();
            SetupList();
            ReloadButtons();
        }

        #region Header

        public abstract string headerText { get; }
        public abstract bool displayAutoImportField { get; }
        public virtual string subtitleText { get; }

        void SetupHeader()
        {

            view.Q<Label>("label-title").text = headerText;
            view.Q<Label>("label-subtitle").text = subtitleText;
            view.Q<Label>("label-subtitle").SetVisible(!string.IsNullOrWhiteSpace(subtitleText));

            SetupAutoImportOption();

        }

        #endregion
        #region Import option field

        void SetupAutoImportOption()
        {

            var importPopup = view.Q("import-option-field");
            importPopup.SetVisible(displayAutoImportField);

            importPopup.BindToSettings();
            importPopup.SetEnabled(true);
            importPopup.tooltip =
                "Manual:\n" +
                "Manually import each scene.\n\n" +
                "SceneCreated:\n" +
                "Import scenes when they are created.";

        }

        #endregion
        #region List

        ListView list;
        Toggle allToggle;

        public CheckableItem<T>[] items { get; private set; }

        public abstract IEnumerable<T> GetItems();

        public void ReloadItems()
        {
            items = GetItems().Select(i => new CheckableItem<T>() { value = i, isChecked = items?.FirstOrDefault(i2 => EqualityComparer<T>.Default.Equals(i2.value, i))?.isChecked ?? true }).ToArray();
            if (list is not null)
            {
                list.itemsSource = items;
                view.Q("label-no-items").SetVisible(items.Length == 0);
                list.Rebuild();
                UpdateAllToggle();
            }
        }

        public abstract void SetupItem(VisualElement element, CheckableItem<T> item, int index, out string text);

        void SetupItem(VisualElement element, int index)
        {

            var toggle = (Toggle)element;

            toggle.RegisterValueChangedCallback(OnCheckedChanged);

            var item = items[index];
            toggle.userData = item;
            toggle.SetValueWithoutNotify(item.isChecked);

            SetupItem(element, item, index, out var text);
            toggle.label = text;

        }

        void UnsetupItem(VisualElement element, int index)
        {
            var toggle = (Toggle)element;
            toggle.UnregisterValueChangedCallback(OnCheckedChanged);
        }

        void OnCheckedChanged(ChangeEvent<bool> e)
        {

            if (e.currentTarget is not Toggle toggle)
                return;

            if (toggle.userData is not CheckableItem<T> item)
                return;

            item.isChecked = e.newValue;
            ReloadButtons();
            UpdateAllToggle();

        }

        void SetupList()
        {

            list = view.Q<ListView>();
            list.itemsSource = items;
            list.makeItem = () => new Toggle() { name = "list-item-toggle" };

            list.bindItem = SetupItem;
            list.unbindItem = UnsetupItem;
            ReloadItems();

        }

        void UpdateAllToggle()
        {

            var isAllChecked = items.All(i => i.isChecked);
            var isAllUnchecked = items.All(i => !i.isChecked);

            allToggle.SetValueWithoutNotify(isAllChecked);
            allToggle.showMixedValue = !isAllChecked && !isAllUnchecked;
            allToggle.SetVisible(items.Length > 1);

        }

        #endregion
        #region Footer

        Button button1;
        Button button2;

        public abstract string button1Text { get; }
        public virtual string button2Text { get; }

        void SetupFooter()
        {
            SetupFooterButton("button-cancel", "Cancel", OnCancelClick);
            SetupFooterButton(ref button1, "button-1", button1Text, () => OnButton1Click(items.Where(i => i.isChecked).Select(i => i.value)));
            SetupFooterButton(ref button2, "button-2", button2Text, () => OnButton2Click(items.Where(i => i.isChecked).Select(i => i.value)));
        }

        void SetupFooterButton(string name, string text, Action click)
        {
            Button button = null;
            SetupFooterButton(ref button, name, text, click);
        }

        void SetupFooterButton(ref Button button, string name, string text, Action click)
        {

            button = view.Q<Button>(name);
            button.text = text;
            button.SetVisible(!string.IsNullOrWhiteSpace(text));

            button.clicked += () =>
            {
                click.Invoke();
                ClosePopup();
                notifications.ReloadPersistentNotifications();
            };

        }

        public void ReloadButtons()
        {
            var isEnabled = items?.Count(i => i.isChecked) > 0;
            button1.SetEnabled(isEnabled);
            button2.SetEnabled(isEnabled);
        }

        public abstract void OnButton1Click(IEnumerable<T> items);

        public virtual void OnButton2Click(IEnumerable<T> items) { }

        public virtual void OnCancelClick() { }

        #endregion

    }

}
