using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A zero-allocation enumerator over the runtime indices of a tag container.
   /// </summary>
   /// <remarks>
   /// The enumerator holds a reference to the container's live storage. Mutating the container while
   /// enumerating is undefined; copy the container or drain it into a list when a stable view is needed.
   /// </remarks>
   [DebuggerDisplay("Count = {m_Count}")]
   public struct GameplayTagEnumerator : IEnumerator<GameplayTag>, IEnumerable<GameplayTag>
   {
      private readonly int[] m_Indices;
      private readonly int m_Count;
      private int m_Position;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal GameplayTagEnumerator(int[] indices, int count)
      {
         m_Indices = indices;
         m_Count = count;
         m_Position = -1;
      }

      public readonly GameplayTag Current
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => new(m_Indices[m_Position]);
      }

      readonly object IEnumerator.Current => Current;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool MoveNext() => ++m_Position < m_Count;

      public void Reset() => m_Position = -1;

      public readonly void Dispose()
      {
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly GameplayTagEnumerator GetEnumerator() => this;

      readonly IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => this;

      readonly IEnumerator IEnumerable.GetEnumerator() => this;
   }
}
