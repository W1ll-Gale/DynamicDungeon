using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonEditorWindow : EditorWindow
    {
        private const string IdleStatusText = "Idle";
        private const string NoGraphTitle = "No Graph";
        private const float DiagnosticsPanelHeight = 120.0f;
        private const string AutoSavePrefsKey = "DynamicDungeon.AutoSave";
        private const string CanvasScrollXPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollX";
        private const string CanvasScrollYPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollY";
        private const string CanvasScrollZPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollZ";
        private const string CanvasZoomPrefsKey = "DynamicDungeon.EditorWindow.CanvasZoom";
        private const string CanvasViewportGraphGuidPrefsKey = "DynamicDungeon.EditorWindow.CanvasViewportGraphGuid";

        private ObjectField _graphField;
        private Label _statusLabel;
        private ToolbarToggle _autoSaveToggle;
        private BreadcrumbBar _breadcrumbBar;
        private DynamicDungeonGraphView _graphView;
        private GenerationOrchestrator _generationOrchestrator;
        private DiagnosticsPanel _diagnosticsPanel;
        private bool _skipNextPreviewRefresh;

        [SerializeField]
        private GenGraph _loadedGraph;

        [SerializeField]
        private bool _isDiagnosticsPanelCollapsed;

        [SerializeField]
        private float _diagnosticsPanelExpandedHeight = DiagnosticsPanelHeight;

        private bool AutoSave
        {
            get
            {
                return EditorPrefs.GetBool(AutoSavePrefsKey, false);
            }
            set
            {
                EditorPrefs.SetBool(AutoSavePrefsKey, value);
            }
        }

        [MenuItem("DynamicDungeon/Graph Editor")]
        public static void OpenWindow()
        {
            DynamicDungeonEditorWindow window = GetWindow<DynamicDungeonEditorWindow>();
            window.minSize = new Vector2(640.0f, 360.0f);
            window.Show();
            window.RefreshWindowTitle();
        }

        public static void OpenGraph(GenGraph graph)
        {
            DynamicDungeonEditorWindow window = GetWindow<DynamicDungeonEditorWindow>();
            window.minSize = new Vector2(640.0f, 360.0f);
            window.Show();
            window.LoadGraph(graph);
        }

        [OnOpenAsset]
        public static bool TryOpenGraphAsset(int instanceId, int line)
        {
            GenGraph graph = Selection.activeObject as GenGraph;
            if (graph == null)
            {
                return false;
            }

            OpenGraph(graph);
            return true;
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            Toolbar toolbar = BuildToolbar();
            rootVisualElement.Add(toolbar);

            _breadcrumbBar = new BreadcrumbBar(OnBreadcrumbGraphChanged);
            rootVisualElement.Add(_breadcrumbBar);

            _graphView = new DynamicDungeonGraphView();
            rootVisualElement.Add(_graphView);

            _diagnosticsPanel = BuildDiagnosticsPanel();
            rootVisualElement.Add(_diagnosticsPanel);

            _generationOrchestrator = new GenerationOrchestrator(_graphView, SetStatus, OnDiagnosticsUpdated);
            _graphView.SetGenerationOrchestrator(_generationOrchestrator);
            _graphView.SetAfterMutationCallback(OnAfterGraphMutation);
            _graphView.SetSubGraphEnterCallback(OnEnterSubGraph);

            _graphField.SetValueWithoutNotify(_loadedGraph);

            if (_loadedGraph != null)
            {
                RestoreGraphAfterReload();
            }
            else
            {
                _generationOrchestrator.SetGraph(null);
                _diagnosticsPanel.SetGraphContext(_graphView, null);
            }

            SetStatus(IdleStatusText);
            RefreshWindowTitle();
        }

        private void OnEnable()
        {
            RefreshWindowTitle();
        }

        private void OnDisable()
        {
            SaveViewportPreferences();

            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanelExpandedHeight = _diagnosticsPanel.GetExpandedHeight();
                _isDiagnosticsPanelCollapsed = _diagnosticsPanel.IsCollapsed();
            }

            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.Dispose();
                _generationOrchestrator = null;
            }
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();

            _graphField = new ObjectField("Graph");
            _graphField.objectType = typeof(GenGraph);
            _graphField.allowSceneObjects = false;
            _graphField.style.minWidth = 260.0f;
            _graphField.RegisterValueChangedCallback(OnGraphFieldValueChanged);
            toolbar.Add(_graphField);

            ToolbarButton generateButton = new ToolbarButton(GenerateGraph);
            generateButton.text = "Generate";
            toolbar.Add(generateButton);

            ToolbarButton cancelButton = new ToolbarButton(CancelGeneration);
            cancelButton.text = "Cancel";
            toolbar.Add(cancelButton);

            _autoSaveToggle = new ToolbarToggle();
            _autoSaveToggle.text = "Auto Save";
            _autoSaveToggle.SetValueWithoutNotify(AutoSave);
            _autoSaveToggle.RegisterValueChangedCallback(OnAutoSaveToggleChanged);
            toolbar.Add(_autoSaveToggle);

            _statusLabel = new Label(IdleStatusText);
            _statusLabel.style.marginLeft = 8.0f;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(_statusLabel);

            return toolbar;
        }

        private DiagnosticsPanel BuildDiagnosticsPanel()
        {
            DiagnosticsPanel diagnosticsPanel = new DiagnosticsPanel();
            diagnosticsPanel.style.flexShrink = 0.0f;
            diagnosticsPanel.style.borderTopWidth = 1.0f;
            diagnosticsPanel.style.borderTopColor = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            diagnosticsPanel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            diagnosticsPanel.style.paddingLeft = 8.0f;
            diagnosticsPanel.style.paddingRight = 8.0f;
            diagnosticsPanel.style.paddingTop = 0.0f;
            diagnosticsPanel.style.paddingBottom = 6.0f;
            diagnosticsPanel.SetExpandedHeight(_diagnosticsPanelExpandedHeight);
            diagnosticsPanel.SetCollapsed(_isDiagnosticsPanelCollapsed);
            return diagnosticsPanel;
        }

        private void CancelGeneration()
        {
            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.CancelGeneration();
            }
        }

        private void GenerateGraph()
        {
            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.GenerateAll();
            }
        }

        private void LoadGraph(GenGraph graph)
        {
            _loadedGraph = graph;

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(graph);
            }

            // Reset the breadcrumb trail whenever a new top-level graph is picked
            // from the ObjectField — navigation history for the previous graph is
            // no longer meaningful.
            if (_breadcrumbBar != null)
            {
                // Flush all entries by popping past index 0 (BreadcrumbBar.PopTo
                // discards silently when depth is out of range), then Push the new
                // root.  Push fires OnBreadcrumbGraphChanged which loads the canvas,
                // so we return early to avoid a second load via LoadGraphInCanvas.
                _breadcrumbBar.ResetTo(graph, graph != null ? graph.name : string.Empty);

                if (graph != null)
                {
                    return;
                }
            }

            LoadGraphInCanvas(graph, Vector3.zero, 1.0f);
        }

        /// <summary>
        /// Reloads the canvas, orchestrator, and diagnostics panel for
        /// <paramref name="graph"/> and restores the supplied viewport state.
        /// This is the single point through which all graph-change events
        /// (direct load, Push, PopTo) route their canvas updates.
        /// </summary>
        private void LoadGraphInCanvas(GenGraph graph, Vector3 scrollOffset, float zoomScale)
        {
            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.SetGraph(graph);
            }

            if (_graphView != null)
            {
                if (graph == null)
                {
                    _graphView.ClearGraph();
                }
                else
                {
                    _graphView.LoadGraph(graph);
                    _graphView.RestoreViewportState(scrollOffset, zoomScale);

                    if (_generationOrchestrator != null && !_skipNextPreviewRefresh)
                    {
                        _generationOrchestrator.RequestPreviewRefresh();
                    }
                }
            }

            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.SetGraphContext(_graphView, graph);
                _diagnosticsPanel.Populate(System.Array.Empty<GraphDiagnostic>());
            }

            SetStatus(IdleStatusText);
            RefreshWindowTitle();
        }

        // --- Sub-graph navigation ---

        /// <summary>
        /// Called by <see cref="DynamicDungeonGraphView"/> when a sub-graph node's
        /// Enter action is triggered.  Captures the current viewport and delegates
        /// to <see cref="BreadcrumbBar.Push"/>.
        /// </summary>
        private void OnEnterSubGraph(GenGraph nestedGraph, string label)
        {
            if (nestedGraph == null || _breadcrumbBar == null || _graphView == null)
            {
                return;
            }

            Vector3 currentScrollOffset;
            float currentZoomScale;
            _graphView.GetViewportState(out currentScrollOffset, out currentZoomScale);
            _breadcrumbBar.SaveViewportState(currentScrollOffset, currentZoomScale);

            // Push fires OnBreadcrumbGraphChanged which loads the canvas.
            _breadcrumbBar.Push(nestedGraph, label);
        }

        /// <summary>
        /// Called by <see cref="BreadcrumbBar"/> whenever the visible graph changes.
        /// </summary>
        private void OnBreadcrumbGraphChanged(GenGraph graph, Vector3 scrollOffset, float zoomScale)
        {
            LoadGraphInCanvas(graph, scrollOffset, zoomScale);
        }

        private void OnAfterGraphMutation()
        {
            if (!AutoSave)
            {
                return;
            }

            if (_generationOrchestrator != null && _generationOrchestrator.IsGenerating)
            {
                return;
            }

            AssetDatabase.SaveAssets();
        }

        private void OnAutoSaveToggleChanged(ChangeEvent<bool> changeEvent)
        {
            AutoSave = changeEvent.newValue;
        }

        private void OnDiagnosticsUpdated(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.Populate(diagnostics);
                _diagnosticsPanelExpandedHeight = _diagnosticsPanel.GetExpandedHeight();
                _isDiagnosticsPanelCollapsed = _diagnosticsPanel.IsCollapsed();
            }
        }

        private void OnGraphFieldValueChanged(ChangeEvent<UnityEngine.Object> changeEvent)
        {
            LoadGraph(changeEvent.newValue as GenGraph);
        }

        private void RefreshWindowTitle()
        {
            string graphName = _loadedGraph != null ? _loadedGraph.name : NoGraphTitle;
            titleContent = new GUIContent(graphName);
        }

        private void RestoreGraphAfterReload()
        {
            _skipNextPreviewRefresh = true;
            LoadGraph(_loadedGraph);
            _skipNextPreviewRefresh = false;

            if (_graphView == null || _loadedGraph == null)
            {
                return;
            }

            string viewportGraphGuid = EditorPrefs.GetString(CanvasViewportGraphGuidPrefsKey, string.Empty);
            string loadedGraphGuid;
            if (!TryGetGraphGuid(_loadedGraph, out loadedGraphGuid))
            {
                return;
            }

            if (!string.Equals(viewportGraphGuid, loadedGraphGuid, StringComparison.Ordinal))
            {
                return;
            }

            Vector3 scrollOffset = new Vector3(
                EditorPrefs.GetFloat(CanvasScrollXPrefsKey, 0.0f),
                EditorPrefs.GetFloat(CanvasScrollYPrefsKey, 0.0f),
                EditorPrefs.GetFloat(CanvasScrollZPrefsKey, 0.0f));
            float zoomScale = EditorPrefs.GetFloat(CanvasZoomPrefsKey, 1.0f);
            _graphView.RestoreViewportState(scrollOffset, zoomScale);
        }

        private void SaveViewportPreferences()
        {
            if (_graphView == null || _loadedGraph == null || _graphView.Graph == null)
            {
                return;
            }

            Vector3 scrollOffset;
            float zoomScale;
            _graphView.GetViewportState(out scrollOffset, out zoomScale);

            EditorPrefs.SetFloat(CanvasScrollXPrefsKey, scrollOffset.x);
            EditorPrefs.SetFloat(CanvasScrollYPrefsKey, scrollOffset.y);
            EditorPrefs.SetFloat(CanvasScrollZPrefsKey, scrollOffset.z);
            EditorPrefs.SetFloat(CanvasZoomPrefsKey, zoomScale);

            string viewportGraphGuid;
            if (TryGetGraphGuid(_graphView.Graph, out viewportGraphGuid))
            {
                EditorPrefs.SetString(CanvasViewportGraphGuidPrefsKey, viewportGraphGuid);
            }
        }

        private static bool TryGetGraphGuid(GenGraph graph, out string guid)
        {
            guid = string.Empty;

            if (graph == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            guid = AssetDatabase.AssetPathToGUID(assetPath);
            return !string.IsNullOrWhiteSpace(guid);
        }

        private void SetStatus(string statusText)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = statusText;
            }
        }
    }
}
