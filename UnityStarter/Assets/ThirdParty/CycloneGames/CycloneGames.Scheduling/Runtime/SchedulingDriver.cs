using CycloneGames.Scheduling.Core;
using UnityEngine;

namespace CycloneGames.Scheduling.Runtime
{
    /// <summary>
    /// Explicit Unity host for one WorkScheduler. Self-created schedulers are owned by the driver;
    /// bound schedulers remain owned by their external composition root.
    /// </summary>
    [AddComponentMenu("CycloneGames/Scheduling Driver")]
    public sealed class SchedulingDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Fixed number of outstanding work registrations. Capacity changes require recreating the host.")]
        private int capacity = 1024;

        [SerializeField]
        [Min(0)]
        [Tooltip("Maximum Step or shutdown termination calls per Update. Zero pauses execution and draining.")]
        private int maxStepsPerFrame = 64;

        [SerializeField]
        [Min(0)]
        [Tooltip("Soft time budget in milliseconds. A running Step cannot be preempted. Zero pauses execution.")]
        private double softBudgetMilliseconds = 2.0;

        private WorkScheduler scheduler;
        private bool ownsScheduler;
        private bool hasExternalScheduler;
        private bool isShuttingDown;
        private bool isDestroyed;

        public WorkScheduler Scheduler
        {
            get
            {
                if (this == null || isDestroyed)
                {
                    throw new System.InvalidOperationException(
                        "SchedulingDriver is fake-null, shut down, or destroyed and cannot provide a scheduler.");
                }

                return scheduler ?? CreateOwnedScheduler();
            }
        }

        public bool OwnsScheduler => scheduler != null && ownsScheduler;
        public bool IsShuttingDown => isShuttingDown;

        public int MaxStepsPerFrame
        {
            get => maxStepsPerFrame;
            set => maxStepsPerFrame = value < 0 ? 0 : value;
        }

        public double SoftBudgetMilliseconds
        {
            get => softBudgetMilliseconds;
            set => softBudgetMilliseconds = IsValidBudget(value) ? value : 0;
        }

        /// <summary>
        /// Binds an externally owned scheduler before this driver accepts work. The external owner retains
        /// all shutdown responsibility. Binding after this driver has accepted work is rejected.
        /// </summary>
        public void Bind(WorkScheduler externalScheduler)
        {
            if (this == null || isDestroyed)
            {
                throw new System.InvalidOperationException("A destroyed SchedulingDriver cannot bind a scheduler.");
            }

            if (externalScheduler == null)
            {
                throw new System.ArgumentNullException(nameof(externalScheduler));
            }

            if (isShuttingDown || hasExternalScheduler)
            {
                throw new System.InvalidOperationException(
                    "A scheduler can only be bound once and before shutdown begins.");
            }

            if (scheduler != null)
            {
                if (ReferenceEquals(scheduler, externalScheduler))
                {
                    throw new System.InvalidOperationException(
                        "The driver already owns this scheduler instance. External ownership must use a distinct instance.");
                }

                if (scheduler.GetSnapshot().AcceptedCount != 0)
                {
                    throw new System.InvalidOperationException(
                        "Cannot replace a scheduler after it has accepted work.");
                }

                if (ownsScheduler)
                {
                    scheduler.Shutdown();
                }
            }

            scheduler = externalScheduler;
            ownsScheduler = false;
            hasExternalScheduler = true;
        }

        /// <summary>
        /// Begins a bounded shutdown of the self-created scheduler. Update terminates queued work in batches
        /// instead of executing further Steps. External schedulers are shut down by their composition root.
        /// </summary>
        public void BeginShutdown()
        {
            if (this == null || isDestroyed || isShuttingDown)
            {
                return;
            }

            if (scheduler == null)
            {
                isDestroyed = true;
                return;
            }

            if (!ownsScheduler)
            {
                throw new System.InvalidOperationException(
                    "An externally owned scheduler must be shut down by its composition root.");
            }

            isShuttingDown = true;
            scheduler.ShutdownStep(0);
        }

        /// <summary>
        /// Stops this host immediately. A self-created scheduler is synchronously shut down; an externally
        /// owned scheduler is detached without cancellation and remains the external owner's responsibility.
        /// </summary>
        public void Shutdown()
        {
            if (isDestroyed)
            {
                return;
            }

            isDestroyed = true;
            isShuttingDown = false;
            if (scheduler == null)
            {
                return;
            }

            try
            {
                if (ownsScheduler)
                {
                    scheduler.Shutdown();
                }
            }
            finally
            {
                scheduler = null;
                ownsScheduler = false;
            }
        }

        private void Awake()
        {
            _ = Scheduler;
        }

        private void Update()
        {
            if (scheduler == null || isDestroyed)
            {
                return;
            }

            if (isShuttingDown)
            {
                scheduler.ShutdownStep(maxStepsPerFrame);
                if (scheduler.OutstandingCount == 0)
                {
                    scheduler = null;
                    ownsScheduler = false;
                    isShuttingDown = false;
                    isDestroyed = true;
                }

                return;
            }

            scheduler.Tick(maxStepsPerFrame, softBudgetMilliseconds);
        }

        private void OnValidate()
        {
            capacity = capacity < 1 ? 1 : capacity;
            maxStepsPerFrame = maxStepsPerFrame < 0 ? 0 : maxStepsPerFrame;
            if (!IsValidBudget(softBudgetMilliseconds))
            {
                softBudgetMilliseconds = 0;
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private WorkScheduler CreateOwnedScheduler()
        {
            scheduler = new WorkScheduler(capacity < 1 ? 1 : capacity);
            ownsScheduler = true;
            return scheduler;
        }

        private static bool IsValidBudget(double value)
        {
            return !double.IsNaN(value) && !double.IsNegativeInfinity(value) && value >= 0;
        }
    }
}
