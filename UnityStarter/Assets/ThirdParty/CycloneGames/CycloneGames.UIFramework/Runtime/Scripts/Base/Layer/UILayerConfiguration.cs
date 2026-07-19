using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    [CreateAssetMenu(fileName = "UILayer_", menuName = "CycloneGames/UIFramework/Layer Configuration")]
    public sealed class UILayerConfiguration : ScriptableObject
    {
        [SerializeField] private string layerName;

        public string LayerName => layerName ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(layerName);

#if UNITY_EDITOR
        private void OnValidate()
        {
            layerName = layerName?.Trim();
        }
#endif
    }
}
