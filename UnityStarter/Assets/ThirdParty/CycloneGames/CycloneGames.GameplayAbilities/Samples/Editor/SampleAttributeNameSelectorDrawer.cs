using System;
using UnityEditor;
using CycloneGames.GameplayAbilities.Editor;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample.Editor
{
    /// <summary>
    /// specific implementation of AttributeNameSelectorDrawer_Base for GASSampleTags.
    /// </summary>
    [CustomPropertyDrawer(typeof(AttributeNameSelectorAttribute))]
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