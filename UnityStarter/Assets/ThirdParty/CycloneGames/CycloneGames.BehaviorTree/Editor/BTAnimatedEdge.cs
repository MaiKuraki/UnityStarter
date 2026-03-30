using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    /// <summary>
    /// Custom edge with animated flowing dot particles along a cubic bezier path.
    /// Renders glowing dots with halos and a breathing pulse effect.
    /// Supports per-state colors and optional child-index badge.
    /// </summary>
    public class BTAnimatedEdge : Edge
    {
        private bool _isAnimating;
        private float _animationOffset;
        private float _pulsePhase;

        private const float ANIMATION_SPEED = 100f;
        private const float DOT_SPACING = 28f;
        private const float DOT_RADIUS = 3f;
        private const float DOT_LEAD_RADIUS = 4f;
        private const float PULSE_SPEED = 3.5f;
        private const int BEZIER_SAMPLES = 20;

        private Color _dotColor = new Color(0.56f, 1f, 0.56f, 0.9f);
        private Color _glowColor = new Color(0.27f, 0.56f, 0.29f, 0.4f);

        private VisualElement _arrowContainer;
        private IVisualElementScheduledItem _scheduledAnimation;
        private Label _indexLabel;

        // Cached bezier samples — avoids per-frame allocation
        private readonly Vector2[] _bezierPoints = new Vector2[BEZIER_SAMPLES + 1];
        private readonly float[] _bezierLengths = new float[BEZIER_SAMPLES + 1];

        public BTAnimatedEdge()
        {
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
            _arrowContainer.generateVisualContent += OnGenerateVisualContent;
        }

        public void SetAnimating(bool animating)
        {
            if (_isAnimating == animating) return;
            _isAnimating = animating;

            if (_isAnimating)
            {
                _scheduledAnimation = schedule.Execute(UpdateAnimation).Every(16); // ~60fps
            }
            else
            {
                _scheduledAnimation?.Pause();
                _animationOffset = 0;
                _pulsePhase = 0;
                _arrowContainer.MarkDirtyRepaint();
            }
        }

        /// <summary>Sets dot and glow colors for state-specific rendering.</summary>
        public void SetColors(Color dotColor, Color glowColor)
        {
            _dotColor = dotColor;
            _glowColor = glowColor;
        }

        /// <summary>Shows a child-index badge near the child end of the edge.</summary>
        public void SetChildIndex(int index)
        {
            if (index <= 0) return;
            _indexLabel = new Label(index.ToString())
            {
                name = "edge-index",
                pickingMode = PickingMode.Ignore
            };
            _indexLabel.style.position = Position.Absolute;
            _indexLabel.style.fontSize = 8;
            _indexLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _indexLabel.style.color = new Color(1f, 1f, 1f, 0.55f);
            _indexLabel.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _indexLabel.style.borderTopLeftRadius = 4;
            _indexLabel.style.borderTopRightRadius = 4;
            _indexLabel.style.borderBottomLeftRadius = 4;
            _indexLabel.style.borderBottomRightRadius = 4;
            _indexLabel.style.paddingLeft = 3;
            _indexLabel.style.paddingRight = 3;
            _indexLabel.style.paddingTop = 1;
            _indexLabel.style.paddingBottom = 1;
            _indexLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(_indexLabel);
            RegisterCallback<GeometryChangedEvent>(_ => UpdateIndexPosition());
        }

        private void UpdateIndexPosition()
        {
            if (_indexLabel == null) return;
            Vector2 start = GetOutputPosition();
            Vector2 end = GetInputPosition();
            if (start == Vector2.zero || end == Vector2.zero) return;

            // Position at 65% along the edge (closer to child input)
            Vector2 pos = Vector2.Lerp(start, end, 0.65f);
            _indexLabel.style.left = pos.x + 6;
            _indexLabel.style.top = pos.y - 7;
        }

        private void UpdateAnimation()
        {
            if (!_isAnimating) return;

            _animationOffset += ANIMATION_SPEED * 0.016f;
            if (_animationOffset > DOT_SPACING)
                _animationOffset -= DOT_SPACING;

            _pulsePhase += PULSE_SPEED * 0.016f;
            if (_pulsePhase > Mathf.PI * 2f)
                _pulsePhase -= Mathf.PI * 2f;

            _arrowContainer.MarkDirtyRepaint();
            UpdateIndexPosition();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            if (!_isAnimating) return;
            if (output == null || input == null) return;

            var painter = mgc.painter2D;
            if (painter == null) return;

            Vector2 startPos = GetOutputPosition();
            Vector2 endPos = GetInputPosition();
            if (startPos == Vector2.zero || endPos == Vector2.zero) return;

            // Build cubic bezier with vertical tangents (top-to-bottom tree layout)
            float dy = Mathf.Abs(endPos.y - startPos.y);
            float tangentStrength = Mathf.Clamp(dy * 0.4f, 20f, 80f);
            Vector2 cp1 = startPos + new Vector2(0, tangentStrength);
            Vector2 cp2 = endPos - new Vector2(0, tangentStrength);

            float totalLength = SampleBezier(startPos, cp1, cp2, endPos);
            if (totalLength < DOT_SPACING * 0.5f) return;

            // Breathing pulse
            float pulse = 0.75f + 0.25f * Mathf.Sin(_pulsePhase);

            bool isFirst = true;
            for (float dist = _animationOffset; dist < totalLength; dist += DOT_SPACING)
            {
                Vector2 pos = GetPointAtDistance(dist);
                float radius = isFirst ? DOT_LEAD_RADIUS : DOT_RADIUS;
                float alpha = isFirst ? pulse : pulse * 0.75f;

                // Outer glow halo
                Color glow = _glowColor;
                glow.a *= alpha * 0.5f;
                painter.fillColor = glow;
                DrawCircle(painter, pos, radius * 2.5f);

                // Main dot
                Color dot = _dotColor;
                dot.a *= alpha;
                painter.fillColor = dot;
                DrawCircle(painter, pos, radius);

                // Bright center highlight
                Color center = Color.white;
                center.a = alpha * 0.5f;
                painter.fillColor = center;
                DrawCircle(painter, pos, radius * 0.35f);

                isFirst = false;
            }
        }

        private void DrawCircle(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.Arc(center, radius, 0f, 360f);
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>Samples a cubic bezier into cached arrays. Returns total arc length.</summary>
        private float SampleBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            float totalLength = 0f;
            _bezierPoints[0] = p0;
            _bezierLengths[0] = 0f;

            for (int i = 1; i <= BEZIER_SAMPLES; i++)
            {
                float t = i / (float)BEZIER_SAMPLES;
                float u = 1f - t;
                _bezierPoints[i] = u * u * u * p0
                                 + 3f * u * u * t * p1
                                 + 3f * u * t * t * p2
                                 + t * t * t * p3;
                totalLength += Vector2.Distance(_bezierPoints[i - 1], _bezierPoints[i]);
                _bezierLengths[i] = totalLength;
            }
            return totalLength;
        }

        /// <summary>Gets a point at a given arc-length distance along the sampled bezier.</summary>
        private Vector2 GetPointAtDistance(float distance)
        {
            if (distance <= 0) return _bezierPoints[0];

            for (int i = 1; i <= BEZIER_SAMPLES; i++)
            {
                if (_bezierLengths[i] >= distance)
                {
                    float segStart = _bezierLengths[i - 1];
                    float segLen = _bezierLengths[i] - segStart;
                    float localT = segLen > 0.001f ? (distance - segStart) / segLen : 0f;
                    return Vector2.Lerp(_bezierPoints[i - 1], _bezierPoints[i], localT);
                }
            }
            return _bezierPoints[BEZIER_SAMPLES];
        }

        private Vector2 GetOutputPosition()
        {
            if (output?.parent == null) return Vector2.zero;
            var outputRect = output.worldBound;
            var myRect = worldBound;
            if (outputRect.width <= 0 || myRect.width <= 0) return Vector2.zero;
            return new Vector2(outputRect.center.x - myRect.x, outputRect.yMax - myRect.y);
        }

        private Vector2 GetInputPosition()
        {
            if (input?.parent == null) return Vector2.zero;
            var inputRect = input.worldBound;
            var myRect = worldBound;
            if (inputRect.width <= 0 || myRect.width <= 0) return Vector2.zero;
            return new Vector2(inputRect.center.x - myRect.x, inputRect.yMin - myRect.y);
        }
    }
}
