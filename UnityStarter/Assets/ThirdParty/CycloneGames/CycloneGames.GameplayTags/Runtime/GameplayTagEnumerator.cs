using System.Collections;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
   public struct GameplayTagEnumerator : IEnumerator<GameplayTag>, IEnumerable<GameplayTag>
   {
      public readonly GameplayTag Current
      {
         get
         {
            GameplayTagDefinition definition = GameplayTagManager.GetDefinitionFromRuntimeIndex(m_Indexes[m_CurrentIndex]);
            return definition.Tag;
         }
      }

      readonly object IEnumerator.Current => Current;

      private List<int> m_Indexes;
      private int m_CurrentIndex;

      internal GameplayTagEnumerator(List<int> indices)
      {
         m_Indexes = indices;
         m_CurrentIndex = -1;
      }

      public readonly void Dispose()
      {
      }

      public bool MoveNext()
      {
         m_CurrentIndex++;
         return m_CurrentIndex < m_Indexes.Count;
      }

      public void Reset()
      {
         m_CurrentIndex = -1;
      }

      public readonly GameplayTagEnumerator GetEnumerator()
      {
         return this;
      }

      readonly IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator()
      {
         return this;
      }

      readonly IEnumerator IEnumerable.GetEnumerator()
      {
         return this;
      }
   }
}