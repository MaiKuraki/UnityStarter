using System;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Marks a property for automatic injection from UIServiceLocator.
    /// Used when no DI framework is present.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class UIInjectAttribute : Attribute
    {
    }
}
