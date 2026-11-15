using System.Collections.Generic;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath
{
    public sealed class InteractionDeterministicAuthorityService
    {
        /// <summary>
        /// Implementation safety ceiling. Static readonly, not const, so a future adjustment is not
        /// inlined into already-compiled consumers running against a different runtime ceiling.
        /// </summary>
        public static readonly int MaximumRegisteredTargetCount = 65_536;

        /// <summary>
        /// Implementation safety ceiling. Static readonly, not const, so a future adjustment is not
        /// inlined into already-compiled consumers running against a different runtime ceiling.
        /// </summary>
        public static readonly int MaximumQueueOwnerCount = 65_536;

        private readonly Dictionary<ulong, InteractionDeterministicTargetSnapshot> _targets = new Dictionary<ulong, InteractionDeterministicTargetSnapshot>();
        private readonly Dictionary<ulong, InteractionQueue> _queuesByTarget = new Dictionary<ulong, InteractionQueue>();
        private long _rejectedTargetAdmissionCount;
        private long _rejectedQueueOwnerAdmissionCount;
        private readonly InteractionRateLimiter _rateLimiter = new InteractionRateLimiter();
        private readonly InteractionRequestHistory _requestHistory = new InteractionRequestHistory();
        private readonly InteractionMetrics _metrics = new InteractionMetrics();

        public InteractionDeterministicAuthorityService(InteractionAuthorityOptions options)
        {
            Options = options;
        }

        public InteractionAuthorityOptions Options { get; private set; }
        public InteractionMetrics Metrics => _metrics;
        public int RegisteredTargetCount => _targets.Count;

        /// <summary>Number of retained queue owners.</summary>
        public int QueueOwnerCount => _queuesByTarget.Count;

        /// <summary>New target registrations refused because the ceiling was reached.</summary>
        public long RejectedTargetAdmissionCount => _rejectedTargetAdmissionCount;

        /// <summary>New queue owners refused because the ceiling was reached.</summary>
        public long RejectedQueueOwnerAdmissionCount => _rejectedQueueOwnerAdmissionCount;

        public void Configure(InteractionAuthorityOptions options)
        {
            Options = options;
            ClearRuntimeState();
        }

        public bool TryRegisterTarget(InteractionDeterministicTargetSnapshot snapshot)
        {
            if (!snapshot.IsValid)
            {
                return false;
            }

            if (!_targets.ContainsKey(snapshot.TargetStableId) &&
                _targets.Count >= MaximumRegisteredTargetCount)
            {
                if (_rejectedTargetAdmissionCount < long.MaxValue)
                {
                    _rejectedTargetAdmissionCount++;
                }

                return false;
            }

            _targets[snapshot.TargetStableId] = snapshot;
            return true;
        }

        public bool UnregisterTarget(ulong targetStableId)
        {
            _queuesByTarget.Remove(targetStableId);
            return _targets.Remove(targetStableId);
        }

        /// <summary>Releases rate-limit state after an authenticated instigator disconnects.</summary>
        public bool RemoveInstigatorRateLimitWindow(ulong instigatorStableId)
        {
            return _rateLimiter.Remove(instigatorStableId);
        }

        public bool TryGetTarget(ulong targetStableId, out InteractionDeterministicTargetSnapshot snapshot)
        {
            return _targets.TryGetValue(targetStableId, out snapshot);
        }

        public InteractionValidationResult ValidateRequest(InteractionRequest request, FPVector3 instigatorPosition, int serverTick)
        {
            InteractionValidationResult result = ValidateRequestInternal(request, instigatorPosition, serverTick);
            _metrics.RecordValidation(result);
            return result;
        }

        public InteractionValidationResult ValidateRequest(
            InteractionRequest request,
            IInteractionDeterministicPositionProvider instigatorPositionProvider,
            int serverTick)
        {
            if (instigatorPositionProvider == null ||
                !instigatorPositionProvider.TryGetDeterministicInteractionPosition(out FPVector3 instigatorPosition))
            {
                InteractionValidationResult invalidResult = InteractionValidationResult.Reject(request, InteractionValidationFailure.InvalidRequest);
                _metrics.RecordValidation(invalidResult);
                return invalidResult;
            }

            return ValidateRequest(request, instigatorPosition, serverTick);
        }

        public InteractionValidationResult TryQueueRequest(InteractionRequest request, FPVector3 instigatorPosition, int serverTick)
        {
            InteractionValidationResult result = ValidateRequestInternal(request, instigatorPosition, serverTick);
            if (!result.IsAccepted)
            {
                _metrics.RecordValidation(result);
                return result;
            }

            if (!TryGetOrCreateQueue(request.TargetStableId, out InteractionQueue queue))
            {
                result = InteractionValidationResult.Reject(request, InteractionValidationFailure.QueueFull);
                _metrics.RecordValidation(result);
                return result;
            }
            if (Options.MaxQueuedRequestsPerInstigator > 0 &&
                queue.CountQueuedForInstigator(request.InstigatorStableId) >= Options.MaxQueuedRequestsPerInstigator)
            {
                result = InteractionValidationResult.Reject(request, InteractionValidationFailure.TooManyQueuedForInstigator);
                _metrics.RecordValidation(result);
                return result;
            }

            if (!queue.TryEnqueue(request))
            {
                result = InteractionValidationResult.Reject(request, InteractionValidationFailure.QueueFull);
                _metrics.RecordValidation(result);
                return result;
            }

            result = InteractionValidationResult.Accept(request, result.Target, queue.Count);
            _metrics.RecordValidation(result);
            return result;
        }

        public InteractionValidationResult TryQueueRequest(
            InteractionRequest request,
            IInteractionDeterministicPositionProvider instigatorPositionProvider,
            int serverTick)
        {
            if (instigatorPositionProvider == null ||
                !instigatorPositionProvider.TryGetDeterministicInteractionPosition(out FPVector3 instigatorPosition))
            {
                InteractionValidationResult invalidResult = InteractionValidationResult.Reject(request, InteractionValidationFailure.InvalidRequest);
                _metrics.RecordValidation(invalidResult);
                return invalidResult;
            }

            return TryQueueRequest(request, instigatorPosition, serverTick);
        }

        /// <summary>
        /// Returns the queue for a target, creating one on demand. Returns false when the owner
        /// ceiling is reached so callers can reject through policy instead of exceptions.
        /// </summary>
        public bool TryGetOrCreateQueue(ulong targetStableId, out InteractionQueue queue)
        {
            if (_queuesByTarget.TryGetValue(targetStableId, out queue))
            {
                return true;
            }

            if (_queuesByTarget.Count >= MaximumQueueOwnerCount)
            {
                if (_rejectedQueueOwnerAdmissionCount < long.MaxValue)
                {
                    _rejectedQueueOwnerAdmissionCount++;
                }

                queue = null;
                return false;
            }

            queue = new InteractionQueue(Options.QueueCapacityPerTarget);
            _queuesByTarget.Add(targetStableId, queue);
            return true;
        }

        /// <summary>Fail-fast variant for call sites where a full table is an explicit invariant.</summary>
        public InteractionQueue GetOrCreateQueue(ulong targetStableId)
        {
            if (TryGetOrCreateQueue(targetStableId, out InteractionQueue queue))
            {
                return queue;
            }

            throw new System.InvalidOperationException(
                "The deterministic authority reached its queue-owner ceiling; shard by world or raise the reviewed limit.");
        }

        public void Clear()
        {
            _targets.Clear();
            ClearRuntimeState();
        }

        private void ClearRuntimeState()
        {
            _queuesByTarget.Clear();
            _rateLimiter.Clear();
            _requestHistory.Clear();
            _metrics.Reset();
        }

        private InteractionValidationResult ValidateRequestInternal(InteractionRequest request, FPVector3 instigatorPosition, int serverTick)
        {
            if (!request.IsValid)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.InvalidRequest);
            }

            if (request.WorldId != Options.WorldId)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.WrongWorld);
            }

            if (Options.RequireStableIds && request.InstigatorStableId == InteractionStableId.None)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.MissingInstigatorStableId);
            }

            if (Options.RequireStableIds && request.TargetStableId == InteractionStableId.None)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.MissingTargetStableId);
            }

            if (Options.MaxFutureTickDelta > 0 && request.Tick - serverTick > Options.MaxFutureTickDelta)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.TickTooFarInFuture);
            }

            if (Options.MaxPastTickDelta > 0 && serverTick - request.Tick > Options.MaxPastTickDelta)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.TickTooOld);
            }

            if (!_rateLimiter.TryConsume(
                    request.InstigatorStableId,
                    serverTick,
                    Options.MaxRequestsPerRateLimitWindow,
                    Options.RateLimitWindowTicks))
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.RateLimited);
            }

            if (!_targets.TryGetValue(request.TargetStableId, out InteractionDeterministicTargetSnapshot target))
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.UnknownTarget);
            }

            if (target.WorldId != request.WorldId)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.WrongWorld);
            }

            if (!target.IsAvailable)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.TargetUnavailable);
            }

            if (!target.CanExecuteAction(request.ActionId))
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.ActionNotAllowed);
            }

            if (target.InteractionRange.RawValue > 0L)
            {
                FPInt64 maxRangeSqr = target.InteractionRange * target.InteractionRange;
                if (FPVector3.DistanceSqr(instigatorPosition, target.Position) > maxRangeSqr)
                {
                    return InteractionValidationResult.Reject(request, InteractionValidationFailure.OutOfRange);
                }
            }

            // Recording happens after every rejection path so only requests that would execute
            // consume replay-history capacity. Recording earlier let an unauthenticated peer fill
            // the globally shared history with rejects and starve legitimate traffic.
            InteractionRequestHistoryResult historyResult = _requestHistory.MarkSeen(
                request,
                serverTick,
                Options.RequestHistoryWindowTicks,
                Options.RequestHistoryCapacity);
            if (historyResult == InteractionRequestHistoryResult.Duplicate)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.DuplicateRequest);
            }

            if (historyResult == InteractionRequestHistoryResult.CapacityExceeded)
            {
                return InteractionValidationResult.Reject(request, InteractionValidationFailure.RequestHistoryFull);
            }

            return InteractionValidationResult.Accept(request, target.ToInteractionTargetSnapshot());
        }
    }
}
