using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.GameplayTags
{
   public class GameplayTagManager
   {
      private static Dictionary<string, GameplayTagDefinition> s_TagDefinitionsByName = new();
      private static GameplayTagDefinition[] s_TagsDefinitions;
      private static GameplayTag[] s_Tags;
      private static bool s_IsInitialized;

      public static ReadOnlySpan<GameplayTag> GetAllTags()
      {
         InitializeIfNeeded();
         return new ReadOnlySpan<GameplayTag>(s_Tags);
      }

      internal static GameplayTagDefinition GetDefinitionFromRuntimeIndex(int runtimeIndex)
      {
         InitializeIfNeeded();
         return s_TagsDefinitions[runtimeIndex];
      }

      public static GameplayTag RequestTag(string name)
      {
         if (string.IsNullOrEmpty(name))
         {
            return GameplayTag.None;
         }

         if (!TryGetDefinition(name, out GameplayTagDefinition definition))
         {
            Debug.LogWarning($"No tag registered with name \"{name}\".");
            return GameplayTag.None;
         }

         return definition.Tag;
      }

      public static bool RequestTag(string name, out GameplayTag tag)
      {
         if (TryGetDefinition(name, out GameplayTagDefinition definition))
         {
            tag = definition.Tag;
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      private static bool TryGetDefinition(string name, out GameplayTagDefinition definition)
      {
         InitializeIfNeeded();
         return s_TagDefinitionsByName.TryGetValue(name, out definition);
      }

      public static void InitializeIfNeeded()
      {
         if (s_IsInitialized)
         {
            return;
         }

         GameplayTagRegistrationContext context = new();

         foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
         {
            foreach (GameplayTagAttribute attribute in assembly.GetCustomAttributes<GameplayTagAttribute>())
            {
               try
               {
                  context.RegisterTag(attribute.TagName, attribute.Description, attribute.Flags);
               }
               catch (Exception exception)
               {
                  Debug.LogError($"Failed to register tag {attribute.TagName} from assembly {assembly.FullName} with error: {exception.Message}");
               }
            }
         }

         s_TagsDefinitions = context.GenerateDefinitions();

         // Skip the first tag definition which is the "None" tag.
         IEnumerable<GameplayTag> tags = s_TagsDefinitions
            .Select(definition => definition.Tag)
            .Skip(1);

         s_Tags = Enumerable.ToArray(tags);
         foreach (GameplayTagDefinition definition in s_TagsDefinitions)
         {
            s_TagDefinitionsByName[definition.TagName] = definition;
         }

         s_IsInitialized = true;
      }
   
      static GameplayTagRegistrationContext dynContext;
      private static void PreRegisterDynamicTags()
      {
         InitializeIfNeeded();
         dynContext = new();
      }

      /// <summary>
      /// Registers a new dynamic gameplay tag during runtime.
      /// This method should be called within the registration pipeline:
      /// 'PreRegister -> Register -> PostRegister'. The Register can be called in a loop.
      /// </summary>
      /// <param name="name">The name of the tag to register.</param>
      /// <param name="description">An optional description of the tag.</param>
      /// <param name="flags">Optional flags associated with the tag.</param>
      /// <example>
      /// <code>
      /// -------------------------------------------------------------------------------
      ///   GameplayTagManager.PreRegisterDynamicTags();
      ///   foreach (string tag in new List<string> { "Example.Tag1", "Example.Tag2" })
      ///   {
      ///       GameplayTagManager.RegisterDynamicTag(tag);
      ///   }
      ///   GameplayTagManager.PostRegisterDynamicTags();
      /// --------------------------------------------------------------------------------
      /// </code>
      /// </example>
      private static void InternalRegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         // Check if the tag is already registered
         if (s_TagDefinitionsByName.ContainsKey(name))
            return;

         dynContext?.RegisterTag(name, description, flags);
      }

      private static void PostRegisterDynamicTags()
      {
         // Generate new tag definitions from the context
         var newDefinitions = dynContext.GenerateDefinitions();

         // Update s_TagsDefinitions with the new definitions
         s_TagsDefinitions = s_TagsDefinitions.Concat(newDefinitions).ToArray();

         // Update the dictionary with new tag definitions
         foreach (var definition in newDefinitions)
         {
            s_TagDefinitionsByName[definition.TagName] = definition;
         }

         // Re-Generate Definitions(Sorting and re-Index)
         s_TagsDefinitions = dynContext.GenerateDefinitions(true);

         // Update the array of tags, skipping the "None" tag
         s_Tags = s_TagsDefinitions.Select(def => def.Tag).Skip(1).ToArray();

         dynContext = null;
      }

      public static void RegisterDynamicTags(IEnumerable<string> tags)
      {
         PreRegisterDynamicTags();
         foreach (string tag in tags)
         {
            InternalRegisterDynamicTag(tag);
         }
         PostRegisterDynamicTags();
      }

      /// <summary>
      /// WARNING: You should not using this method in the for loop, if you need register a list, using RegisterDynamicTags.
      /// </summary>
      /// <param name="name"></param>
      /// <param name="description"></param>
      /// <param name="flags"></param>
      public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         PreRegisterDynamicTags();
         InternalRegisterDynamicTag(name, description, flags);
         PostRegisterDynamicTags();
      }
   }
}