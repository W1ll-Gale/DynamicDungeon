using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GenEdgeView : Edge
    {
        private const float DashLength = 10.0f;
        private const float GapLength = 6.0f;
        private const int EdgeWidth = 2;

        private readonly bool _isCastEdge;

        public bool IsCastEdge
        {
            get
            {
                return _isCastEdge;
            }
        }

        public GenEdgeView(bool isCastEdge, Color edgeColour)
        {
            _isCastEdge = isCastEdge;

            if (_isCastEdge)
            {
                generateVisualContent += OnGenerateVisualContent;
            }

            ApplyEdgeColour(edgeColour);
        }

        public void ApplyEdgeColour(Color edgeColour)
        {
            if (edgeControl != null)
            {
                edgeControl.inputColor = edgeColour;
                edgeControl.outputColor = edgeColour;
                edgeControl.edgeWidth = EdgeWidth;
                edgeControl.visible = !_isCastEdge;
            }

            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Edge currentEdge = this;

            if (!_isCastEdge || input == null || output == null)
            {
                return;
            }

            Vector2 startPoint = currentEdge.WorldToLocal(output.worldBound.center);
            Vector2 endPoint = currentEdge.WorldToLocal(input.worldBound.center);
            Vector2 delta = endPoint - startPoint;
            float totalLength = delta.magnitude;

            if (totalLength <= Mathf.Epsilon)
            {
                return;
            }

            Color drawColour = edgeControl != null ? edgeControl.inputColor : Color.white;
            Vector2 direction = delta / totalLength;
            float travelledDistance = 0.0f;
            Painter2D painter = context.painter2D;
            painter.strokeColor = drawColour;
            painter.lineWidth = EdgeWidth;

            while (travelledDistance < totalLength)
            {
                float dashEndDistance = Mathf.Min(travelledDistance + DashLength, totalLength);
                Vector2 dashStartPoint = startPoint + (direction * travelledDistance);
                Vector2 dashEndPoint = startPoint + (direction * dashEndDistance);

                painter.BeginPath();
                painter.MoveTo(dashStartPoint);
                painter.LineTo(dashEndPoint);
                painter.Stroke();

                travelledDistance += DashLength + GapLength;
            }
        }
    }
}
