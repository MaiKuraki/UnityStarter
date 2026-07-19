using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    public enum NetworkHostCandidateKind : byte
    {
        Unknown,
        PlayerListenServer,
        RelayCoordinator,
        DedicatedServer
    }

    public enum HostMigrationState : byte
    {
        Stable,
        HostSuspectedLost,
        Migrating,
        Recovering,
        Failed
    }

    public enum HostMigrationReason : byte
    {
        None,
        HostDisconnected,
        HostTimeout,
        ManualTransfer,
        BetterHostAvailable,
        Shutdown
    }

    [Flags]
    public enum NetworkAuthorityTransferScope : uint
    {
        None = 0,
        SessionOwner = 1u << 0,
        SimulationAuthority = 1u << 1,
        SpawnAuthority = 1u << 2,
        ObjectAuthority = 1u << 3,
        SceneAuthority = 1u << 4,
        MatchState = 1u << 5,
        RandomSeedAuthority = 1u << 6,
        All = SessionOwner
              | SimulationAuthority
              | SpawnAuthority
              | ObjectAuthority
              | SceneAuthority
              | MatchState
              | RandomSeedAuthority
    }

    public readonly struct NetworkHostParticipant
    {
        public readonly int ConnectionId;
        public readonly ulong PlayerId;
        public readonly bool IsConnected;
        public readonly bool CanHost;
        public readonly int AuthorityRank;
        public readonly NetworkHostCandidateKind Kind;
        public readonly int PingMs;
        public readonly float PacketLoss;
        public readonly int HardwareScore;
        public readonly int CapacityScore;
        public readonly double JoinedAtTime;
        public readonly double LastSeenTime;
        public readonly NetworkTickId LastConfirmedTick;

        public NetworkHostParticipant(
            int connectionId,
            ulong playerId,
            bool isConnected,
            bool canHost,
            int authorityRank = 0,
            NetworkHostCandidateKind kind = NetworkHostCandidateKind.PlayerListenServer,
            int pingMs = -1,
            float packetLoss = 0f,
            int hardwareScore = 0,
            int capacityScore = 0,
            double joinedAtTime = 0d,
            double lastSeenTime = 0d,
            NetworkTickId lastConfirmedTick = default)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (packetLoss < 0f || float.IsNaN(packetLoss))
            {
                throw new ArgumentOutOfRangeException(nameof(packetLoss));
            }

            ConnectionId = connectionId;
            PlayerId = playerId;
            IsConnected = isConnected;
            CanHost = canHost;
            AuthorityRank = authorityRank;
            Kind = kind;
            PingMs = pingMs;
            PacketLoss = packetLoss;
            HardwareScore = hardwareScore;
            CapacityScore = capacityScore;
            JoinedAtTime = joinedAtTime;
            LastSeenTime = lastSeenTime;
            LastConfirmedTick = lastConfirmedTick;
        }

        public bool IsEligibleHost
        {
            get
            {
                return IsConnected && CanHost;
            }
        }

        public NetworkHostParticipant WithConnectionState(
            bool isConnected,
            double lastSeenTime,
            NetworkTickId lastConfirmedTick)
        {
            return new NetworkHostParticipant(
                ConnectionId,
                PlayerId,
                isConnected,
                CanHost,
                AuthorityRank,
                Kind,
                PingMs,
                PacketLoss,
                HardwareScore,
                CapacityScore,
                JoinedAtTime,
                lastSeenTime,
                lastConfirmedTick);
        }
    }

    public readonly struct HostMigrationOptions
    {
        public static readonly HostMigrationOptions Default = new HostMigrationOptions(
            maxHostSilenceSeconds: 8d,
            transferScopes: NetworkAuthorityTransferScope.All,
            allowCurrentHostAsCandidate: false);

        public readonly double MaxHostSilenceSeconds;
        public readonly NetworkAuthorityTransferScope TransferScopes;
        public readonly bool AllowCurrentHostAsCandidate;

        public HostMigrationOptions(
            double maxHostSilenceSeconds,
            NetworkAuthorityTransferScope transferScopes,
            bool allowCurrentHostAsCandidate)
        {
            if (maxHostSilenceSeconds <= 0d || double.IsNaN(maxHostSilenceSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(maxHostSilenceSeconds));
            }

            MaxHostSilenceSeconds = maxHostSilenceSeconds;
            TransferScopes = transferScopes;
            AllowCurrentHostAsCandidate = allowCurrentHostAsCandidate;
        }
    }

    public readonly struct NetworkHostMigrationSnapshot
    {
        public readonly string SessionId;
        public readonly int Generation;
        public readonly int HostConnectionId;
        public readonly ulong HostPlayerId;
        public readonly NetworkTickId Tick;
        public readonly ulong PayloadHash;
        public readonly int EstimatedPayloadBytes;
        public readonly double CapturedAtTime;

        public NetworkHostMigrationSnapshot(
            string sessionId,
            int generation,
            int hostConnectionId,
            ulong hostPlayerId,
            NetworkTickId tick,
            ulong payloadHash,
            int estimatedPayloadBytes,
            double capturedAtTime)
        {
            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            if (hostConnectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(hostConnectionId));
            }

            if (estimatedPayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            }

            SessionId = sessionId ?? string.Empty;
            Generation = generation;
            HostConnectionId = hostConnectionId;
            HostPlayerId = hostPlayerId;
            Tick = tick;
            PayloadHash = payloadHash;
            EstimatedPayloadBytes = estimatedPayloadBytes;
            CapturedAtTime = capturedAtTime;
        }

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(SessionId)
                       && HostConnectionId > 0
                       && Tick.IsValid;
            }
        }
    }

    public readonly struct NetworkAuthorityTransferPlan
    {
        public readonly string SessionId;
        public readonly int Generation;
        public readonly int FromHostConnectionId;
        public readonly int ToHostConnectionId;
        public readonly ulong ToHostPlayerId;
        public readonly NetworkHostCandidateKind ToHostKind;
        public readonly NetworkTickId TransferTick;
        public readonly NetworkAuthorityTransferScope TransferScopes;
        public readonly HostMigrationReason Reason;

        public NetworkAuthorityTransferPlan(
            string sessionId,
            int generation,
            int fromHostConnectionId,
            int toHostConnectionId,
            ulong toHostPlayerId,
            NetworkHostCandidateKind toHostKind,
            NetworkTickId transferTick,
            NetworkAuthorityTransferScope transferScopes,
            HostMigrationReason reason)
        {
            SessionId = sessionId ?? string.Empty;
            Generation = generation;
            FromHostConnectionId = fromHostConnectionId;
            ToHostConnectionId = toHostConnectionId;
            ToHostPlayerId = toHostPlayerId;
            ToHostKind = toHostKind;
            TransferTick = transferTick;
            TransferScopes = transferScopes;
            Reason = reason;
        }

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(SessionId)
                       && Generation > 0
                       && FromHostConnectionId > 0
                       && ToHostConnectionId > 0
                       && TransferTick.IsValid
                       && TransferScopes != NetworkAuthorityTransferScope.None;
            }
        }
    }

    public interface IHostCandidatePolicy
    {
        bool TrySelectHost(
            IReadOnlyList<NetworkHostParticipant> participants,
            int currentHostConnectionId,
            bool allowCurrentHost,
            out NetworkHostParticipant host);
    }

    public sealed class DefaultHostCandidatePolicy : IHostCandidatePolicy
    {
        public static readonly DefaultHostCandidatePolicy Instance = new DefaultHostCandidatePolicy();

        public bool TrySelectHost(
            IReadOnlyList<NetworkHostParticipant> participants,
            int currentHostConnectionId,
            bool allowCurrentHost,
            out NetworkHostParticipant host)
        {
            if (participants == null)
            {
                throw new ArgumentNullException(nameof(participants));
            }

            host = default;
            bool found = false;

            for (int i = 0; i < participants.Count; i++)
            {
                NetworkHostParticipant candidate = participants[i];
                if (!candidate.IsEligibleHost)
                {
                    continue;
                }

                if (!allowCurrentHost && candidate.ConnectionId == currentHostConnectionId)
                {
                    continue;
                }

                if (!found || Compare(candidate, host) < 0)
                {
                    host = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static int Compare(in NetworkHostParticipant left, in NetworkHostParticipant right)
        {
            int value = right.AuthorityRank.CompareTo(left.AuthorityRank);
            if (value != 0)
            {
                return value;
            }

            value = GetKindPriority(right.Kind).CompareTo(GetKindPriority(left.Kind));
            if (value != 0)
            {
                return value;
            }

            value = right.LastConfirmedTick.Value.CompareTo(left.LastConfirmedTick.Value);
            if (value != 0)
            {
                return value;
            }

            value = right.CapacityScore.CompareTo(left.CapacityScore);
            if (value != 0)
            {
                return value;
            }

            value = right.HardwareScore.CompareTo(left.HardwareScore);
            if (value != 0)
            {
                return value;
            }

            value = left.PacketLoss.CompareTo(right.PacketLoss);
            if (value != 0)
            {
                return value;
            }

            int leftPing = left.PingMs < 0 ? int.MaxValue : left.PingMs;
            int rightPing = right.PingMs < 0 ? int.MaxValue : right.PingMs;
            value = leftPing.CompareTo(rightPing);
            if (value != 0)
            {
                return value;
            }

            value = left.JoinedAtTime.CompareTo(right.JoinedAtTime);
            return value != 0
                ? value
                : left.ConnectionId.CompareTo(right.ConnectionId);
        }

        private static int GetKindPriority(NetworkHostCandidateKind kind)
        {
            switch (kind)
            {
                case NetworkHostCandidateKind.DedicatedServer:
                    return 3;
                case NetworkHostCandidateKind.RelayCoordinator:
                    return 2;
                case NetworkHostCandidateKind.PlayerListenServer:
                    return 1;
                default:
                    return 0;
            }
        }
    }

    public sealed class HostMigrationCoordinator
    {
        private readonly Dictionary<int, NetworkHostParticipant> _participants;
        private readonly List<NetworkHostParticipant> _scratch;
        private readonly IHostCandidatePolicy _candidatePolicy;
        private readonly HostMigrationOptions _options;

        private NetworkAuthorityTransferPlan _pendingPlan;

        public HostMigrationCoordinator(
            string sessionId,
            IHostCandidatePolicy candidatePolicy = null,
            HostMigrationOptions? options = null,
            int participantCapacity = 16)
        {
            if (participantCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(participantCapacity));
            }

            SessionId = sessionId ?? string.Empty;
            _candidatePolicy = candidatePolicy ?? DefaultHostCandidatePolicy.Instance;
            _options = options ?? HostMigrationOptions.Default;
            _participants = new Dictionary<int, NetworkHostParticipant>(participantCapacity);
            _scratch = new List<NetworkHostParticipant>(participantCapacity);
            State = HostMigrationState.Stable;
        }

        public string SessionId { get; }
        public int CurrentHostConnectionId { get; private set; }
        public ulong CurrentHostPlayerId { get; private set; }
        public int Generation { get; private set; }
        public HostMigrationState State { get; private set; }
        public NetworkHostMigrationSnapshot LastSnapshot { get; private set; }

        public event Action<NetworkAuthorityTransferPlan> OnHostMigrationStarted;
        public event Action<NetworkAuthorityTransferPlan> OnHostMigrationCompleted;
        public event Action<string> OnHostMigrationFailed;

        public int ParticipantCount
        {
            get
            {
                return _participants.Count;
            }
        }

        public void UpsertParticipant(in NetworkHostParticipant participant)
        {
            _participants[participant.ConnectionId] = participant;
        }

        public bool RemoveParticipant(int connectionId)
        {
            return _participants.Remove(connectionId);
        }

        public bool TryGetParticipant(int connectionId, out NetworkHostParticipant participant)
        {
            return _participants.TryGetValue(connectionId, out participant);
        }

        public void SetCurrentHost(int connectionId, ulong playerId = 0UL, int generation = 0)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            CurrentHostConnectionId = connectionId;
            CurrentHostPlayerId = playerId;
            Generation = generation;
            State = HostMigrationState.Stable;
            _pendingPlan = default;
        }

        public void RecordSnapshot(in NetworkHostMigrationSnapshot snapshot)
        {
            if (!snapshot.IsValid)
            {
                throw new ArgumentException("Host migration snapshot must include session id, host id, and a valid tick.", nameof(snapshot));
            }

            if (!string.Equals(SessionId, snapshot.SessionId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Host migration snapshot belongs to a different session.", nameof(snapshot));
            }

            LastSnapshot = snapshot;
        }

        public bool MarkDisconnected(int connectionId, double time, NetworkTickId lastConfirmedTick)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0d)
                throw new ArgumentOutOfRangeException(nameof(time));

            if (!_participants.TryGetValue(connectionId, out NetworkHostParticipant participant))
            {
                return false;
            }

            _participants[connectionId] = participant.WithConnectionState(false, time, lastConfirmedTick);
            if (connectionId == CurrentHostConnectionId && State == HostMigrationState.Stable)
            {
                State = HostMigrationState.HostSuspectedLost;
            }

            return true;
        }

        public bool Update(double currentTime, NetworkTickId transferTick, out NetworkAuthorityTransferPlan plan)
        {
            plan = default;
            if (double.IsNaN(currentTime) || double.IsInfinity(currentTime) || currentTime < 0d)
                throw new ArgumentOutOfRangeException(nameof(currentTime));

            if ((State != HostMigrationState.Stable && State != HostMigrationState.HostSuspectedLost)
                || CurrentHostConnectionId <= 0)
            {
                return false;
            }

            if (!_participants.TryGetValue(CurrentHostConnectionId, out NetworkHostParticipant host))
            {
                return TryBeginMigration(HostMigrationReason.HostDisconnected, transferTick, out plan);
            }

            if (!host.IsConnected)
            {
                return TryBeginMigration(HostMigrationReason.HostDisconnected, transferTick, out plan);
            }

            if (host.LastSeenTime > 0d
                && currentTime - host.LastSeenTime >= _options.MaxHostSilenceSeconds)
            {
                State = HostMigrationState.HostSuspectedLost;
                return TryBeginMigration(HostMigrationReason.HostTimeout, transferTick, out plan);
            }

            return false;
        }

        public bool TryBeginMigration(
            HostMigrationReason reason,
            NetworkTickId transferTick,
            out NetworkAuthorityTransferPlan plan)
        {
            plan = default;
            if (State != HostMigrationState.Stable && State != HostMigrationState.HostSuspectedLost)
                return false;

            if (CurrentHostConnectionId <= 0)
            {
                Fail("Current host is not set.");
                return false;
            }

            if (!transferTick.IsValid)
            {
                Fail("Transfer tick is invalid.");
                return false;
            }

            CollectParticipants();
            bool allowCurrentHost = _options.AllowCurrentHostAsCandidate
                                    && reason != HostMigrationReason.HostDisconnected
                                    && reason != HostMigrationReason.HostTimeout;

            if (!_candidatePolicy.TrySelectHost(_scratch, CurrentHostConnectionId, allowCurrentHost, out NetworkHostParticipant newHost))
            {
                _scratch.Clear();
                Fail("No eligible host migration candidate is available.");
                return false;
            }

            _scratch.Clear();
            plan = new NetworkAuthorityTransferPlan(
                SessionId,
                Generation + 1,
                CurrentHostConnectionId,
                newHost.ConnectionId,
                newHost.PlayerId,
                newHost.Kind,
                transferTick,
                _options.TransferScopes,
                reason);

            _pendingPlan = plan;
            State = HostMigrationState.Migrating;
            OnHostMigrationStarted?.Invoke(plan);
            return true;
        }

        public bool CompleteMigration(int newHostConnectionId, NetworkTickId acceptedTick)
        {
            if (!_pendingPlan.IsValid || State != HostMigrationState.Migrating)
            {
                return false;
            }

            if (newHostConnectionId != _pendingPlan.ToHostConnectionId)
            {
                Fail("Migration completion target does not match the pending host.");
                return false;
            }

            if (acceptedTick.IsValid && acceptedTick < _pendingPlan.TransferTick)
            {
                Fail("Migration completion tick is older than the transfer tick.");
                return false;
            }

            CurrentHostConnectionId = _pendingPlan.ToHostConnectionId;
            CurrentHostPlayerId = _pendingPlan.ToHostPlayerId;
            Generation = _pendingPlan.Generation;
            State = HostMigrationState.Stable;

            NetworkAuthorityTransferPlan completedPlan = _pendingPlan;
            _pendingPlan = default;
            OnHostMigrationCompleted?.Invoke(completedPlan);
            return true;
        }

        public void FailMigration(string reason)
        {
            Fail(reason);
        }

        private void CollectParticipants()
        {
            _scratch.Clear();
            foreach (KeyValuePair<int, NetworkHostParticipant> pair in _participants)
            {
                _scratch.Add(pair.Value);
            }
        }

        private void Fail(string reason)
        {
            State = HostMigrationState.Failed;
            _pendingPlan = default;
            OnHostMigrationFailed?.Invoke(reason ?? string.Empty);
        }
    }
}
