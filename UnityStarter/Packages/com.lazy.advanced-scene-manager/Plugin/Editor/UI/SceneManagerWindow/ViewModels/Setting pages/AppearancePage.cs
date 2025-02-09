using System;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class AppearancePage : ViewModel, ISettingsPage
    {

        public string Header => "Appearance";

        public override void OnAdded()
        {

            view.BindToUserSettings();

            SetupToolbarButton();
            SetupASMElementLocationSection(ElementLocation.Header, view.Q<GroupBox>("group-buttons-header"));
            SetupASMElementLocationSection(ElementLocation.Collection, view.Q<GroupBox>("group-buttons-collection"));
            SetupASMElementLocationSection(ElementLocation.Scene, view.Q<GroupBox>("group-buttons-scene"));
            SetupASMElementLocationSection(ElementLocation.Footer, view.Q<GroupBox>("group-buttons-footer"));

        }

        void SetupToolbarButton()
        {

            var groupInstalled = view.Q("group-toolbar").Q("group-installed");
            var groupNotInstalled = view.Q("group-toolbar").Q("group-not-installed");

#if TOOLBAR_EXTENDER

            groupInstalled.SetVisible(true);
            groupNotInstalled.SetVisible(false);

            Setup(view.Q("slider-toolbar-button-offset"));
            Setup(view.Q("slider-toolbar-button-count"));

            void Setup(VisualElement element)
            {
                element.SetVisible(true);
                element.Q("unity-drag-container").RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (e.pressedButtons == 1)
                        ToolbarButton.Repaint();
                });
            }

#endif

        }

        #region ASM Element section

        void SetupASMElementLocationSection(ElementLocation location, GroupBox group)
        {

            var list = group.Q("extendable-element-container");
            var scroll = group.Q("scroll-extendable-buttons");
            var listGroup = group.Q("extendable-buttons-group");
            var separator = group.Q("separator");

            var maxCount = group.Q<IntegerField>("int-extendable-button-max-count");
            maxCount?.BindToUserSettings();

            maxCount?.RegisterValueChangedCallback(e =>
            {
                if (location == ElementLocation.Collection)
                    SceneManager.settings.user.OnPropertyChanged(nameof(ASMUserSettings.m_maxExtendableButtonsOnCollection));
                else if (location == ElementLocation.Scene)
                    SceneManager.settings.user.OnPropertyChanged(nameof(ASMUserSettings.m_maxExtendableButtonsOnScene));
            });

            var callbacks = ExtendableViewModel.Enumerate(location);

            list.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            list.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            list.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);

            var anyItems = false;
            list.Clear();
            foreach (var callback in callbacks)
            {
                try
                {

                    var element = callback.ElementCallback?.Invoke(location, null, null);
                    if (element is not null)
                    {

                        element.AddToClassList("extendable-button-toggle");

                        list.Add(element);
                        element.userData = callback;

                        UpdateVisible(element, callback);
                        anyItems = true;

                    }

                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            listGroup.SetVisible(anyItems);
            separator?.SetVisible(anyItems);

            //TODO: Remove this if we add any static elements to group
            if (!anyItems && group.name == "group-buttons-scene")
            {
                var l = new Label("No items");
                l.style.opacity = 0.4f;
                l.style.marginLeft = 4;
                l.style.marginTop = -8;
                group.Add(l);
            }

        }

        VisualElement draggedElement;
        VisualElement dummy;
        int originalIndex;
        Vector2 originalPosition;
        bool isDragging;

        const float dragThreshold = 10.0f; // Distance threshold in pixels

        void OnPointerDown(PointerDownEvent e)
        {
            if (!((VisualElement)e.target).ClassListContains("extendable-button-toggle"))
                return;

            // Call BeginDrag to start the dragging process
            BeginDrag(e);
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (draggedElement == null)
                return;

            e.StopImmediatePropagation();
            e.StopPropagation();

            var distanceMoved = Vector2.Distance(e.localPosition, originalPosition);

            if (!isDragging && distanceMoved < dragThreshold)
                return;  // Do nothing until the pointer has moved past the threshold

            // Once we've crossed the threshold, start the drag
            if (!isDragging)
                isDragging = true;

            UpdateDragPosition(e.localPosition);

        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (draggedElement == null) return;

            e.StopImmediatePropagation();
            e.StopPropagation();

            // Call EndDrag to finalize the drag and cleanup
            EndDrag(e);
        }

        void BeginDrag(PointerDownEvent e)
        {
            e.StopImmediatePropagation();
            e.StopPropagation();

            draggedElement = e.target as VisualElement;
            originalIndex = draggedElement.parent.IndexOf(draggedElement);

            originalPosition = e.localPosition;
            isDragging = false;

            draggedElement.panel.visualTree.RegisterCallback<PointerUpEvent>(OnPointerUp);
            draggedElement.panel.visualTree.RegisterCallback<MouseLeaveWindowEvent>(OnMouseLeaveWindow);

            // Temporarily disable picking for all siblings while dragging
            SetPickingModeForButtons(PickingMode.Ignore);
        }

        void EndDrag(PointerUpEvent e)
        {

            if (draggedElement is null)
                return;

            var callback = (ExtendableViewModel.Callback)draggedElement.userData;
            if (!isDragging)
                ToggleVisibility(callback);
            else
                SaveOrder(draggedElement, callback);

            SetPickingModeForButtons(PickingMode.Position);

            draggedElement.ReleasePointer(e.pointerId);
            SetPosition(Position.Relative);
            draggedElement = null;

            dummy?.RemoveFromHierarchy();
            dummy = null;

            isDragging = false;

            if (e != null && e.target != null)
            {
                ((VisualElement)e.target).panel?.visualTree.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                ((VisualElement)e.target).panel?.visualTree.UnregisterCallback<MouseLeaveWindowEvent>(OnMouseLeaveWindow);
            }

            if (e is not null)
            {
                e.StopImmediatePropagation();
                e.StopPropagation();
            }
        }

        void SetPickingModeForButtons(PickingMode mode)
        {

            if (draggedElement == null)
                return;

            foreach (var element in draggedElement.parent.Children())
                element.pickingMode = mode;

        }

        void UpdateDragPosition(Vector2 currentPosition)
        {

            if (draggedElement.parent.Children().OfType<Button>().Count() == 1)
                return;

            SetPosition(Position.Absolute, currentPosition.x - (draggedElement.resolvedStyle.width / 2));

            if (dummy is null)
            {
                // Create a dummy element to keep track of the original spot
                dummy = new VisualElement();
                dummy.style.width = draggedElement.resolvedStyle.width + draggedElement.resolvedStyle.marginLeft + draggedElement.resolvedStyle.marginRight;
                draggedElement.parent.Insert(originalIndex, dummy);
            }

            // Reorder the element based on the new position
            VisualElement container = draggedElement.parent;
            foreach (var sibling in container.Children())
            {
                if (sibling == draggedElement) continue;

                if (IsMouseOverElement(currentPosition, sibling))
                {
                    int targetIndex = container.IndexOf(sibling);

                    // For row-reverse, insertion logic needs to consider reverse order
                    if (targetIndex != originalIndex)
                    {
                        container.Remove(draggedElement);
                        originalIndex = targetIndex > originalIndex ? targetIndex - 1 : targetIndex;
                        container.Insert(targetIndex, draggedElement);
                        dummy.RemoveFromHierarchy();
                        container.Insert(originalIndex, dummy);
                    }
                    break;
                }
            }
        }

        void SetPosition(Position position, float left = 0)
        {

            if (draggedElement == null)
                return;

            if (draggedElement.parent.Children().OfType<Button>().Count() == 1)
                return;

            draggedElement.style.position = position;
            draggedElement.style.left = position == Position.Absolute ? new Length(left) : StyleKeyword.Auto;
            draggedElement.pickingMode = position == Position.Absolute ? PickingMode.Ignore : PickingMode.Position;

        }

        bool IsMouseOverElement(Vector2 mousePosition, VisualElement element)
        {
            var worldBound = element.localBound;
            return worldBound.Contains(mousePosition);
        }

        void OnMouseLeaveWindow(MouseLeaveWindowEvent evt)
        {
            // Drop the element when the mouse leaves the window by calling EndDrag
            EndDrag(PointerUpEvent.GetPooled());
        }

        void UpdateVisible(VisualElement element, ExtendableViewModel.Callback callback)
        {
            element.EnableInClassList("active", SceneManager.settings.user.m_extendableButtons.GetValueOrDefault(callback.Name, callback.Attribute.IsVisibleByDefault));
        }

        void ToggleVisibility(ExtendableViewModel.Callback callback)
        {

            var d = SceneManager.settings.user.m_extendableButtons;
            if (!d.ContainsKey(callback.Name))
                d.Add(callback.Name, callback.Attribute.IsVisibleByDefault);

            d[callback.Name] = !d[callback.Name];
            SceneManager.settings.user.Save();
            SceneManager.settings.user.OnPropertyChanged(nameof(SceneManager.settings.user.m_extendableButtons));

            UpdateVisible(draggedElement, callback);

        }

        void SaveOrder(VisualElement draggedElement, ExtendableViewModel.Callback callback)
        {

            var arr = draggedElement.parent.Children().Select(e => e.userData).OfType<ExtendableViewModel.Callback>().Select(c => c.Name).ToArray();

            if (callback.Attribute.Location == ElementLocation.Header)
            {
                SceneManager.settings.user.m_extendableButtonOrder_Header = arr;
                SceneManager.settings.user.OnPropertyChanged(nameof(SceneManager.settings.user.m_extendableButtonOrder_Header));
            }
            else if (callback.Attribute.Location == ElementLocation.Collection)
            {
                SceneManager.settings.user.m_extendableButtonOrder_Collection = arr;
                SceneManager.settings.user.OnPropertyChanged(nameof(SceneManager.settings.user.m_extendableButtonOrder_Header));
            }
            else if (callback.Attribute.Location == ElementLocation.Scene)
            {
                SceneManager.settings.user.m_extendableButtonOrder_Scene = arr;
                SceneManager.settings.user.OnPropertyChanged(nameof(SceneManager.settings.user.m_extendableButtonOrder_Header));
            }
            else if (callback.Attribute.Location == ElementLocation.Footer)
            {
                SceneManager.settings.user.m_extendableButtonOrder_Footer = arr;
                SceneManager.settings.user.OnPropertyChanged(nameof(SceneManager.settings.user.m_extendableButtonOrder_Header));
            }

        }

        #endregion

    }

}
