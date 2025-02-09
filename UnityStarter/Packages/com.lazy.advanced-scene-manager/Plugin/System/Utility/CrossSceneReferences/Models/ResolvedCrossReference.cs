namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>Represents a resolved reference.</summary>
    public struct ResolvedCrossReference
    {

        /// <summary>The unresolved reference.</summary>
        public CrossSceneReference reference;

        /// <summary>The unresolved and resolved reference to the variable.</summary>
        public (ObjectReference reference, ResolvedReference resolve) variable;

        /// <summary>The unresolved and resolved reference to the value.</summary>
        public (ObjectReference reference, ResolvedReference resolve) value;

        /// <summary>The result when setting value.</summary>
        public ResolveStatus result;

        public override string ToString() =>
            variable.resolve.ToString(includeScene: false) +
            " → " +
            value.resolve.ToString();

        public ResolvedCrossReference(ResolvedReference variable, ResolvedReference value, CrossSceneReference reference, ResolveStatus result)
        {
            this.reference = reference;
            this.variable = (reference.variable, variable);
            this.value = (reference.value, value);
            this.result = result;
        }

    }

}
