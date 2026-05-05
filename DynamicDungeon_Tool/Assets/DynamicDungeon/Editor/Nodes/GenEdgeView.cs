using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GenEdgeView : Edge
    {
        private const int EdgeWidth = 2;
        private const float BoxMinWidth = 36.0f;
        private const float BoxMinHeight = 24.0f;
        private const float BoxPaddingHorizontal = 6.0f;
        private const float BoxPaddingVertical = 3.0f;
        private const float BoxBorderWidth = 2.0f;
        private const float BoxCornerRadius = 5.0f;
        private const int LabelFontSize = 10;
        private const float HoverTooltipOffsetY = 8.0f;
        private const float HoverTooltipMinWidth = 92.0f;
        private const float HoverTooltipPaddingHorizontal = 6.0f;
        private const float HoverTooltipPaddingVertical = 3.0f;
        private const int HoverTooltipFontSize = 10;

        private CastMode _castMode;
        private readonly VisualElement _centerBox;
        private readonly Label _label;
        private readonly VisualElement _hoverTooltip;
        private readonly Label _hoverTooltipLabel;
        private Color _outputColour;
        private Color _inputColour;

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

        public GenEdgeView(CastMode castMode, Color outputColour, Color inputColour)
        {
            _castMode = castMode;

            _centerBox = new VisualElement();
            _centerBox.name = "gen-edge-center-box";
            _centerBox.pickingMode = PickingMode.Position;
            _centerBox.style.position = Position.Absolute;
            _centerBox.style.minWidth = BoxMinWidth;
            _centerBox.style.minHeight = BoxMinHeight;
            _centerBox.style.paddingLeft = BoxPaddingHorizontal;
            _centerBox.style.paddingRight = BoxPaddingHorizontal;
            _centerBox.style.paddingTop = BoxPaddingVertical;
            _centerBox.style.paddingBottom = BoxPaddingVertical;
            _centerBox.style.justifyContent = Justify.Center;
            _centerBox.style.alignItems = Align.Center;
            _centerBox.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.96f);
            _centerBox.style.borderLeftWidth = BoxBorderWidth;
            _centerBox.style.borderRightWidth = BoxBorderWidth;
            _centerBox.style.borderTopWidth = BoxBorderWidth;
            _centerBox.style.borderBottomWidth = BoxBorderWidth;
            _centerBox.style.borderTopLeftRadius = BoxCornerRadius;
            _centerBox.style.borderTopRightRadius = BoxCornerRadius;
            _centerBox.style.borderBottomLeftRadius = BoxCornerRadius;
            _centerBox.style.borderBottomRightRadius = BoxCornerRadius;

            _label = new Label();
            _label.pickingMode = PickingMode.Ignore;
            _label.style.fontSize = LabelFontSize;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.marginLeft = 0.0f;
            _label.style.marginRight = 0.0f;
            _label.style.marginTop = 0.0f;
            _label.style.marginBottom = 0.0f;
            _centerBox.Add(_label);
            Add(_centerBox);

            _hoverTooltip = new VisualElement();
            _hoverTooltip.pickingMode = PickingMode.Ignore;
            _hoverTooltip.style.position = Position.Absolute;
            _hoverTooltip.style.display = DisplayStyle.None;
            _hoverTooltip.style.minWidth = HoverTooltipMinWidth;
            _hoverTooltip.style.paddingLeft = HoverTooltipPaddingHorizontal;
            _hoverTooltip.style.paddingRight = HoverTooltipPaddingHorizontal;
            _hoverTooltip.style.paddingTop = HoverTooltipPaddingVertical;
            _hoverTooltip.style.paddingBottom = HoverTooltipPaddingVertical;
            _hoverTooltip.style.backgroundColor = new Color(0.05f, 0.05f, 0.05f, 0.98f);
            _hoverTooltip.style.borderLeftWidth = 1.0f;
            _hoverTooltip.style.borderRightWidth = 1.0f;
            _hoverTooltip.style.borderTopWidth = 1.0f;
            _hoverTooltip.style.borderBottomWidth = 1.0f;
            _hoverTooltip.style.borderTopLeftRadius = 4.0f;
            _hoverTooltip.style.borderTopRightRadius = 4.0f;
            _hoverTooltip.style.borderBottomLeftRadius = 4.0f;
            _hoverTooltip.style.borderBottomRightRadius = 4.0f;
            _hoverTooltip.style.justifyContent = Justify.Center;
            _hoverTooltip.style.alignItems = Align.Center;

            _hoverTooltipLabel = new Label();
            _hoverTooltipLabel.pickingMode = PickingMode.Ignore;
            _hoverTooltipLabel.style.fontSize = HoverTooltipFontSize;
            _hoverTooltipLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hoverTooltipLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hoverTooltip.Add(_hoverTooltipLabel);
            Add(_hoverTooltip);

            RegisterCallback<AttachToPanelEvent>(OnAttachedToPanel);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _centerBox.RegisterCallback<GeometryChangedEvent>(OnCenterBoxGeometryChanged);
            _hoverTooltip.RegisterCallback<GeometryChangedEvent>(OnHoverTooltipGeometryChanged);
            _centerBox.RegisterCallback<MouseEnterEvent>(OnCenterBoxMouseEnter);
            _centerBox.RegisterCallback<MouseLeaveEvent>(OnCenterBoxMouseLeave);

            ApplyCastMode(castMode);
            ApplyEdgeColours(outputColour, inputColour);
        }

        public override bool UpdateEdgeControl()
        {
            bool result = base.UpdateEdgeControl();
            UpdateOverlayPositions();
            return result;
        }

        public void ApplyCastMode(CastMode mode)
        {
            _castMode = mode;
            _label.text = GetCastModeAbbreviation(mode);
            _centerBox.style.display = IsCastEdge ? DisplayStyle.Flex : DisplayStyle.None;
            _hoverTooltipLabel.text = BuildTooltipText(mode);
            _hoverTooltip.style.display = DisplayStyle.None;
            UpdateTooltip();
            UpdateOverlayPositions();
            MarkDirtyRepaint();
        }

        public void ApplyEdgeColours(Color outputColour, Color inputColour)
        {
            _outputColour = outputColour;
            _inputColour = inputColour;

            if (edgeControl != null)
            {
                edgeControl.edgeWidth = EdgeWidth;
                edgeControl.outputColor = outputColour;
                edgeControl.inputColor = inputColour;
            }

            Color accentColour = Color.Lerp(outputColour, inputColour, 0.5f);
            Color textColour = Color.Lerp(accentColour, Color.white, 0.35f);
            _centerBox.style.borderLeftColor = accentColour;
            _centerBox.style.borderRightColor = accentColour;
            _centerBox.style.borderTopColor = accentColour;
            _centerBox.style.borderBottomColor = accentColour;
            _label.style.color = textColour;
            _hoverTooltip.style.borderLeftColor = accentColour;
            _hoverTooltip.style.borderRightColor = accentColour;
            _hoverTooltip.style.borderTopColor = accentColour;
            _hoverTooltip.style.borderBottomColor = accentColour;
            _hoverTooltipLabel.style.color = textColour;
            MarkDirtyRepaint();
        }

        private void UpdateTooltip()
        {
            tooltip = string.Empty;
        }

        private void OnGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            UpdateOverlayPositions();
        }

        private void OnAttachedToPanel(AttachToPanelEvent attachToPanelEvent)
        {
            _centerBox.BringToFront();
            _hoverTooltip.BringToFront();
            schedule.Execute(UpdateOverlayPositions).ExecuteLater(0);
        }

        private void OnCenterBoxGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            UpdateOverlayPositions();
        }

        private void OnHoverTooltipGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            UpdateOverlayPositions();
        }

        private void OnCenterBoxMouseEnter(MouseEnterEvent mouseEnterEvent)
        {
            if (!IsCastEdge)
            {
                return;
            }

            _hoverTooltip.style.display = DisplayStyle.Flex;
            UpdateOverlayPositions();
        }

        private void OnCenterBoxMouseLeave(MouseLeaveEvent mouseLeaveEvent)
        {
            _hoverTooltip.style.display = DisplayStyle.None;
        }

        private void UpdateOverlayPositions()
        {
            if (!IsCastEdge || edgeControl == null)
            {
                return;
            }

            _centerBox.BringToFront();
            _hoverTooltip.BringToFront();
            Vector2 center = (edgeControl.from + edgeControl.to) * 0.5f;
            float boxWidth = _centerBox.layout.width > 0.0f ? _centerBox.layout.width : BoxMinWidth;
            float boxHeight = _centerBox.layout.height > 0.0f ? _centerBox.layout.height : BoxMinHeight;

            _centerBox.style.left = center.x - (boxWidth * 0.5f);
            _centerBox.style.top = center.y - (boxHeight * 0.5f);

            float tooltipWidth = _hoverTooltip.layout.width > 0.0f ? _hoverTooltip.layout.width : HoverTooltipMinWidth;
            float tooltipHeight = _hoverTooltip.layout.height > 0.0f ? _hoverTooltip.layout.height : (HoverTooltipFontSize + (HoverTooltipPaddingVertical * 2.0f));
            _hoverTooltip.style.left = center.x - (tooltipWidth * 0.5f);
            _hoverTooltip.style.top = center.y - (boxHeight * 0.5f) - tooltipHeight - HoverTooltipOffsetY;
        }

        private static string GetCastModeAbbreviation(CastMode mode)
        {
            switch (mode)
            {
                case CastMode.FloatToIntFloor:
                    return "Float → Int";
                case CastMode.FloatToIntRound:
                    return "Float → Int";
                case CastMode.FloatToBoolMask:
                    return "Float → Mask";
                case CastMode.IntToBoolMask:
                    return "Int → Mask";
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
