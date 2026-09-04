using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A resolved gameplay tag.
   /// </summary>
   /// <remarks>
   /// <para>
   /// A <see cref="GameplayTag"/> is a bare registry index with no reference payload. It is four bytes
   /// wide, copies like an integer, and every operation on it - equality, hashing, ordering - is a pure
   /// integer operation. There is no lazy name resolution, no hidden string comparison, and no path
   /// where reading a property can allocate or throw.
   /// </para>
   /// <para>
   /// An index is only meaningful against the registry that produced it. In ambient (non-DI) code that
   /// registry is <see cref="GameplayTagManager"/>; in DI code it is the <see cref="GameplayTagRegistry"/>
   /// instance you were handed. Mixing tags across registries is a programming error, exactly like using
   /// an index from one array on another.
   /// </para>
   /// <para>
   /// The durable identity of a tag is its name. Names live in the registry and in host persistence
   /// adapters, never inside the tag. This is what keeps the hot path free of string work and makes the
   /// value safe to replicate over the network, hand to a job, or reinterpret as native memory.
   /// </para>
   /// <para>
   /// This type is intentionally not directly serializable by any engine. Persist
   /// <see cref="GameplayTagContainer.ToPersisted"/> output through a host adapter instead; see
   /// <c>IGameplayTagHostPlatform</c>.
   /// </para>
   /// </remarks>
   [DebuggerDisplay("{DebuggerText,nq}")]
   public readonly struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
   {
      /// <summary>The empty tag. Its runtime index is 0.</summary>
      public static readonly GameplayTag None = default;

      private readonly int m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal GameplayTag(int runtimeIndex)
      {
         m_RuntimeIndex = runtimeIndex;
      }

      /// <summary>True when this tag carries a positive registry index.</summary>
      public bool IsValid
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => m_RuntimeIndex > 0;
      }

      /// <summary>True when this is <see cref="None"/>.</summary>
      public bool IsNone
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => m_RuntimeIndex == 0;
      }

      /// <summary>The registry index this tag resolves against. 0 is <see cref="None"/>.</summary>
      public int RuntimeIndex
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => m_RuntimeIndex;
      }

      /// <summary>
      /// The full dotted name of this tag, resolved against the ambient registry.
      /// Returns <see cref="string.Empty"/> for <see cref="None"/>.
      /// </summary>
      /// <remarks>
      /// Resolves against <see cref="GameplayTagManager"/>. In DI code call
      /// <see cref="GameplayTagRegistry.GetName"/> on the owning registry instead.
      /// </remarks>
      public string Name => GameplayTagManager.GetName(m_RuntimeIndex);

      /// <summary>
      /// The last dotted segment of <see cref="Name"/>, resolved against the ambient registry.
      /// </summary>
      public string Label => GameplayTagManager.GetLabel(m_RuntimeIndex);

      /// <summary>
      /// The authoring description of this tag, resolved against the ambient registry.
      /// Returns <see cref="string.Empty"/> when the tag has none.
      /// </summary>
      public string Description => GameplayTagManager.GetDescription(m_RuntimeIndex);

      /// <summary>
      /// The platform-stable 64-bit identifier of this tag, resolved against the ambient registry.
      /// Safe to replicate: it is derived from the ordinal name, never from <c>string.GetHashCode</c>.
      /// </summary>
      public ulong StableId => GameplayTagManager.GetStableId(m_RuntimeIndex);

      /// <summary>Authoring flags of this tag, resolved against the ambient registry.</summary>
      public GameplayTagFlags Flags => GameplayTagManager.GetFlags(m_RuntimeIndex);

      /// <summary>
      /// The number of segments in this tag's name, resolved against the ambient registry.
      /// A root tag is level 1. <see cref="None"/> is level 0.
      /// </summary>
      public int HierarchyLevel => GameplayTagManager.GetHierarchyLevel(m_RuntimeIndex);

      /// <summary>True when this tag has no direct children in the ambient registry.</summary>
      public bool IsLeaf => GameplayTagManager.IsLeaf(m_RuntimeIndex);

      /// <summary>
      /// The immediate parent of this tag in the ambient registry, or <see cref="None"/> for a root tag.
      /// </summary>
      public GameplayTag ParentTag => GameplayTagManager.GetParentTag(m_RuntimeIndex);

      internal string DebuggerText
      {
         get
         {
            if (m_RuntimeIndex == 0)
               return "<None>";

            string name = GameplayTagManager.GetName(m_RuntimeIndex);
            return string.IsNullOrEmpty(name) ? $"#{m_RuntimeIndex}" : name;
         }
      }

      /// <summary>True when this tag descends from <paramref name="ancestor"/> in the ambient registry.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool IsChildOf(in GameplayTag ancestor)
         => GameplayTagManager.IsChildOf(m_RuntimeIndex, ancestor.m_RuntimeIndex);

      /// <summary>True when <paramref name="descendant"/> descends from this tag in the ambient registry.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool IsParentOf(in GameplayTag descendant)
         => GameplayTagManager.IsChildOf(descendant.m_RuntimeIndex, m_RuntimeIndex);

      /// <summary>
      /// The number of leading hierarchy segments this tag shares with <paramref name="other"/>.
      /// "A.B.C" against "A.B.D" returns 2.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public int MatchesTagDepth(in GameplayTag other)
         => GameplayTagManager.MatchesTagDepth(m_RuntimeIndex, other.m_RuntimeIndex);

      /// <summary>
      /// Appends the strict ancestors of this tag to <paramref name="destination"/>, root first.
      /// The caller owns the buffer, so this never allocates.
      /// </summary>
      public void AppendAncestors(System.Collections.Generic.List<GameplayTag> destination)
         => GameplayTagManager.AppendAncestors(m_RuntimeIndex, destination);

      /// <summary>
      /// Appends the direct children of this tag to <paramref name="destination"/> in ascending index order.
      /// The caller owns the buffer, so this never allocates.
      /// </summary>
      public void AppendChildren(System.Collections.Generic.List<GameplayTag> destination)
         => GameplayTagManager.AppendChildren(m_RuntimeIndex, destination);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public bool Equals(GameplayTag other) => m_RuntimeIndex == other.m_RuntimeIndex;

      public override bool Equals(object obj) => obj is GameplayTag other && m_RuntimeIndex == other.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public override int GetHashCode() => m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public int CompareTo(GameplayTag other) => m_RuntimeIndex.CompareTo(other.m_RuntimeIndex);

      public override string ToString()
      {
         if (m_RuntimeIndex == 0)
            return "<None>";

         string name = GameplayTagManager.GetName(m_RuntimeIndex);
         return string.IsNullOrEmpty(name) ? $"#{m_RuntimeIndex}" : name;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator ==(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex == right.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator !=(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex != right.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator <(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex < right.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator <=(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex <= right.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator >(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex > right.m_RuntimeIndex;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool operator >=(GameplayTag left, GameplayTag right) => left.m_RuntimeIndex >= right.m_RuntimeIndex;
   }
}
