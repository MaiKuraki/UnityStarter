using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.UI;

namespace AdvancedSceneManager.Defaults
{

    public class TotalProgressBarLoadingScreen : LoadingScreen
    {
        #region strings
        // Define constants for the operation texts
        private const string LoadingText = "Loading...";
        private const string UnloadingText = "Unloading...";
        private const string UnknownText = "";
        private const string CompleteText = "Finishing...";
        #endregion
        [SerializeField] Text text;
        [SerializeField] Slider slider;


        public override IEnumerator OnOpen()
        {
            yield return null;
        }

        public override IEnumerator OnClose()
        {
            LoadingScreenUtility.ReportProgress(new MessageLoadProgressData(CompleteText, 1f));
            yield return new WaitForSeconds(1);
        }

        readonly HashSet<SceneOperation> totalSceneLoading = new();

        float GetOverallProgress
        {
            get
            {
                // Calculate the weighted average progress
                float totalProgressSum = totalSceneLoading
                    .Where(s => s != null)
                    .Sum(s => (float)Math.Round(s.progressScope.totalProgress * s.progressScope.operationCount, 4));

                int totalOperationCount = totalSceneLoading
                    .Where(s => s != null)
                    .Sum(s => s.progressScope.operationCount);

                // Avoid division by zero
                float overallProgress = totalOperationCount > 0 ? totalProgressSum / totalOperationCount : 0;
                return overallProgress;
            }
        }


        public override void OnProgressChanged(ILoadProgressData progress)
        {
            switch (progress)
            {
                case SceneLoadProgressData sceneLoadProgressData:

                    // handle when the scene is open in editor before playmode
                    if (sceneLoadProgressData.operation == null) return;

                    // we dont wanna add loading screens
                    //if (sceneLoadProgressData.operation.isLoadingScreen) return;

                    // add the progress scope to the table, this contains the total progress for the operation.
                    // Hashsets cannot contain duplicates.
                    totalSceneLoading.Add(sceneLoadProgressData.operation);

                    text.text = SceneManager.settings.project.allowLoadingScenesInParallel ?
                        LoadingText : // Scenes are loaded in parallell, progress will jump between scenes too often to print scene name.
                        CreateSceneLoadMessage(sceneLoadProgressData);

                    slider.value = GetOverallProgress;
                    break;
                case MessageLoadProgressData messageLoadProgressData:
                    text.text = messageLoadProgressData.message;
                    slider.value = messageLoadProgressData.value;
                    break;
                default:
                    text.text = LoadingText;
                    slider.value = progress.value;
                    break;
            }
        }

        #region _
        // If progress is called often, lets optimize string manipulation
        private static string CreateSceneLoadMessage(SceneLoadProgressData sceneLoadProgressData)
        {
            // Use ReadOnlySpan<char> directly from the scene name
            ReadOnlySpan<char> sceneNameAsSpan = sceneLoadProgressData.scene.name.AsSpan();

            // Use stack-allocated span for operation text to avoid allocations
            Span<char> operationKindAsSpan = stackalloc char[12]; // Max length for "Unloading..."
            int operationLength = 0;

            // Map OperationKind to const strings and copy to the span
            ReadOnlySpan<char> operationText = sceneLoadProgressData.operationKind switch
            {
                SceneOperationKind.Load => LoadingText.AsSpan(),
                SceneOperationKind.Unload => UnloadingText.AsSpan(),
                _ => UnknownText.AsSpan()
            };

            // Copy operation text to the stack-allocated span
            operationText.CopyTo(operationKindAsSpan);
            operationLength = operationText.Length;

            // Slice to the actual length used for output
            ReadOnlySpan<char> finalOperationKind = operationKindAsSpan[..operationLength];

            // Create a new span to hold the final message
            Span<char> messageSpan = stackalloc char[finalOperationKind.Length + sceneNameAsSpan.Length + 1]; // +1 for space

            // Copy the operation kind to the message span
            finalOperationKind.CopyTo(messageSpan);
            messageSpan[finalOperationKind.Length] = ' '; // Add space between operation text and scene name
            sceneNameAsSpan.CopyTo(messageSpan[(finalOperationKind.Length + 1)..]); // Copy scene name to message span

            return new string(messageSpan); // Convert the message span back to a string
        }
        #endregion
    }

}