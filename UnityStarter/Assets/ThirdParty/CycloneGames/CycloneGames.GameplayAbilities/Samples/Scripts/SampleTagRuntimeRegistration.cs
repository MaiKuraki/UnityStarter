using CycloneGames.GameplayTags.Runtime;
using System;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// This class ensures that all project-specific GameplayTags are registered at game startup.
    /// It uses the [RuntimeInitializeOnLoadMethod] attribute to hook into the application's launch process,
    /// guaranteeing that the GameplayTagManager is populated before any gameplay logic runs.
    /// This is the runtime equivalent of the editor's [InitializeOnLoad] script.
    /// </summary>
    public static class SampleTagRuntimeRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterTags()
        {
            // First, ensure the manager's base initialization has run.
            GameplayTagManager.InitializeIfNeeded();
            
            // Then, explicitly register all tags from our project's central tag definition class.
            // This provides a robust, explicit registration step for runtime builds.
            RegisterAllTagsFromType(typeof(GASSampleTags));
        }
        
        private static void RegisterAllTagsFromType(Type tagDefinitionType)
        {
            if (tagDefinitionType == null) return;

            var fields = tagDefinitionType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            foreach (var field in fields)
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                {
                    string tagName = (string)field.GetValue(null);
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        GameplayTagManager.RegisterDynamicTag(tagName, description: field.Name);
                    }
                }
            }
            
            Debug.Log($"[SampleTagRuntimeRegistration] Explicitly registered tags from {tagDefinitionType.Name} for runtime.");
        }
    }
}