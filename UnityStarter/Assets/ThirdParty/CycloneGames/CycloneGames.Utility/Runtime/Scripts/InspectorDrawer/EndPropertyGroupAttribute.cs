using System;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// Ends a continuous PropertyGroup before the decorated serialized field is drawn.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class EndPropertyGroupAttribute : Attribute
    {
    }
}
