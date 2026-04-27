using System.Collections.Generic;
using System.Diagnostics;

namespace CycloneGames.GameplayTags.Core
{
   internal class GameplayTagContainerDebugView
   {
      [DebuggerDisplay("{DebuggerDisplay,nq}")]
      public struct Tag
      {
         [DebuggerBrowsable(DebuggerBrowsableState.Never)]
         private IGameplayTagContainer m_Container { get; set; }

         [DebuggerBrowsable(DebuggerBrowsableState.Never)]
         private GameplayTag m_Tag;

         [DebuggerBrowsable(DebuggerBrowsableState.Never)]
         private readonly string DebuggerDisplay
         {
            get
            {
               string name = m_Tag.Name;

               if (m_Container is IGameplayTagCountContainer countContainer)
               {
                  int count = countContainer.GetTagCount(m_Tag);
                  int explicitCount = countContainer.GetExplicitTagCount(m_Tag);

                  return $"{name} (Explicit: {explicitCount}, Total: {count})";
               }

               bool isExplicit = m_Container.HasTagExact(m_Tag);
               return isExplicit ? $"{name} (Explicit)" : name;
            }
         }

         public Tag(IGameplayTagContainer container, GameplayTag tag)
         {
            m_Container = container;
            m_Tag = tag;
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      public IGameplayTagContainer Container { get; set; }

      [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
      public Tag[] Tags
      {
         get
         {
            int count = Container.TagCount;
            var result = new Tag[count];
            int i = 0;
            foreach (GameplayTag tag in Container.GetTags())
            {
               if (i >= count) break;
               result[i++] = new Tag(Container, tag);
            }
            return result;
         }
      }

      public GameplayTagContainerDebugView(IGameplayTagContainer container)
      {
         Container = container;
      }
   }
}