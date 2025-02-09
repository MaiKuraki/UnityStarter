using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedSceneManager.Utility
{

    /// <summary>Represents a queueable item.</summary>
    /// <remarks>See also <see cref="QueueUtility{T}"/>.</remarks>
    public interface IQueueable
    {

        /// <summary>Called when it is this queueables turn.</summary>
        /// <param name="onComplete">Must be called when operation is done, otherwise queue will be stuck.</param>
        void OnTurn(Action onComplete);

        /// <summary>Called when queueable is cancelled.</summary>
        void OnCancel();

        /// <summary>Called to make sure the item can actually be queued.</summary>
        bool CanQueue();

    }

    /// <summary>A utility that provides queuing.</summary>
    public static class QueueUtility<T> where T : IQueueable
    {

        static readonly List<T> m_queue = new List<T>();
        static readonly List<T> m_running = new List<T>();

        /// <summary>Gets whatever any items in the queue are running.</summary>
        public static bool isBusy => queue.Any() || running.Any();

        /// <summary>Occurs when an queued item finishes and queue is empty.</summary>
        public static event Action queueEmpty;

        /// <summary>Occurs when an queued is added.</summary>
        public static event Action queueFilled;

        /// <summary>Gets the items currently in queue.</summary>
        public static IEnumerable<T> queue => m_queue;

        /// <summary>Gets the items that are currently running.</summary>
        public static IEnumerable<T> running => m_running;

        /// <summary>Get if the item is queued.</summary>
        public static bool IsQueued(T queueable) =>
            queue.Contains(queueable);

        /// <summary>Gets if the item is running.</summary>
        public static bool IsRunning(T queueable) =>
            running.Contains(queueable);

        /// <summary>Queues this scene operation.</summary>
        /// <param name="queueable">The item to queue.</param>
        /// <param name="ignoreQueue">Specifies whatever queue should be ignored, and item invoked immediately.</param>
        internal static T Queue(T queueable, bool ignoreQueue = false)
        {

            if (!ignoreQueue && !queueable.CanQueue())
                return queueable;

            if (!ignoreQueue && !m_queue.Contains(queueable))
                m_queue.Add(queueable);

            if (m_queue.Count == 1 || ignoreQueue)
            {
                if (!ignoreQueue)
                    queueFilled?.Invoke();
                OnTurn(queueable, ignoreQueue);
            }

            return queueable;

        }

        /// <summary>Cancels the queuable.</summary>
        public static void Stop(T queueable)
        {
            if (m_queue.Remove(queueable) || m_running.Remove(queueable))
                queueable.OnCancel();
        }

        /// <summary>Cancels all queued and running items.</summary>
        public static void StopAll()
        {
            foreach (var item in queue.Concat(running).Distinct().ToArray())
                item.OnCancel();
            m_queue.Clear();
            m_running.Clear();
        }

        static void OnTurn(T queueable, bool ignoreQueue = false)
        {

            if (!m_running.Contains(queueable))
                m_running.Add(queueable);

            queueable.OnTurn(onComplete: () =>
            {

                _ = m_queue.Remove(queueable);
                _ = m_running.Remove(queueable);

                if (ignoreQueue)
                    return;

                if (m_queue.Any())
                    OnTurn(m_queue[0]);
                else
                    queueEmpty?.Invoke();

            });

        }

    }

}
