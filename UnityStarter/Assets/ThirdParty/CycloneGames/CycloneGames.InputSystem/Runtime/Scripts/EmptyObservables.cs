using R3;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Singleton empty observables to eliminate GC allocations from Observable.Empty calls.
    /// </summary>
    internal static class EmptyObservables
    {
        public static readonly Observable<Vector2> Vector2 = Observable.Empty<Vector2>();
        public static readonly Observable<Unit> Unit = Observable.Empty<Unit>();
        public static readonly Observable<float> Float = Observable.Empty<float>();
        public static readonly Observable<bool> Bool = Observable.Empty<bool>();
    }
}