using System;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>A reference to a variable that references another object in some other scene.</summary>
    [Serializable]
    public class CrossSceneReference
    {

        public string id;
        public ObjectReference variable;
        public ObjectReference value;

        public CrossSceneReference()
        { }

        public CrossSceneReference(ObjectReference variable, ObjectReference value)
        {
            this.variable = variable;
            this.value = value;
            id = GuidReferenceUtility.GenerateID();
        }

        public override bool Equals(object obj) =>
            id == (obj as CrossSceneReference)?.id;

        public override int GetHashCode() =>
            id.GetHashCode();

    }

}
