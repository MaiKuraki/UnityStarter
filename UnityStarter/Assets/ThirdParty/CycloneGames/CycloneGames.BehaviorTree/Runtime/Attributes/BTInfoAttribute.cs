using System;

namespace CycloneGames.BehaviorTree.Runtime.Attributes
{
    public class BTInfoAttribute : Attribute
    {
        public readonly string Category;
        public readonly string Description;

        public BTInfoAttribute(string category = null, string description = null)
        {
            Category = string.IsNullOrEmpty(category) ? "Base" : category;
            Description = string.IsNullOrEmpty(description) ? "No description provided." : description;
        }
    }
}