using CycloneGames.Scheduling.Core;
using UnityEngine;

namespace CycloneGames.Scheduling.Runtime
{
    /// <summary>
    /// Explicit Unity host for one WorkScheduler. Disabling the component pauses execution;
    /// destroying it cancels all outstanding work through WorkScheduler.Shutdown.
    /// </summary>
    [AddComponentMenu("CycloneGames/Scheduling Driver")]
    public sealed class SchedulingDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Fixed number of outstanding work registrations. Capacity changes require recreating the host.")]
        private int capacity = 1024;

        [SerializeField]
        [Min(0)]
        [Tooltip("Maximum Step calls per Update. Zero pauses execution.")]
        private int maxStepsPerFrame = 64;

        [SerializeField]
        [Min(0)]
        [Tooltip("Soft time budget in milliseconds. A running Step cannot be preempted. Zero pauses execution.")]
        private double softBudgetMilliseconds = 2.0;

        private WorkScheduler scheduler;
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

                return scheduler ?? (scheduler = new WorkScheduler(capacity < 1 ? 1 : capacity));
            }
        }

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

        private void Awake()
        {
            _ = Scheduler;
        }

        private void Update()
        {
            if (scheduler != null)
            {
                scheduler.Tick(maxStepsPerFrame, softBudgetMilliseconds);
            }
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

        public void Shutdown()
        {
            if (isDestroyed)
            {
                return;
            }

            isDestroyed = true;
            if (scheduler == null)
            {
                return;
            }

            try
            {
                scheduler.Shutdown();
            }
            finally
            {
                scheduler = null;
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

        private static bool IsValidBudget(double value)
        {
            return !double.IsNaN(value) && !double.IsNegativeInfinity(value) && value >= 0;
        }
    }
}
