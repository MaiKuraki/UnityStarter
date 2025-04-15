using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Proxima
{
    internal class ProximaGameObjectCommands
    {
        [Serializable]
        internal class GameObjectInfo
        {
            public int Id;
            public string Name;
            public bool ActiveSelf;
            public int Parent;
            public int Depth;
            public int SiblingIndex;
            public int ChildCount;
            public int Order;
            public int Layer;
            public string Tag;
            public string Scene;
        }

        [Serializable]
        internal class GameObjectList
        {
            public List<GameObjectInfo> GameObjects = new List<GameObjectInfo>();
            public string[] Layers;
            public List<int> Destroyed = new List<int>();
        }

        private static Dictionary<int, GameObject> _idToGameObject;
        public static Dictionary<int, GameObject> IdToGameObject => _idToGameObject;
        private static List<GameObject> _rootGameObjects;
        private static GameObjectList _gameObjects;
        private static GameObjectList _changeList;
        private static Dictionary<int, GameObjectInfo> _gameObjectToInfo;
        private static int _lastUpdate;
        private static int _order;
        private static GameObject _lastUpdatedGameObject;
        private static int _lastDeleteCheckedGameObjectInfo = -1;
        private static Scene _dontDestroyOnLoadScene;
        private static HashSet<string> _pendingStreamIds;
        private static bool _showHidden;
        public static bool ShowHidden => _showHidden;

        [ProximaInitialize]
        public static void Init()
        {
            _idToGameObject = new Dictionary<int, GameObject>();
            _rootGameObjects = new List<GameObject>();
            _gameObjects = new GameObjectList();
            _gameObjects.Layers = Enumerable.Range(0, 31).Select(index => LayerMask.LayerToName(index)).ToArray();
            _changeList = new GameObjectList();
            _gameObjectToInfo = new Dictionary<int, GameObjectInfo>();
            _lastUpdate = -1;
            _pendingStreamIds = new HashSet<string>();

            var ddolGO = new GameObject("Proxima DDOL");
            GameObject.DontDestroyOnLoad(ddolGO);
            _dontDestroyOnLoadScene = ddolGO.scene;
            GameObject.Destroy(ddolGO);
        }

        [ProximaTeardown]
        public static void Teardown()
        {
            _idToGameObject = null;
            _rootGameObjects = null;
            _gameObjects = null;
            _changeList = null;
            _gameObjectToInfo = null;
            _lastUpdate = -1;
            _pendingStreamIds = null;
            _order = 0;
            _lastUpdatedGameObject = null;
            _lastDeleteCheckedGameObjectInfo = -1;
            _dontDestroyOnLoadScene = default;
        }

        [ProximaCommand("Internal")]
        public static void SetParent(int id, int parentId, int index)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                _idToGameObject.TryGetValue(parentId, out var parentGo);
                go.transform.SetParent(parentGo?.transform, true);
                go.transform.SetSiblingIndex(index);
            }
            else
            {
                Log.Warning($"SetParent: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void SetScene(int id, int sceneIndex, int index)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                if (sceneIndex < SceneManager.sceneCount)
                {
                    go.transform.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(go, SceneManager.GetSceneAt(sceneIndex));
                    go.transform.SetSiblingIndex(index);
                }
            }
            else
            {
                Log.Warning($"SetScene: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void SetActive(int id, bool active)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                go.SetActive(active);
            }
            else
            {
                Log.Warning($"SetActive: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void SetName(int id, string name)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                go.name = name;
            }
            else
            {
                Log.Warning($"SetName: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void SetLayer(int id, int layer)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                go.layer = layer;
            }
            else
            {
                Log.Warning($"SetLayer: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void SetTag(int id, string tag)
        {
            if (_idToGameObject.TryGetValue(id, out var go))
            {
                go.tag = tag;
            }
            else
            {
                Log.Warning($"SetTag: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void CreateGameObject(int parentId)
        {
            var go = new GameObject();
            if (_idToGameObject.TryGetValue(parentId, out var parentGo))
            {
                go.transform.SetParent(parentGo.transform, true);
            }
        }

        [ProximaCommand("Internal")]
        public static void DuplicateGameObject(int id)
        {
            if (!_idToGameObject.TryGetValue(id, out var go))
            {
                Log.Warning($"DuplicateGameObject: GameObject with id {id} not found");

            }

            GameObject.Instantiate(go, go.transform.parent);
        }

        [ProximaCommand("Internal")]
        public static void DestroyGameObject(int id)
        {
            if (_idToGameObject.TryGetValue(id, out var go) && go)
            {
                GameObject.Destroy(go);
            }
            else
            {
                Log.Warning($"DestroyGameObject: GameObject with id {id} not found");
            }
        }

        [ProximaCommand("Internal")]
        public static void AddGameObjectComponent(int id, string component)
        {
            if (!_idToGameObject.TryGetValue(id, out var go))
            {
                Log.Warning($"AddComponent: GameObject with id {id} not found");
                return;
            }

            var componentType = ProximaCommandHelpers.FindFirstComponentType(component);
            if (componentType == null)
            {
                throw new Exception($"Component type {component} not found.");
            }

            go.AddComponent(componentType);
        }

        [ProximaCommand("Internal")]
        public static void SetShowHidden(bool showHidden)
        {
            _showHidden = showHidden;
        }

        [ProximaStreamStart("GameObjects")]
        public static void StartStream(string id)
        {
            _pendingStreamIds.Add(id);
        }

        [ProximaStreamUpdate("GameObjects")]
        public static GameObjectList UpdateStream(string id)
        {
            UpdateInfoLists();

            if (_pendingStreamIds.Contains(id))
            {
                _pendingStreamIds.Remove(id);
                return _gameObjects;
            }

            bool changed = _changeList.Destroyed.Count > 0 || _changeList.GameObjects.Count > 0;
            return changed ? _changeList : null;
        }

        [ProximaStreamStop("GameObjects")]
        public static void StopStream(string id)
        {
            _pendingStreamIds.Remove(id);
        }

        public static void UpdateInfoLists()
        {
            if (_lastUpdate == Time.frameCount)
            {
                return;
            }

            _lastUpdate = Time.frameCount;
            _changeList.GameObjects.Clear();
            _changeList.Destroyed.Clear();

            if (!_lastUpdatedGameObject)
            {
                _lastUpdatedGameObject = null;
            }

            var updates = 0;
            while (updates <= ProximaInspector.MaxGameObjectUpdatesPerFrame && UpdateNextGameObjectInfo())
            {
                updates++;
            }

            var deleteUpdates = 0;
            while (deleteUpdates <= ProximaInspector.MaxGameObjectUpdatesPerFrame && UpdateNextDeletedGameObjectInfo())
            {
                deleteUpdates++;
            }
        }

        private static bool UpdateNextGameObjectInfo()
        {
            _lastUpdatedGameObject = GetNextGameObject(_lastUpdatedGameObject);
            if (_lastUpdatedGameObject != null)
            {
                UpdateGameObjectInfo(_lastUpdatedGameObject);
                return true;
            }

            return false;
        }

        private static bool ShouldHide(GameObject go)
        {
            return !_showHidden && go.hideFlags.HasFlag(HideFlags.HideInHierarchy);
        }

        private static GameObject GetNextChildGameObject(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (!ShouldHide(child.gameObject))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static GameObject GetNextSiblingGameObject(Transform parent, Transform t)
        {
            var nextSiblingIndex = t.GetSiblingIndex() + 1;
            while (nextSiblingIndex < parent.childCount)
            {
                var nextSibling = parent.GetChild(nextSiblingIndex);
                var go = nextSibling.gameObject;
                if (!ShouldHide(go))
                {
                    return go;
                }

                nextSiblingIndex++;
            }

            return null;
        }

        private static GameObject GetNextRootGameObject(GameObject go)
        {
            var rootIndex = _rootGameObjects.IndexOf(go);
            if (rootIndex >= 0)
            {
                return GetNextRootGameObject(rootIndex + 1);
            }

            return null;
        }

        private static GameObject GetNextRootGameObject(int rootIndex)
        {
            while (rootIndex < _rootGameObjects.Count)
            {
                var nextRoot = _rootGameObjects[rootIndex];
                if (nextRoot && !ShouldHide(nextRoot))
                {
                    return nextRoot;
                }

                rootIndex++;
            }

            return null;
        }

        private static GameObject GetNextSceneGameObject(Scene? currentScene)
        {
            var nextSceneIndex = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i) == currentScene)
                {
                    nextSceneIndex = i + 1;
                }
            }

            if (nextSceneIndex == 0)
            {
                _order = 0;
            }

            while (nextSceneIndex < SceneManager.sceneCount)
            {
                var nextScene = SceneManager.GetSceneAt(nextSceneIndex);
                if (nextScene.isLoaded)
                {
                    nextScene.GetRootGameObjects(_rootGameObjects);
                    var nextRoot = GetNextRootGameObject(0);
                    if (nextRoot != null)
                    {
                        return nextRoot;
                    }
                }

                nextSceneIndex++;
            }

            if (_dontDestroyOnLoadScene != null && currentScene != _dontDestroyOnLoadScene)
            {
                _dontDestroyOnLoadScene.GetRootGameObjects(_rootGameObjects);
                var nextRoot = GetNextRootGameObject(0);
                if (nextRoot != null)
                {
                    return nextRoot;
                }
            }

            return null;
        }

        private static GameObject GetNextGameObject(GameObject go)
        {
            if (go == null)
            {
                return GetNextSceneGameObject(null);
            }

            _order++;

            var t = go.transform;
            var nextChild = GetNextChildGameObject(t);
            if (nextChild != null)
            {
                return nextChild;
            }

            var parent = t.parent;
            while (parent != null)
            {
                var nextSibling = GetNextSiblingGameObject(parent, t);
                if (nextSibling != null)
                {
                    return nextSibling;
                }

                t = parent;
                parent = t.parent;
            }

            go = t.gameObject;
            go.scene.GetRootGameObjects(_rootGameObjects);
            var nextRoot = GetNextRootGameObject(go);
            if (nextRoot != null)
            {
                return nextRoot;
            }

            return GetNextSceneGameObject(go.scene);
        }

        private static bool FastStringEqual(string lhs, string rhs)
        {
            return lhs.Length == rhs.Length && lhs[0] == rhs[0] && lhs[lhs.Length - 1] == rhs[rhs.Length - 1];
        }

        private static void UpdateGameObjectInfo(GameObject go)
        {
            var id = go.GetInstanceID();
            var parentId = go.transform.parent ? go.transform.parent.gameObject.GetInstanceID() : 0;
            var siblingIndex = go.transform.GetSiblingIndex();

            int depth = 0;
            if (go.transform.parent)
            {
                if (_gameObjectToInfo.TryGetValue(parentId, out var parentGoi))
                {
                    depth = parentGoi.Depth + 1;
                }
            }

            if (!_gameObjectToInfo.TryGetValue(id, out var goi))
            {
                goi = new GameObjectInfo {
                    Id = id,
                    Name = go.name,
                    ActiveSelf = go.activeSelf,
                    Parent = parentId,
                    Depth = depth,
                    SiblingIndex = siblingIndex,
                    ChildCount = go.transform.childCount,
                    Order = _order,
                    Tag = go.tag,
                    Layer = go.layer,
                    Scene = depth == 0 ? go.scene.name + "#" + go.scene.handle : null
                };

                _gameObjects.GameObjects.Add(goi);
                _gameObjectToInfo.Add(id, goi);
                _idToGameObject.Add(id, go);
                _changeList.GameObjects.Add(goi);
            }
            else
            {
                if (!FastStringEqual(goi.Name, go.name) ||
                    goi.ActiveSelf != go.activeSelf ||
                    goi.Parent != parentId ||
                    goi.Depth != depth ||
                    goi.SiblingIndex != siblingIndex ||
                    goi.ChildCount != go.transform.childCount ||
                    goi.Order != _order ||
                    !FastStringEqual(goi.Tag, go.tag) ||
                    goi.Layer != go.layer)
                {
                    goi.Name = go.name;
                    goi.ActiveSelf = go.activeSelf;
                    goi.Parent = parentId;
                    goi.Depth = depth;
                    goi.SiblingIndex = siblingIndex;
                    goi.ChildCount = go.transform.childCount;
                    goi.Order = _order;
                    goi.Tag = go.tag;
                    goi.Layer = go.layer;
                    goi.Scene = depth == 0 ? go.scene.name + "#" + go.scene.handle : null;
                    _changeList.GameObjects.Add(goi);
                }
            }
        }

        private static bool UpdateNextDeletedGameObjectInfo()
        {
            if (_gameObjects.GameObjects.Count == 0)
            {
                return false;
            }

            _lastDeleteCheckedGameObjectInfo = (_lastDeleteCheckedGameObjectInfo + 1) % _gameObjects.GameObjects.Count;
            var goi = _gameObjects.GameObjects[_lastDeleteCheckedGameObjectInfo];
            var go = _idToGameObject[goi.Id];
            if (!go || ShouldHide(go))
            {
                _gameObjectToInfo.Remove(goi.Id);
                _gameObjects.GameObjects.RemoveAt(_lastDeleteCheckedGameObjectInfo);
                _changeList.Destroyed.Add(goi.Id);
                _idToGameObject.Remove(goi.Id);
            }

            return true;
        }
    }
}