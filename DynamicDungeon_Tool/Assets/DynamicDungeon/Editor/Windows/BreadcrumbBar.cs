using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    /// <summary>
    /// A horizontal trail of clickable breadcrumb labels representing the current
    /// sub-graph navigation depth.  The root level is always the top-level
    /// GenGraph asset name.  Each call to Push() appends one entry and each call
    /// to PopTo() truncates the trail back to the requested depth and fires the
    /// graph-change action so the window can reload the canvas.
    /// </summary>
    public sealed class BreadcrumbBar : VisualElement
    {
        // --- Navigation entry ---

        private sealed class BreadcrumbEntry
        {
            // Graph to restore when this level is popped back to.
            public readonly GenGraph Graph;

            // Display label shown in the trail.
            public readonly string Label;

            // Canvas scroll position at the time the user left this level.
            public Vector3 ScrollOffset;

            // Canvas zoom scale at the time the user left this level.
            public float ZoomScale;

            public BreadcrumbEntry(GenGraph graph, string label)
            {
                Graph = graph;
                Label = label ?? string.Empty;
                ScrollOffset = Vector3.zero;
                ZoomScale = 1.0f;
            }
        }

        // --- Visual constants ---

        private static readonly Color SeparatorColour = new Color(0.55f, 0.55f, 0.55f, 1.0f);
        private static readonly Color ActiveLabelColour = new Color(0.95f, 0.95f, 0.95f, 1.0f);
        private static readonly Color InactiveLabelColour = new Color(0.65f, 0.80f, 1.0f, 1.0f);

        // --- Internal state ---

        // Ordered stack; index 0 = root, last index = current depth.
        private readonly List<BreadcrumbEntry> _entries = new List<BreadcrumbEntry>();

        private readonly VisualElement _container;

        // Fired when the visible graph changes (either Push or PopTo).
        // Arguments: new graph, restored scroll offset, restored zoom scale.
        private readonly Action<GenGraph, Vector3, float> _onGraphChanged;

        // --- Public API ---

        public int Depth
        {
            get
            {
                return _entries.Count - 1;
            }
        }

        public BreadcrumbBar(Action<GenGraph, Vector3, float> onGraphChanged)
        {
            _onGraphChanged = onGraphChanged;

            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingLeft = 8.0f;
            style.paddingRight = 8.0f;
            style.paddingTop = 2.0f;
            style.paddingBottom = 2.0f;
            style.minHeight = 24.0f;
            style.backgroundColor = new Color(0.14f, 0.14f, 0.14f, 1.0f);
            style.borderBottomWidth = 1.0f;
            style.borderBottomColor = new Color(0.10f, 0.10f, 0.10f, 1.0f);

            _container = new VisualElement();
            _container.style.flexDirection = FlexDirection.Row;
            _container.style.alignItems = Align.Center;
            _container.style.flexGrow = 1.0f;
            Add(_container);
        }

        /// <summary>
        /// Records the current canvas viewport state for the active level so it
        /// can be restored when the user navigates back.  Call this just before
        /// pushing a new level.
        /// </summary>
        public void SaveViewportState(Vector3 scrollOffset, float zoomScale)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            BreadcrumbEntry activeEntry = _entries[_entries.Count - 1];
            activeEntry.ScrollOffset = scrollOffset;
            activeEntry.ZoomScale = zoomScale;
        }

        /// <summary>
        /// Appends a new level to the breadcrumb trail and fires the graph-changed
        /// action so the window reloads the canvas with <paramref name="graph"/>.
        /// </summary>
        public void Push(GenGraph graph, string label)
        {
            if (graph == null)
            {
                return;
            }

            _entries.Add(new BreadcrumbEntry(graph, label));
            Rebuild();
            _onGraphChanged?.Invoke(graph, Vector3.zero, 1.0f);
        }

        /// <summary>
        /// Navigates back to the entry at <paramref name="depth"/>, discarding all
        /// entries above it, and fires the graph-changed action with that level's
        /// restored viewport state.
        /// </summary>
        public void PopTo(int depth)
        {
            if (depth < 0 || depth >= _entries.Count)
            {
                return;
            }

            // Truncate to the target depth (inclusive).
            while (_entries.Count - 1 > depth)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            BreadcrumbEntry target = _entries[depth];
            Rebuild();
            _onGraphChanged?.Invoke(target.Graph, target.ScrollOffset, target.ZoomScale);
        }

        /// <summary>
        /// Clears the entire trail and atomically establishes a new root entry,
        /// firing the graph-changed callback exactly once.  Pass <c>null</c> for
        /// <paramref name="graph"/> to clear the trail without loading a graph.
        /// </summary>
        public void ResetTo(GenGraph graph, string label)
        {
            _entries.Clear();

            if (graph == null)
            {
                Rebuild();
                return;
            }

            _entries.Add(new BreadcrumbEntry(graph, label));
            Rebuild();
            _onGraphChanged?.Invoke(graph, Vector3.zero, 1.0f);
        }

        // --- Private helpers ---

        private void Rebuild()
        {
            _container.Clear();

            int entryIndex;
            for (entryIndex = 0; entryIndex < _entries.Count; entryIndex++)
            {
                BreadcrumbEntry entry = _entries[entryIndex];
                bool isActive = entryIndex == _entries.Count - 1;

                if (entryIndex > 0)
                {
                    Label separator = new Label(" > ");
                    separator.style.color = SeparatorColour;
                    separator.style.unityTextAlign = TextAnchor.MiddleCenter;
                    _container.Add(separator);
                }

                int capturedDepth = entryIndex;
                Label crumb = new Label(entry.Label);
                crumb.style.unityTextAlign = TextAnchor.MiddleLeft;
                crumb.style.paddingLeft = 2.0f;
                crumb.style.paddingRight = 2.0f;
                crumb.style.color = isActive ? ActiveLabelColour : InactiveLabelColour;

                if (!isActive)
                {
                    crumb.RegisterCallback<MouseDownEvent>(mouseEvent =>
                    {
                        if (mouseEvent != null && mouseEvent.button == 0)
                        {
                            PopTo(capturedDepth);
                            mouseEvent.StopPropagation();
                        }
                    });

                    crumb.RegisterCallback<MouseEnterEvent>(_ =>
                    {
                        crumb.style.color = new Color(1.0f, 1.0f, 1.0f, 1.0f);
                    });

                    crumb.RegisterCallback<MouseLeaveEvent>(_ =>
                    {
                        crumb.style.color = InactiveLabelColour;
                    });
                }

                _container.Add(crumb);
            }
        }
    }
}
