using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace CycloneGames.GameplayTags
{
public struct GameplayTagContainerIndices
   {
      public readonly bool IsCreated => Explicit != null && Implicit != null;
      public readonly bool IsEmpty => !IsCreated || Explicit.Count == 0;
      public readonly int TagCount => IsCreated ? Implicit.Count : 0;
      public readonly int ExplicitTagCount => IsCreated ? Explicit.Count : 0;

      internal List<int> Explicit { get; private set; }
      internal List<int> Implicit { get; private set; }

      public static void Create(ref GameplayTagContainerIndices indices)
      {
         if (indices.IsCreated)
            return;

         indices = new GameplayTagContainerIndices()
         {
            Explicit = new(),
            Implicit = new()
         };
      }

      public static GameplayTagContainerIndices Create()
      {
         return new GameplayTagContainerIndices()
         {
            Explicit = new(),
            Implicit = new()
         };
      }

      internal readonly void Clear()
      {
         Explicit?.Clear();
         Implicit?.Clear();
      }

      internal readonly void CopyTo(in GameplayTagContainerIndices other)
      {
         other.Clear();
         other.Explicit.AddRange(this.Explicit);
         other.Implicit.AddRange(this.Implicit);
      }
   }

   public interface IGameplayTagContainer : IEnumerable<GameplayTag>
   {
      /// <summary>
      /// Gets a value indicating whether this container is empty.
      /// </summary>
      public bool IsEmpty { get; }

      /// <summary>
      /// Gets the count of explicit tags in this container.
      /// Explicit tags are the tags that have been directly added to this container.
      /// </summary>
      public int ExplicitTagCount { get; }

      /// <summary>
      /// Gets the total count of tags in this container, including implicit tags.
      /// Implicit tags are tags that are indirectly included based on the hierarchy of explicit tags.
      /// For example, if "ParentTag" is an implicit tag and "ParentTag.ChildTag" is an explicit tag,
      /// then "ParentTag" will be counted as an implicit tag in this container.
      /// </summary>
      public int TagCount { get; }

      /// <summary>
      /// Gets the indeces of tags in this container.
      /// </summary>
      GameplayTagContainerIndices Indices { get; }

      /// <summary>
      /// Adds a tag to this container.
      /// </summary>
      /// <param name="gameplayTag">The tag to add.</param>
      public void AddTag(GameplayTag gameplayTag);

      /// <summary>
      /// Removes a tag from this container.
      /// </summary>
      /// <param name="gameplayTag">The tag to remove.</param>
      public void RemoveTag(GameplayTag gameplayTag);

      /// <summary>
      /// Gets an enumerator for all tags in this container.
      /// </summary>
      /// <returns>An enumerator for all tags in this container.</returns>
      public GameplayTagEnumerator GetTags();

      /// <summary>
      /// Gets an enumerator for the explicit tags in this container.
      /// </summary>
      /// <returns>An enumerator for the explicit tags in this container.</returns>
      public GameplayTagEnumerator GetExplicitTags();

      /// <summary>
      /// Adds tags from another container to this container.
      /// </summary>
      /// <typeparam name="T">The type of the other container.</typeparam>
      /// <param name="other">The other container.</param>
      public void AddTags<T>(in T other) where T : IGameplayTagContainer;

      /// <summary>
      /// Gets the parent tags of a tag in this container.
      /// </summary>
      /// <param name="tag">The tag to get the parent tags of.</param>
      /// <param name="parentTags">The list to populate with the parent tags.</param>
      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags);

      /// <summary>
      /// Gets the child tags of a tag in this container.
      /// </summary>
      /// <param name="tag">The tag to get the child tags of.</param>
      /// <param name="childTags">The list to populate with the child tags.</param>
      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags);

      /// <summary>
      /// Gets the explicit parent tags of a tag in this container.
      /// </summary>
      /// <param name="tag">The tag to get the explicit parent tags of.</param>
      ///<param name = "parentTags" > The list to populate with the explicit parent tags.</param>
      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags);

      /// <summary>
      /// Gets the explicit child tags of a tag in this container.
      /// </summary>
      /// <param name="tag"></param>
      /// <param name="childTags"></param>
      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags);

      /// <summary>
      /// Removes tags from this container that are present in another container.
      /// </summary>
      /// <typeparam name="T">The type of the other container.</typeparam>
      /// <param name="other">The other container.</param>
      public void RemoveTags<T>(in T other) where T : IGameplayTagContainer;

      /// <summary>
      /// Clears all tags from this container.
      /// </summary>
      public void Clear();
   }

   [Serializable]
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   public class GameplayTagContainer : IGameplayTagContainer, ISerializationCallbackReceiver, IEnumerable<GameplayTag>
   {
      public static GameplayTagContainer Empty { get; } = new();

      /// <inheritdoc />
      public bool IsEmpty => m_Indices.IsEmpty;

      /// <inheritdoc />
      public int ExplicitTagCount => m_Indices.ExplicitTagCount;

      /// <inheritdoc />
      public int TagCount => m_Indices.TagCount;

      /// <inheritdoc />
      public GameplayTagContainerIndices Indices => m_Indices;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "It's used for debugging")]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

      [SerializeField]
      private List<string> m_SerializedExplicitTags;

      private GameplayTagContainerIndices m_Indices = new();

      /// <summary>
      /// Default constructor.
      /// </summary>
      public GameplayTagContainer()
      { }

      /// <summary>
      /// Initializes a new instance of the <see cref="GameplayTagContainer"/> class by copying tags from another container.
      /// </summary>
      public GameplayTagContainer(IGameplayTagContainer other)
      {
         Copy(this, other);
      }

      /// <summary>
      /// Creates a clone of this container.
      /// </summary>
      public GameplayTagContainer Clone()
      {
         GameplayTagContainer clone = new();
         Copy(clone, this);

         return clone;
      }

      /// <summary>
      /// Copies tags from one container to another.
      /// </summary>
      /// <param name="dest">The destination container.</param>
      /// <param name="src">The source container.</param>
      public static void Copy<T>(GameplayTagContainer dest, in T src) where T : IGameplayTagContainer
      {
         if (src.IsEmpty)
            return;

         GameplayTagContainerIndices.Create(ref dest.m_Indices);
         src.Indices.CopyTo(dest.m_Indices);
      }

      /// <summary>
      /// Creates a container that is the intersection of two other containers.
      /// </summary>
      /// <typeparam name="T">The type of the first container.</typeparam>
      /// <typeparam name="U">The type of the second container.</typeparam>
      /// <param name="lhs">The first container.</param>
      /// <param name="rhs">The second container.</param>
      /// <returns>A new <see cref="GameplayTagContainer"/> that contains the intersection of the two containers.</returns>
      public static GameplayTagContainer Intersection<T, U>(in T lhs, in U rhs) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         GameplayTagContainer intersection = new();
         intersection.AddIntersection(lhs, rhs);
         return intersection;
      }

      public static void Intersection<T, U>(GameplayTagContainer output, in T lhs, in U rhs) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         if (output == null)
            throw new ArgumentNullException(nameof(output));

         if (!output.IsEmpty)
            throw new ArgumentException("Output container must be empty.", nameof(output));

         output.AddIntersection(lhs, rhs);
      }

      /// <summary>
      /// Adds the intersection of two containers to this container.
      /// </summary>
      /// <typeparam name="T">The type of the first container.</typeparam>
      /// <typeparam name="U">The type of the second container.</typeparam>
      /// <param name="lhs">The first container.</param>
      /// <param name="rhs">The second container.</param>
      internal void AddIntersection<T, U>(in T lhs, in U rhs) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         static void OrderedListIntersection(List<int> a, List<int> b, List<int> dst)
         {
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
               int aElement = a[i], bElement = b[j];
               if (aElement == bElement)
               {
                  dst.Add(aElement);
                  i++;
                  j++;
                  continue;
               }

               if (aElement < bElement)
               {
                  i++;
                  continue;
               }

               j++;
            }
         }

         if (lhs.IsEmpty || rhs.IsEmpty)
            return;

         if (!m_Indices.IsCreated)
            m_Indices = GameplayTagContainerIndices.Create();

         OrderedListIntersection(lhs.Indices.Explicit, rhs.Indices.Explicit, m_Indices.Explicit);
         OrderedListIntersection(lhs.Indices.Implicit, rhs.Indices.Implicit, m_Indices.Implicit);
      }

      /// <summary>
      /// Creates a container that is the union of two other containers.
      /// </summary>
      /// <typeparam name="T">The type of the first container.</typeparam>
      /// <typeparam name="U">The type of the second container.</typeparam>
      /// <param name="lhs">The first container.</param>
      /// <param name="rhs">The second container.</param>
      /// <returns>A new <see cref="GameplayTagContainer"/> that contains the union of the two containers.</returns>
      public static GameplayTagContainer Union<T, U>(in T lhs, in U rhs) where T : IGameplayTagContainer where U : IGameplayTagContainer
      {
         static void OrderedListUnion(List<int> a, List<int> b, List<int> dst)
         {
            dst.Capacity = Mathf.Max(dst.Capacity, a.Count + b.Count);

            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
               int aElement = a[i], bElement = b[j];
               if (aElement == bElement)
               {
                  dst.Add(aElement);
                  i++;
                  j++;
                  continue;
               }

               if (aElement < bElement)
               {
                  dst.Add(aElement);
                  i++;
                  continue;
               }

               dst.Add(bElement);
               j++;
            }

            for (; i < a.Count; i++)
               dst.Add(a[i]);

            for (; j < b.Count; j++)
               dst.Add(b[j]);
         }

         GameplayTagContainer union = new();
         GameplayTagContainerIndices.Create(ref union.m_Indices);

         if (lhs.IsEmpty && rhs.IsEmpty)
            return union;

         if (lhs.IsEmpty)
            return new GameplayTagContainer(rhs);

         if (rhs.IsEmpty)
            new GameplayTagContainer(lhs);

         OrderedListUnion(lhs.Indices.Explicit, rhs.Indices.Explicit, union.m_Indices.Explicit);
         OrderedListUnion(lhs.Indices.Implicit, rhs.Indices.Implicit, union.m_Indices.Implicit);

         return union;
      }

      /// <summary>
      /// Compares the explicit tags between this instance and another <see cref="IGameplayTagContainer"/> instance.
      /// It populates the lists of added and removed tags based on the comparison.
      /// </summary>
      /// <typeparam name="T">Type that implements <see cref="IGameplayTagContainer"/>.</typeparam>
      /// <param name="other">The other tag container to compare against.</param>
      /// <param name="added">The list that will be populated with tags that are in this container but not in the other.</param>
      /// <param name="removed">The list that will be populated with tags that are in the other container but not in this one.</param>
      public void GetDiffExplicitTags<T>(T other, List<GameplayTag> added, List<GameplayTag> removed) where T : IGameplayTagContainer
      {
         // Get the indices of the explicit tags from the other container.
         GameplayTagContainerIndices otherIndices = other.Indices;

         // Get the explicit tag indices from both containers.
         List<int> currentContainerTagIndices = Indices.Explicit;
         List<int> otherContainerTagIndices = otherIndices.Explicit;

         // Initialize counters for both lists.
         int currentIndex = 0, otherIndex = 0;

         // Traverse both lists of explicit tag indices.
         while (currentIndex < Indices.ExplicitTagCount && otherIndex < otherIndices.ExplicitTagCount)
         {
            int currentTagIndex = currentContainerTagIndices[currentIndex], otherTagIndex = otherContainerTagIndices[otherIndex];

            // If both indices match, the tag is present in both containers. Move to the next element in both lists.
            if (currentTagIndex == otherTagIndex)
            {
               currentIndex++;
               otherIndex++;
               continue;
            }

            // If the tag index in this container is smaller, it means the tag is present here but not in the other container.
            // Add it to the added list and increment the index for this container.
            if (currentTagIndex < otherTagIndex)
            {
               added.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(currentTagIndex).Tag);
               currentIndex++;
               continue;
            }

            // If the tag index in the other container is smaller, it means the tag is present in the other container but not in this one.
            // Add it to the removed list and increment the index for the other container.
            removed.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(otherTagIndex).Tag);
            otherIndex++;
         }

         // If there are remaining elements in this container's explicit tags, they are considered added.
         for (; currentIndex < Indices.ExplicitTagCount; currentIndex++)
            added.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(currentContainerTagIndices[currentIndex]).Tag);

         // If there are remaining elements in the other container's explicit tags, they are considered removed.
         for (; otherIndex < otherIndices.ExplicitTagCount; otherIndex++)
            removed.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(otherContainerTagIndices[otherIndex]).Tag);
      }

      /// <summary>
      /// Gets an enumerator for the explicit tags in this container.
      /// </summary>
      public GameplayTagEnumerator GetExplicitTags()
      {
         return new GameplayTagEnumerator(m_Indices.Explicit);
      }

      /// <summary>
      /// Gets an enumerator for all tags in this container.
      /// </summary>
      public GameplayTagEnumerator GetTags()
      {
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      /// <inheritdoc />
      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Implicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Implicit, tag, childTags);
      }

      /// <inheritdoc />
      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Explicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Explicit, tag, childTags);
      }

      /// <inheritdoc />
      public void Clear()
      {
         m_Indices.Clear();
         m_SerializedExplicitTags?.Clear();
      }

      /// <inheritdoc />
      public void AddTag(GameplayTag tag)
      {
         GameplayTagContainerIndices.Create(ref m_Indices);
         int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
         if (index >= 0)
            return;

         m_Indices.Explicit.Insert(~index, tag.RuntimeIndex);
         AddImplicitTagsFor(tag);
      }

      /// <inheritdoc />
      public void AddTags<T>(in T container) where T : IGameplayTagContainer
      {
         foreach (GameplayTag tag in container.GetExplicitTags())
            AddTag(tag);
      }

      /// <inheritdoc />
      public void RemoveTag(GameplayTag tag)
      {
         if (!m_Indices.IsCreated)
            return;

         int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
         if (index < 0)
         {
            GameplayTagUtility.WarnNotExplictlyAddedTagRemoval(tag);
            return;
         }

         m_Indices.Explicit.RemoveAt(index);
         FillImplictTags();
      }

      /// <inheritdoc />
      public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
      {
         if (!m_Indices.IsCreated)
            return;

         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
            if (index < 0)
            {
               GameplayTagUtility.WarnNotExplictlyAddedTagRemoval(tag);
               return;
            }

            m_Indices.Explicit.RemoveAt(index);
         }

         FillImplictTags();
      }

      private void AddImplicitTagsFor(GameplayTag tag)
      {
         ReadOnlySpan<GameplayTag> tags = tag.HierarchyTags;
         for (int i = tags.Length - 1; i >= 0; i--)
         {
            GameplayTag parent = tags[i];
            int index = BinarySearchUtility.Search(m_Indices.Implicit, parent.RuntimeIndex);
            if (index >= 0)
               break;

            m_Indices.Implicit.Insert(~index, parent.RuntimeIndex);
         }
      }

      private void FillImplictTags()
      {
         m_Indices.Implicit.Clear();

         for (int i = 0; i < m_Indices.Explicit.Count; i++)
         {
            GameplayTagDefinition definition = GameplayTagManager.GetDefinitionFromRuntimeIndex(m_Indices.Explicit[i]);

            foreach (GameplayTag tag in definition.HierarchyTags)
            {
               if (m_Indices.Implicit.Count > 0 && m_Indices.Implicit[^1] >= tag.RuntimeIndex)
                  continue;

               m_Indices.Implicit.Add(tag.RuntimeIndex);
            }
         }
      }

      void ISerializationCallbackReceiver.OnBeforeSerialize()
      {
         m_SerializedExplicitTags ??= new();

         m_SerializedExplicitTags.Clear();
         if (m_Indices.Explicit == null)
            return;

         foreach (GameplayTag tag in new GameplayTagEnumerator(m_Indices.Explicit))
         {
            if (tag == GameplayTag.None)
               continue;

            m_SerializedExplicitTags.Add(tag.Name);
         }
      }

      void ISerializationCallbackReceiver.OnAfterDeserialize()
      {
         m_Indices = GameplayTagContainerIndices.Create();
         if (m_SerializedExplicitTags == null || m_SerializedExplicitTags.Count == 0)
            return;

         for (int i = 0; i < m_SerializedExplicitTags.Count;)
         {
            GameplayTag tag = GameplayTagManager.RequestTag(m_SerializedExplicitTags[i]);
            if (tag == GameplayTag.None)
            {
               m_SerializedExplicitTags.RemoveAt(i);
               continue;
            }

            int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
            if (index < 0)
            {
               m_Indices.Explicit.Insert(~index, tag.RuntimeIndex);
               i++;
               continue;
            }

            m_SerializedExplicitTags.RemoveAt(i);
         }

         FillImplictTags();
      }

      /// <summary>
      /// This method is implemented only to allow the use of collection initializer syntax.
      /// It is hidden from IntelliSense to avoid cluttering the API.
      /// </summary>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void Add(GameplayTag tag)
      {
         AddTag(tag);
      }

      public IEnumerator<GameplayTag> GetEnumerator()
      {
         return GetTags();
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }
   }
}