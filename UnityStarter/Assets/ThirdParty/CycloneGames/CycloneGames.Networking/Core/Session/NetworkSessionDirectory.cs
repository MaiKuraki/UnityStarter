using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    [Flags]
    public enum NetworkSessionConnectivity : uint
    {
        None = 0,
        Direct = 1u << 0,
        Lan = 1u << 1,
        PlatformLobby = 1u << 2,
        Relay = 1u << 3,
        BackendMatch = 1u << 4,
        DedicatedServer = 1u << 5,
        All = Direct | Lan | PlatformLobby | Relay | BackendMatch | DedicatedServer
    }

    public enum NetworkSessionDiscoverySource : byte
    {
        Unknown,
        Manual,
        Lan,
        Platform,
        Backend,
        Relay,
        DedicatedServer
    }

    public readonly struct NetworkSessionDescriptor
    {
        public readonly string SessionId;
        public readonly string Name;
        public readonly string GameMode;
        public readonly string Map;
        public readonly string Region;
        public readonly string BuildId;
        public readonly string HostAddress;
        public readonly ushort Port;
        public readonly int CurrentPlayers;
        public readonly int MaxPlayers;
        public readonly int ReservedSlots;
        public readonly int PingMs;
        public readonly float AverageSkillRating;
        public readonly bool HasSkillRating;
        public readonly bool IsPrivate;
        public readonly bool IsJoinable;
        public readonly bool RequiresPassword;
        public readonly bool SupportsHostMigration;
        public readonly bool SupportsReconnection;
        public readonly SessionState State;
        public readonly NetworkSessionConnectivity Connectivity;
        public readonly NetworkSessionDiscoverySource Source;
        public readonly double LastSeenTime;
        public readonly IReadOnlyDictionary<string, string> Properties;

        public NetworkSessionDescriptor(
            string sessionId,
            string name,
            string gameMode,
            int currentPlayers,
            int maxPlayers,
            string map = "",
            string region = "",
            string buildId = "",
            string hostAddress = "",
            ushort port = 0,
            int reservedSlots = 0,
            int pingMs = -1,
            float averageSkillRating = 0f,
            bool hasSkillRating = false,
            bool isPrivate = false,
            bool isJoinable = true,
            bool requiresPassword = false,
            bool supportsHostMigration = false,
            bool supportsReconnection = false,
            SessionState state = SessionState.Lobby,
            NetworkSessionConnectivity connectivity = NetworkSessionConnectivity.Direct,
            NetworkSessionDiscoverySource source = NetworkSessionDiscoverySource.Unknown,
            double lastSeenTime = 0d,
            IReadOnlyDictionary<string, string> properties = null)
        {
            if (maxPlayers < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            }

            if (currentPlayers < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentPlayers));
            }

            if (reservedSlots < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reservedSlots));
            }

            SessionId = sessionId ?? string.Empty;
            Name = name ?? string.Empty;
            GameMode = gameMode ?? string.Empty;
            Map = map ?? string.Empty;
            Region = region ?? string.Empty;
            BuildId = buildId ?? string.Empty;
            HostAddress = hostAddress ?? string.Empty;
            Port = port;
            CurrentPlayers = currentPlayers;
            MaxPlayers = maxPlayers;
            ReservedSlots = reservedSlots;
            PingMs = pingMs;
            AverageSkillRating = averageSkillRating;
            HasSkillRating = hasSkillRating;
            IsPrivate = isPrivate;
            IsJoinable = isJoinable;
            RequiresPassword = requiresPassword;
            SupportsHostMigration = supportsHostMigration;
            SupportsReconnection = supportsReconnection;
            State = state;
            Connectivity = connectivity;
            Source = source;
            LastSeenTime = lastSeenTime;
            Properties = properties;
        }

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrEmpty(SessionId) && MaxPlayers > 0;
            }
        }

        public int OpenSlots
        {
            get
            {
                int openSlots = MaxPlayers - CurrentPlayers - ReservedSlots;
                return openSlots > 0 ? openSlots : 0;
            }
        }

        public bool IsFull
        {
            get
            {
                return OpenSlots <= 0;
            }
        }

        public static NetworkSessionDescriptor FromSession(
            NetworkSession session,
            NetworkSessionDiscoverySource source,
            NetworkSessionConnectivity connectivity,
            string region = "",
            string buildId = "",
            int pingMs = -1,
            double lastSeenTime = 0d)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new NetworkSessionDescriptor(
                session.SessionId,
                session.Name,
                session.GameMode,
                session.CurrentPlayers,
                session.MaxPlayers,
                session.Map,
                region,
                buildId,
                session.HostAddress,
                session.Port,
                pingMs: pingMs,
                isPrivate: session.IsPrivate,
                isJoinable: session.State == SessionState.Lobby || session.State == SessionState.InProgress,
                state: session.State,
                connectivity: connectivity,
                source: source,
                lastSeenTime: lastSeenTime,
                properties: session.Properties);
        }
    }

    public sealed class NetworkSessionSearchCriteria
    {
        public string GameMode { get; set; }
        public string Map { get; set; }
        public string Region { get; set; }
        public string BuildId { get; set; }
        public bool HideFullSessions { get; set; } = true;
        public bool HidePrivateSessions { get; set; } = true;
        public bool RequireJoinable { get; set; } = true;
        public bool RequireHostMigration { get; set; }
        public bool RequireReconnection { get; set; }
        public bool AllowPasswordProtected { get; set; }
        public int MinOpenSlots { get; set; } = 1;
        public int MaxResults { get; set; } = 50;
        public int MaxPingMs { get; set; } = -1;
        public float SkillRating { get; set; }
        public float MaxSkillDelta { get; set; } = -1f;
        public NetworkSessionConnectivity RequiredConnectivity { get; set; } = NetworkSessionConnectivity.None;
        public Dictionary<string, string> RequiredProperties { get; set; }
    }

    public sealed class NetworkSessionDirectory
    {
        private readonly Dictionary<string, NetworkSessionDescriptor> _sessions;
        private readonly List<NetworkSessionDescriptor> _scratch;
        private readonly SearchComparer _searchComparer = new SearchComparer();

        public NetworkSessionDirectory(int capacity = 128)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _sessions = new Dictionary<string, NetworkSessionDescriptor>(capacity, StringComparer.Ordinal);
            _scratch = new List<NetworkSessionDescriptor>(capacity);
        }

        public int Count
        {
            get
            {
                return _sessions.Count;
            }
        }

        public bool TryGet(string sessionId, out NetworkSessionDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                descriptor = default;
                return false;
            }

            return _sessions.TryGetValue(sessionId, out descriptor);
        }

        public void Upsert(in NetworkSessionDescriptor descriptor)
        {
            if (!descriptor.IsValid)
            {
                throw new ArgumentException("Session descriptor must have a non-empty id and a positive max player count.", nameof(descriptor));
            }

            _sessions[descriptor.SessionId] = descriptor;
        }

        public bool Remove(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return false;
            }

            return _sessions.Remove(sessionId);
        }

        public int RemoveStale(double olderThanTime)
        {
            _scratch.Clear();
            foreach (KeyValuePair<string, NetworkSessionDescriptor> pair in _sessions)
            {
                if (pair.Value.LastSeenTime > 0d && pair.Value.LastSeenTime < olderThanTime)
                {
                    _scratch.Add(pair.Value);
                }
            }

            int removed = 0;
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (_sessions.Remove(_scratch[i].SessionId))
                {
                    removed++;
                }
            }

            _scratch.Clear();
            return removed;
        }

        public int Search(NetworkSessionSearchCriteria criteria, IList<NetworkSessionDescriptor> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            criteria ??= new NetworkSessionSearchCriteria();
            results.Clear();
            _scratch.Clear();

            foreach (KeyValuePair<string, NetworkSessionDescriptor> pair in _sessions)
            {
                NetworkSessionDescriptor session = pair.Value;
                if (Matches(session, criteria))
                {
                    _scratch.Add(session);
                }
            }

            _searchComparer.Criteria = criteria;
            _scratch.Sort(_searchComparer);

            int maxResults = criteria.MaxResults <= 0 ? _scratch.Count : Math.Min(criteria.MaxResults, _scratch.Count);
            for (int i = 0; i < maxResults; i++)
            {
                results.Add(_scratch[i]);
            }

            int count = maxResults;
            _scratch.Clear();
            return count;
        }

        public bool TryFindBest(NetworkSessionSearchCriteria criteria, out NetworkSessionDescriptor descriptor, out float score)
        {
            criteria ??= new NetworkSessionSearchCriteria();
            descriptor = default;
            score = float.MinValue;
            bool found = false;

            foreach (KeyValuePair<string, NetworkSessionDescriptor> pair in _sessions)
            {
                NetworkSessionDescriptor session = pair.Value;
                if (!Matches(session, criteria))
                {
                    continue;
                }

                float candidateScore = Score(session, criteria);
                if (!found
                    || candidateScore > score
                    || (Math.Abs(candidateScore - score) <= float.Epsilon
                        && string.CompareOrdinal(session.SessionId, descriptor.SessionId) < 0))
                {
                    descriptor = session;
                    score = candidateScore;
                    found = true;
                }
            }

            return found;
        }

        public void Clear()
        {
            _sessions.Clear();
            _scratch.Clear();
        }

        public static bool Matches(in NetworkSessionDescriptor session, NetworkSessionSearchCriteria criteria)
        {
            if (!session.IsValid)
            {
                return false;
            }

            criteria ??= new NetworkSessionSearchCriteria();

            if (criteria.RequireJoinable && !session.IsJoinable)
            {
                return false;
            }

            if (criteria.HideFullSessions && session.IsFull)
            {
                return false;
            }

            if (criteria.HidePrivateSessions && session.IsPrivate)
            {
                return false;
            }

            if (!criteria.AllowPasswordProtected && session.RequiresPassword)
            {
                return false;
            }

            if (criteria.RequireHostMigration && !session.SupportsHostMigration)
            {
                return false;
            }

            if (criteria.RequireReconnection && !session.SupportsReconnection)
            {
                return false;
            }

            if (criteria.MinOpenSlots > 0 && session.OpenSlots < criteria.MinOpenSlots)
            {
                return false;
            }

            if (criteria.MaxPingMs >= 0 && session.PingMs >= 0 && session.PingMs > criteria.MaxPingMs)
            {
                return false;
            }

            if (criteria.RequiredConnectivity != NetworkSessionConnectivity.None
                && (session.Connectivity & criteria.RequiredConnectivity) == NetworkSessionConnectivity.None)
            {
                return false;
            }

            if (!MatchesText(criteria.GameMode, session.GameMode)
                || !MatchesText(criteria.Map, session.Map)
                || !MatchesText(criteria.Region, session.Region)
                || !MatchesText(criteria.BuildId, session.BuildId))
            {
                return false;
            }

            if (criteria.MaxSkillDelta >= 0f && session.HasSkillRating)
            {
                float delta = Math.Abs(session.AverageSkillRating - criteria.SkillRating);
                if (delta > criteria.MaxSkillDelta)
                {
                    return false;
                }
            }

            return MatchesProperties(session.Properties, criteria.RequiredProperties);
        }

        public static float Score(in NetworkSessionDescriptor session, NetworkSessionSearchCriteria criteria)
        {
            criteria ??= new NetworkSessionSearchCriteria();
            float score = 1000f;

            if (session.PingMs >= 0)
            {
                score -= session.PingMs * 2f;
            }

            score += session.OpenSlots * 12f;

            if (session.SupportsHostMigration)
            {
                score += 40f;
            }

            if (session.SupportsReconnection)
            {
                score += 25f;
            }

            if (session.State == SessionState.Lobby)
            {
                score += 20f;
            }

            if (criteria.RequiredConnectivity != NetworkSessionConnectivity.None
                && (session.Connectivity & criteria.RequiredConnectivity) != NetworkSessionConnectivity.None)
            {
                score += 30f;
            }

            if (criteria.MaxSkillDelta >= 0f && session.HasSkillRating)
            {
                score -= Math.Abs(session.AverageSkillRating - criteria.SkillRating);
            }

            return score;
        }

        private static bool MatchesText(string expected, string actual)
        {
            return string.IsNullOrEmpty(expected) || string.Equals(expected, actual, StringComparison.Ordinal);
        }

        private static bool MatchesProperties(
            IReadOnlyDictionary<string, string> properties,
            Dictionary<string, string> requiredProperties)
        {
            if (requiredProperties == null || requiredProperties.Count == 0)
            {
                return true;
            }

            if (properties == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, string> required in requiredProperties)
            {
                if (!properties.TryGetValue(required.Key, out string value)
                    || !string.Equals(value, required.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class SearchComparer : IComparer<NetworkSessionDescriptor>
        {
            public NetworkSessionSearchCriteria Criteria;

            public int Compare(NetworkSessionDescriptor x, NetworkSessionDescriptor y)
            {
                float xScore = Score(x, Criteria);
                float yScore = Score(y, Criteria);
                int scoreComparison = yScore.CompareTo(xScore);
                return scoreComparison != 0
                    ? scoreComparison
                    : string.CompareOrdinal(x.SessionId, y.SessionId);
            }
        }
    }
}
