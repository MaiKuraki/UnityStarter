using System;
using System.Collections;
using System.Linq;
using AdvancedSceneManager.Callbacks.Events;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Core
{

    partial class SceneOperation
    {

        private IEnumerator PrepareForExecution()
        {

            //Lets wait a bit so that users can change properties before we freeze
            yield return null;

            if (preload.Any(s => open.Contains(s) || close.Contains(s)))
            {
                Debug.LogError("A scene cannot be both preloaded and opened/closed in a single scene operation.");
                Cancel();
                yield break;
            }

            if (IsDuplicate())
            {
                Debug.LogWarning("A duplicate scene operation was detected, it has been halted. This behavior can be changed in settings.");
                Cancel();
                yield break;
            }

            yield return InvokeCallback<StartPhaseEvent>(new());

            //operation may have been cancelled in beforeStart
            if (wasCancelled)
                yield break;

            Freeze();

            LogUtility.LogStart(this);

            FallbackSceneUtility.EnsureOpen();

            if (reportsProgress)
                progressScope
                    .Expect(SceneOperationKind.Unload, close)
                    .Expect(SceneOperationKind.Load, open)
                    .Expect(SceneOperationKind.Load, preload);

#if UNITY_EDITOR
            LogOperationStart();
#endif

        }

        bool IsDuplicate() =>
            SceneManager.settings.project.checkForDuplicateSceneOperations &&
            QueueUtility<SceneOperation>.running.Concat(QueueUtility<SceneOperation>.queue).Any(o => o != this && IsDuplicate(this, o));

        static bool IsDuplicate(SceneOperation left, SceneOperation right)
        {

            if (left.open.Count() + left.close.Count() == 0 ||
                right.open.Count() + right.close.Count() == 0)
                return false;

            return left.open.SequenceEqual(right.open) && left.close.SequenceEqual(right.close);
        }

    }

}
