using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Built-in <see cref="InstigatorHandle"/> for MonoBehaviour-based games.
    /// Wraps a <see cref="UnityEngine.GameObject"/> and uses its InstanceID as identity.
    /// <para/>
    /// Cache one instance per detector/player to avoid per-interaction allocation:
    /// <code>
    /// private GameObjectInstigator _cachedInstigator;
    /// void Awake() => _cachedInstigator = new GameObjectInstigator(gameObject);
    /// </code>
    /// </summary>
    public sealed class GameObjectInstigator : InstigatorHandle
    {
        public GameObject GameObject { get; }

        public override int Id => GameObject.GetInstanceID();

        public GameObjectInstigator(GameObject gameObject)
        {
            GameObject = gameObject;
        }

        public override bool TryGetPosition(out Vector3 position)
        {
            if (GameObject == null) { position = default; return false; }
            position = GameObject.transform.position;
            return true;
        }

        /// <summary>Convenience wrapper for <see cref="GameObject.GetComponent{T}"/>.</summary>
        public T GetComponent<T>() => GameObject.GetComponent<T>();
    }
}
