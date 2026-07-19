using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
   [Serializable]
   [DebuggerDisplay("{m_Name,nq}")]
   public struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
   {
      public static readonly GameplayTag None = new() { m_RuntimeIndex = 0, m_Name = null };

      public readonly bool IsNone
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => GetResolvedRuntimeIndex() == 0 && string.IsNullOrEmpty(m_Name);
      }

      public readonly bool IsValid
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => GetResolvedRuntimeIndex() > 0;
      }

      public readonly bool IsLeaf
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => !IsNone && GameplayTagManager.IsLeafTag(GetResolvedRuntimeIndex());
      }

      public readonly int RuntimeIndex
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => GetResolvedRuntimeIndex();
      }

      public readonly ulong StableId
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => GameplayTagManager.GetStableIdFromRuntimeIndex(GetResolvedRuntimeIndex());
      }

      public readonly GameplayTagDefinition Definition
      {
         get
         {
            ValidateIsNotNone();
            int runtimeIndex = GetResolvedRuntimeIndex();
            return runtimeIndex >= 0
               ? GameplayTagManager.GetDefinitionFromRuntimeIndex(runtimeIndex)
               : GameplayTagDefinition.CreateInvalidDefinition(m_Name);
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> ParentTags => GameplayTagManager.GetParentTagsSpan(GetResolvedRuntimeIndex());

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> ChildTags => GameplayTagManager.GetChildTagsSpan(GetResolvedRuntimeIndex());

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly ReadOnlySpan<GameplayTag> HierarchyTags => GameplayTagManager.GetHierarchyTagsSpan(GetResolvedRuntimeIndex());

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly string Label => GameplayTagManager.GetTagLabel(GetResolvedRuntimeIndex());

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public readonly int HierarchyLevel => GameplayTagManager.GetTagHierarchyLevel(GetResolvedRuntimeIndex());

      public readonly string Description => IsValid && !IsNone ? GameplayTagManager.GetTagDescription(GetResolvedRuntimeIndex()) : "Invalid Tag";

      public readonly GameplayTag ParentTag
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get
         {
            int parentRuntimeIndex = GameplayTagManager.GetParentRuntimeIndex(GetResolvedRuntimeIndex());
            return parentRuntimeIndex <= 0 ? None : GameplayTagManager.GetTagFromRuntimeIndex(parentRuntimeIndex);
         }
      }

      public readonly GameplayTagFlags Flags => IsValid && !IsNone ? GameplayTagManager.GetTagFlags(GetResolvedRuntimeIndex()) : GameplayTagFlags.None;

      public readonly string Name
      {
         get
         {
            ValidateIsNotNone();
            return IsValid ? GameplayTagManager.GetTagName(GetResolvedRuntimeIndex()) : m_Name;
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public string m_Name;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private int m_RuntimeIndex;

      internal readonly int CachedRuntimeIndex
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => m_RuntimeIndex;
      }

      internal GameplayTag(GameplayTagDefinition definition)
      {
         definition ??= GameplayTagDefinition.NoneTagDefinition;
         m_RuntimeIndex = definition.RuntimeIndex;
         m_Name = definition.IsNone() ? null : definition.TagName;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool IsParentOf(in GameplayTag tag)
      {
         ValidateIsNotNone();
         return GameplayTagManager.IsParentOf(GetResolvedRuntimeIndex(), tag.GetResolvedRuntimeIndex());
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool IsChildOf(in GameplayTag parentTag)
      {
         ValidateIsNotNone();
         return GameplayTagManager.IsChildOf(GetResolvedRuntimeIndex(), parentTag.GetResolvedRuntimeIndex());
      }

      /// <summary>
      /// Returns the number of matching hierarchy levels between this tag and another.
      /// E.g., "A.B.C" vs "A.B.D" returns 2 (matching "A" and "A.B").
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly int MatchesTagDepth(in GameplayTag other)
      {
         return GameplayTagManager.MatchesTagDepth(this, other);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly bool Equals(GameplayTag other)
      {
         if (!string.IsNullOrEmpty(m_Name) || !string.IsNullOrEmpty(other.m_Name))
            return string.Equals(m_Name, other.m_Name, StringComparison.Ordinal);

         return GetResolvedRuntimeIndex() == other.GetResolvedRuntimeIndex();
      }

      public override readonly bool Equals(object obj)
      {
         return obj is GameplayTag other && Equals(other);
      }

      public override readonly int GetHashCode()
      {
         return !string.IsNullOrEmpty(m_Name)
            ? StringComparer.Ordinal.GetHashCode(m_Name)
            : GetResolvedRuntimeIndex();
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly int CompareTo(GameplayTag other)
      {
         if (!string.IsNullOrEmpty(m_Name) || !string.IsNullOrEmpty(other.m_Name))
            return string.Compare(m_Name, other.m_Name, StringComparison.Ordinal);

         return GetResolvedRuntimeIndex().CompareTo(other.GetResolvedRuntimeIndex());
      }

      public override readonly string ToString()
      {
         if (IsNone)
            return "<None>";

         return IsValid ? GameplayTagManager.GetTagName(GetResolvedRuntimeIndex()) : m_Name;
      }

      /// <summary>
      /// Resolves the current runtime index from the stable tag name.
      /// Runtime indices are snapshot-local, so cached indices are validated before use.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private readonly int GetResolvedRuntimeIndex()
      {
         return GetResolvedRuntimeIndex(GameplayTagManager.Snapshot);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal readonly int GetResolvedRuntimeIndex(TagDataSnapshot snap)
      {
         if (string.IsNullOrEmpty(m_Name))
            return 0;
         if (snap == null)
            throw new ArgumentNullException(nameof(snap));

         int idx = m_RuntimeIndex;
         if (idx > 0 && (uint)idx < (uint)snap.TagNames.Length &&
             string.Equals(snap.TagNames[idx], m_Name, StringComparison.Ordinal))
         {
            return idx;
         }

         if (snap.TryGetRuntimeIndex(m_Name, out int resolved))
         {
            return resolved;
         }

         return -1;
      }

      [Conditional("DEBUG")]
      private readonly void ValidateIsNotNone()
      {
         if (IsNone)
            throw new InvalidOperationException("Cannot perform operation on GameplayTag.None.");
      }

      [Conditional("DEBUG")]
      internal readonly void ValidateIsValid()
      {
         if (IsNone)
            throw new InvalidOperationException("Cannot perform operation on GameplayTag.None.");

         if (!IsValid)
            throw new InvalidOperationException($"GameplayTag \"{m_Name}\" is not valid.");
      }

      public static implicit operator GameplayTag(string tagName)
      {
         return GameplayTagManager.RequestTag(tagName);
      }

      public static bool operator ==(in GameplayTag lhs, in GameplayTag rhs)
      {
         return lhs.Equals(rhs);
      }

      public static bool operator !=(in GameplayTag lhs, in GameplayTag rhs)
      {
         return !lhs.Equals(rhs);
      }
   }
}
