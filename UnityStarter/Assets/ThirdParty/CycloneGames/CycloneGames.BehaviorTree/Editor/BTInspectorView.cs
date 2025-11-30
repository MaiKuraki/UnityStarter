using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BTInspectorView : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<BTInspectorView, VisualElement.UxmlTraits> { }
        private UnityEditor.Editor _editor;
        public BTInspectorView()
        {
        }

        internal void UpdateSelection(BTNodeView nodeView)
        {
            Clear();
            UnityEngine.Object.DestroyImmediate(_editor);
            _editor = UnityEditor.Editor.CreateEditor(nodeView.Node);
            IMGUIContainer container = new IMGUIContainer(() =>
            {
                if (_editor == null)
                {
                    return;
                }
                if (_editor.targets == null)
                {
                    return;
                }
                //ignore multiple targets
                if (_editor.targets.Length > 1)
                {
                    return;
                }
                if (_editor.target != null)
                {
                    _editor.OnInspectorGUI();
                }
            });
            Add(container);
        }
    }
}