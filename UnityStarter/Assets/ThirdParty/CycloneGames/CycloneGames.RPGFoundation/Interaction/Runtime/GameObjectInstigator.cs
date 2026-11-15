using UnityEngine;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.Compat;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    /// <summary>
    /// Built-in <see cref="InstigatorHandle"/> for MonoBehaviour-based games. Wraps a
    /// <see cref="UnityEngine.GameObject"/> and uses its instance id as identity. Cache one instance
    /// per player; constructing per interaction allocates.
    /// </summary>
    public sealed class GameObjectInstigator : InstigatorHandle
    {
        private readonly ulong _stableId;

        public GameObject GameObject { get; }

        public override int Id => GameObject != null ? unchecked((int)UnityObjectId.Of(GameObject)) : 0;

        public override ulong StableId => _stableId != InteractionStableId.None ? _stableId : base.StableId;

        public GameObjectInstigator(GameObject gameObject, ulong stableId = InteractionStableId.None)
        {
            GameObject = gameObject;
            _stableId = stableId;
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
