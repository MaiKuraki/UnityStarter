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
         get => GetResolvedRuntimeIndex() >= 0;
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
         int runtimeIndex = GetResolvedRuntimeIndex();
         int otherRuntimeIndex = other.GetResolvedRuntimeIndex();
         if (runtimeIndex != otherRuntimeIndex)
            return false;

         return runtimeIndex >= 0 || string.Equals(m_Name, other.m_Name, StringComparison.Ordinal);
      }

      public override readonly bool Equals(object obj)
      {
         return obj is GameplayTag other && Equals(other);
      }

      public override readonly int GetHashCode()
      {
         int runtimeIndex = GetResolvedRuntimeIndex();
         return runtimeIndex >= 0 ? runtimeIndex : HashCode.Combine(runtimeIndex, m_Name);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public readonly int CompareTo(GameplayTag other)
      {
         return GetResolvedRuntimeIndex().CompareTo(other.GetResolvedRuntimeIndex());
      }

      public override readonly string ToString()
      {
         if (IsNone)
            return "<None>";

         return IsValid ? GameplayTagManager.GetTagName(GetResolvedRuntimeIndex()) : m_Name;
      }

      /// <summary>
      /// Resolves the runtime index from m_Name if m_RuntimeIndex is unset.
      /// Uses Unsafe.AsRef to cache the resolved index back into the struct field,
      /// which works when this struct lives in an array/field (not a stack copy).
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private readonly int GetResolvedRuntimeIndex()
      {
         int idx = m_RuntimeIndex;
         if (idx != 0) return idx;

         if (string.IsNullOrEmpty(m_Name))
            return 0;

         TagDataSnapshot snap = GameplayTagManager.Snapshot;
         if (snap != null && snap.TryGetRuntimeIndex(m_Name, out int resolved))
         {
            // Write back to cache — works when this struct is in an array/field,
            // harmless no-op on stack copies. No 'unsafe' keyword required.
            Unsafe.AsRef(in m_RuntimeIndex) = resolved;
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
