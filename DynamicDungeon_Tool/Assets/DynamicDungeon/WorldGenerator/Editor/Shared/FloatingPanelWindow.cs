using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Shared
{
    public class FloatingPanelWindow : VisualElement
    {
        private const float HeaderHeight = 28.0f;
        private const float MinimumWidth = 220.0f;
        private const float MinimumHeight = 160.0f;
        private const float ResizeHandleSize = 14.0f;

        private readonly Label _titleLabel;
        private readonly VisualElement _panelContentContainer;
        private readonly VisualElement _resizeHandle;
        private readonly Button _collapseButton;
        private readonly FloatingWindowLayout _layout;

        private Action _layoutChanged;
        private Rect _parentRect = new Rect(0.0f, 0.0f, 1200.0f, 800.0f);
        private Vector2 _dragStartMousePosition;
        private Vector2 _dragStartPosition;
        private Vector2 _resizeStartMousePosition;
        private Vector2 _resizeStartSize;
        private bool _isDragging;
        private bool _isResizing;
        private bool _isCollapsed;
        private float _expandedHeight;
        private float _expandedWidth;

        public override VisualElement contentContainer
        {
            get
            {
                return _panelContentContainer;
            }
        }

        public FloatingPanelWindow(string title, FloatingWindowLayout layout)
        {
            _layout = layout ?? new FloatingWindowLayout();
            _expandedHeight = Mathf.Max(MinimumHeight, _layout.Size.y);
            _expandedWidth = Mathf.Max(MinimumWidth, _layout.Size.x);

            style.position = Position.Absolute;
            style.flexDirection = FlexDirection.Column;
            style.overflow = Overflow.Hidden;
            style.minWidth = MinimumWidth;
            style.minHeight = MinimumHeight;
            style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            style.borderLeftWidth = 1.0f;
            style.borderTopWidth = 1.0f;
            style.borderRightWidth = 1.0f;
            style.borderBottomWidth = 1.0f;
            style.borderLeftColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
            style.borderTopColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
            style.borderRightColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
            style.borderBottomColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);
            style.borderTopLeftRadius = 5.0f;
            style.borderTopRightRadius = 5.0f;
            style.borderBottomLeftRadius = 5.0f;
            style.borderBottomRightRadius = 5.0f;

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0.0f;
            header.style.height = HeaderHeight;
            header.style.paddingLeft = 8.0f;
            header.style.paddingRight = 8.0f;
            header.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1.0f);
            header.style.borderBottomWidth = 1.0f;
            header.style.borderBottomColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            header.RegisterCallback<MouseDownEvent>(OnHeaderMouseDown);
            header.RegisterCallback<MouseMoveEvent>(OnHeaderMouseMove);
            header.RegisterCallback<MouseUpEvent>(OnHeaderMouseUp);
            hierarchy.Add(header);

            _titleLabel = new Label(title ?? string.Empty);
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.flexGrow = 1.0f;
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(_titleLabel);

            _collapseButton = new Button(ToggleCollapsed);
            _collapseButton.text = "\u25BE";
            _collapseButton.tooltip = "Collapse window";
            _collapseButton.style.width = 18.0f;
            _collapseButton.style.minWidth = 18.0f;
            _collapseButton.style.height = 18.0f;
            _collapseButton.style.paddingLeft = 0.0f;
            _collapseButton.style.paddingRight = 0.0f;
            _collapseButton.style.paddingTop = 0.0f;
            _collapseButton.style.paddingBottom = 0.0f;
            _collapseButton.RegisterCallback<MouseDownEvent>(StopHeaderDragFromButton);
            _collapseButton.RegisterCallback<MouseUpEvent>(StopHeaderDragFromButton);
            header.Add(_collapseButton);

            _panelContentContainer = new VisualElement();
            _panelContentContainer.style.flexGrow = 1.0f;
            _panelContentContainer.style.flexDirection = FlexDirection.Column;
            hierarchy.Add(_panelContentContainer);

            _resizeHandle = new VisualElement();
            _resizeHandle.style.position = Position.Absolute;
            _resizeHandle.style.right = 0.0f;
            _resizeHandle.style.bottom = 0.0f;
            _resizeHandle.style.width = ResizeHandleSize;
            _resizeHandle.style.height = ResizeHandleSize;
            _resizeHandle.style.backgroundColor = new Color(0.28f, 0.28f, 0.28f, 0.8f);
            _resizeHandle.style.borderTopLeftRadius = 4.0f;
            _resizeHandle.RegisterCallback<MouseDownEvent>(OnResizeHandleMouseDown);
            _resizeHandle.RegisterCallback<MouseMoveEvent>(OnResizeHandleMouseMove);
            _resizeHandle.RegisterCallback<MouseUpEvent>(OnResizeHandleMouseUp);
            hierarchy.Add(_resizeHandle);

            RegisterCallback<MouseDownEvent>(OnWindowMouseDown);
            RegisterCallback<MouseUpEvent>(OnWindowMouseUp);
            RegisterCallback<WheelEvent>(OnWindowWheel);

            ApplyLayoutToElement();
            ApplyCollapsedState();
        }

        public FloatingWindowLayout LayoutState
        {
            get
            {
                return _layout;
            }
        }

        public bool IsVisibleForTesting
        {
            get
            {
                return style.display != DisplayStyle.None;
            }
        }

        public Rect GetWindowRectForTesting()
        {
            return GetWindowRect();
        }

        public bool IsCollapsedForTesting
        {
            get
            {
                return _isCollapsed;
            }
        }

        protected virtual bool UsesUniformResize
        {
            get
            {
                return false;
            }
        }

        public void SetLayoutChangedCallback(Action layoutChanged)
        {
            _layoutChanged = layoutChanged;
        }

        public void SetVisible(bool isVisible)
        {
            style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (isVisible)
            {
                BringToFront();
                ApplyLayoutToElement();
                ApplyCollapsedState();
            }
        }

        public void SetCollapsed(bool isCollapsed)
        {
            if (_isCollapsed != isCollapsed)
            {
                if (isCollapsed)
                {
                    _expandedWidth = Mathf.Max(MinimumWidth, GetWindowRect().width);
                    _expandedHeight = Mathf.Max(MinimumHeight, GetWindowRect().height);
                }
                else
                {
                    _expandedWidth = Mathf.Max(MinimumWidth, _expandedWidth);
                    _expandedHeight = Mathf.Max(MinimumHeight, _expandedHeight);
                }

                _isCollapsed = isCollapsed;
            }

            ApplyCollapsedState();
        }

        public void UpdateParentRect(Rect parentRect)
        {
            if (parentRect.width <= 0.0f || parentRect.height <= 0.0f)
            {
                return;
            }

            _parentRect = parentRect;
            _layout.ClampToParent(_parentRect);
            ApplyLayoutToElement();
            ApplyCollapsedState();
        }

        public void CaptureCurrentLayout()
        {
            Rect windowRect = GetWindowRect();
            if (_isCollapsed)
            {
                windowRect.width = _expandedWidth;
                windowRect.height = _expandedHeight;
            }

            _layout.Capture(windowRect, _parentRect);
        }

        private void OnWindowMouseDown(MouseDownEvent mouseDownEvent)
        {
            BringToFront();
            mouseDownEvent.StopPropagation();
        }

        private void OnWindowMouseUp(MouseUpEvent mouseUpEvent)
        {
            mouseUpEvent.StopPropagation();
        }

        private void OnWindowWheel(WheelEvent wheelEvent)
        {
            wheelEvent.StopPropagation();
        }

        private void OnHeaderMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent.button != 0)
            {
                return;
            }

            BringToFront();
            _isDragging = true;
            _dragStartMousePosition = mouseDownEvent.mousePosition;
            _dragStartPosition = GetWindowRect().position;
            ((VisualElement)mouseDownEvent.currentTarget).CaptureMouse();
            mouseDownEvent.StopPropagation();
        }

        private void OnHeaderMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!_isDragging)
            {
                return;
            }

            Vector2 delta = mouseMoveEvent.mousePosition - _dragStartMousePosition;
            Rect windowRect = GetWindowRect();
            windowRect.position = _dragStartPosition + delta;
            SetWindowRect(_isCollapsed ? ClampCollapsedRectToParent(windowRect) : ClampRectToParent(windowRect));
            mouseMoveEvent.StopPropagation();
        }

        private void OnHeaderMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (!_isDragging || mouseUpEvent.button != 0)
            {
                return;
            }

            _isDragging = false;
            VisualElement header = (VisualElement)mouseUpEvent.currentTarget;
            if (header.HasMouseCapture())
            {
                header.ReleaseMouse();
            }

            CaptureCurrentLayout();
            _layoutChanged?.Invoke();
            mouseUpEvent.StopPropagation();
        }

        private void OnResizeHandleMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent.button != 0)
            {
                return;
            }

            BringToFront();
            _isResizing = true;
            _resizeStartMousePosition = mouseDownEvent.mousePosition;
            _resizeStartSize = GetWindowRect().size;
            ((VisualElement)mouseDownEvent.currentTarget).CaptureMouse();
            mouseDownEvent.StopPropagation();
        }

        private void OnResizeHandleMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!_isResizing)
            {
                return;
            }

            Vector2 delta = mouseMoveEvent.mousePosition - _resizeStartMousePosition;
            Rect windowRect = GetWindowRect();
            if (UsesUniformResize)
            {
                float widthScale = _resizeStartSize.x <= 0.0f
                    ? 1.0f
                    : (_resizeStartSize.x + delta.x) / _resizeStartSize.x;
                float heightScale = _resizeStartSize.y <= 0.0f
                    ? 1.0f
                    : (_resizeStartSize.y + delta.y) / _resizeStartSize.y;
                float uniformScale = Mathf.Max(
                    MinimumWidth / Mathf.Max(1.0f, _resizeStartSize.x),
                    MinimumHeight / Mathf.Max(1.0f, _resizeStartSize.y),
                    widthScale,
                    heightScale);

                windowRect.size = new Vector2(
                    _resizeStartSize.x * uniformScale,
                    _resizeStartSize.y * uniformScale);
            }
            else
            {
                windowRect.size = new Vector2(
                    Mathf.Max(MinimumWidth, _resizeStartSize.x + delta.x),
                    Mathf.Max(MinimumHeight, _resizeStartSize.y + delta.y));
            }

            SetWindowRect(ClampRectToParent(windowRect));
            mouseMoveEvent.StopPropagation();
        }

        private void OnResizeHandleMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (!_isResizing || mouseUpEvent.button != 0)
            {
                return;
            }

            _isResizing = false;
            VisualElement handle = (VisualElement)mouseUpEvent.currentTarget;
            if (handle.HasMouseCapture())
            {
                handle.ReleaseMouse();
            }

            _expandedHeight = Mathf.Max(MinimumHeight, GetWindowRect().height);
            _expandedWidth = Mathf.Max(MinimumWidth, GetWindowRect().width);
            CaptureCurrentLayout();
            _layoutChanged?.Invoke();
            mouseUpEvent.StopPropagation();
        }

        private void ApplyLayoutToElement()
        {
            Rect rect = _layout.GetRect(_parentRect);
            if (!_isCollapsed)
            {
                _expandedWidth = Mathf.Max(MinimumWidth, rect.width);
                _expandedHeight = Mathf.Max(MinimumHeight, rect.height);
            }

            rect.width = _expandedWidth;
            if (_isCollapsed)
            {
                rect.height = HeaderHeight;
            }

            SetWindowRect(rect);
        }

        private Rect GetWindowRect()
        {
            float x = style.left.keyword == StyleKeyword.Undefined ? style.left.value.value : resolvedStyle.left;
            float y = style.top.keyword == StyleKeyword.Undefined ? style.top.value.value : resolvedStyle.top;
            float width = style.width.keyword == StyleKeyword.Undefined ? style.width.value.value : resolvedStyle.width;
            float height = style.height.keyword == StyleKeyword.Undefined ? style.height.value.value : resolvedStyle.height;

            if (float.IsNaN(width) || width <= 0.0f)
            {
                width = _layout.Size.x;
            }

            if (float.IsNaN(height) || height <= 0.0f)
            {
                height = _isCollapsed ? HeaderHeight : _layout.Size.y;
            }

            return new Rect(x, y, width, height);
        }

        private void SetWindowRect(Rect windowRect)
        {
            style.left = windowRect.x;
            style.top = windowRect.y;
            style.width = windowRect.width;
            style.height = windowRect.height;
        }

        private Rect ClampRectToParent(Rect rect)
        {
            rect.width = Mathf.Clamp(rect.width, MinimumWidth, Mathf.Max(MinimumWidth, _parentRect.width));
            rect.height = Mathf.Clamp(rect.height, MinimumHeight, Mathf.Max(MinimumHeight, _parentRect.height));
            rect.x = Mathf.Clamp(rect.x, 0.0f, Mathf.Max(0.0f, _parentRect.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0.0f, Mathf.Max(0.0f, _parentRect.height - rect.height));
            return rect;
        }

        private Rect ClampCollapsedRectToParent(Rect rect)
        {
            rect.width = Mathf.Clamp(rect.width, MinimumWidth, Mathf.Max(MinimumWidth, _parentRect.width));
            rect.x = Mathf.Clamp(rect.x, 0.0f, Mathf.Max(0.0f, _parentRect.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0.0f, Mathf.Max(0.0f, _parentRect.height - rect.height));
            return rect;
        }

        private void ToggleCollapsed()
        {
            SetCollapsed(!_isCollapsed);
            _layoutChanged?.Invoke();
        }

        private void ApplyCollapsedState()
        {
            _panelContentContainer.style.display = _isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _resizeHandle.style.display = _isCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _collapseButton.text = _isCollapsed ? "\u25B8" : "\u25BE";
            _collapseButton.tooltip = _isCollapsed ? "Expand window" : "Collapse window";
            style.minHeight = _isCollapsed ? HeaderHeight : MinimumHeight;

            Rect rect = GetWindowRect();
            rect.width = Mathf.Max(MinimumWidth, _expandedWidth);
            rect.height = _isCollapsed ? HeaderHeight : Mathf.Max(MinimumHeight, _expandedHeight);
            SetWindowRect(ClampCollapsedRectToParent(rect));
        }

        private static void StopHeaderDragFromButton(EventBase eventBase)
        {
            eventBase.StopPropagation();
        }
    }
}
