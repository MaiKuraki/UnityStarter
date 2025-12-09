using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    /// <summary>
    /// Inspector view for displaying and editing selected node properties.
    /// </summary>
    public class BTInspectorView : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<BTInspectorView, VisualElement.UxmlTraits> { }
        
        private UnityEditor.Editor _editor;
        private IMGUIContainer _currentContainer;
        private ScrollView _scrollView;
        private Label _nodeTitleLabel;
        
        public BTInspectorView()
        {
            style.flexDirection = FlexDirection.Column;
            
            _nodeTitleLabel = new Label
            {
                name = "node-title",
                text = "No Node Selected"
            };
            _nodeTitleLabel.style.fontSize = 14;
            _nodeTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nodeTitleLabel.style.color = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            _nodeTitleLabel.style.paddingTop = 4;
            _nodeTitleLabel.style.paddingBottom = 4;
            _nodeTitleLabel.style.paddingLeft = 8;
            _nodeTitleLabel.style.paddingRight = 8;
            _nodeTitleLabel.style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));
            _nodeTitleLabel.style.marginBottom = 4;
            _nodeTitleLabel.style.whiteSpace = WhiteSpace.Normal;
            _nodeTitleLabel.style.display = DisplayStyle.Flex;
            Add(_nodeTitleLabel);
            
            _scrollView = new ScrollView();
            _scrollView.style.flexGrow = 1;
            _scrollView.style.overflow = Overflow.Hidden;
            Add(_scrollView);
        }
        
        internal void UpdateSelection(BTNodeView nodeView)
        {
            if (_editor != null)
            {
                UnityEngine.Object.DestroyImmediate(_editor);
                _editor = null;
            }
            
            if (_currentContainer != null)
            {
                _currentContainer.RemoveFromHierarchy();
                _currentContainer = null;
            }
            
            if (_scrollView != null)
            {
                _scrollView.Clear();
            }
            
            if (nodeView?.Node == null)
            {
                if (_nodeTitleLabel != null)
                {
                    _nodeTitleLabel.text = "No Node Selected";
                }
                return;
            }
            
            string nodeName = BTNodeView.ConvertToReadableName(nodeView.Node.name);
            if (_nodeTitleLabel != null)
            {
                _nodeTitleLabel.text = nodeName;
            }
            
            _editor = UnityEditor.Editor.CreateEditor(nodeView.Node);
            _currentContainer = new IMGUIContainer(() =>
            {
                if (_editor == null || _editor.target == null) return;
                if (_editor.targets == null || _editor.targets.Length > 1) return;
                
                float labelWidth = _scrollView.layout.width > 0 
                    ? Mathf.Max(180f, _scrollView.layout.width * 0.5f) 
                    : 180f;
                UnityEditor.EditorGUIUtility.labelWidth = labelWidth;
                _editor.OnInspectorGUI();
            });
            
            _currentContainer.style.flexGrow = 1;
            _scrollView.Add(_currentContainer);
        }
        
        internal void Clear()
        {
            if (_editor != null)
            {
                UnityEngine.Object.DestroyImmediate(_editor);
                _editor = null;
            }
            
            if (_currentContainer != null)
            {
                _currentContainer.RemoveFromHierarchy();
                _currentContainer = null;
            }
            
            if (_scrollView != null)
            {
                _scrollView.Clear();
            }
            
            if (_nodeTitleLabel != null)
            {
                _nodeTitleLabel.text = "No Node Selected";
            }
        }
    }
}