using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Scene reference to the manager owned by the composition root. Presentation components read
    /// <see cref="Manager"/> instead of resolving a process-wide instance, so a scene can be opened
    /// in isolation and two managers can coexist in one process.
    /// </summary>
    public sealed class InputManagerProvider : MonoBehaviour
    {
        private InputManager _manager;

        /// <summary>Assigned manager, or null before <see cref="SetManager"/> runs.</summary>
        public InputManager Manager => _manager;

        /// <summary>Assigns the manager. Calling again replaces the reference.</summary>
        /// <param name="manager">Manager owned by the composition root.</param>
        public void SetManager(InputManager manager)
        {
            _manager = manager;
        }

        /// <summary>Clears the reference when the manager is disposed, so consumers fail closed.</summary>
        /// <param name="manager">Manager being disposed. A different instance is ignored.</param>
        public void ClearManager(InputManager manager)
        {
            if (ReferenceEquals(_manager, manager)) _manager = null;
        }
    }
}
