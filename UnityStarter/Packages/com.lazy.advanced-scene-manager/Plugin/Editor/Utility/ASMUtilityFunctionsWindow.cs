using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    class ASMUtilityFunctionsWindow : EditorWindow
    {

        [ASMWindowElement(ElementLocation.Header, isVisibleByDefault: true)]
        static VisualElement Button()
        {

            var button = new Button() { text = "", tooltip = "ASM Utility functions" };
            button.UseFontAwesome(solid: true);
            button.clicked += Open;

            return button;

        }

        [Shortcut(nameof(ASMUtilityFunctionsWindow), displayName = "ASM/ASM utility functions window")]
        private static void Open()
        {
            var w = GetWindow<ASMUtilityFunctionsWindow>(utility: true, "ASM Utility functions", true);
            w.position = new Rect((Screen.mainWindowDisplayInfo.width / 2) - 400, (Screen.mainWindowDisplayInfo.height / 2) - 200, 800, 400);
        }

        static ASMUtilityFunction[] callbacks;

        [InitializeInEditorMethod]
        static void Initialize() =>
            callbacks = TypeUtility.FindSubclassesAndInstantiate<ASMUtilityFunction>().ToArray();

        VisualElement listTab;
        VisualElement optionsTab;
        VisualElement optionsContent;
        Button backButton;
        Label optionsNameLabel;
        Label infoLabel;
        TextField searchField;
        VisualElement list;

        bool shouldClose;
        void CreateGUI()
        {

            shouldClose = false;
            var viewLocator = AssetDatabaseUtility.FindAssets<ViewLocator>().FirstOrDefault();
            if (!viewLocator || !viewLocator.misc.utilityFunctionsWindow)
            {
                shouldClose = true; //Why unity, does the window still open? And why can't I just call Close() or DestroyImmediate() in here?
                throw new InvalidOperationException("Could not find ViewLocator, you may have to re-install ASM.");
            }

            viewLocator.misc.utilityFunctionsWindow.CloneTree(rootVisualElement);
            listTab = rootVisualElement.Q("tab-list");
            optionsTab = rootVisualElement.Q("tab-options");
            optionsContent = rootVisualElement.Q("options-content");
            backButton = rootVisualElement.Q<Button>("button-back");
            optionsNameLabel = rootVisualElement.Q<Label>("label-name");
            infoLabel = rootVisualElement.Q<Label>("text-info");
            searchField = rootVisualElement.Q<TextField>("text-search");
            list = rootVisualElement.Q("list-functions");

            backButton.clicked += () =>
            {
                listTab.SetVisible(true);
                optionsTab.SetVisible(false);
                optionsContent.Clear();
            };

            searchField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                    Close();
            }, TrickleDown.TrickleDown);

            searchField.RegisterValueChangedCallback(e => QueueSearch(e.newValue));

            listTab.SetVisible(true);
            optionsTab.SetVisible(false);
            optionsContent.Clear();

            ReloadList();

        }

        private void OnDisable()
        {
            listTab?.SetVisible(true);
            optionsTab?.SetVisible(false);
            optionsContent?.Clear();
        }

        private void OnFocus()
        {
            var search = rootVisualElement.Q<TextField>("text-search");
            search?.Focus();
            search?.SelectAll();
        }

        private void OnLostFocus()
        {
            if (listTab.style.display == DisplayStyle.Flex)
                Close();
        }

        new Vector2 minSize = new Vector2(350, 200);
        private void OnGUI()
        {
            if (shouldClose)
            {
                Close();
                return;
            }

            base.minSize = minSize;

            //Close if escape is pressed
            if (listTab.style.display == DisplayStyle.Flex && Event.current.isKey && Event.current.keyCode == KeyCode.Escape)
                Close();
        }

        void InvokeCallback(ASMUtilityFunction function)
        {

            VisualElement optionsGUI = null;
            function.OnInvoke(ref optionsGUI);

            if (optionsGUI is null)
            {
                Close();
            }
            else
            {

                optionsContent.Clear();
                optionsContent.Add(optionsGUI);

                optionsNameLabel.text = function.Name;

                listTab.SetVisible(false);
                optionsTab.SetVisible(true);

                function.onCloseRequest = Close;

            }

        }

        #region List

        void ReloadList()
        {

            list.Clear();
            foreach (var group in callbacks.OrderBy(c => c.Group).GroupBy(c => c.Group).Select(g => (groupName: g.Key, callbacks: g)))
            {

                var groupBox = new GroupBox
                {
                    text = string.IsNullOrWhiteSpace(group.groupName) ? "Other" : group.groupName,
                };

                groupBox.style.width = 140;
                groupBox.style.backgroundColor = new Color(0, 0, 0, 0);
                groupBox.Q<Label>().style.opacity = 0.6f;
                groupBox.Q<Label>().style.paddingBottom = 0;
                groupBox.Q<Label>().style.fontSize = 12;
                groupBox.Q<Label>().style.marginLeft = 3;

                foreach (var function in group.callbacks.OrderBy(c => c.Order))
                {
                    var button = new Button(() => InvokeCallback(function))
                    {
                        text = function.Name,
                        userData = function
                    };

                    button.style.unityTextAlign = TextAnchor.MiddleLeft;
                    button.style.marginTop = 0;
                    button.style.marginBottom = 0;
                    button.style.paddingTop = 4;
                    button.style.paddingBottom = 4;
                    button.style.paddingLeft = 4;
                    button.style.paddingRight = 4;

                    button.RegisterCallback<PointerEnterEvent>(e => infoLabel.text = string.IsNullOrWhiteSpace(function.Description) ? "No information" : function.Description);
                    button.RegisterCallback<PointerOutEvent>(e => infoLabel.text = "Hover over a button for more information");

                    groupBox.Add(button);
                }

                list.Add(groupBox);

            }
        }

        CancellationTokenSource debounceTokenSource;
        async void QueueSearch(string q)
        {
            debounceTokenSource?.Cancel();
            debounceTokenSource?.Dispose();

            debounceTokenSource = new CancellationTokenSource();
            var token = debounceTokenSource.Token;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(0.25f), token);

                if (!token.IsCancellationRequested)
                    Search(q);
            }
            catch (TaskCanceledException)
            { }
        }

        void Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                ResetSearch();
                return;
            }

            list.Query().OfType<GroupBox>().ForEach(group =>
            {

                var anyVisible = false;

                group.Query<Button>().ForEach(button =>
                {

                    var function = (ASMUtilityFunction)button.userData;

                    if (function.Name.Contains(q, StringComparison.InvariantCultureIgnoreCase) || function.Group.Contains(q, StringComparison.InvariantCultureIgnoreCase))
                    {
                        anyVisible = true;
                        button.SetVisible(true);
                    }
                    else
                        button.SetVisible(false);

                });

                group.SetVisible(anyVisible);

            });

        }

        void ResetSearch()
        {
            list.Query().OfType<GroupBox>().ForEach(group =>
            {
                group.Query<Button>().ForEach(button => button.SetVisible(true));
                group.SetVisible(true);
            });
        }

        #endregion

    }

}
