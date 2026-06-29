using System;
using CycloneGames.GameplayAbilities.Editor;

using UnityEditor;
using CycloneGames.GameplayAbilities.Runtime;
namespace CycloneGames.GameplayAbilities.Sample.Editor
{
    /// <summary>
    /// Sample implementation of AttributeNameSelectorDrawer_Base for GASSampleTags.
    /// Projects can copy this pattern when they want an attribute selector bound to a specific constants type.
    /// </summary>
    // [CustomPropertyDrawer(typeof(AttributeNameSelectorAttribute))]
    public class SampleAttributeNameSelectorDrawer : AttributeNameSelectorDrawer_Base
    {
        /// <summary>
        /// implementation of GetConstantsType to return the type of GASSampleTags.
        /// </summary>
        protected override Type GetConstantsType()
        {
            return typeof(GASSampleTags);
        }
    }
}
