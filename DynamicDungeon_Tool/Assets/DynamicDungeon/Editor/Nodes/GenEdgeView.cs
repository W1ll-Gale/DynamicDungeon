using DynamicDungeon.Runtime.Graph;
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
        private const float LabelPaddingHorizontal = 4.0f;
        private const float LabelPaddingVertical = 2.0f;

        // Cast state
        private CastMode _castMode;
        private Label _castLabel;

        public bool IsCastEdge
        {
            get
            {
                return _castMode != CastMode.None;
            }
        }

        public CastMode ActiveCastMode
        {
            get
            {
                return _castMode;
            }
        }

        public GenEdgeView(CastMode castMode, Color edgeColour)
        {
            _castMode = castMode;

            if (_castMode != CastMode.None)
            {
                _castLabel = new Label();
                _castLabel.style.position = Position.Absolute;
                _castLabel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.88f);
                _castLabel.style.color = new Color(0.9f, 0.9f, 0.9f, 1.0f);
                _castLabel.style.paddingLeft = LabelPaddingHorizontal;
                _castLabel.style.paddingRight = LabelPaddingHorizontal;
                _castLabel.style.paddingTop = LabelPaddingVertical;
                _castLabel.style.paddingBottom = LabelPaddingVertical;
                _castLabel.style.borderTopLeftRadius = 2.0f;
                _castLabel.style.borderTopRightRadius = 2.0f;
                _castLabel.style.borderBottomLeftRadius = 2.0f;
                _castLabel.style.borderBottomRightRadius = 2.0f;
                _castLabel.style.fontSize = 9;
                _castLabel.style.translate = new StyleTranslate(
                    new Translate(
                        new Length(-50.0f, LengthUnit.Percent),
                        new Length(-50.0f, LengthUnit.Percent)));
                _castLabel.pickingMode = PickingMode.Ignore;
                _castLabel.text = GetCastModeAbbreviation(_castMode);
                Add(_castLabel);

                generateVisualContent += OnGenerateVisualContent;
                RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

                // edgeControl is null at constructor time — hide it once the edge is in the panel.
                RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            }

            ApplyEdgeColour(edgeColour);
            UpdateTooltip();
        }

        public void ApplyCastMode(CastMode mode)
        {
            _castMode = mode;

            if (_castLabel != null)
            {
                _castLabel.text = GetCastModeAbbreviation(mode);
            }

            UpdateTooltip();
            MarkDirtyRepaint();
        }

        public void ApplyEdgeColour(Color edgeColour)
        {
            if (edgeControl != null)
            {
                edgeControl.inputColor = edgeColour;
                edgeControl.outputColor = edgeColour;
                edgeControl.edgeWidth = EdgeWidth;
                edgeControl.visible = !IsCastEdge;
            }

            MarkDirtyRepaint();
        }

        private void UpdateTooltip()
        {
            tooltip = BuildTooltipText(_castMode);
        }

        private void OnAttachedToPanel(AttachToPanelEvent attachEvent)
        {
            HideEdgeControl();
        }

        private void HideEdgeControl()
        {
            if (edgeControl != null)
            {
                edgeControl.visible = false;
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            RepositionLabel();
        }

        private void RepositionLabel()
        {
            if (_castLabel == null || input == null || output == null)
            {
                return;
            }

            VisualElement currentElement = this;
            Vector2 startPoint = currentElement.WorldToLocal(output.worldBound.center);
            Vector2 endPoint = currentElement.WorldToLocal(input.worldBound.center);
            Vector2 midpoint = (startPoint + endPoint) * 0.5f;

            _castLabel.style.left = midpoint.x;
            _castLabel.style.top = midpoint.y;
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (!IsCastEdge || input == null || output == null)
            {
                return;
            }

            HideEdgeControl();
            RepositionLabel();

            VisualElement currentElement = this;
            Vector2 startPoint = currentElement.WorldToLocal(output.worldBound.center);
            Vector2 endPoint = currentElement.WorldToLocal(input.worldBound.center);
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

        // ⌊ U+230A  ⌋ U+230B  ⌉ U+2309  → U+2192
        private static string GetCastModeAbbreviation(CastMode mode)
        {
            switch (mode)
            {
                case CastMode.FloatToIntFloor:
                    return "⌊f→i⌋";
                case CastMode.FloatToIntRound:
                    return "⌊f→i⌉";
                case CastMode.FloatToBoolMask:
                    return "f→b";
                case CastMode.IntToBoolMask:
                    return "i→b";
                default:
                    return string.Empty;
            }
        }

        private static string BuildTooltipText(CastMode mode)
        {
            switch (mode)
            {
                case CastMode.FloatToIntFloor:
                    return "Cast: Float → Int (Floor)";
                case CastMode.FloatToIntRound:
                    return "Cast: Float → Int (Round)";
                case CastMode.FloatToBoolMask:
                    return "Cast: Float → Bool Mask";
                case CastMode.IntToBoolMask:
                    return "Cast: Int → Bool Mask";
                default:
                    return string.Empty;
            }
        }
    }
}
