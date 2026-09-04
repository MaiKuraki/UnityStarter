using System;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The flattened output of one registry build.
   /// </summary>
   /// <remarks>
   /// This is a build-time exchange type only. It is never published to callers and never retained
   /// beyond the construction of a <see cref="TagDataSnapshot"/>, which is why it can expose raw
   /// mutable arrays without any of the immutability machinery the published snapshot needs.
   /// </remarks>
   internal sealed class GameplayTagBuildResult
   {
      /// <summary>Full dotted names. Index 0 is the <see cref="GameplayTag.None"/> sentinel.</summary>
      internal readonly string[] Names;

      /// <summary>Authoring descriptions, parallel to <see cref="Names"/>.</summary>
      internal readonly string[] Descriptions;

      /// <summary>Authoring flags, parallel to <see cref="Names"/>.</summary>
      internal readonly GameplayTagFlags[] Flags;

      /// <summary>
      /// Immediate parent index, parallel to <see cref="Names"/>. 0 for a root tag and for the
      /// <see cref="GameplayTag.None"/> sentinel. Always strictly less than the child index, which is
      /// the invariant the snapshot's compressed hierarchy storage depends on.
      /// </summary>
      internal readonly int[] ParentIndices;

      internal GameplayTagBuildResult(
         string[] names,
         string[] descriptions,
         GameplayTagFlags[] flags,
         int[] parentIndices)
      {
         Names = names;
         Descriptions = descriptions;
         Flags = flags;
         ParentIndices = parentIndices;
      }
   }
}
