using System;
using R3;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Context stack of one player: which mapping contexts are active, in which order, with which policy, and
    /// which observables are bound to them.
    /// </summary>
    public interface IInputContextController
    {
        /// <summary>Name of the top active context, or null when the stack is empty.</summary>
        ReadOnlyReactiveProperty<string> ActiveContextName { get; }

        /// <summary>Raised after the active set changes and the projection is applied.</summary>
        event Action<string> OnContextChanged;

        /// <summary>Pushes a context, moving it to the top when it is already on the stack.</summary>
        void PushContext(InputContext context);

        /// <summary>
        /// Captures a context above the stack. The returned scope releases the capture and may be disposed once.
        /// </summary>
        IDisposable CaptureContext(InputContext context);

        /// <summary>Removes a context from the stack and from any capture.</summary>
        bool RemoveContext(InputContext context);

        /// <summary>Pops the top context. Does nothing when the stack is empty.</summary>
        void PopContext();

        /// <summary>Recomputes the active set without changing the stack.</summary>
        void RefreshActiveContext();

        /// <summary>Overrides priority and blocking for one player. No-op when the values already match.</summary>
        bool SetContextPolicy(InputContext context, int priority, bool blocksLowerPriority);

        /// <summary>Removes a per-player policy override, restoring the configured values.</summary>
        bool ClearContextPolicy(InputContext context);

        /// <summary>Removes a bound observable from a context without touching its action bindings.</summary>
        bool RemoveBindingFromContext(InputContext context, Observable<Unit> source);

        /// <summary>Removes a bound observable from a context without touching its action bindings.</summary>
        bool RemoveBindingFromContext(InputContext context, Observable<Vector2> source);

        /// <summary>Removes a bound observable from a context without touching its action bindings.</summary>
        bool RemoveBindingFromContext(InputContext context, Observable<float> source);

        /// <summary>Removes a bound observable from a context without touching its action bindings.</summary>
        bool RemoveBindingFromContext(InputContext context, Observable<bool> source);

        /// <summary>Stops all dispatch for this player until the matching <see cref="UnblockInput"/>.</summary>
        void BlockInput();

        /// <summary>Blocks input and releases the block when the scope is disposed.</summary>
        IDisposable BlockInputScope();

        /// <summary>Releases one block level. Dispatch resumes at zero.</summary>
        void UnblockInput();
    }
}
