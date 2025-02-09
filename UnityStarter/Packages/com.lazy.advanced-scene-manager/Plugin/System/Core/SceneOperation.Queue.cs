using System;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;

namespace AdvancedSceneManager.Core
{

    partial class SceneOperation
    {

        /// <summary>Cancel this operation.</summary>
        /// <remarks>Note that the operation might not be cancelled immediately, if user defined callbacks are currently running.</remarks>
        public void Cancel()
        {
            wasCancelled = true;
            hasRun = true;
        }

        /// <summary>Inherited from <see cref="CustomYieldInstruction"/>. Tells unity whatever the operation is done or not.</summary>
        public override bool keepWaiting
        {
            get
            {
                // single conditional to reduce nesting
                if (hasRun || this == done)
                    return false;

                return !isFrozen || (operationCoroutine?.keepWaiting ?? false);
            }
        }

        /// <summary>Gets a <see cref="SceneOperation"/> that has already completed.</summary>
        public static SceneOperation done { get; } = new SceneOperation() { hasRun = true };

        /// <summary>Queues a new scene operation.</summary>
        public static SceneOperation Queue() =>
            QueueUtility<SceneOperation>.Queue(new());

        /// <summary>Starts a new scene operation, ignoring queue.</summary>
        public static SceneOperation Start() =>
            QueueUtility<SceneOperation>.Queue(new(), true);

        GlobalCoroutine operationCoroutine;

        void IQueueable.OnTurn(Action onComplete) =>
             operationCoroutine = Run().StartCoroutine(description: description ?? "SceneOperation", onComplete: () =>
             {
                 onComplete.Invoke();
             });

        void IQueueable.OnCancel() =>
            Cancel();

        bool IQueueable.CanQueue()
        {

            if (!Profile.current)
                throw new InvalidOperationException("Cannot queue a scene operation with no active profile.");

            return true;

        }

    }

}
