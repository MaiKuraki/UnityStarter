using System;

namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// An assembly-level attribute that directs the GameplayTagManager to scan a specified static class
    /// for public constant strings and register them as GameplayTags during initialization.
    /// This allows for defining tags in a centralized static class while having them available in the editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public class RegisterGameplayTagsFromAttribute : Attribute
    {
        public Type TargetType { get; }

        public RegisterGameplayTagsFromAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }
}