using System;
using UnityEngine;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Represents a async operation that returns a value.</summary>
    public class Async<T> : CustomYieldInstruction
    {

        /// <summary>Gets a <see cref="Async{T}"/> that is already completed.</summary>
        public static Async<T> complete { get; } = new(null);

        public static Async<T> FromResult(T result) =>
            new() { value = result };

        /// <summary>Gets the value that was produced by the async operation.</summary>
        public T value { get; set; }

        public override bool keepWaiting => !EvalComplete();

        readonly Func<(bool isDone, T value)> waitUntil;
        Action<T> callback;

        public Async(Func<(bool isDone, T value)> waitUntil) =>
            this.waitUntil = waitUntil;

        private Async()
        { }

        public Async(GlobalCoroutine coroutine, Func<T> callback) : this(() => (coroutine.isComplete, callback.Invoke()))
        { }

        bool EvalComplete()
        {

            if (waitUntil is null)
                return true;

            (bool isDone, T value) = waitUntil.Invoke();

            if (!isDone)
                return false;

            this.value = value;
            callback?.Invoke(value);
            return true;

        }

        /// <summary>Calls the callback when the async operation is complete.</summary>
        public void OnComplete(Action<T> callback) =>
           this.callback += callback;

    }

}
