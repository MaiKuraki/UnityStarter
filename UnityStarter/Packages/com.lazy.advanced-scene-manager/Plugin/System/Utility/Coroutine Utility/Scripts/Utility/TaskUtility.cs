using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

#if UNITY_2023_1_OR_NEWER

    /// <summary>Provides utility methods for working with tasks.</summary>
    public static class TaskUtility
    {

        /// <summary>Awaits the coroutine.</summary>
        public static Task StartCoroutineAsTask(this IEnumerator coroutine)
        {
            TaskCompletionSource<bool> taskCompletionSource = new();
            RunCoroutine(coroutine, taskCompletionSource).StartCoroutine();
            return taskCompletionSource.Task;
        }

        private static IEnumerator RunCoroutine(IEnumerator coroutine, TaskCompletionSource<bool> tcs)
        {
            yield return coroutine;
            tcs.SetResult(true);
        }

        /// <summary>Awaits the coroutine.</summary>
        public static Awaitable<bool> StartCoroutineAsAwaitable(this IEnumerator coroutine)
        {
            AwaitableCompletionSource<bool> awaitableCompletionSource = new();
            RunCoroutine(coroutine, awaitableCompletionSource).StartCoroutine();
            return awaitableCompletionSource.Awaitable;

        }

        private static IEnumerator RunCoroutine(IEnumerator coroutine, AwaitableCompletionSource<bool> acs)
        {
            yield return coroutine;
            acs.SetResult(true);
        }

        public static CoroutineAwaiter GetAwaiter(this IEnumerator coroutine)
        {
            return new CoroutineAwaiter(coroutine);
        }

    }

    /// <summary>An awaiter for coroutines.<code><see langword="await"/> Coroutine();</code></summary>
    /// <remarks>See also <see cref="TaskUtility.GetAwaiter(IEnumerator)"/>.</remarks>
    public class CoroutineAwaiter : INotifyCompletion
    {
        private bool isCompleted;
        private Action continuation;

        public CoroutineAwaiter(IEnumerator coroutine)
        {
            RunCoroutine(coroutine);
        }

        private async void RunCoroutine(IEnumerator coroutine)
        {
            await coroutine.StartCoroutineAsTask();
            isCompleted = true;
            continuation?.Invoke();
        }

        public bool IsCompleted => isCompleted;

        public void GetResult() { }

        public void OnCompleted(Action continuation)
        {
            this.continuation = continuation;
        }
    }

#endif

}
