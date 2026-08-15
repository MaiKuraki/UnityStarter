using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>
    /// Fully-resolved, engine-free input for one damage validation. The integration layer fills this from
    /// server-authoritative state (real actor positions, the connection that actually sent the request,
    /// weapon rules) before calling <see cref="IServerDamageValidator.Validate"/>. All fields are values,
    /// so validation allocates nothing and is trivially unit-testable without a scene.
    /// </summary>
    public readonly struct ServerDamageValidationRequest
    {
        public readonly int InstigatorActorId;
        public readonly int TargetActorId;

        /// <summary>Connection the server knows owns the instigator actor.</summary>
        public readonly int InstigatorOwnerConnectionId;

        /// <summary>Connection that actually delivered the request. Must match the owner to pass.</summary>
        public readonly int RequestConnectionId;

        public readonly bool TargetCanBeDamaged;

        /// <summary>Server-authoritative instigator position.</summary>
        public readonly NetworkVector3 InstigatorPosition;

        /// <summary>Server-authoritative target position.</summary>
        public readonly NetworkVector3 TargetPosition;

        /// <summary>Client-claimed raw damage (untrusted; clamped to <see cref="MaxDamage"/> on accept).</summary>
        public readonly float RequestedDamage;

        /// <summary>Authoritative damage cap for the weapon/ability.</summary>
        public readonly float MaxDamage;

        /// <summary>Squared maximum range. 0 or less disables the range check. Squared to avoid a sqrt.</summary>
        public readonly float MaxRangeSqr;

        /// <summary>Server time in seconds.</summary>
        public readonly double CurrentTimeSeconds;

        /// <summary>
        /// Last accepted damage time for standalone validator calls. <see cref="ServerAuthoritativeDamageProcessor"/>
        /// always replaces this value with its owned <see cref="DamageCooldownTracker"/> state.
        /// </summary>
        public readonly double LastAcceptedTimeSeconds;

        /// <summary>Minimum seconds between accepted damages. 0 or less disables the cooldown check.</summary>
        public readonly double CooldownSeconds;

        public ServerDamageValidationRequest(
            int instigatorActorId,
            int targetActorId,
            int instigatorOwnerConnectionId,
            int requestConnectionId,
            bool targetCanBeDamaged,
            NetworkVector3 instigatorPosition,
            NetworkVector3 targetPosition,
            float requestedDamage,
            float maxDamage,
            float maxRangeSqr,
            double currentTimeSeconds,
            double lastAcceptedTimeSeconds,
            double cooldownSeconds)
        {
            InstigatorActorId = instigatorActorId;
            TargetActorId = targetActorId;
            InstigatorOwnerConnectionId = instigatorOwnerConnectionId;
            RequestConnectionId = requestConnectionId;
            TargetCanBeDamaged = targetCanBeDamaged;
            InstigatorPosition = instigatorPosition;
            TargetPosition = targetPosition;
            RequestedDamage = requestedDamage;
            MaxDamage = maxDamage;
            MaxRangeSqr = maxRangeSqr;
            CurrentTimeSeconds = currentTimeSeconds;
            LastAcceptedTimeSeconds = lastAcceptedTimeSeconds;
            CooldownSeconds = cooldownSeconds;
        }
    }

    public readonly struct ServerDamageValidationResult
    {
        public readonly ServerDamageRejectReason Reason;

        /// <summary>Authoritative damage to apply. 0 unless <see cref="Accepted"/> is true.</summary>
        public readonly float ApprovedDamage;

        private ServerDamageValidationResult(ServerDamageRejectReason reason, float approvedDamage)
        {
            Reason = reason;
            ApprovedDamage = approvedDamage;
        }

        public bool Accepted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Reason == ServerDamageRejectReason.Accepted;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ServerDamageValidationResult Accept(float approvedDamage)
        {
            if (!IsFiniteNonNegative(approvedDamage))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(approvedDamage),
                    "Approved damage must be finite and non-negative.");
            }

            return new ServerDamageValidationResult(ServerDamageRejectReason.Accepted, approvedDamage);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ServerDamageValidationResult Reject(ServerDamageRejectReason reason)
        {
            if (!IsValidRejectReason(reason))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reason),
                    "A rejection requires a defined non-Accepted reason.");
            }

            return new ServerDamageValidationResult(reason, 0f);
        }

        internal bool IsWellFormed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Accepted
                ? IsFiniteNonNegative(ApprovedDamage)
                : IsValidRejectReason(Reason) && ApprovedDamage == 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFiniteNonNegative(float value)
        {
            return value >= 0f && value < float.PositiveInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidRejectReason(ServerDamageRejectReason reason)
        {
            byte value = (byte)reason;
            return value >= (byte)ServerDamageRejectReason.InvalidPayload
                && value <= (byte)ServerDamageRejectReason.CooldownCapacityReached;
        }
    }

    /// <summary>
    /// Server-authoritative gate that decides whether a client damage request may apply and how much.
    /// Implementations must be pure and allocation-free so they can run on every incoming hit.
    /// </summary>
    public interface IServerDamageValidator
    {
        ServerDamageValidationResult Validate(in ServerDamageValidationRequest request);
    }

    /// <summary>
    /// Default validator enforcing the baseline server-authoritative damage rules: payload sanity,
    /// instigator ownership, target damageability, fire-rate cooldown, weapon range, and damage clamping.
    /// Stateless, branch-light, no allocation and no square root (range uses squared distance).
    /// </summary>
    public sealed class DefaultServerDamageValidator : IServerDamageValidator
    {
        public static readonly DefaultServerDamageValidator Instance = new DefaultServerDamageValidator();

        public ServerDamageValidationResult Validate(in ServerDamageValidationRequest request)
        {
            // 1. Payload sanity. Every resolved value remains a trust boundary even when supplied by an adapter.
            if (request.InstigatorActorId <= 0
                || request.TargetActorId <= 0
                || request.InstigatorActorId == request.TargetActorId
                || request.InstigatorOwnerConnectionId <= 0
                || request.RequestConnectionId <= 0
                || !request.InstigatorPosition.IsFinite()
                || !request.TargetPosition.IsFinite()
                || !IsFiniteNonNegative(request.RequestedDamage)
                || !IsFiniteNonNegative(request.MaxDamage)
                || !IsFiniteNonNegative(request.MaxRangeSqr)
                || !IsFiniteNonNegative(request.CurrentTimeSeconds)
                || !IsValidLastAcceptedTime(request.LastAcceptedTimeSeconds)
                || !IsFiniteNonNegative(request.CooldownSeconds)
                || request.LastAcceptedTimeSeconds > request.CurrentTimeSeconds)
            {
                return ServerDamageValidationResult.Reject(ServerDamageRejectReason.InvalidPayload);
            }

            // 2. Ownership. The delivering connection must own the instigator it claims to act as.
            if (request.RequestConnectionId != request.InstigatorOwnerConnectionId)
            {
                return ServerDamageValidationResult.Reject(ServerDamageRejectReason.OwnershipMismatch);
            }

            // 3. Target must currently accept damage.
            if (!request.TargetCanBeDamaged)
            {
                return ServerDamageValidationResult.Reject(ServerDamageRejectReason.TargetNotDamageable);
            }

            // 4. Fire-rate cooldown.
            if (request.CooldownSeconds > 0d
                && (request.CurrentTimeSeconds - request.LastAcceptedTimeSeconds) < request.CooldownSeconds)
            {
                return ServerDamageValidationResult.Reject(ServerDamageRejectReason.OnCooldown);
            }

            // 5. Range gate using squared distance (no sqrt).
            if (request.MaxRangeSqr > 0f
                && NetworkVector3.SqrDistance(request.InstigatorPosition, request.TargetPosition) > request.MaxRangeSqr)
            {
                return ServerDamageValidationResult.Reject(ServerDamageRejectReason.OutOfRange);
            }

            // 6. Accept with damage clamped to the authoritative cap.
            float approved = request.RequestedDamage > request.MaxDamage ? request.MaxDamage : request.RequestedDamage;
            return ServerDamageValidationResult.Accept(approved);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFiniteNonNegative(float value)
        {
            // NaN fails every comparison, so (value >= 0) already rejects NaN; the upper bound rejects +Infinity.
            return value >= 0f && value < float.PositiveInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && value < double.PositiveInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidLastAcceptedTime(double value)
        {
            return value == double.NegativeInfinity || IsFiniteNonNegative(value);
        }
    }

    /// <summary>
    /// Immutable diagnostics for one <see cref="DamageCooldownTracker"/> instance.
    /// </summary>
    public readonly struct DamageCooldownTrackerSnapshot
    {
        public DamageCooldownTrackerSnapshot(
            int trackedCount,
            int maximumTrackedInstigators,
            long rejectedAdmissionCount)
        {
            TrackedCount = trackedCount;
            MaximumTrackedInstigators = maximumTrackedInstigators;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int TrackedCount { get; }
        public int MaximumTrackedInstigators { get; }
        public long RejectedAdmissionCount { get; }
        public int RemainingCapacity => MaximumTrackedInstigators - TrackedCount;
    }

    /// <summary>
    /// Bounded, owner-thread cooldown state. Admission fails closed when the configured instigator budget
    /// is full; callers remove actors during teardown and clear the tracker at session shutdown.
    /// </summary>
    public sealed class DamageCooldownTracker
    {
        public const int MaximumSupportedTrackedInstigators = 1_048_576;
        public const int DefaultMaximumTrackedInstigators = 65_536;

        private readonly Dictionary<int, double> _lastAcceptedByInstigator;
        private readonly int _maximumTrackedInstigators;
        private readonly int _ownerThreadId;
        private long _rejectedAdmissionCount;

        public DamageCooldownTracker(
            int initialCapacity = 64,
            int maximumTrackedInstigators = DefaultMaximumTrackedInstigators)
        {
            if (maximumTrackedInstigators < 0
                || maximumTrackedInstigators > MaximumSupportedTrackedInstigators)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTrackedInstigators));
            }

            if (initialCapacity < 0 || initialCapacity > maximumTrackedInstigators)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _maximumTrackedInstigators = maximumTrackedInstigators;
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _lastAcceptedByInstigator = new Dictionary<int, double>(initialCapacity);
        }

        public int TrackedCount
        {
            get
            {
                AssertOwnerThread();
                return _lastAcceptedByInstigator.Count;
            }
        }

        public int MaximumTrackedInstigators => _maximumTrackedInstigators;

        public DamageCooldownTrackerSnapshot GetAdmissionSnapshot()
        {
            AssertOwnerThread();
            return new DamageCooldownTrackerSnapshot(
                _lastAcceptedByInstigator.Count,
                _maximumTrackedInstigators,
                _rejectedAdmissionCount);
        }

        /// <summary>
        /// Returns the last accepted time for an instigator, or <see cref="double.NegativeInfinity"/> when
        /// none is recorded. The sentinel makes <c>currentTime - lastAccepted</c> evaluate as never-on-cooldown.
        /// </summary>
        public double GetLastAcceptedTime(int instigatorActorId)
        {
            AssertOwnerThread();
            if (instigatorActorId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instigatorActorId));
            }

            return _lastAcceptedByInstigator.TryGetValue(instigatorActorId, out double time)
                ? time
                : double.NegativeInfinity;
        }

        public bool TryMarkAccepted(int instigatorActorId, double timeSeconds)
        {
            AssertOwnerThread();
            if (instigatorActorId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instigatorActorId));
            }

            if (timeSeconds < 0d || double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(timeSeconds));
            }

            if (_lastAcceptedByInstigator.TryGetValue(instigatorActorId, out double previousTime))
            {
                if (timeSeconds < previousTime)
                {
                    throw new InvalidOperationException(
                        "Damage cooldown time cannot move backwards for an instigator.");
                }

                _lastAcceptedByInstigator[instigatorActorId] = timeSeconds;
                return true;
            }

            if (_lastAcceptedByInstigator.Count >= _maximumTrackedInstigators)
            {
                _rejectedAdmissionCount++;
                return false;
            }

            _lastAcceptedByInstigator.Add(instigatorActorId, timeSeconds);
            return true;
        }

        public bool Remove(int instigatorActorId)
        {
            AssertOwnerThread();
            if (instigatorActorId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instigatorActorId));
            }

            return _lastAcceptedByInstigator.Remove(instigatorActorId);
        }

        public void Clear()
        {
            AssertOwnerThread();
            _lastAcceptedByInstigator.Clear();
        }

        internal void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "DamageCooldownTracker must be accessed on its owning server simulation thread.");
            }
        }
    }

    /// <summary>
    /// Orchestrates the server-authoritative damage flow: validate the request, update the fire-rate
    /// cooldown on accept, and produce a broadcast-ready <see cref="DamageResultMessage"/>. Engine-free and
    /// allocation-free so it can run on every inbound hit and be unit-tested without a scene.
    /// </summary>
    /// <remarks>
    /// Typical server usage per inbound <see cref="DamageRequestMessage"/>:
    /// <list type="number">
    /// <item>Resolve authoritative instigator/target facts (positions, owner connection, weapon rules).</item>
    /// <item>Build a <see cref="ServerDamageValidationRequest"/> from authoritative facts. <see cref="Process"/>
    /// replaces its last-accepted time with the state owned by <see cref="CooldownTracker"/>.</item>
    /// <item>Call <see cref="Process"/>; on accept apply <see cref="ServerDamageValidationResult.ApprovedDamage"/>
    /// to the target actor, then broadcast the produced result message to observers.</item>
    /// </list>
    /// The processor owns the cooldown lifecycle; remember to call <c>CooldownTracker.Remove</c> on actor
    /// destruction and <c>CooldownTracker.Clear</c> on session shutdown.
    /// </remarks>
    public sealed class ServerAuthoritativeDamageProcessor
    {
        private readonly IServerDamageValidator _customValidator;
        private readonly DamageCooldownTracker _cooldownTracker;

        public ServerAuthoritativeDamageProcessor(
            IServerDamageValidator validator = null,
            DamageCooldownTracker cooldownTracker = null)
        {
            _customValidator = ReferenceEquals(validator, DefaultServerDamageValidator.Instance)
                ? null
                : validator;
            _cooldownTracker = cooldownTracker ?? new DamageCooldownTracker();
        }

        public DamageCooldownTracker CooldownTracker => _cooldownTracker;

        public ServerDamageValidationResult Process(
            in ServerDamageValidationRequest request,
            out DamageResultMessage resultMessage,
            uint requestSequence = 0u,
            byte damageEventType = 0,
            NetworkVector3 hitLocation = default)
        {
            _cooldownTracker.AssertOwnerThread();
            double authoritativeLastAcceptedTime = request.InstigatorActorId > 0
                ? _cooldownTracker.GetLastAcceptedTime(request.InstigatorActorId)
                : double.NegativeInfinity;
            ServerDamageValidationRequest authoritativeRequest = WithLastAcceptedTime(
                in request,
                authoritativeLastAcceptedTime);

            bool hitLocationIsFinite = hitLocation.IsFinite();
            ServerDamageValidationResult baselineResult = hitLocationIsFinite
                ? DefaultServerDamageValidator.Instance.Validate(in authoritativeRequest)
                : ServerDamageValidationResult.Reject(ServerDamageRejectReason.InvalidPayload);
            ServerDamageValidationResult result = baselineResult;
            if (baselineResult.Accepted && _customValidator != null)
            {
                result = _customValidator.Validate(in authoritativeRequest);
                if (!result.IsWellFormed ||
                    result.Accepted && result.ApprovedDamage > baselineResult.ApprovedDamage)
                {
                    result = ServerDamageValidationResult.Reject(ServerDamageRejectReason.Custom);
                }
            }

            resultMessage = CreateResultMessage(
                in request,
                in result,
                requestSequence,
                damageEventType,
                hitLocationIsFinite ? hitLocation : NetworkVector3.Zero);

            // Validate the complete outbound contract before committing authoritative cooldown state.
            DamageNetworkingExtensions.ValidateDamageResult(in resultMessage);
            if (result.Accepted && !_cooldownTracker.TryMarkAccepted(
                    authoritativeRequest.InstigatorActorId,
                    authoritativeRequest.CurrentTimeSeconds))
            {
                result = ServerDamageValidationResult.Reject(
                    ServerDamageRejectReason.CooldownCapacityReached);
                resultMessage = CreateResultMessage(
                    in request,
                    in result,
                    requestSequence,
                    damageEventType,
                    hitLocationIsFinite ? hitLocation : NetworkVector3.Zero);
                DamageNetworkingExtensions.ValidateDamageResult(in resultMessage);
            }

            return result;
        }

        private static DamageResultMessage CreateResultMessage(
            in ServerDamageValidationRequest request,
            in ServerDamageValidationResult result,
            uint requestSequence,
            byte damageEventType,
            in NetworkVector3 hitLocation)
        {
            return new DamageResultMessage
            {
                RequestSequence = requestSequence,
                // Zero is the result wire contract's unresolved-identity sentinel. A malformed
                // inbound negative identifier must never be reflected into an outbound packet.
                InstigatorActorId = request.InstigatorActorId >= 0 ? request.InstigatorActorId : 0,
                TargetActorId = request.TargetActorId >= 0 ? request.TargetActorId : 0,
                AppliedDamage = result.ApprovedDamage,
                ResultCode = result.Reason,
                DamageEventType = damageEventType,
                HitLocation = hitLocation
            };
        }

        private static ServerDamageValidationRequest WithLastAcceptedTime(
            in ServerDamageValidationRequest request,
            double lastAcceptedTimeSeconds)
        {
            return new ServerDamageValidationRequest(
                request.InstigatorActorId,
                request.TargetActorId,
                request.InstigatorOwnerConnectionId,
                request.RequestConnectionId,
                request.TargetCanBeDamaged,
                request.InstigatorPosition,
                request.TargetPosition,
                request.RequestedDamage,
                request.MaxDamage,
                request.MaxRangeSqr,
                request.CurrentTimeSeconds,
                lastAcceptedTimeSeconds,
                request.CooldownSeconds);
        }
    }
}
