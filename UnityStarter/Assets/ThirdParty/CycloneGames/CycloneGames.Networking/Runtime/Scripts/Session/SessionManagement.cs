using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Session
{
    /// <summary>
    /// Represents a network session (game room, lobby, match instance).
    /// </summary>
    public sealed class NetworkSession
    {
        public string SessionId { get; set; }
        public string Name { get; set; }
        public string Map { get; set; }
        public string GameMode { get; set; }
        public int MaxPlayers { get; set; }
        public int CurrentPlayers { get; set; }
        public SessionState State { get; set; }
        public bool IsPrivate { get; set; }
        public string HostAddress { get; set; }
        public ushort Port { get; set; }

        // Arbitrary custom properties (game version, mods, region, password hash, etc.)
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> Properties => _properties;

        public void SetProperty(string key, string value) => _properties[key] = value;
        public string GetProperty(string key, string defaultValue = "") =>
            _properties.TryGetValue(key, out var v) ? v : defaultValue;

        public bool IsFull => CurrentPlayers >= MaxPlayers;
    }

    public enum SessionState : byte
    {
        Lobby,
        Starting,
        InProgress,
        Ending,
        Closed
    }

    /// <summary>
    /// Lobby management interface. Implement for specific backends
    /// (Steam, Epic, PlayStation, Xbox, custom master-server, etc.)
    /// </summary>
    public interface ILobbyManager
    {
        NetworkSession CurrentSession { get; }
        IReadOnlyList<NetworkSession> AvailableSessions { get; }

        void CreateSession(NetworkSession session);
        void JoinSession(string sessionId);
        void LeaveSession();

        /// <summary>
        /// Refresh the list of available sessions from the backend.
        /// </summary>
        void RefreshSessionList(SessionFilter filter = null);

        void UpdateSession(NetworkSession session);
        void SetReady(bool ready);

        event Action<NetworkSession> OnSessionCreated;
        event Action<NetworkSession> OnSessionJoined;
        event Action OnSessionLeft;
        event Action<IReadOnlyList<NetworkSession>> OnSessionListUpdated;
        event Action<INetConnection> OnPlayerJoined;
        event Action<INetConnection> OnPlayerLeft;
        event Action<string> OnError;
    }

    public sealed class SessionFilter
    {
        public string GameMode { get; set; }
        public string Map { get; set; }
        public bool HideFullSessions { get; set; } = true;
        public bool HidePrivateSessions { get; set; } = true;
        public int MaxResults { get; set; } = 50;
        public Dictionary<string, string> RequiredProperties { get; set; }
    }

    /// <summary>
    /// Matchmaking interface. Implement for skill-based matching, region-based, etc.
    /// </summary>
    public interface IMatchmaker
    {
        MatchmakingState State { get; }

        void StartMatchmaking(MatchmakingRequest request);
        void CancelMatchmaking();

        event Action<NetworkSession> OnMatchFound;
        event Action<float> OnMatchmakingProgress; // 0..1 search progress/time
        event Action<string> OnMatchmakingFailed;
    }

    public enum MatchmakingState : byte
    {
        Idle,
        Searching,
        Found,
        Joining,
        Failed
    }

    public sealed class MatchmakingRequest
    {
        public string GameMode { get; set; }
        public string Region { get; set; }
        public int TeamSize { get; set; } = 1;
        public float MaxWaitSeconds { get; set; } = 120f;
        public Dictionary<string, string> Preferences { get; set; }
        public float SkillRating { get; set; }
    }

    /// <summary>
    /// Host migration interface for P2P and listen-server games.
    /// When the current host disconnects, a new host is elected.
    /// Use cases: Monster Hunter, Pal World, co-op games.
    /// </summary>
    public interface IHostMigration
    {
        bool IsSupported { get; }
        INetConnection CurrentHost { get; }

        void StartHostMigration(INetConnection newHost);

        event Action OnHostMigrating;
        event Action<INetConnection> OnHostMigrated;
        event Action<string> OnHostMigrationFailed;
    }
}
