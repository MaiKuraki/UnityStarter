using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;

using UnityEngine;

namespace CycloneGames.GameplayTags.Tests.Editor
{
   public sealed class GameplayTagTestHolder : ScriptableObject
   {
      public SerializableGameplayTag Tag;
      public SerializableGameplayTagContainer Container;
   }
}
