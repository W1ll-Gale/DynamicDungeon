using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Shared
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

        private Func<string, string> _resolveElementName;
        private Func<string, bool> _focusElement;
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

            Populate(Array.Empty<SharedGraphDiagnostic>());
            ApplyPanelHeight();
        }

        public void SetContext(Func<string, string> resolveElementName, Func<string, bool> focusElement)
        {
            _resolveElementName = resolveElementName;
            _focusElement = focusElement;
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

        public void Populate(IReadOnlyList<SharedGraphDiagnostic> diagnostics)
        {
            _scrollView.Clear();

            IReadOnlyList<SharedGraphDiagnostic> safeDiagnostics = diagnostics ?? Array.Empty<SharedGraphDiagnostic>();
            if (safeDiagnostics.Count == 0)
            {
                _scrollView.Add(_emptyLabel);
                return;
            }

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < safeDiagnostics.Count; diagnosticIndex++)
            {
                SharedGraphDiagnostic diagnostic = safeDiagnostics[diagnosticIndex];
                _scrollView.Add(CreateDiagnosticEntry(diagnostic));
            }
        }

        private Button CreateDiagnosticEntry(SharedGraphDiagnostic diagnostic)
        {
            string elementName = ResolveElementName(diagnostic.ElementId);
            string entryText = string.IsNullOrWhiteSpace(elementName)
                ? diagnostic.Message ?? string.Empty
                : (diagnostic.Message ?? string.Empty) + " [" + elementName + "]";

            Button entryButton = new Button(
                () =>
                {
                    if (_focusElement != null && !string.IsNullOrWhiteSpace(diagnostic.ElementId))
                    {
                        _focusElement(diagnostic.ElementId);
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
            entryButton.AddManipulator(new ContextualMenuManipulator(
                menuPopulateEvent =>
                {
                    menuPopulateEvent.menu.AppendAction(
                        "Copy Error",
                        _ => GUIUtility.systemCopyBuffer = entryText);
                }));

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

        private string ResolveElementName(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return "Graph";
            }

            if (_resolveElementName == null)
            {
                return "Graph";
            }

            string resolvedName = _resolveElementName(elementId);
            return string.IsNullOrWhiteSpace(resolvedName) ? "Graph" : resolvedName;
        }

        private static Color ResolveSeverityColour(SharedDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case SharedDiagnosticSeverity.Info:
                    return new Color(0.28f, 0.72f, 0.38f, 1.0f);
                case SharedDiagnosticSeverity.Error:
                    return new Color(0.82f, 0.24f, 0.24f, 1.0f);
                case SharedDiagnosticSeverity.Warning:
                    return new Color(0.91f, 0.73f, 0.19f, 1.0f);
                default:
                    return new Color(0.28f, 0.72f, 0.38f, 1.0f);
            }
        }
    }
}
