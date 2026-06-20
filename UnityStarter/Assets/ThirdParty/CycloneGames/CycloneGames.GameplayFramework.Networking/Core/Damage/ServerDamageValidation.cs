using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        public readonly float CurrentTimeSeconds;

        /// <summary>Last accepted damage time for this instigator (see <see cref="DamageCooldownTracker"/>).</summary>
        public readonly float LastAcceptedTimeSeconds;

        /// <summary>Minimum seconds between accepted damages. 0 or less disables the cooldown check.</summary>
        public readonly float CooldownSeconds;

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
            float currentTimeSeconds,
            float lastAcceptedTimeSeconds,
            float cooldownSeconds)
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
            return new ServerDamageValidationResult(ServerDamageRejectReason.Accepted, approvedDamage);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ServerDamageValidationResult Reject(ServerDamageRejectReason reason)
        {
            return new ServerDamageValidationResult(reason, 0f);
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
            // 1. Payload sanity. Reject self-damage spoofing, unset ids and non-finite/negative magnitudes.
            if (request.InstigatorActorId == 0
                || request.TargetActorId == 0
                || request.InstigatorActorId == request.TargetActorId
                || !IsFiniteNonNegative(request.RequestedDamage)
                || !IsFiniteNonNegative(request.MaxDamage))
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
            if (request.CooldownSeconds > 0f
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
    }

    /// <summary>
    /// Tracks the last accepted damage time per instigator so <see cref="IServerDamageValidator"/> can
    /// enforce fire-rate cooldowns. Owned by the server damage system; not thread-safe by design—drive it
    /// from the single server simulation thread.
    /// </summary>
    /// <remarks>
    /// The map grows with the number of distinct instigators. The owner must call <see cref="Remove"/>
    /// when an actor is destroyed and <see cref="Clear"/> on session shutdown to keep it bounded.
    /// </remarks>
    public sealed class DamageCooldownTracker
    {
        private readonly Dictionary<int, float> _lastAcceptedByInstigator;

        public DamageCooldownTracker(int capacity = 64)
        {
            if (capacity < 0)
            {
                capacity = 0;
            }

            _lastAcceptedByInstigator = new Dictionary<int, float>(capacity);
        }

        public int TrackedCount => _lastAcceptedByInstigator.Count;

        /// <summary>
        /// Returns the last accepted time for an instigator, or <see cref="float.NegativeInfinity"/> when
        /// none is recorded. The sentinel makes <c>currentTime - lastAccepted</c> evaluate as never-on-cooldown.
        /// </summary>
        public float GetLastAcceptedTime(int instigatorActorId)
        {
            return _lastAcceptedByInstigator.TryGetValue(instigatorActorId, out float time)
                ? time
                : float.NegativeInfinity;
        }

        public void MarkAccepted(int instigatorActorId, float timeSeconds)
        {
            _lastAcceptedByInstigator[instigatorActorId] = timeSeconds;
        }

        public void Remove(int instigatorActorId)
        {
            _lastAcceptedByInstigator.Remove(instigatorActorId);
        }

        public void Clear()
        {
            _lastAcceptedByInstigator.Clear();
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
    /// <item>Build a <see cref="ServerDamageValidationRequest"/>, reading the last accepted time from
    /// <see cref="CooldownTracker"/>.</item>
    /// <item>Call <see cref="Process"/>; on accept apply <see cref="ServerDamageValidationResult.ApprovedDamage"/>
    /// to the target actor, then broadcast the produced result message to observers.</item>
    /// </list>
    /// The processor owns the cooldown lifecycle; remember to call <c>CooldownTracker.Remove</c> on actor
    /// destruction and <c>CooldownTracker.Clear</c> on session shutdown.
    /// </remarks>
    public sealed class ServerAuthoritativeDamageProcessor
    {
        private readonly IServerDamageValidator _validator;
        private readonly DamageCooldownTracker _cooldownTracker;

        public ServerAuthoritativeDamageProcessor(
            IServerDamageValidator validator = null,
            DamageCooldownTracker cooldownTracker = null)
        {
            _validator = validator ?? DefaultServerDamageValidator.Instance;
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
            ServerDamageValidationResult result = _validator.Validate(request);
            if (result.Accepted)
            {
                _cooldownTracker.MarkAccepted(request.InstigatorActorId, request.CurrentTimeSeconds);
            }

            resultMessage = new DamageResultMessage
            {
                RequestSequence = requestSequence,
                InstigatorActorId = request.InstigatorActorId,
                TargetActorId = request.TargetActorId,
                AppliedDamage = result.ApprovedDamage,
                ResultCode = (byte)result.Reason,
                DamageEventType = damageEventType,
                HitLocation = hitLocation
            };

            return result;
        }
    }
}
