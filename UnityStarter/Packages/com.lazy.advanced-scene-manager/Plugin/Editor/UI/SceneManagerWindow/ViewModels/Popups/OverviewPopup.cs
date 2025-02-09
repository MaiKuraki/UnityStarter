using System;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class OverviewPopup : ViewModel, IPopup
    {

        bool isImportedExpanded
        {
            get => Get(false);
            set => Set(value);
        }

        bool isUnimportedExpanded
        {
            get => Get(false);
            set => Set(value);
        }

        bool isASMExpanded
        {
            get => Get(false);
            set => Set(value);
        }

        string lastSearch
        {
            get => Get(string.Empty);
            set => Set(value);
        }

        Foldout importedFoldout;
        Foldout unimportedFoldout;
        Foldout asmFoldout;

        VisualElement searchContainer;

        public override void OnAdded()
        {

            importedFoldout = view.Q<Foldout>("foldout-imported");
            unimportedFoldout = view.Q<Foldout>("foldout-unimported");
            asmFoldout = view.Q<Foldout>("foldout-asm");
            searchContainer = view.Q("container-search");

            SetupSearch();

            Reload();
            SceneImportUtility.scenesChanged += Reload;

            view.Q<ScrollView>().PersistScrollPosition();

        }

        bool isReopen;
        public override void OnReopen() =>
            isReopen = true;

        public override void OnRemoved()
        {
            isReopen = false;
            lastSearch = string.Empty;
            view.Q<ScrollView>().ClearScrollPosition();
        }

        public override void OnWindowDisable()
        {
            isImportedExpanded = importedFoldout.value;
            isUnimportedExpanded = unimportedFoldout.value;
            isASMExpanded = asmFoldout.value;
            lastSearch = searchField.value;
        }

        void Reload()
        {

            StopDelayedSearch();

            importedFoldout.Clear();
            unimportedFoldout.Clear();
            asmFoldout.Clear();
            searchContainer.Clear();

            unimportedFoldout.SetVisible(false);
            importedFoldout.SetVisible(false);
            asmFoldout.SetVisible(false);
            searchContainer.SetVisible(false);

            if (!isSearch)
            {

                var asmScenes = SceneManager.assets.defaults.Enumerate().OrderBy(s => s.name).ToArray();
                var importedScenes = SceneManager.assets.scenes.Where(s => s).Except(asmScenes).OrderBy(s => s.name).ToArray();
                var unimportedScenes = SceneImportUtility.unimportedScenes.Select(AssetDatabase.LoadAssetAtPath<SceneAsset>).Where(s => s).OrderBy(s => s.name).ToArray();

                foreach (var scene in importedScenes)
                    importedFoldout.Add(SceneField(scene));

                foreach (var scene in unimportedScenes)
                    unimportedFoldout.Add(SceneField(scene));

                foreach (var scene in asmScenes)
                    asmFoldout.Add(SceneField(scene));

                unimportedFoldout.SetVisible(unimportedScenes.Any());
                importedFoldout.SetVisible(importedScenes.Any());
                asmFoldout.SetVisible(asmScenes.Any());

                importedFoldout.value = isReopen ? isImportedExpanded : !unimportedScenes.Any();
                unimportedFoldout.value = isReopen ? isUnimportedExpanded : unimportedScenes.Any();
                asmFoldout.value = isReopen ? isASMExpanded : false;

            }
            else
            {

                var scenes = SceneManager.assets.scenes.Cast<object>().Concat(SceneImportUtility.unimportedScenes);
                var dict = scenes.ToDictionary(kvp =>
                {
                    if (kvp is Scene s)
                        return s.path;
                    else if (kvp is string path)
                        return path;
                    return string.Empty;
                });

                var searchedScenes = dict.Select(kvp => (score: GetMatchDistance(kvp.Key, searchField.text), value: kvp.Value)).Where(kvp => kvp.score < 10).OrderByDescending(kvp => kvp.score).ToArray();

                foreach (var (score, value) in searchedScenes)
                {
                    if (value is Scene s)
                        searchContainer.Add(SceneField(s));
                    else if (value is string path && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is SceneAsset asset && asset)
                        searchContainer.Add(SceneField(asset));
                }

                searchContainer.SetVisible(true);

            }

        }

        VisualElement SceneField(Scene scene)
        {

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            var element = new SceneField();
            element.style.marginBottom = 8;
            element.value = scene;
            element.Q(className: "unity-object-field__input").SetEnabled(false);
            element.style.flexGrow = 1;
            container.Add(element);

            container.Add(
                VisualElementUtility.MenuButton(container,
                    ("Unimport", () => SceneImportUtility.Unimport(scene), !scene.isDefaultASMScene)
                ));

            return container;

        }

        VisualElement SceneField(SceneAsset scene)
        {

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;

            var element = new ObjectField();
            element.Q(className: "unity-object-field__input").SetEnabled(false);
            element.value = scene;
            element.style.flexGrow = 1;

            container.Add(element);

            container.Add(
                VisualElementUtility.MenuButton(container,
                    ("Import", () => SceneImportUtility.Import(AssetDatabase.GetAssetPath(scene)), true)
                ));

            return container;

        }

        #region Setup search

        System.Timers.Timer timer;
        TextField searchField;
        bool isSearch => !string.IsNullOrWhiteSpace(searchField?.text);

        void SetupSearch()
        {

            searchField = view.Q<TextField>("text-search");

#if UNITY_2022_1_OR_NEWER
            searchField.selectAllOnMouseUp = false;
            searchField.selectAllOnFocus = false;
#endif

            searchField.value = lastSearch;

            if (timer is null)
            {
                timer = new(500) { Enabled = false };
                timer.Elapsed += (s, e) =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        timer.Stop();
                        Reload();
                    };
                };
            }

            searchField.RegisterCallback<KeyUpEvent>(e =>
            {

                timer.Stop();
                if (e.keyCode is KeyCode.KeypadEnter or KeyCode.Return)
                    Reload();
                else
                    timer.Start();

            });

        }

        void StopDelayedSearch() =>
            timer?.Stop();

        // Custom logic to determine match distance
        private static int GetMatchDistance(string key, string searchString)
        {
            // Check if searchString is a substring of key
            int substringIndex = key.IndexOf(searchString, StringComparison.OrdinalIgnoreCase);
            if (substringIndex != -1)
            {
                return 0; // Perfect match found
            }

            // If not, use Levenshtein distance
            return LevenshteinDistance(key, searchString);
        }

        // Levenshtein distance algorithm
        private static int LevenshteinDistance(string s, string t)
        {
            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++)
                d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[s.Length, t.Length];
        }

        #endregion


    }

}
