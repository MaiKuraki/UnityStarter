using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    /// <summary>
    /// Custom edge with animated arrow flow effect for running connections.
    /// </summary>
    public class BTAnimatedEdge : Edge
    {
        private bool _isAnimating;
        private float _animationOffset;
        private const float ANIMATION_SPEED = 80f;
        private const float ARROW_SPACING = 30f;
        private const float ARROW_SIZE = 6f;
        
        private VisualElement _arrowContainer;
        private IVisualElementScheduledItem _scheduledAnimation;
        
        public BTAnimatedEdge()
        {
            // Create arrow container overlay
            _arrowContainer = new VisualElement
            {
                name = "arrow-container",
                pickingMode = PickingMode.Ignore
            };
            _arrowContainer.style.position = Position.Absolute;
            _arrowContainer.style.left = 0;
            _arrowContainer.style.top = 0;
            _arrowContainer.style.right = 0;
            _arrowContainer.style.bottom = 0;
            
            Add(_arrowContainer);
            
            // Use generateVisualContent for custom drawing
            _arrowContainer.generateVisualContent += OnGenerateVisualContent;
        }
        
        public void SetAnimating(bool animating)
        {
            if (_isAnimating == animating) return;
            _isAnimating = animating;
            
            if (_isAnimating)
            {
                // Start animation loop
                _scheduledAnimation = schedule.Execute(UpdateAnimation).Every(16); // ~60fps
            }
            else
            {
                // Stop animation
                _scheduledAnimation?.Pause();
                _animationOffset = 0;
                _arrowContainer.MarkDirtyRepaint();
            }
        }
        
        private void UpdateAnimation()
        {
            if (!_isAnimating) return;
            
            _animationOffset += ANIMATION_SPEED * 0.016f; // 16ms per frame
            if (_animationOffset > ARROW_SPACING)
            {
                _animationOffset -= ARROW_SPACING;
            }
            
            _arrowContainer.MarkDirtyRepaint();
        }
        
        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (!_isAnimating) return;
            if (output == null || input == null) return;
            
            // Get edge control points
            var painter = mgc.painter2D;
            if (painter == null) return;
            
            // Calculate edge path points
            Vector2 startPos = GetOutputPosition();
            Vector2 endPos = GetInputPosition();
            
            if (startPos == Vector2.zero || endPos == Vector2.zero) return;
            
            // Draw arrows along the path
            float totalLength = Vector2.Distance(startPos, endPos);
            if (totalLength < ARROW_SPACING) return;
            
            Vector2 direction = (endPos - startPos).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            
            painter.fillColor = new Color(0.56f, 1f, 0.56f, 0.9f); // Light green
            painter.strokeColor = new Color(0.27f, 0.56f, 0.29f, 1f); // Dark green
            painter.lineWidth = 1f;
            
            // Draw arrows starting from offset
            for (float dist = _animationOffset; dist < totalLength; dist += ARROW_SPACING)
            {
                float t = dist / totalLength;
                Vector2 pos = Vector2.Lerp(startPos, endPos, t);
                
                // Draw arrow pointing towards end
                DrawArrow(painter, pos, direction, perpendicular);
            }
        }
        
        private void DrawArrow(Painter2D painter, Vector2 pos, Vector2 dir, Vector2 perp)
        {
            // Arrow triangle pointing in direction of flow
            Vector2 tip = pos + dir * ARROW_SIZE;
            Vector2 left = pos - dir * ARROW_SIZE * 0.5f + perp * ARROW_SIZE * 0.5f;
            Vector2 right = pos - dir * ARROW_SIZE * 0.5f - perp * ARROW_SIZE * 0.5f;
            
            painter.BeginPath();
            painter.MoveTo(tip);
            painter.LineTo(left);
            painter.LineTo(right);
            painter.ClosePath();
            painter.Fill();
        }
        
        private Vector2 GetOutputPosition()
        {
            if (output?.parent == null) return Vector2.zero;
            
            var outputRect = output.worldBound;
            var myRect = worldBound;
            
            if (outputRect.width <= 0 || myRect.width <= 0) return Vector2.zero;
            
            return new Vector2(
                outputRect.center.x - myRect.x,
                outputRect.yMax - myRect.y
            );
        }
        
        private Vector2 GetInputPosition()
        {
            if (input?.parent == null) return Vector2.zero;
            
            var inputRect = input.worldBound;
            var myRect = worldBound;
            
            if (inputRect.width <= 0 || myRect.width <= 0) return Vector2.zero;
            
            return new Vector2(
                inputRect.center.x - myRect.x,
                inputRect.yMin - myRect.y
            );
        }
    }
}
