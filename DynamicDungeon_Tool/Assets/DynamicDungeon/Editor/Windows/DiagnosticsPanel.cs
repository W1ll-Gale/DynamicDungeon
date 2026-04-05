using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DiagnosticsPanel : VisualElement
    {
        private const float CollapsedHeight = 28.0f;
        private const float MinimumExpandedHeight = 90.0f;
        private const float MaximumExpandedHeight = 360.0f;
        private const float ResizeHandleHeight = 4.0f;
        private static readonly Color ResizeHandleDefaultColour = new Color(0.22f, 0.22f, 0.22f, 1.0f);
        private static readonly Color ResizeHandleHoverColour = new Color(0.32f, 0.32f, 0.32f, 1.0f);
        private static readonly Color ResizeHandleActiveColour = new Color(0.42f, 0.42f, 0.42f, 1.0f);

        private readonly Label _collapseGlyphLabel;
        private readonly Label _emptyLabel;
        private readonly ScrollView _scrollView;
        private readonly VisualElement _contentContainer;
        private readonly VisualElement _resizeHandle;

        private DynamicDungeonGraphView _graphView;
        private GenGraph _graph;
        private bool _isCollapsed;
        private bool _isResizing;
        private float _expandedHeight;
        private float _resizeStartHeight;
        private Vector2 _resizeStartMousePosition;

        public DiagnosticsPanel()
        {
            style.flexGrow = 0.0f;
            style.flexDirection = FlexDirection.Column;

            _expandedHeight = 120.0f;

            _resizeHandle = new VisualElement();
            _resizeHandle.style.height = ResizeHandleHeight;
            _resizeHandle.style.backgroundColor = ResizeHandleDefaultColour;
            _resizeHandle.RegisterCallback<MouseDownEvent>(OnResizeHandleMouseDown);
            _resizeHandle.RegisterCallback<MouseMoveEvent>(OnResizeHandleMouseMove);
            _resizeHandle.RegisterCallback<MouseUpEvent>(OnResizeHandleMouseUp);
            _resizeHandle.RegisterCallback<MouseEnterEvent>(OnResizeHandleMouseEnter);
            _resizeHandle.RegisterCallback<MouseLeaveEvent>(OnResizeHandleMouseLeave);
            Add(_resizeHandle);

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 4.0f;
            Add(headerRow);

            Button collapseButton = new Button(ToggleCollapsedState);
            collapseButton.style.width = 22.0f;
            collapseButton.style.minWidth = 22.0f;
            collapseButton.style.height = 18.0f;
            collapseButton.style.marginRight = 6.0f;
            collapseButton.style.paddingLeft = 0.0f;
            collapseButton.style.paddingRight = 0.0f;
            collapseButton.style.paddingTop = 0.0f;
            collapseButton.style.paddingBottom = 0.0f;
            headerRow.Add(collapseButton);

            _collapseGlyphLabel = new Label();
            _collapseGlyphLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            collapseButton.Add(_collapseGlyphLabel);

            Label headerLabel = new Label("Diagnostics");
            headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerLabel.style.flexGrow = 1.0f;
            headerRow.Add(headerLabel);

            _contentContainer = new VisualElement();
            _contentContainer.style.flexGrow = 1.0f;
            _contentContainer.style.flexDirection = FlexDirection.Column;
            Add(_contentContainer);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1.0f;
            _contentContainer.Add(_scrollView);

            _emptyLabel = new Label("No issues");
            _emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f, 1.0f);

            Populate(System.Array.Empty<GraphDiagnostic>());
            ApplyPanelHeight();
        }

        public void SetGraphContext(DynamicDungeonGraphView graphView, GenGraph graph)
        {
            _graphView = graphView;
            _graph = graph;
        }

        public void SetExpandedHeight(float height)
        {
            _expandedHeight = Mathf.Clamp(height, MinimumExpandedHeight, MaximumExpandedHeight);
            ApplyPanelHeight();
        }

        public float GetExpandedHeight()
        {
            return _expandedHeight;
        }

        public void SetCollapsed(bool isCollapsed)
        {
            _isCollapsed = isCollapsed;
            ApplyPanelHeight();
        }

        public bool IsCollapsed()
        {
            return _isCollapsed;
        }

        public void Populate(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            _scrollView.Clear();

            IReadOnlyList<GraphDiagnostic> safeDiagnostics = diagnostics ?? System.Array.Empty<GraphDiagnostic>();
            if (safeDiagnostics.Count == 0)
            {
                _scrollView.Add(_emptyLabel);
                return;
            }

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < safeDiagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = safeDiagnostics[diagnosticIndex];
                _scrollView.Add(CreateDiagnosticEntry(diagnostic));
            }
        }

        private Button CreateDiagnosticEntry(GraphDiagnostic diagnostic)
        {
            string nodeName = ResolveNodeName(diagnostic.NodeId);
            string entryText = string.IsNullOrWhiteSpace(nodeName)
                ? diagnostic.Message ?? string.Empty
                : (diagnostic.Message ?? string.Empty) + " [" + nodeName + "]";

            Button entryButton = new Button(
                () =>
                {
                    if (_graphView != null && !string.IsNullOrWhiteSpace(diagnostic.NodeId))
                    {
                        _graphView.SelectAndFrameNode(diagnostic.NodeId);
                    }
                });

            entryButton.style.flexDirection = FlexDirection.Row;
            entryButton.style.alignItems = Align.FlexStart;
            entryButton.style.justifyContent = Justify.FlexStart;
            entryButton.style.unityTextAlign = TextAnchor.UpperLeft;
            entryButton.style.whiteSpace = WhiteSpace.Normal;
            entryButton.style.marginBottom = 4.0f;
            entryButton.style.paddingLeft = 6.0f;
            entryButton.style.paddingRight = 6.0f;
            entryButton.style.paddingTop = 4.0f;
            entryButton.style.paddingBottom = 4.0f;

            VisualElement severityIcon = new VisualElement();
            severityIcon.style.width = 8.0f;
            severityIcon.style.height = 8.0f;
            severityIcon.style.marginTop = 5.0f;
            severityIcon.style.marginRight = 8.0f;
            severityIcon.style.backgroundColor = ResolveSeverityColour(diagnostic.Severity);
            severityIcon.style.flexShrink = 0.0f;
            entryButton.Add(severityIcon);

            Label messageLabel = new Label(entryText);
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.flexGrow = 1.0f;
            messageLabel.style.unityTextAlign = TextAnchor.UpperLeft;
            entryButton.Add(messageLabel);

            return entryButton;
        }

        private void ToggleCollapsedState()
        {
            _isCollapsed = !_isCollapsed;
            ApplyPanelHeight();
        }

        private void ApplyPanelHeight()
        {
            style.height = _isCollapsed ? CollapsedHeight : _expandedHeight;
            _resizeHandle.style.display = _isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _resizeHandle.style.backgroundColor = ResizeHandleDefaultColour;
            _contentContainer.style.display = _isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _collapseGlyphLabel.text = _isCollapsed ? "▸" : "▾";
        }

        private void OnResizeHandleMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (_isCollapsed || mouseDownEvent.button != 0)
            {
                return;
            }

            _isResizing = true;
            _resizeStartHeight = _expandedHeight;
            _resizeStartMousePosition = mouseDownEvent.mousePosition;
            _resizeHandle.CaptureMouse();
            _resizeHandle.style.backgroundColor = ResizeHandleActiveColour;
            mouseDownEvent.StopPropagation();
        }

        private void OnResizeHandleMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!_isResizing)
            {
                return;
            }

            float deltaY = mouseMoveEvent.mousePosition.y - _resizeStartMousePosition.y;
            _expandedHeight = Mathf.Clamp(_resizeStartHeight - deltaY, MinimumExpandedHeight, MaximumExpandedHeight);
            ApplyPanelHeight();
            mouseMoveEvent.StopPropagation();
        }

        private void OnResizeHandleMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (!_isResizing || mouseUpEvent.button != 0)
            {
                return;
            }

            _isResizing = false;
            if (_resizeHandle.HasMouseCapture())
            {
                _resizeHandle.ReleaseMouse();
            }

            _resizeHandle.style.backgroundColor = ResizeHandleHoverColour;
            mouseUpEvent.StopPropagation();
        }

        private void OnResizeHandleMouseEnter(MouseEnterEvent mouseEnterEvent)
        {
            if (_isCollapsed || _isResizing)
            {
                return;
            }

            _resizeHandle.style.backgroundColor = ResizeHandleHoverColour;
        }

        private void OnResizeHandleMouseLeave(MouseLeaveEvent mouseLeaveEvent)
        {
            if (_isCollapsed || _isResizing)
            {
                return;
            }

            _resizeHandle.style.backgroundColor = ResizeHandleDefaultColour;
        }

        private string ResolveNodeName(string nodeId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return "Graph";
            }

            GenNodeData nodeData = _graph.GetNode(nodeId);
            if (nodeData == null)
            {
                return "Graph";
            }

            if (!string.IsNullOrWhiteSpace(nodeData.NodeName))
            {
                return nodeData.NodeName;
            }

            return nodeData.NodeTypeName ?? "Graph";
        }

        private static Color ResolveSeverityColour(DiagnosticSeverity severity)
        {
            switch (severity)
            {
                case DiagnosticSeverity.Error:
                    return new Color(0.82f, 0.24f, 0.24f, 1.0f);
                case DiagnosticSeverity.Warning:
                    return new Color(0.91f, 0.73f, 0.19f, 1.0f);
                default:
                    return new Color(0.28f, 0.72f, 0.38f, 1.0f);
            }
        }
    }
}
