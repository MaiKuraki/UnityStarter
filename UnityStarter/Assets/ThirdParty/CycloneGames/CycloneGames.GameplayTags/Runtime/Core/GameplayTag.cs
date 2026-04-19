using System;
using System.Diagnostics;

namespace CycloneGames.GameplayTags.Runtime
{
   [Serializable]
   [DebuggerDisplay("{m_Name,nq}")]
   public struct GameplayTag : IEquatable<GameplayTag>
   {
      public static readonly GameplayTag None = new() { m_RuntimeIndex = 0, m_Name = null };

      public readonly bool IsNone => GetResolvedRuntimeIndex() == 0 && string.IsNullOrEmpty(m_Name);

      public readonly bool IsValid => GetResolvedRuntimeIndex() >= 0;

      public readonly bool IsLeaf => !IsNone && GameplayTagManager.IsLeafTag(GetResolvedRuntimeIndex());

      public readonly int RuntimeIndex => GetResolvedRuntimeIndex();

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
         get
         {
            int parentRuntimeIndex = GameplayTagManager.GetParentRuntimeIndex(GetResolvedRuntimeIndex());
            if (parentRuntimeIndex <= 0)
               return None;

            return GameplayTagManager.GetTagFromRuntimeIndex(parentRuntimeIndex);
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

      public readonly bool IsParentOf(in GameplayTag tag)
      {
         ValidateIsNotNone();
         return GameplayTagManager.IsParentOf(GetResolvedRuntimeIndex(), tag.GetResolvedRuntimeIndex());
      }

      public readonly bool IsChildOf(in GameplayTag parentTag)
      {
         ValidateIsNotNone();
         return GameplayTagManager.IsChildOf(GetResolvedRuntimeIndex(), parentTag.GetResolvedRuntimeIndex());
      }

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

      public override readonly string ToString()
      {
         if (IsNone)
            return "<None>";

         return IsValid ? GameplayTagManager.GetTagName(GetResolvedRuntimeIndex()) : m_Name;
      }

      private readonly int GetResolvedRuntimeIndex()
      {
         if (m_RuntimeIndex != 0)
         {
            return m_RuntimeIndex;
         }

         if (string.IsNullOrEmpty(m_Name))
         {
            return 0;
         }

         if (GameplayTagManager.TryRequestTag(m_Name, out GameplayTag resolvedTag))
         {
            return resolvedTag.m_RuntimeIndex;
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
