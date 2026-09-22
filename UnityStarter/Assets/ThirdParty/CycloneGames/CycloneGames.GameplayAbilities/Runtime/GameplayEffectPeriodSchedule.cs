using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Deterministic schedule of the periodic executions a GameplayEffect definition produces.
    /// This mirrors <see cref="ActiveGameplayEffect.Tick"/> so authoring previews, tests, and runtime
    /// behavior share one contract instead of three independent interpretations of Duration and Period.
    /// </summary>
    public readonly struct GameplayEffectPeriodSchedule
    {
        /// <summary>
        /// Tolerance used when deciding whether the duration is an exact multiple of the period.
        /// </summary>
        public const float ComparisonEpsilon = 1e-4f;

        /// <summary>
        /// Time in seconds from application of the execution at the given zero-based index,
        /// or -1 when the index is out of range. Index 0 is the immediate execution when
        /// <see cref="ExecutesOnApplication"/> is true, otherwise it is the first period elapse.
        /// </summary>
        public float GetExecutionTime(int index)
        {
            if (index < 0 || !IsPeriodic || Period <= 0f)
            {
                return -1f;
            }

            if (IsUnbounded == false && ExecutionCount >= 0 && index >= ExecutionCount)
            {
                return -1f;
            }

            return ExecutesOnApplication ? Period * index : Period * (index + 1);
        }

        private GameplayEffectPeriodSchedule(
            bool isPeriodic,
            bool isUnbounded,
            int executionCount,
            float period,
            bool executesOnApplication,
            float firstExecutionTime,
            float lastExecutionTime,
            bool endsOnDurationBoundary,
            float untickedTail)
        {
            IsPeriodic = isPeriodic;
            IsUnbounded = isUnbounded;
            ExecutionCount = executionCount;
            Period = period;
            ExecutesOnApplication = executesOnApplication;
            FirstExecutionTime = firstExecutionTime;
            LastExecutionTime = lastExecutionTime;
            EndsOnDurationBoundary = endsOnDurationBoundary;
            UntickedTail = untickedTail;
        }

        /// <summary>
        /// True when the definition produces periodic executions at all.
        /// </summary>
        public bool IsPeriodic { get; }

        /// <summary>
        /// True when the effect has no finite duration, so the schedule has no end.
        /// </summary>
        public bool IsUnbounded { get; }

        /// <summary>
        /// Total executions over the effect lifetime, -1 when <see cref="IsUnbounded"/>, 0 when the
        /// effect never executes.
        /// </summary>
        public int ExecutionCount { get; }

        /// <summary>
        /// The configured period in seconds, 0 when the effect is not periodic.
        /// </summary>
        public float Period { get; }

        /// <summary>
        /// True when the definition executes once immediately on application, in addition to the
        /// regular period schedule. UE5: bExecutePeriodicEffectOnApplication.
        /// </summary>
        public bool ExecutesOnApplication { get; }

        /// <summary>
        /// Time in seconds from application of the first execution, -1 when there is none.
        /// </summary>
        public float FirstExecutionTime { get; }

        /// <summary>
        /// Time in seconds from application of the last execution, -1 when there is none or the
        /// schedule is unbounded.
        /// </summary>
        public float LastExecutionTime { get; }

        /// <summary>
        /// True when the last execution lands exactly on the duration expiry. UE5 issues that tick
        /// through CheckForFinalPeriodicExec; before that existed, an entire tick was silently lost.
        /// </summary>
        public bool EndsOnDurationBoundary { get; }

        /// <summary>
        /// Duration in seconds left after the last execution, 0 when the schedule ends on the boundary.
        /// </summary>
        public float UntickedTail { get; }

        /// <summary>
        /// Computes the schedule for a definition.
        /// </summary>
        public static GameplayEffectPeriodSchedule Compute(
            EDurationPolicy durationPolicy,
            float duration,
            float period,
            bool executeOnApplication)
        {
            if (durationPolicy == EDurationPolicy.Instant ||
                float.IsNaN(period) || float.IsInfinity(period) || period <= 0f)
            {
                return default;
            }

            float firstExecutionTime = executeOnApplication ? 0f : period;

            if (durationPolicy != EDurationPolicy.HasDuration ||
                float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f)
            {
                return new GameplayEffectPeriodSchedule(
                    true, true, -1, period, executeOnApplication, firstExecutionTime, -1f, false, 0f);
            }

            float quotient = duration / period;
            float tolerance = Math.Max(ComparisonEpsilon, quotient * 1e-6f);
            int loopExecutions = (int)Math.Floor(quotient + tolerance);
            if (loopExecutions < 0)
            {
                loopExecutions = 0;
            }

            float lastLoopTime = loopExecutions * period;
            int executionCount = loopExecutions + (executeOnApplication ? 1 : 0);
            float lastExecutionTime = loopExecutions > 0
                ? lastLoopTime
                : (executeOnApplication ? 0f : -1f);

            float boundaryTolerance = Math.Max(ComparisonEpsilon, duration * 1e-6f);
            bool endsOnBoundary = loopExecutions > 0 &&
                                  Math.Abs(duration - lastLoopTime) <= boundaryTolerance;
            float untickedTail = loopExecutions > 0
                ? Math.Max(0f, duration - lastLoopTime)
                : duration;

            return new GameplayEffectPeriodSchedule(
                true, false, executionCount, period, executeOnApplication,
                firstExecutionTime, lastExecutionTime, endsOnBoundary, untickedTail);
        }
    }
}
