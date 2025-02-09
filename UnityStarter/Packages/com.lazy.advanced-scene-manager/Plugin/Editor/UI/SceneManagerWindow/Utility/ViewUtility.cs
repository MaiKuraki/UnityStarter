using System;
using System.Diagnostics;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AdvancedSceneManager.Editor.UI
{

    static class ViewUtility
    {

        public static void InvokeView<T>(this T viewModel, Action action) where T : IView
        {
            try
            {
                action.Invoke();
            }
            catch (Exception ex)
            {

                if (viewModel?.view is VisualElement element)
                {

                    if (element.userData is HelpBox errorElement)
                        element.userData = errorElement = ExceptionBox(ex, element.style.flexGrow, element.style.flexShrink, $"Could not invoke {GetMethodName(action)} for '{viewModel.GetType().Name}':", errorElement);
                    else
                    {
                        element.Hide();
                        element.userData = errorElement = ExceptionBox(ex, element.style.flexGrow, element.style.flexShrink, $"Could not invoke {GetMethodName(action)} for '{viewModel.GetType().Name}':");
                        errorElement.RemoveFromHierarchy();
                        var index = element.parent.IndexOf(element);
                        element.parent.Insert(index, errorElement);
                    }

                }

                Debug.LogException(ex);

            }
        }

        public static TemplateContainer Instantiate(VisualTreeAsset view)
        {

            try
            {
                if (!view)
                    throw new NullReferenceException("Could not instantiate the view, as it could not be found. Please try triggering a recompile, restart unity, re-import or re-install of ASM.");

                var template = view.Instantiate();
                return template;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                var template = new TemplateContainer();
                template.Add(ExceptionBox(ex, 0, 0));
                return template;
            }

        }

        public static HelpBox ExceptionBox(Exception ex, StyleFloat flexGrow, StyleFloat flexShrink, string header = null, HelpBox existingBox = null)
        {

            var box = existingBox ??= new HelpBox() { name = "Error", messageType = HelpBoxMessageType.Error };

            box.text = header + "\n" + ex.Message;

            box.style.flexGrow = flexGrow;
            box.style.flexShrink = flexShrink;
            box.style.marginLeft = 6;
            box.style.marginTop = 6;
            box.style.marginRight = 6;
            box.style.marginBottom = 6;

            box.AddToClassList("cursor-link");

            box.Children().ForEach(e => e.pickingMode = PickingMode.Ignore);

            if (box.userData is EventCallback<ClickEvent> callback)
                box.UnregisterCallback(callback);

            box.userData = callback = new(e => ViewCallerInCodeEditor(ex));
            box.RegisterCallback(callback);

            return box;

        }

        static string GetMethodName(Delegate del) =>
             del.Method.Name;

        /// <summary>View caller in code editor, only accessible in editor.</summary>
        static void ViewCallerInCodeEditor(Exception ex)
        {

            // Extract stack trace information
            var stackTrace = new StackTrace(ex, true);
            if (stackTrace.FrameCount == 0)
            {
                Debug.LogError("No stack trace information available.");
                return;
            }

            var frame = stackTrace.GetFrame(0);
            var fileName = frame.GetFileName();
            var lineNumber = frame.GetFileLineNumber();

            if (string.IsNullOrEmpty(fileName) || lineNumber == 0)
            {
                Debug.LogError("No valid file or line number information available in the stack trace.");
                return;
            }

            fileName = fileName.ConvertToUnixPath();
            // Convert file path to relative path
            var relativePath =
                fileName.Contains("/Packages/")
                ? fileName.Substring(fileName.IndexOf("/Packages/") + 1)
                : "Assets" + fileName.Replace(UnityEngine.Application.dataPath, "");

            // Load asset and open in code editor
            var asset = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
            if (asset)
                _ = AssetDatabase.OpenAsset(asset, lineNumber, 0);
            else
                Debug.LogError($"Could not find '{relativePath}'");

        }

    }

}
