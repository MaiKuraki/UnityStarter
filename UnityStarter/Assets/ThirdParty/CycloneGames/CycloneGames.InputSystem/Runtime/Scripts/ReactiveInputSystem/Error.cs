using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class Error
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ArgumentNullException<T>(T value)
        {
            if (value == null) throw new ArgumentNullException();
            if (value is UnityEngine.Object unityObject && unityObject == null) throw new ArgumentNullException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ArgumentOutOfRangeException(bool condition)
        {
            if (condition) throw new ArgumentOutOfRangeException();
        }
    }
}
