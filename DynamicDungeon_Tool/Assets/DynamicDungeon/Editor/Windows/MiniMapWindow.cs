using DynamicDungeon.Editor.Nodes;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class MiniMapWindow : FloatingPanelWindow
    {
        private const float FramePadding = 8.0f;
        private const float InnerPadding = 10.0f;
        private const float DefaultGraphPadding = 80.0f;
        private const float GraphZoomStep = 1.12f;
        private static readonly float MinGraphZoomScale = ContentZoomer.DefaultMinScale;
        private static readonly float MaxGraphZoomScale = ContentZoomer.DefaultMaxScale;
        private static readonly Color FrameBackgroundColor = new Color(0.17f, 0.17f, 0.17f, 1.0f);
        private static readonly Color CanvasBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
        private static readonly Color FrameBorderColor = new Color(0.78f, 0.78f, 0.78f, 0.75f);
        private static readonly Color NodeFillColor = new Color(0.58f, 0.58f, 0.58f, 1.0f);
        private static readonly Color GroupFillColor = new Color(0.42f, 0.42f, 0.42f, 0.65f);
        private static readonly Color ViewportStrokeColor = new Color(0.84f, 0.84f, 0.18f, 1.0f);

        private readonly Label _zoomLabel;
        private readonly MiniMapCanvas _canvas;
        private DynamicDungeonGraphView _graphView;

        protected override bool UsesUniformResize
        {
            get
            {
                return true;
            }
        }

        public MiniMapWindow(FloatingWindowLayout layout, DynamicDungeonGraphView graphView)
            : base("MiniMap", layout)
        {
            name = "DynamicDungeonMiniMapWindow";

            contentContainer.style.paddingLeft = FramePadding;
            contentContainer.style.paddingRight = FramePadding;
            contentContainer.style.paddingTop = FramePadding;
            contentContainer.style.paddingBottom = FramePadding;

            VisualElement frame = new VisualElement();
            frame.style.flexGrow = 1.0f;
            frame.style.flexDirection = FlexDirection.Column;
            frame.style.backgroundColor = FrameBackgroundColor;
            frame.style.borderLeftWidth = 1.0f;
            frame.style.borderTopWidth = 1.0f;
            frame.style.borderRightWidth = 1.0f;
            frame.style.borderBottomWidth = 1.0f;
            frame.style.borderLeftColor = FrameBorderColor;
            frame.style.borderTopColor = FrameBorderColor;
            frame.style.borderRightColor = FrameBorderColor;
            frame.style.borderBottomColor = FrameBorderColor;
            frame.style.borderTopLeftRadius = 4.0f;
            frame.style.borderTopRightRadius = 4.0f;
            frame.style.borderBottomLeftRadius = 4.0f;
            frame.style.borderBottomRightRadius = 4.0f;
            frame.style.overflow = Overflow.Hidden;
            contentContainer.Add(frame);

            _zoomLabel = new Label("MiniMap 1.00x");
            _canvas = new MiniMapCanvas(this);
            _canvas.style.flexGrow = 1.0f;
            _canvas.style.position = Position.Relative;
            _canvas.style.backgroundColor = CanvasBackgroundColor;
            frame.Add(_canvas);

            _zoomLabel.style.position = Position.Absolute;
            _zoomLabel.style.left = 8.0f;
            _zoomLabel.style.top = 6.0f;
            _zoomLabel.style.paddingLeft = 6.0f;
            _zoomLabel.style.paddingRight = 6.0f;
            _zoomLabel.style.paddingTop = 2.0f;
            _zoomLabel.style.paddingBottom = 2.0f;
            _zoomLabel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            _zoomLabel.style.borderTopLeftRadius = 3.0f;
            _zoomLabel.style.borderTopRightRadius = 3.0f;
            _zoomLabel.style.borderBottomLeftRadius = 3.0f;
            _zoomLabel.style.borderBottomRightRadius = 3.0f;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _zoomLabel.style.color = new Color(0.86f, 0.86f, 0.86f, 1.0f);
            _zoomLabel.style.fontSize = 10.0f;
            _zoomLabel.pickingMode = PickingMode.Ignore;
            _canvas.Add(_zoomLabel);

            AttachToGraphView(graphView);
            schedule.Execute(RefreshMiniMap).Every(16);
        }

        public void AttachToGraphView(DynamicDungeonGraphView graphView)
        {
            _graphView = graphView;
            if (_graphView == null)
            {
                return;
            }

            _graphView.SetViewTransformChangedCallback(RefreshMiniMap);
            _graphView.RegisterCallback<GeometryChangedEvent>(OnGraphViewGeometryChanged);
            RefreshMiniMap();
        }

        private void OnGraphViewGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            RefreshMiniMap();
        }

        private void RefreshMiniMap()
        {
            UpdateZoomLabel();
            _canvas.MarkDirtyRepaint();
        }

        private void UpdateZoomLabel()
        {
            if (_graphView == null)
            {
                _zoomLabel.text = "1.00x";
                return;
            }

            Vector3 scrollOffset;
            float zoomScale;
            _graphView.GetViewportState(out scrollOffset, out zoomScale);
            _zoomLabel.text = zoomScale.ToString("0.00") + "x";
        }

        private bool TryGetRenderData(out MiniMapRenderData renderData)
        {
            renderData = default;
            if (_graphView == null || _graphView.layout.width <= 0.0f || _graphView.layout.height <= 0.0f)
            {
                return false;
            }

            Rect canvasRect = _canvas.contentRect;
            if (canvasRect.width <= 0.0f || canvasRect.height <= 0.0f)
            {
                return false;
            }

            Rect viewportRect = GetViewportRectInGraphSpace();
            Rect contentBounds = GetGraphContentBounds();
            Rect displayBounds = GetStableDisplayBounds(contentBounds, viewportRect);

            Rect drawRect = new Rect(
                InnerPadding,
                InnerPadding,
                Mathf.Max(1.0f, canvasRect.width - (InnerPadding * 2.0f)),
                Mathf.Max(1.0f, canvasRect.height - (InnerPadding * 2.0f)));

            float scale = Mathf.Min(drawRect.width / displayBounds.width, drawRect.height / displayBounds.height);
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0.0f)
            {
                scale = 1.0f;
            }

            Vector2 offset = new Vector2(
                drawRect.x + ((drawRect.width - (displayBounds.width * scale)) * 0.5f) - (displayBounds.xMin * scale),
                drawRect.y + ((drawRect.height - (displayBounds.height * scale)) * 0.5f) - (displayBounds.yMin * scale));

            renderData = new MiniMapRenderData(drawRect, displayBounds, viewportRect, scale, offset);
            return true;
        }

        private Rect GetGraphContentBounds()
        {
            bool hasBounds = false;
            Rect bounds = default;

            foreach (GraphElement element in _graphView.graphElements)
            {
                if (element == null || element is Edge)
                {
                    continue;
                }

                Rect elementRect = element.GetPosition();
                if (elementRect.width <= 0.0f || elementRect.height <= 0.0f)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = elementRect;
                    hasBounds = true;
                    continue;
                }

                bounds = UnionRects(bounds, elementRect);
            }

            if (hasBounds)
            {
                return bounds;
            }

            return new Rect(-100.0f, -100.0f, 200.0f, 200.0f);
        }

        private Rect GetStableDisplayBounds(Rect contentBounds, Rect viewportRect)
        {
            Rect paddedContentBounds = new Rect(
                contentBounds.xMin - DefaultGraphPadding,
                contentBounds.yMin - DefaultGraphPadding,
                contentBounds.width + (DefaultGraphPadding * 2.0f),
                contentBounds.height + (DefaultGraphPadding * 2.0f));

            float stableWidth = Mathf.Max(paddedContentBounds.width, viewportRect.width + (DefaultGraphPadding * 2.0f), 1.0f);
            float stableHeight = Mathf.Max(paddedContentBounds.height, viewportRect.height + (DefaultGraphPadding * 2.0f), 1.0f);
            Vector2 stableCenter = paddedContentBounds.center;

            return new Rect(
                stableCenter.x - (stableWidth * 0.5f),
                stableCenter.y - (stableHeight * 0.5f),
                stableWidth,
                stableHeight);
        }

        private Rect GetViewportRectInGraphSpace()
        {
            Vector3 scrollOffset;
            float zoomScale;
            _graphView.GetViewportState(out scrollOffset, out zoomScale);

            float safeZoomScale = Mathf.Approximately(zoomScale, 0.0f) ? 1.0f : zoomScale;
            Vector2 topLeft = new Vector2(
                -scrollOffset.x / safeZoomScale,
                -scrollOffset.y / safeZoomScale);
            Vector2 size = new Vector2(
                _graphView.layout.width / safeZoomScale,
                _graphView.layout.height / safeZoomScale);
            Vector2 bottomRight = topLeft + size;

            return Rect.MinMaxRect(
                Mathf.Min(topLeft.x, bottomRight.x),
                Mathf.Min(topLeft.y, bottomRight.y),
                Mathf.Max(topLeft.x, bottomRight.x),
                Mathf.Max(topLeft.y, bottomRight.y));
        }

        private Vector2 GraphToMiniMap(Vector2 graphPoint, MiniMapRenderData renderData)
        {
            return new Vector2(
                (graphPoint.x * renderData.Scale) + renderData.Offset.x,
                (graphPoint.y * renderData.Scale) + renderData.Offset.y);
        }

        private Rect GraphToMiniMap(Rect graphRect, MiniMapRenderData renderData)
        {
            Vector2 topLeft = GraphToMiniMap(graphRect.min, renderData);
            Vector2 bottomRight = GraphToMiniMap(graphRect.max, renderData);
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        private Vector2 MiniMapToGraph(Vector2 miniMapPoint, MiniMapRenderData renderData)
        {
            return new Vector2(
                (miniMapPoint.x - renderData.Offset.x) / renderData.Scale,
                (miniMapPoint.y - renderData.Offset.y) / renderData.Scale);
        }

        private void CenterViewportOnGraphPoint(Vector2 graphPoint)
        {
            if (_graphView == null || _graphView.layout.width <= 0.0f || _graphView.layout.height <= 0.0f)
            {
                return;
            }

            Vector3 scrollOffset;
            float zoomScale;
            _graphView.GetViewportState(out scrollOffset, out zoomScale);
            SetViewportCenterAndZoom(graphPoint, zoomScale);
        }

        private bool TryFocusNodeAtMiniMapPosition(Vector2 localMousePosition)
        {
            MiniMapRenderData renderData;
            if (!TryGetRenderData(out renderData))
            {
                return false;
            }

            foreach (GraphElement element in _graphView.graphElements)
            {
                GenNodeView nodeView = element as GenNodeView;
                if (nodeView == null)
                {
                    continue;
                }

                Rect nodeRect = nodeView.GetPosition();
                if (nodeRect.width <= 0.0f || nodeRect.height <= 0.0f)
                {
                    continue;
                }

                Rect miniMapRect = GraphToMiniMap(nodeRect, renderData);
                if (!miniMapRect.Contains(localMousePosition))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(nodeView.NodeData.NodeId))
                {
                    _graphView.SelectAndFrameNode(nodeView.NodeData.NodeId);
                }
                else
                {
                    CenterViewportOnGraphPoint(nodeRect.center);
                }

                RefreshMiniMap();
                return true;
            }

            return false;
        }

        private void SetViewportCenterAndZoom(Vector2 graphPoint, float zoomScale)
        {
            Vector3 newPosition = new Vector3(
                (_graphView.layout.width * 0.5f) - (graphPoint.x * zoomScale),
                (_graphView.layout.height * 0.5f) - (graphPoint.y * zoomScale),
                0.0f);
            _graphView.UpdateViewTransform(newPosition, new Vector3(zoomScale, zoomScale, 1.0f));
            RefreshMiniMap();
        }

        private void ZoomViewport(float wheelDeltaY)
        {
            if (_graphView == null)
            {
                return;
            }

            Rect viewportRect = GetViewportRectInGraphSpace();
            Vector3 scrollOffset;
            float zoomScale;
            _graphView.GetViewportState(out scrollOffset, out zoomScale);

            float zoomFactor = wheelDeltaY < 0.0f ? GraphZoomStep : 1.0f / GraphZoomStep;
            float newZoomScale = Mathf.Clamp(zoomScale * zoomFactor, MinGraphZoomScale, MaxGraphZoomScale);
            if (Mathf.Approximately(newZoomScale, zoomScale))
            {
                return;
            }

            SetViewportCenterAndZoom(viewportRect.center, newZoomScale);
        }

        private static Rect UnionRects(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static void DrawFilledRect(Painter2D painter, Rect rect, Color fillColor)
        {
            painter.fillColor = fillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawStrokedRect(Painter2D painter, Rect rect, Color strokeColor, float lineWidth)
        {
            painter.strokeColor = strokeColor;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Stroke();
        }

        private readonly struct MiniMapRenderData
        {
            public readonly Rect DrawRect;
            public readonly Rect DisplayBounds;
            public readonly Rect ViewportRect;
            public readonly float Scale;
            public readonly Vector2 Offset;

            public MiniMapRenderData(Rect drawRect, Rect displayBounds, Rect viewportRect, float scale, Vector2 offset)
            {
                DrawRect = drawRect;
                DisplayBounds = displayBounds;
                ViewportRect = viewportRect;
                Scale = scale;
                Offset = offset;
            }
        }

        private sealed class MiniMapCanvas : VisualElement
        {
            private readonly MiniMapWindow _owner;
            private bool _isDragging;
            private Vector2 _dragViewportCenterOffset;

            public MiniMapCanvas(MiniMapWindow owner)
            {
                _owner = owner;
                generateVisualContent += OnGenerateVisualContent;
                RegisterCallback<MouseDownEvent>(OnMouseDown);
                RegisterCallback<MouseMoveEvent>(OnMouseMove);
                RegisterCallback<MouseUpEvent>(OnMouseUp);
                RegisterCallback<WheelEvent>(OnWheel);
            }

            private void OnGenerateVisualContent(MeshGenerationContext context)
            {
                MiniMapRenderData renderData;
                if (!_owner.TryGetRenderData(out renderData))
                {
                    return;
                }

                Painter2D painter = context.painter2D;
                MiniMapWindow.DrawStrokedRect(painter, renderData.DrawRect, FrameBorderColor, 1.0f);

                foreach (GraphElement element in _owner._graphView.graphElements)
                {
                    if (element == null || element is Edge)
                    {
                        continue;
                    }

                    Rect elementRect = element.GetPosition();
                    if (elementRect.width <= 0.0f || elementRect.height <= 0.0f)
                    {
                        continue;
                    }

                    Rect miniMapRect = _owner.GraphToMiniMap(elementRect, renderData);
                    Color fillColor = element is Group ? GroupFillColor : NodeFillColor;
                    MiniMapWindow.DrawFilledRect(painter, miniMapRect, fillColor);
                }

                Rect viewportRect = _owner.GraphToMiniMap(renderData.ViewportRect, renderData);
                MiniMapWindow.DrawStrokedRect(painter, viewportRect, ViewportStrokeColor, 1.5f);
            }

            private void OnMouseDown(MouseDownEvent mouseDownEvent)
            {
                if (mouseDownEvent.button != 0)
                {
                    return;
                }

                if (_owner.TryFocusNodeAtMiniMapPosition(mouseDownEvent.localMousePosition))
                {
                    mouseDownEvent.StopPropagation();
                    return;
                }

                _isDragging = true;
                UpdateDragViewportCenterOffset(mouseDownEvent.localMousePosition);
                this.CaptureMouse();
                CenterViewport(mouseDownEvent.localMousePosition);
                mouseDownEvent.StopPropagation();
            }

            private void OnMouseMove(MouseMoveEvent mouseMoveEvent)
            {
                if (!_isDragging)
                {
                    return;
                }

                CenterViewport(mouseMoveEvent.localMousePosition);
                mouseMoveEvent.StopPropagation();
            }

            private void OnMouseUp(MouseUpEvent mouseUpEvent)
            {
                if (!_isDragging || mouseUpEvent.button != 0)
                {
                    return;
                }

                _isDragging = false;
                if (this.HasMouseCapture())
                {
                    this.ReleaseMouse();
                }

                mouseUpEvent.StopPropagation();
            }

            private void OnWheel(WheelEvent wheelEvent)
            {
                _owner.ZoomViewport(wheelEvent.delta.y);
                wheelEvent.StopPropagation();
            }

            private void UpdateDragViewportCenterOffset(Vector2 localMousePosition)
            {
                MiniMapRenderData renderData;
                if (!_owner.TryGetRenderData(out renderData))
                {
                    _dragViewportCenterOffset = Vector2.zero;
                    return;
                }

                Rect viewportRect = _owner.GraphToMiniMap(renderData.ViewportRect, renderData);
                if (!viewportRect.Contains(localMousePosition))
                {
                    _dragViewportCenterOffset = Vector2.zero;
                    return;
                }

                Vector2 graphPoint = _owner.MiniMapToGraph(localMousePosition, renderData);
                _dragViewportCenterOffset = renderData.ViewportRect.center - graphPoint;
            }

            private void CenterViewport(Vector2 localMousePosition)
            {
                MiniMapRenderData renderData;
                if (!_owner.TryGetRenderData(out renderData))
                {
                    return;
                }

                Vector2 graphPoint = _owner.MiniMapToGraph(localMousePosition, renderData);
                _owner.CenterViewportOnGraphPoint(graphPoint + _dragViewportCenterOffset);
            }
        }
    }
}
