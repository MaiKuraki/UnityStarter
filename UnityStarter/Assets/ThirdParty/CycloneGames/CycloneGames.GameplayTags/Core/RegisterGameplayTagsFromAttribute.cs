using System;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// An assembly-level attribute that directs Editor discovery to scan a specified static class
    /// for public constant strings. Discovered tags are included in the validated Player build catalog;
    /// Player startup does not scan the target type through reflection.
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
