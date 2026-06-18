using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Interaction.Core
{
    public sealed class InteractionAuthorityService
    {
        private readonly Dictionary<ulong, InteractionTargetSnapshot> _targets = new Dictionary<ulong, InteractionTargetSnapshot>();
        private readonly Dictionary<ulong, InteractionQueue> _queuesByTarget = new Dictionary<ulong, InteractionQueue>();
        private readonly InteractionRateLimiter _rateLimiter = new InteractionRateLimiter();
        private readonly InteractionRequestHistory _requestHistory = new InteractionRequestHistory();
        private readonly InteractionMetrics _metrics = new InteractionMetrics();

        public InteractionAuthorityService(InteractionAuthorityOptions options)
        {
            Options = options;
        }

        public InteractionAuthorityOptions Options { get; private set; }
        public InteractionMetrics Metrics => _metrics;
        public int RegisteredTargetCount => _targets.Count;

        public void Configure(InteractionAuthorityOptions options)
        {
            Options = options;
            ClearRuntimeState();
        }

        public bool TryRegisterTarget(InteractionTargetSnapshot snapshot)
        {
            if (!snapshot.IsValid)
            {
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

        public bool TryGetTarget(ulong targetStableId, out InteractionTargetSnapshot snapshot)
        {
            return _targets.TryGetValue(targetStableId, out snapshot);
        }

        public InteractionValidationResult ValidateRequest(InteractionRequest request, InteractionVector3 instigatorPosition, int serverTick)
        {
            InteractionValidationResult result = ValidateRequestInternal(request, instigatorPosition, serverTick);
            _metrics.RecordValidation(result);
            return result;
        }

        public InteractionValidationResult ValidateRequest(InteractionRequest request, IInteractionPositionProvider instigatorPositionProvider, int serverTick)
        {
            if (instigatorPositionProvider == null ||
                !instigatorPositionProvider.TryGetInteractionPosition(out InteractionVector3 instigatorPosition))
            {
                InteractionValidationResult invalidResult = InteractionValidationResult.Reject(request, InteractionValidationFailure.InvalidRequest);
                _metrics.RecordValidation(invalidResult);
                return invalidResult;
            }

            return ValidateRequest(request, instigatorPosition, serverTick);
        }

        public InteractionValidationResult TryQueueRequest(InteractionRequest request, InteractionVector3 instigatorPosition, int serverTick)
        {
            InteractionValidationResult result = ValidateRequestInternal(request, instigatorPosition, serverTick);
            if (!result.IsAccepted)
            {
                _metrics.RecordValidation(result);
                return result;
            }

            InteractionQueue queue = GetOrCreateQueue(request.TargetStableId);
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

        public InteractionValidationResult TryQueueRequest(InteractionRequest request, IInteractionPositionProvider instigatorPositionProvider, int serverTick)
        {
            if (instigatorPositionProvider == null ||
                !instigatorPositionProvider.TryGetInteractionPosition(out InteractionVector3 instigatorPosition))
            {
                InteractionValidationResult invalidResult = InteractionValidationResult.Reject(request, InteractionValidationFailure.InvalidRequest);
                _metrics.RecordValidation(invalidResult);
                return invalidResult;
            }

            return TryQueueRequest(request, instigatorPosition, serverTick);
        }

        public InteractionQueue GetOrCreateQueue(ulong targetStableId)
        {
            if (!_queuesByTarget.TryGetValue(targetStableId, out InteractionQueue queue))
            {
                queue = new InteractionQueue(Options.QueueCapacityPerTarget);
                _queuesByTarget.Add(targetStableId, queue);
            }

            return queue;
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

        private InteractionValidationResult ValidateRequestInternal(InteractionRequest request, InteractionVector3 instigatorPosition, int serverTick)
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

            if (!_targets.TryGetValue(request.TargetStableId, out InteractionTargetSnapshot target))
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

            if (target.InteractionRange > 0f)
            {
                float maxRangeSqr = target.InteractionRange * target.InteractionRange;
                if (InteractionVector3.DistanceSquared(instigatorPosition, target.Position) > maxRangeSqr)
                {
                    return InteractionValidationResult.Reject(request, InteractionValidationFailure.OutOfRange);
                }
            }

            return InteractionValidationResult.Accept(request, target);
        }
    }
}
