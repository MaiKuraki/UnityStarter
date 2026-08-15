using System;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Application-lifetime owner for GameInstances whose terminal cleanup may require retries.
    /// Implementations must be owner-thread-bound and must not allocate while registering into
    /// capacity that was established before GameInstance creation.
    /// </summary>
    public interface IGameplayWorldTerminalCleanupOwner
    {
        int Capacity { get; }
        int PendingCount { get; }
        bool HasCapacity { get; }
        bool TryRegister(GameInstance gameInstance);
        bool Contains(GameInstance gameInstance);
        void ReleaseCompleted(GameInstance gameInstance);
        bool TryCleanupAll();
    }

    /// <summary>
    /// Fixed-capacity, allocation-free retry registry for terminal GameInstance ownership.
    /// The application composition root must retain this registry longer than every World host.
    /// </summary>
    public sealed class GameplayWorldTerminalCleanupRegistry :
        IGameplayWorldTerminalCleanupOwner
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        private readonly GameInstance[] instances;
        private readonly int ownerThreadId;
        private int pendingCount;

        public GameplayWorldTerminalCleanupRegistry(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            instances = new GameInstance[capacity];
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int Capacity
        {
            get
            {
                AssertOwnerThread();
                return instances.Length;
            }
        }

        public int PendingCount
        {
            get
            {
                AssertOwnerThread();
                return pendingCount;
            }
        }

        public bool HasCapacity
        {
            get
            {
                AssertOwnerThread();
                return pendingCount < instances.Length;
            }
        }

        public bool TryRegister(GameInstance gameInstance)
        {
            AssertOwnerThread();
            if (gameInstance == null)
            {
                throw new ArgumentNullException(nameof(gameInstance));
            }

            int availableIndex = -1;
            for (int index = 0; index < instances.Length; index++)
            {
                GameInstance registered = instances[index];
                if (ReferenceEquals(registered, gameInstance))
                {
                    return true;
                }

                if (registered == null && availableIndex < 0)
                {
                    availableIndex = index;
                }
            }

            if (availableIndex < 0)
            {
                return false;
            }

            instances[availableIndex] = gameInstance;
            pendingCount++;
            return true;
        }

        public bool Contains(GameInstance gameInstance)
        {
            AssertOwnerThread();
            if (gameInstance == null)
            {
                return false;
            }

            for (int index = 0; index < instances.Length; index++)
            {
                if (ReferenceEquals(instances[index], gameInstance))
                {
                    return true;
                }
            }

            return false;
        }

        public void ReleaseCompleted(GameInstance gameInstance)
        {
            AssertOwnerThread();
            if (gameInstance == null)
            {
                throw new ArgumentNullException(nameof(gameInstance));
            }
            if (!gameInstance.IsDisposalComplete)
            {
                throw new InvalidOperationException(
                    "A GameInstance cannot leave terminal ownership before disposal completes.");
            }

            for (int index = 0; index < instances.Length; index++)
            {
                if (!ReferenceEquals(instances[index], gameInstance))
                {
                    continue;
                }

                instances[index] = null;
                pendingCount--;
                return;
            }
        }

        public bool TryCleanupAll()
        {
            AssertOwnerThread();
            OutOfMemoryException terminalOutOfMemory = null;
            for (int index = 0; index < instances.Length; index++)
            {
                GameInstance gameInstance = instances[index];
                if (gameInstance == null)
                {
                    continue;
                }

                try
                {
                    gameInstance.Dispose();
                }
                catch (Exception exception)
                {
                    if (!CaptureOutOfMemory(ref terminalOutOfMemory, exception))
                    {
                        Log.Error(
                            exception,
                            "GameInstance terminal disposal failed; it will be retried on the next cleanup pass.");
                    }
                }

                if (!gameInstance.IsDisposalComplete)
                {
                    continue;
                }

                instances[index] = null;
                pendingCount--;
            }

            if (terminalOutOfMemory != null)
            {
                throw terminalOutOfMemory;
            }

            return pendingCount == 0;
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Gameplay World terminal cleanup ownership must be accessed on its creation thread.");
            }
        }

        private static bool CaptureOutOfMemory(
            ref OutOfMemoryException terminalOutOfMemory,
            Exception exception)
        {
            OutOfMemoryException captured = FindOutOfMemory(exception);
            if (terminalOutOfMemory == null && captured != null)
            {
                terminalOutOfMemory = captured;
            }

            return captured != null;
        }

        private static OutOfMemoryException FindOutOfMemory(Exception exception)
        {
            if (exception is OutOfMemoryException outOfMemoryException)
            {
                return outOfMemoryException;
            }

            if (exception is AggregateException aggregateException)
            {
                for (int index = 0; index < aggregateException.InnerExceptions.Count; index++)
                {
                    OutOfMemoryException nested = FindOutOfMemory(
                        aggregateException.InnerExceptions[index]);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Unity authoring owner for the application-lifetime terminal cleanup registry. Keep this
    /// component on a dedicated root object. In Play Mode the object survives scene changes.
    /// Call TryCleanupAll explicitly during application shutdown and verify it returns true.
    /// </summary>
    [DefaultExecutionOrder(-11000)]
    [DisallowMultipleComponent]
    public sealed class GameplayWorldTerminalCleanupOwner : MonoBehaviour,
        IGameplayWorldTerminalCleanupOwner
    {
        private static readonly LogChannel Log = GameplayFrameworkLog.Channel;

        [SerializeField, Min(1)] private int capacity = 4;

        private GameplayWorldTerminalCleanupRegistry registry;
        private int ownerThreadId;

        public int Capacity
        {
            get { AssertOwnerThread(); return GetRegistry().Capacity; }
        }
        public int PendingCount
        {
            get { AssertOwnerThread(); return GetRegistry().PendingCount; }
        }
        public bool HasCapacity
        {
            get { AssertOwnerThread(); return GetRegistry().HasCapacity; }
        }

        private void Awake()
        {
            BindOwnerThread();
            GetRegistry();
            if (Application.isPlaying)
            {
                if (transform.parent != null)
                {
                    throw new InvalidOperationException(
                        "GameplayWorldTerminalCleanupOwner must be placed on a root GameObject.");
                }

                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            BindOwnerThread();
        }

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
        }

        public bool TryRegister(GameInstance gameInstance)
        {
            AssertOwnerThread();
            return GetRegistry().TryRegister(gameInstance);
        }

        public bool Contains(GameInstance gameInstance)
        {
            AssertOwnerThread();
            return GetRegistry().Contains(gameInstance);
        }

        public void ReleaseCompleted(GameInstance gameInstance)
        {
            AssertOwnerThread();
            GetRegistry().ReleaseCompleted(gameInstance);
        }

        public bool TryCleanupAll()
        {
            AssertOwnerThread();
            return GetRegistry().TryCleanupAll();
        }

        private void OnDestroy()
        {
            BindOwnerThread();
            if (registry == null || registry.TryCleanupAll())
            {
                return;
            }

            Log.Error(
                "Gameplay World terminal cleanup remains incomplete while its application owner is being destroyed.");
        }

        private GameplayWorldTerminalCleanupRegistry GetRegistry()
        {
            if (registry == null)
            {
                registry = new GameplayWorldTerminalCleanupRegistry(capacity);
            }

            return registry;
        }

        private void AssertOwnerThread()
        {
            int expectedThreadId = ownerThreadId;
            if (expectedThreadId == 0)
            {
                throw new InvalidOperationException(
                    "Gameplay World terminal cleanup owner has not entered its Unity lifecycle.");
            }
            if (Thread.CurrentThread.ManagedThreadId != expectedThreadId)
            {
                throw new InvalidOperationException(
                    "Gameplay World terminal cleanup owner must be accessed on its Unity lifecycle thread.");
            }
        }

        private void BindOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (ownerThreadId != 0 && ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "Gameplay World terminal cleanup ownership cannot move between threads.");
            }

            ownerThreadId = currentThreadId;
        }
    }
}
