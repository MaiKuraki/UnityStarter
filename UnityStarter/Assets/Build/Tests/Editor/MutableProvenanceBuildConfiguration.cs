using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class MutableProvenanceBuildConfiguration : ScriptableObject
    {
        [SerializeField] private string value = string.Empty;

        public void SetValue(string nextValue)
        {
            value = nextValue ?? string.Empty;
        }
    }
}
