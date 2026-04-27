using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Scene
{
    /// <summary>
    /// Manages networked scene loading for instanced content.
    /// Supports additive scenes (dungeons, zones), main scene transitions,
    /// and per-connection scene assignment (instanced content for MMOs, private lobbies).
    /// </summary>
    public interface INetworkSceneManager
    {
        string CurrentScene { get; }
        IReadOnlyList<string> LoadedAdditiveScenes { get; }

        void ServerLoadScene(string sceneName, bool additive = false);
        void ServerUnloadScene(string sceneName);

        /// <summary>
        /// Load a scene only for specific connections (instances, private rooms).
        /// </summary>
        void ServerLoadSceneForConnections(string sceneName, IReadOnlyList<INetConnection> connections);

        event Action<string, bool> OnSceneLoading;
        event Action<string> OnSceneLoaded;
        event Action<string> OnSceneUnloaded;
    }

    /// <summary>
    /// Default implementation using Unity SceneManager under the hood.
    /// </summary>
    public sealed class NetworkSceneManager : INetworkSceneManager
    {
        private string _currentScene;
        private readonly List<string> _additiveScenes = new List<string>();

        // connectionId -> list of scene names loaded for that connection
        private readonly Dictionary<int, List<string>> _perConnectionScenes =
            new Dictionary<int, List<string>>();

        public string CurrentScene => _currentScene;
        public IReadOnlyList<string> LoadedAdditiveScenes => _additiveScenes;

        public event Action<string, bool> OnSceneLoading;
        public event Action<string> OnSceneLoaded;
        public event Action<string> OnSceneUnloaded;

        public void ServerLoadScene(string sceneName, bool additive = false)
        {
            OnSceneLoading?.Invoke(sceneName, additive);

            if (additive)
            {
                if (!_additiveScenes.Contains(sceneName))
                    _additiveScenes.Add(sceneName);
            }
            else
            {
                _currentScene = sceneName;
            }

            // Actual scene load will be done via Unity SceneManager by the caller
            OnSceneLoaded?.Invoke(sceneName);
        }

        public void ServerUnloadScene(string sceneName)
        {
            _additiveScenes.Remove(sceneName);
            OnSceneUnloaded?.Invoke(sceneName);
        }

        public void ServerLoadSceneForConnections(string sceneName, IReadOnlyList<INetConnection> connections)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                int connId = connections[i].ConnectionId;
                if (!_perConnectionScenes.TryGetValue(connId, out var scenes))
                {
                    scenes = new List<string>();
                    _perConnectionScenes[connId] = scenes;
                }
                if (!scenes.Contains(sceneName))
                    scenes.Add(sceneName);
            }

            OnSceneLoading?.Invoke(sceneName, true);
            OnSceneLoaded?.Invoke(sceneName);
        }

        /// <summary>
        /// Get scenes loaded for a specific connection.
        /// </summary>
        public IReadOnlyList<string> GetScenesForConnection(int connectionId)
        {
            return _perConnectionScenes.TryGetValue(connectionId, out var scenes)
                ? scenes : Array.Empty<string>();
        }

        public void RemoveConnection(int connectionId)
        {
            if (!_perConnectionScenes.TryGetValue(connectionId, out var scenes))
                return;

            // Collect scenes that are no longer needed by any connection
            for (int i = scenes.Count - 1; i >= 0; i--)
            {
                string scene = scenes[i];
                bool stillNeeded = false;
                foreach (var pair in _perConnectionScenes)
                {
                    if (pair.Key == connectionId) continue;
                    if (pair.Value.Contains(scene))
                    {
                        stillNeeded = true;
                        break;
                    }
                }

                if (!stillNeeded)
                {
                    ServerUnloadScene(scene);
                }
            }

            _perConnectionScenes.Remove(connectionId);
        }
    }
}
