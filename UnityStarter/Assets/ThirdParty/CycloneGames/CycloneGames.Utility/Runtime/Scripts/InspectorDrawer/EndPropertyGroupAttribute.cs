using System;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// When placed on a field, this attribute will terminate any ongoing continuous PropertyGroup.
    /// The field with this attribute and subsequent fields will not be grouped until a new PropertyGroup is declared.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class EndPropertyGroupAttribute : Attribute
    {
    }
}
