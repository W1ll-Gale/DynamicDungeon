using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Shared;
using DynamicDungeon.Runtime;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonEditorWindow : EditorWindow
    {
        private const string IdleStatusText = "Idle";
        private const string WindowTitle = "Tilemap World Graph";
        private const float DiagnosticsPanelHeight = 120.0f;
        private const double AutoSaveDebounceSeconds = 1.0;
        private const double AutoSaveGeneratingRetrySeconds = 0.25;
        private const string AutoSavePrefsKey = "DynamicDungeon.AutoSave";
        private const string CanvasScrollXPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollX";
        private const string CanvasScrollYPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollY";
        private const string CanvasScrollZPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollZ";
        private const string CanvasZoomPrefsKey = "DynamicDungeon.EditorWindow.CanvasZoom";
        private const string CanvasViewportGraphGuidPrefsKey = "DynamicDungeon.EditorWindow.CanvasViewportGraphGuid";
        private const string PanelViewSettingsPrefsKey = "DynamicDungeon.EditorWindow.PanelViewSettings";
        private const string BlackboardLayoutPrefsKey = "DynamicDungeon.EditorWindow.BlackboardLayout";
        private const string GraphSettingsLayoutPrefsKey = "DynamicDungeon.EditorWindow.GraphSettingsLayout";
        private const string GroupNavigatorLayoutPrefsKey = "DynamicDungeon.EditorWindow.GroupNavigatorLayout";
        private const string MiniMapLayoutPrefsKey = "DynamicDungeon.EditorWindow.MiniMapLayout";
        private const string NodeDocumentationBaseUrl = "https://dynamicdungeon.mrbytesized.com/docs/nodes/";
        private const string WorldGeneratorDocumentationUrl = "https://dynamicdungeon.mrbytesized.com/docs/world-generator";

        private ObjectField _graphField;
        private Label _statusLabel;
        private Label _saveStateLabel;
        private ToolbarToggle _autoSaveToggle;
        private ToolbarButton _discardChangesButton;
        private ToolbarToggle _blackboardToggle;
        private ToolbarToggle _settingsToggle;
        private ToolbarToggle _groupNavigatorToggle;
        private ToolbarToggle _miniMapToggle;
        private ToolbarToggle _diagnosticsToggle;
        private ToolbarButton _docsButton;
        private BreadcrumbBar _breadcrumbBar;
        private DynamicDungeonGraphView _graphView;
        private BlackboardWindow _blackboardWindow;
        private GraphSettingsWindow _graphSettingsWindow;
        private GroupNavigatorWindow _groupNavigatorWindow;
        private MiniMapWindow _miniMapWindow;
        private GenerationOrchestrator _generationOrchestrator;
        private DiagnosticsPanel _diagnosticsPanel;
        private bool _skipNextPreviewRefresh;
        private bool _autoSavePending;
        private double _nextAutoSaveTime;

        [SerializeField]
        private GenGraph _loadedGraph;

        [SerializeField]
        private List<GenGraph> _breadcrumbGraphs = new List<GenGraph>();

        [SerializeField]
        private List<string> _breadcrumbLabels = new List<string>();

        [SerializeField]
        private bool _isDiagnosticsPanelCollapsed;

        [SerializeField]
        private float _diagnosticsPanelExpandedHeight = DiagnosticsPanelHeight;

        private PanelViewSettings _panelViewSettings;
        private FloatingWindowLayout _blackboardLayout;
        private FloatingWindowLayout _graphSettingsLayout;
        private FloatingWindowLayout _groupNavigatorLayout;
        private FloatingWindowLayout _miniMapLayout;

        [Serializable]
        internal sealed class PanelViewSettings
        {
            public bool IsBlackboardVisible = false;
            public bool IsGraphSettingsVisible = true;
            public bool IsGroupNavigatorVisible = false;
            public bool IsMiniMapVisible = true;
            public bool IsDiagnosticsVisible = true;
            public bool IsBlackboardCollapsed;
            public bool IsGraphSettingsCollapsed;
            public bool IsGroupNavigatorCollapsed;
            public bool IsMiniMapCollapsed;
        }

        private bool AutoSave
        {
            get
            {
                return LoadAutoSavePreference();
            }
            set
            {
                SaveAutoSavePreference(value);
            }
        }

        [MenuItem(DynamicDungeonMenuPaths.GraphEditor)]
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

        public static void RequestPreviewRefreshForAllOpenWindows()
        {
            DynamicDungeonEditorWindow[] windows = Resources.FindObjectsOfTypeAll<DynamicDungeonEditorWindow>();
            if (windows == null)
            {
                return;
            }

            int index;
            for (index = 0; index < windows.Length; index++)
            {
                DynamicDungeonEditorWindow window = windows[index];
                if (window != null)
                {
                    window.RequestPreviewRefresh();
                }
            }
        }

        internal static void RefreshSaveStateForAllOpenWindows()
        {
            DynamicDungeonEditorWindow[] windows = Resources.FindObjectsOfTypeAll<DynamicDungeonEditorWindow>();
            if (windows == null)
            {
                return;
            }

            int index;
            for (index = 0; index < windows.Length; index++)
            {
                DynamicDungeonEditorWindow window = windows[index];
                if (window != null)
                {
                    window.RefreshSaveStateAfterExternalSave();
                }
            }
        }

        [OnOpenAsset]
        public static bool TryOpenGraphAsset(int instanceId, int line)
        {
            GenGraph graph = EditorUtility.EntityIdToObject(instanceId) as GenGraph;
            if (graph == null)
            {
                return false;
            }

            OpenGraph(graph);
            return true;
        }

        private void CreateGUI()
        {
            LoadPanelState();

            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            Toolbar toolbar = BuildToolbar();
            rootVisualElement.Add(toolbar);

            _breadcrumbBar = new BreadcrumbBar(OnBreadcrumbGraphChanged);
            rootVisualElement.Add(_breadcrumbBar);

            VisualElement contentArea = new VisualElement();
            contentArea.style.flexDirection = FlexDirection.Row;
            contentArea.style.flexGrow = 1.0f;
            rootVisualElement.Add(contentArea);

            _graphView = new DynamicDungeonGraphView();
            _graphView.style.flexGrow = 1.0f;
            _graphView.RegisterCallback<GeometryChangedEvent>(OnGraphViewGeometryChanged);
            contentArea.Add(_graphView);

            _blackboardWindow = new BlackboardWindow(_blackboardLayout, OnAfterGraphMutation);
            _blackboardWindow.SetLayoutChangedCallback(SavePanelState);
            _blackboardWindow.SetCollapsed(_panelViewSettings.IsBlackboardCollapsed);
            _graphView.Add(_blackboardWindow);

            _graphSettingsWindow = new GraphSettingsWindow(
                _graphSettingsLayout,
                () => _generationOrchestrator?.RequestPreviewRefresh(),
                OnAfterGraphMutation);
            _graphSettingsWindow.SetLayoutChangedCallback(SavePanelState);
            _graphSettingsWindow.SetCollapsed(_panelViewSettings.IsGraphSettingsCollapsed);
            _graphView.Add(_graphSettingsWindow);

            _groupNavigatorWindow = new GroupNavigatorWindow(_groupNavigatorLayout, _graphView);
            _groupNavigatorWindow.SetLayoutChangedCallback(SavePanelState);
            _groupNavigatorWindow.SetCollapsed(_panelViewSettings.IsGroupNavigatorCollapsed);
            _graphView.Add(_groupNavigatorWindow);

            _miniMapWindow = new MiniMapWindow(_miniMapLayout, _graphView, CreateMiniMapCallbacks());
            _miniMapWindow.SetLayoutChangedCallback(SavePanelState);
            _miniMapWindow.SetCollapsed(_panelViewSettings.IsMiniMapCollapsed);
            _graphView.Add(_miniMapWindow);

            _diagnosticsPanel = BuildDiagnosticsPanel();
            rootVisualElement.Add(_diagnosticsPanel);

            _generationOrchestrator = new GenerationOrchestrator(_graphView, SetStatus, OnDiagnosticsUpdated, Repaint);
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
                _diagnosticsPanel.SetContext(ResolveDiagnosticElementName, FocusDiagnosticElement);
                _blackboardWindow.SetGraph(null);
                _graphSettingsWindow.SetGraph(null);
                _groupNavigatorWindow.SetGraph(null);
            }

            ApplyPanelVisibility();
            ApplyPanelLayouts();
            SetStatus(IdleStatusText);
            RefreshSaveStateIndicator();
            RefreshWindowTitle();
        }

        private void OnEnable()
        {
            RefreshWindowTitle();
        }

        private void OnDisable()
        {
            SaveBreadcrumbTrail();
            SaveViewportPreferences();
            SavePanelState();

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

            ClearQueuedAutoSave();

            if (AutoSave)
            {
                SaveDirtyGraphImmediately();
            }
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            GraphEditorToolbarControls.ApplyStandardToolbarStyle(toolbar);

            _graphField = new ObjectField("Graph");
            _graphField.objectType = typeof(GenGraph);
            _graphField.allowSceneObjects = false;
            _graphField.label = string.Empty;
            _graphField.style.minWidth = 220.0f;
            _graphField.style.width = 220.0f;
            _graphField.style.marginRight = 6.0f;
            _graphField.RegisterValueChangedCallback(OnGraphFieldValueChanged);
            toolbar.Add(_graphField);

            ToolbarButton generateButton = new ToolbarButton(GenerateGraph);
            generateButton.text = "Generate";
            generateButton.style.marginRight = 2.0f;
            toolbar.Add(generateButton);

            ToolbarButton cancelButton = new ToolbarButton(CancelGeneration);
            cancelButton.text = "Cancel";
            cancelButton.style.marginRight = 6.0f;
            toolbar.Add(cancelButton);

            _autoSaveToggle = GraphEditorToolbarControls.BuildAutoSaveToggle(
                AutoSave,
                "Save graph assets automatically after edits.",
                OnAutoSaveToggleChanged);
            toolbar.Add(_autoSaveToggle);

            _statusLabel = GraphEditorToolbarControls.BuildStatusLabel(IdleStatusText);
            toolbar.Add(_statusLabel);

            _saveStateLabel = GraphEditorToolbarControls.BuildSaveStateLabel();
            toolbar.Add(_saveStateLabel);

            _discardChangesButton = GraphEditorToolbarControls.BuildDiscardButton(
                "Delete all unsaved changes on the loaded graph and reload it from disk.",
                DiscardUnsavedGraphChanges);
            toolbar.Add(_discardChangesButton);

            toolbar.Add(GraphEditorToolbarControls.BuildToolbarSpacer());

            VisualElement panelToggleGroup = GraphEditorToolbarControls.BuildPanelToggleGroup();
            toolbar.Add(panelToggleGroup);

            _blackboardToggle = GraphEditorToolbarControls.BuildPanelToggle(
                LoadBlackboardIcon(),
                "Toggle Blackboard window",
                _panelViewSettings.IsBlackboardVisible,
                OnBlackboardToggleChanged);
            panelToggleGroup.Add(_blackboardToggle);

            _settingsToggle = GraphEditorToolbarControls.BuildPanelToggle(
                LoadInspectorIcon(),
                "Toggle Graph Settings window",
                _panelViewSettings.IsGraphSettingsVisible,
                OnSettingsToggleChanged);
            panelToggleGroup.Add(_settingsToggle);

            _groupNavigatorToggle = GraphEditorToolbarControls.BuildPanelToggle(
                LoadGroupNavigatorIcon(),
                "Toggle Groups window",
                _panelViewSettings.IsGroupNavigatorVisible,
                OnGroupNavigatorToggleChanged);
            panelToggleGroup.Add(_groupNavigatorToggle);

            _miniMapToggle = GraphEditorToolbarControls.BuildPanelToggle(
                LoadMiniMapIcon(),
                "Toggle MiniMap window",
                _panelViewSettings.IsMiniMapVisible,
                OnMiniMapToggleChanged);
            panelToggleGroup.Add(_miniMapToggle);

            _diagnosticsToggle = GraphEditorToolbarControls.BuildPanelToggle(
                LoadDiagnosticsIcon(),
                "Toggle Diagnostics panel",
                _panelViewSettings.IsDiagnosticsVisible,
                OnDiagnosticsToggleChanged);
            panelToggleGroup.Add(_diagnosticsToggle);

            _docsButton = GraphEditorToolbarControls.BuildIconButton(
                LoadDocsIcon(),
                "Open documentation for the selected node",
                OnDocsButtonClicked);
            panelToggleGroup.Add(_docsButton);

            return toolbar;
        }

        private DiagnosticsPanel BuildDiagnosticsPanel()
        {
            return DiagnosticsPanelUtility.BuildDockedPanel(
                ResolveDiagnosticElementName,
                FocusDiagnosticElement,
                _diagnosticsPanelExpandedHeight,
                _isDiagnosticsPanelCollapsed,
                new Color(0.18f, 0.18f, 0.18f, 1.0f));
        }

        private MiniMapGraphCallbacks CreateMiniMapCallbacks()
        {
            return new MiniMapGraphCallbacks
            {
                RegisterViewTransformChanged = callback => _graphView.SetViewTransformChangedCallback(callback),
                GetViewportState = _graphView.GetViewportState,
                GetElementId = GetMiniMapElementId,
                FocusElement = FocusMiniMapElement
            };
        }

        private static string GetMiniMapElementId(GraphElement element)
        {
            GenNodeView nodeView = element as GenNodeView;
            return nodeView != null ? nodeView.NodeData.NodeId : null;
        }

        private bool FocusMiniMapElement(string elementId)
        {
            return _graphView != null && _graphView.SelectAndFrameNode(elementId);
        }

        private string ResolveDiagnosticElementName(string elementId)
        {
            GenGraph graph = GetCurrentGraphForSaveState();
            if (graph == null || string.IsNullOrWhiteSpace(elementId))
            {
                return "Graph";
            }

            GenNodeData nodeData = graph.GetNode(elementId);
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

        private bool FocusDiagnosticElement(string elementId)
        {
            return _graphView != null && _graphView.SelectAndFrameNode(elementId);
        }

        private static IReadOnlyList<SharedGraphDiagnostic> ConvertDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            IReadOnlyList<GraphDiagnostic> safeDiagnostics = diagnostics ?? Array.Empty<GraphDiagnostic>();
            List<SharedGraphDiagnostic> convertedDiagnostics = new List<SharedGraphDiagnostic>(safeDiagnostics.Count);

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < safeDiagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = safeDiagnostics[diagnosticIndex];
                SharedDiagnosticSeverity severity = diagnostic.Severity == DiagnosticSeverity.Error
                    ? SharedDiagnosticSeverity.Error
                    : SharedDiagnosticSeverity.Warning;
                convertedDiagnostics.Add(new SharedGraphDiagnostic(severity, diagnostic.Message, diagnostic.NodeId, diagnostic.PortName));
            }

            return convertedDiagnostics;
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
            if (graph != null && !TryPrepareGraphForEditing(graph))
            {
                return;
            }

            _loadedGraph = graph;

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(graph);
            }

            if (_breadcrumbBar != null)
            {
                ResetSavedBreadcrumbTrail(graph);
                _breadcrumbBar.ResetTo(graph, graph != null ? graph.name : string.Empty);

                if (graph == null)
                {
                    LoadGraphInCanvas(null, Vector3.zero, 1.0f);
                }

                return;
            }

            LoadGraphInCanvas(graph, Vector3.zero, 1.0f);
        }

        private bool TryPrepareGraphForEditing(GenGraph graph)
        {
            string errorMessage;
            if (!GraphOutputUtility.TryValidateCurrentSchema(graph, out errorMessage))
            {
                Debug.LogError("Failed to load graph '" + graph.name + "': " + errorMessage);
                return false;
            }

            return true;
        }

        private void LoadGraphInCanvas(GenGraph graph, Vector3 scrollOffset, float zoomScale)
        {
            _blackboardWindow?.SetGraph(graph);
            _graphSettingsWindow?.SetGraph(graph);
            _groupNavigatorWindow?.SetGraph(graph);

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
                _diagnosticsPanel.SetContext(ResolveDiagnosticElementName, FocusDiagnosticElement);
                _diagnosticsPanel.Populate(Array.Empty<SharedGraphDiagnostic>());
            }

            SetStatus(IdleStatusText);
            RefreshSaveStateIndicator();
            RefreshWindowTitle();
        }

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
            _breadcrumbBar.Push(nestedGraph, label);
            SaveBreadcrumbTrail();
        }

        private void OnBreadcrumbGraphChanged(GenGraph graph, Vector3 scrollOffset, float zoomScale)
        {
            LoadGraphInCanvas(graph, scrollOffset, zoomScale);
            SaveBreadcrumbTrail();
        }

        private void OnAfterGraphMutation()
        {
            _groupNavigatorWindow?.Refresh();
            RefreshSaveStateIndicator();
            QueueDirtyGraphAutoSave();
        }

        private void OnAutoSaveToggleChanged(ChangeEvent<bool> changeEvent)
        {
            AutoSave = changeEvent.newValue;

            if (!AutoSave)
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            QueueDirtyGraphAutoSave();
        }

        private void OnBlackboardToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsBlackboardVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnSettingsToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsGraphSettingsVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnGroupNavigatorToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsGroupNavigatorVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnMiniMapToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsMiniMapVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnDiagnosticsToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsDiagnosticsVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnDocsButtonClicked()
        {
            if (_graphView == null)
            {
                return;
            }

            int selectedNodeCount = _graphView.GetSelectedNodeCount();
            if (selectedNodeCount == 0)
            {
                Application.OpenURL(WorldGeneratorDocumentationUrl);
                return;
            }

            GenNodeData selectedNodeData = _graphView.GetSingleSelectedNodeData();
            if (selectedNodeData == null)
            {
                EditorUtility.DisplayDialog("Node Documentation", "Select exactly one node to open its documentation.", "OK");
                return;
            }

            string documentationUrl;
            if (!TryGetNodeDocumentationUrl(selectedNodeData, out documentationUrl))
            {
                string nodeName = !string.IsNullOrWhiteSpace(selectedNodeData.NodeName)
                    ? selectedNodeData.NodeName
                    : selectedNodeData.NodeTypeName;
                EditorUtility.DisplayDialog("Node Documentation", "No documentation page is registered for '" + nodeName + "'.", "OK");
                return;
            }

            Application.OpenURL(documentationUrl);
        }

        private static bool TryGetNodeDocumentationUrl(GenNodeData nodeData, out string url)
        {
            url = string.Empty;
            if (nodeData == null || string.IsNullOrWhiteSpace(nodeData.NodeTypeName))
            {
                return false;
            }

            string nodeTypeName = GetShortTypeName(nodeData.NodeTypeName);
            string route;
            if (!TryGetNodeDocumentationRoute(nodeTypeName, out route))
            {
                return false;
            }

            url = NodeDocumentationBaseUrl + route;
            return true;
        }

        private static string GetShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
            {
                return string.Empty;
            }

            int separatorIndex = fullTypeName.LastIndexOf('.');
            return separatorIndex >= 0 && separatorIndex < fullTypeName.Length - 1
                ? fullTypeName.Substring(separatorIndex + 1)
                : fullTypeName;
        }

        private static bool TryGetNodeDocumentationRoute(string nodeTypeName, out string route)
        {
            switch (nodeTypeName)
            {
                case "BiomeLayerNode": route = "biome/biomelayernode"; return true;
                case "BiomeLayoutNode": route = "biome/biomelayout"; return true;
                case "BiomeMaskNode": route = "biome/biomemasknode"; return true;
                case "BiomeMergeNode": route = "biome/biomemerge"; return true;
                case "BiomeOverrideNode": route = "biome/biomeoverride"; return true;
                case "BiomeSelectorNode": route = "biome/biomeselector"; return true;
                case "BiomeWeightBlendNode": route = "biome/biomeweightblend"; return true;
                case "WeightedBlendNode": route = "biome/biomeweightblend"; return true;
                case "AxisBandNode": route = "filter-transform/axisbandnode"; return true;
                case "CellularAutomataNode": route = "filter-transform/cellularautomata"; return true;
                case "ClampNode": route = "filter-transform/clampnode"; return true;
                case "ColumnSurfaceBandNode": route = "filter-transform/columnsurfaceband"; return true;
                case "DistanceFieldNode": route = "filter-transform/distancefieldnode"; return true;
                case "EdgeDetectNode": route = "filter-transform/edgedetectnode"; return true;
                case "HeightBandNode": route = "filter-transform/heightbandnode"; return true;
                case "HeightGradientNode": route = "filter-transform/heightgradient"; return true;
                case "InvertNode": route = "filter-transform/invertnode"; return true;
                case "NormaliseNode": route = "filter-transform/normalisenode"; return true;
                case "RemapNode": route = "filter-transform/remapnode"; return true;
                case "SmoothstepNode": route = "filter-transform/smoothstepnode"; return true;
                case "StepNode": route = "filter-transform/stepnode"; return true;
                case "ThresholdNode": route = "filter-transform/thresholdnode"; return true;
                case "ClusterNode": route = "growth-organic/clusternode"; return true;
                case "PerlinWormNode": route = "growth-organic/perlinwormnode"; return true;
                case "VeinNode": route = "growth-organic/veinnode"; return true;
                case "AddNode": route = "maths-composite/addnode"; return true;
                case "BlendNode": route = "maths-composite/blendnode"; return true;
                case "MaskBlendNode": route = "maths-composite/blendnode"; return true;
                case "CombineMasksNode": route = "maths-composite/combinemasks"; return true;
                case "CompositeNode": route = "maths-composite/composite"; return true;
                case "MaskExpressionNode": route = "maths-composite/maskexpression"; return true;
                case "MaskStackNode": route = "maths-composite/maskstacknode"; return true;
                case "MaxNode": route = "maths-composite/maxnode"; return true;
                case "MultiplyNode": route = "maths-composite/multiplynode"; return true;
                case "SelectNode": route = "maths-composite/selectnode"; return true;
                case "SelectMaskNode": route = "maths-composite/selectnode"; return true;
                case "ConstantNode": route = "noise/constantnode"; return true;
                case "FractalNoiseNode": route = "noise/fractalnoise"; return true;
                case "GradientNoiseNode": route = "noise/gradientnoise"; return true;
                case "PerlinNoiseNode": route = "noise/perlinnoise"; return true;
                case "SimplexNoiseNode": route = "noise/simplexnoise"; return true;
                case "SurfaceNoiseNode": route = "noise/surfacenoisenode"; return true;
                case "VoronoiNoiseNode": route = "noise/voronoinoise"; return true;
                case "WhiteNoiseNode": route = "noise/whitenoise"; return true;
                case "DungeonGeneratorNode": route = "placement/dungeongenerator"; return true;
                case "PlacementSetNode": route = "placement/placementsetnode"; return true;
                case "PrefabSpawnerNode": route = "placement/prefabspawnernode"; return true;
                case "PrefabStamperNode": route = "placement/prefabstampernode"; return true;
                default: route = string.Empty; return false;
            }
        }

        private void OnDiagnosticsUpdated(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.Populate(ConvertDiagnostics(diagnostics));
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
            titleContent = new GUIContent(WindowTitle);
        }

        private void ResetSavedBreadcrumbTrail(GenGraph rootGraph)
        {
            if (_breadcrumbGraphs == null)
            {
                _breadcrumbGraphs = new List<GenGraph>();
            }

            if (_breadcrumbLabels == null)
            {
                _breadcrumbLabels = new List<string>();
            }

            _breadcrumbGraphs.Clear();
            _breadcrumbLabels.Clear();

            if (rootGraph != null)
            {
                _breadcrumbGraphs.Add(rootGraph);
                _breadcrumbLabels.Add(rootGraph.name);
            }
        }

        private void SaveBreadcrumbTrail()
        {
            if (_breadcrumbBar == null)
            {
                return;
            }

            if (_breadcrumbGraphs == null)
            {
                _breadcrumbGraphs = new List<GenGraph>();
            }

            if (_breadcrumbLabels == null)
            {
                _breadcrumbLabels = new List<string>();
            }

            _breadcrumbBar.CopyTrail(_breadcrumbGraphs, _breadcrumbLabels);
        }

        private bool TryRestoreBreadcrumbTrailAfterReload()
        {
            if (_breadcrumbBar == null ||
                _loadedGraph == null ||
                _breadcrumbGraphs == null ||
                _breadcrumbGraphs.Count <= 1 ||
                !ReferenceEquals(_breadcrumbGraphs[0], _loadedGraph))
            {
                return false;
            }

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(_loadedGraph);
            }

            _breadcrumbBar.RestoreTrail(_breadcrumbGraphs, _breadcrumbLabels);
            return true;
        }

        private void RestoreGraphAfterReload()
        {
            _skipNextPreviewRefresh = true;
            bool restoredBreadcrumbTrail = TryRestoreBreadcrumbTrailAfterReload();
            if (!restoredBreadcrumbTrail)
            {
                LoadGraph(_loadedGraph);
            }
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
                RequestPreviewRefreshAfterReload();
                return;
            }

            Vector3 scrollOffset = new Vector3(
                EditorPrefs.GetFloat(CanvasScrollXPrefsKey, 0.0f),
                EditorPrefs.GetFloat(CanvasScrollYPrefsKey, 0.0f),
                EditorPrefs.GetFloat(CanvasScrollZPrefsKey, 0.0f));
            float zoomScale = EditorPrefs.GetFloat(CanvasZoomPrefsKey, 1.0f);
            _graphView.RestoreViewportState(scrollOffset, zoomScale);
            RequestPreviewRefreshAfterReload();
        }

        private void RequestPreviewRefreshAfterReload()
        {
            if (_generationOrchestrator != null && _loadedGraph != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }
        }

        private void RequestPreviewRefresh()
        {
            if (_generationOrchestrator != null && _loadedGraph != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }
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

        private void QueueDirtyGraphAutoSave()
        {
            if (!AutoSave)
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            if (!IsTrackedGraphDirty())
            {
                RefreshSaveStateIndicator();
                return;
            }

            _nextAutoSaveTime = EditorApplication.timeSinceStartup + AutoSaveDebounceSeconds;

            if (_autoSavePending)
            {
                return;
            }

            _autoSavePending = true;
            EditorApplication.update -= ProcessQueuedAutoSave;
            EditorApplication.update += ProcessQueuedAutoSave;
        }

        private void ProcessQueuedAutoSave()
        {
            if (!_autoSavePending)
            {
                return;
            }

            if (!AutoSave)
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            if (!IsTrackedGraphDirty())
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime < _nextAutoSaveTime)
            {
                return;
            }

            if (_generationOrchestrator != null && _generationOrchestrator.IsGenerating)
            {
                _nextAutoSaveTime = currentTime + AutoSaveGeneratingRetrySeconds;
                RefreshSaveStateIndicator();
                return;
            }

            ClearQueuedAutoSave();
            SaveDirtyGraphImmediately();
        }

        private void ClearQueuedAutoSave()
        {
            if (!_autoSavePending)
            {
                return;
            }

            _autoSavePending = false;
            EditorApplication.update -= ProcessQueuedAutoSave;
        }

        private void SaveDirtyGraphImmediately()
        {
            GenGraph graph = GetCurrentGraphForSaveState();
            if (graph == null)
            {
                RefreshSaveStateIndicator();
                return;
            }

            if (!IsTrackedGraphDirty())
            {
                RefreshSaveStateIndicator();
                return;
            }

            AssetDatabase.SaveAssets();
            RefreshSaveStateIndicator();
        }

        private void DiscardUnsavedGraphChanges()
        {
            GenGraph graph = GetCurrentGraphForSaveState();
            if (graph == null || !IsTrackedGraphDirty())
            {
                RefreshSaveStateIndicator();
                return;
            }

            List<GenGraph> dirtyGraphs = GetTrackedDirtyGraphs();
            if (dirtyGraphs.Count == 0)
            {
                RefreshSaveStateIndicator();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Discard Unsaved Changes",
                    "Delete all unsaved changes for the loaded graph and reload from disk?",
                    "Discard",
                    "Cancel"))
            {
                return;
            }

            ClearQueuedAutoSave();

            Vector3 scrollOffset = Vector3.zero;
            float zoomScale = 1.0f;
            if (_graphView != null)
            {
                _graphView.GetViewportState(out scrollOffset, out zoomScale);
            }

            bool restoredAnyGraph = false;
            int graphIndex;
            for (graphIndex = 0; graphIndex < dirtyGraphs.Count; graphIndex++)
            {
                if (RestoreGraphFromSavedAsset(dirtyGraphs[graphIndex]))
                {
                    restoredAnyGraph = true;
                }
            }

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(_loadedGraph);
            }

            if (restoredAnyGraph)
            {
                LoadGraphInCanvas(graph, scrollOffset, zoomScale);
                return;
            }

            RefreshSaveStateIndicator();
        }

        private List<GenGraph> GetTrackedDirtyGraphs()
        {
            List<GenGraph> graphs = new List<GenGraph>();
            AddDirtyGraph(graphs, GetCurrentGraphForSaveState());

            if (_loadedGraph != null && !ReferenceEquals(_loadedGraph, GetCurrentGraphForSaveState()))
            {
                AddDirtyGraph(graphs, _loadedGraph);
            }

            return graphs;
        }

        private static void AddDirtyGraph(List<GenGraph> graphs, GenGraph graph)
        {
            if (graphs == null || graph == null || !EditorUtility.IsDirty(graph))
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrWhiteSpace(path) || graphs.Contains(graph))
            {
                return;
            }

            graphs.Add(graph);
        }

        private static bool RestoreGraphFromSavedAsset(GenGraph graph)
        {
            return SavedAssetRestoreUtility.RestoreFromSavedAsset(
                graph,
                "__DynamicDungeonDiscardTemp_",
                "Could not find saved graph asset file at",
                "Could not load saved graph copy from",
                "Could not discard unsaved graph changes for");
        }

        private void RefreshSaveStateAfterExternalSave()
        {
            if (!IsTrackedGraphDirty())
            {
                ClearQueuedAutoSave();
            }

            RefreshSaveStateIndicator();
        }

        private void RefreshSaveStateIndicator()
        {
            if (_saveStateLabel == null)
            {
                return;
            }

            GenGraph graph = GetCurrentGraphForSaveState();
            if (graph == null)
            {
                _saveStateLabel.text = "No Graph";
                _saveStateLabel.tooltip = "No graph asset is loaded.";
                _saveStateLabel.style.color = new Color(0.55f, 0.55f, 0.55f, 1.0f);
                SetDiscardChangesButtonEnabled(false);
                return;
            }

            bool isUnsaved = IsTrackedGraphDirty();
            _saveStateLabel.text = isUnsaved ? "Unsaved" : "Saved";
            _saveStateLabel.tooltip = isUnsaved
                ? (AutoSave
                    ? "The current graph has unsaved changes. Auto Save will save it after editing pauses."
                    : "The current graph has unsaved changes. Auto Save is off.")
                : "The current graph asset is saved.";
            _saveStateLabel.style.color = isUnsaved
                ? new Color(1.0f, 0.62f, 0.2f, 1.0f)
                : new Color(0.62f, 0.82f, 0.62f, 1.0f);
            SetDiscardChangesButtonEnabled(isUnsaved);
        }

        private void SetDiscardChangesButtonEnabled(bool isEnabled)
        {
            GraphEditorToolbarControls.SetButtonEnabledWithOpacity(_discardChangesButton, isEnabled);
        }

        private GenGraph GetCurrentGraphForSaveState()
        {
            return _graphView != null && _graphView.Graph != null
                ? _graphView.Graph
                : _loadedGraph;
        }

        private bool IsTrackedGraphDirty()
        {
            GenGraph graph = GetCurrentGraphForSaveState();
            if (graph != null && EditorUtility.IsDirty(graph))
            {
                return true;
            }

            return _loadedGraph != null
                   && !ReferenceEquals(_loadedGraph, graph)
                   && EditorUtility.IsDirty(_loadedGraph);
        }

        private void OnGraphViewGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            ApplyPanelLayouts();
        }

        private void ApplyPanelVisibility()
        {
            _blackboardWindow?.SetVisible(_panelViewSettings.IsBlackboardVisible);
            _graphSettingsWindow?.SetVisible(_panelViewSettings.IsGraphSettingsVisible);
            _groupNavigatorWindow?.SetVisible(_panelViewSettings.IsGroupNavigatorVisible);
            _miniMapWindow?.SetVisible(_panelViewSettings.IsMiniMapVisible);
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.style.display = _panelViewSettings.IsDiagnosticsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_blackboardToggle != null)
            {
                _blackboardToggle.SetValueWithoutNotify(_panelViewSettings.IsBlackboardVisible);
            }

            if (_settingsToggle != null)
            {
                _settingsToggle.SetValueWithoutNotify(_panelViewSettings.IsGraphSettingsVisible);
            }

            if (_groupNavigatorToggle != null)
            {
                _groupNavigatorToggle.SetValueWithoutNotify(_panelViewSettings.IsGroupNavigatorVisible);
            }

            if (_miniMapToggle != null)
            {
                _miniMapToggle.SetValueWithoutNotify(_panelViewSettings.IsMiniMapVisible);
            }

            if (_diagnosticsToggle != null)
            {
                _diagnosticsToggle.SetValueWithoutNotify(_panelViewSettings.IsDiagnosticsVisible);
            }
        }

        private void ApplyPanelLayouts()
        {
            Rect graphViewRect = GetGraphViewRect();
            if (graphViewRect.width <= 0.0f || graphViewRect.height <= 0.0f)
            {
                return;
            }

            _blackboardWindow?.UpdateParentRect(graphViewRect);
            _graphSettingsWindow?.UpdateParentRect(graphViewRect);
            _groupNavigatorWindow?.UpdateParentRect(graphViewRect);
            _miniMapWindow?.UpdateParentRect(graphViewRect);
        }

        private Rect GetGraphViewRect()
        {
            if (_graphView == null)
            {
                return new Rect(0.0f, 0.0f, 0.0f, 0.0f);
            }

            return new Rect(0.0f, 0.0f, _graphView.layout.width, _graphView.layout.height);
        }

        private void LoadPanelState()
        {
            _panelViewSettings = LoadPanelViewSettings();
            _blackboardLayout = LoadLayout(BlackboardLayoutPrefsKey, CreateDefaultBlackboardLayout());
            _graphSettingsLayout = LoadLayout(GraphSettingsLayoutPrefsKey, CreateDefaultGraphSettingsLayout());
            _groupNavigatorLayout = LoadLayout(GroupNavigatorLayoutPrefsKey, CreateDefaultGroupNavigatorLayout());
            _miniMapLayout = LoadLayout(MiniMapLayoutPrefsKey, CreateDefaultMiniMapLayout());
        }

        private void SavePanelState()
        {
            Rect graphViewRect = GetGraphViewRect();
            if (_blackboardWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _blackboardWindow.CaptureCurrentLayout();
                _panelViewSettings.IsBlackboardCollapsed = _blackboardWindow.IsCollapsedForTesting;
            }

            if (_graphSettingsWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _graphSettingsWindow.CaptureCurrentLayout();
                _panelViewSettings.IsGraphSettingsCollapsed = _graphSettingsWindow.IsCollapsedForTesting;
            }

            if (_groupNavigatorWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _groupNavigatorWindow.CaptureCurrentLayout();
                _panelViewSettings.IsGroupNavigatorCollapsed = _groupNavigatorWindow.IsCollapsedForTesting;
            }

            if (_miniMapWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _miniMapWindow.CaptureCurrentLayout();
                _panelViewSettings.IsMiniMapCollapsed = _miniMapWindow.IsCollapsedForTesting;
            }

            EditorPreferenceUtility.SaveJson(PanelViewSettingsPrefsKey, _panelViewSettings ?? new PanelViewSettings());
            EditorPreferenceUtility.SaveJson(BlackboardLayoutPrefsKey, _blackboardLayout ?? CreateDefaultBlackboardLayout());
            EditorPreferenceUtility.SaveJson(GraphSettingsLayoutPrefsKey, _graphSettingsLayout ?? CreateDefaultGraphSettingsLayout());
            EditorPreferenceUtility.SaveJson(GroupNavigatorLayoutPrefsKey, _groupNavigatorLayout ?? CreateDefaultGroupNavigatorLayout());
            EditorPreferenceUtility.SaveJson(MiniMapLayoutPrefsKey, _miniMapLayout ?? CreateDefaultMiniMapLayout());
        }

        private static PanelViewSettings LoadPanelViewSettings()
        {
            return EditorPreferenceUtility.LoadJson(PanelViewSettingsPrefsKey, new PanelViewSettings());
        }

        private static bool LoadAutoSavePreference()
        {
            return EditorPrefs.GetBool(AutoSavePrefsKey, true);
        }

        private static void SaveAutoSavePreference(bool isEnabled)
        {
            EditorPrefs.SetBool(AutoSavePrefsKey, isEnabled);
        }

        private static FloatingWindowLayout LoadLayout(string prefsKey, FloatingWindowLayout defaultLayout)
        {
            return EditorPreferenceUtility.LoadJson(prefsKey, defaultLayout);
        }

        internal static PanelViewSettings LoadPanelViewSettingsForTesting()
        {
            return LoadPanelViewSettings();
        }

        internal static bool LoadAutoSavePreferenceForTesting()
        {
            return LoadAutoSavePreference();
        }

        internal static void SaveAutoSavePreferenceForTesting(bool isEnabled)
        {
            SaveAutoSavePreference(isEnabled);
        }

        internal static void DeleteAutoSavePreferenceForTesting()
        {
            EditorPrefs.DeleteKey(AutoSavePrefsKey);
        }

        internal static bool HasAutoSavePreferenceForTesting()
        {
            return EditorPrefs.HasKey(AutoSavePrefsKey);
        }

        internal static FloatingWindowLayout LoadBlackboardLayoutForTesting()
        {
            return LoadLayout(BlackboardLayoutPrefsKey, CreateDefaultBlackboardLayout());
        }

        internal static FloatingWindowLayout LoadGraphSettingsLayoutForTesting()
        {
            return LoadLayout(GraphSettingsLayoutPrefsKey, CreateDefaultGraphSettingsLayout());
        }

        internal static FloatingWindowLayout CreateDefaultBlackboardLayoutForTesting()
        {
            return CreateDefaultBlackboardLayout();
        }

        internal static FloatingWindowLayout CreateDefaultGraphSettingsLayoutForTesting()
        {
            return CreateDefaultGraphSettingsLayout();
        }

        internal static FloatingWindowLayout CreateDefaultGroupNavigatorLayoutForTesting()
        {
            return CreateDefaultGroupNavigatorLayout();
        }

        internal static FloatingWindowLayout CreateDefaultMiniMapLayoutForTesting()
        {
            return CreateDefaultMiniMapLayout();
        }

        internal static void SavePanelViewSettingsForTesting(PanelViewSettings panelViewSettings)
        {
            EditorPreferenceUtility.SaveJson(PanelViewSettingsPrefsKey, panelViewSettings);
        }

        internal static void SaveBlackboardLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorPreferenceUtility.SaveJson(BlackboardLayoutPrefsKey, layout);
        }

        internal static void SaveGraphSettingsLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorPreferenceUtility.SaveJson(GraphSettingsLayoutPrefsKey, layout);
        }

        internal static void SaveGroupNavigatorLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorPreferenceUtility.SaveJson(GroupNavigatorLayoutPrefsKey, layout);
        }

        internal static void SaveMiniMapLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorPreferenceUtility.SaveJson(MiniMapLayoutPrefsKey, layout);
        }

        private static FloatingWindowLayout CreateDefaultBlackboardLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = true,
                DockToTop = true,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(280.0f, 420.0f)
            };
        }

        private static FloatingWindowLayout CreateDefaultGraphSettingsLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = false,
                DockToTop = true,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(310.0f, 420.0f)
            };
        }

        private static FloatingWindowLayout CreateDefaultGroupNavigatorLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = true,
                DockToTop = false,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(260.0f, 260.0f)
            };
        }

        private static FloatingWindowLayout CreateDefaultMiniMapLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = false,
                DockToTop = false,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(240.0f, 180.0f)
            };
        }

        private static Texture2D LoadBlackboardIcon()
        {
            string suffix = (EditorGUIUtility.isProSkin ? "_dark" : string.Empty) +
                (EditorGUIUtility.pixelsPerPoint >= 2.0f ? "@2x" : string.Empty);
            Texture2D icon = Resources.Load<Texture2D>("Icons/blackboard" + suffix);
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image as Texture2D;
            }

            return icon;
        }

        private static Texture2D LoadInspectorIcon()
        {
            return EditorGUIUtility.IconContent("UnityEditor.InspectorWindow").image as Texture2D;
        }

        private static Texture2D LoadGroupNavigatorIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image as Texture2D;
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent("UnityEditor.SceneHierarchyWindow").image as Texture2D;
            }

            return icon;
        }

        private static Texture2D LoadMiniMapIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("d_ViewToolZoom").image as Texture2D;
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent("ViewToolZoom").image as Texture2D;
            }

            return icon;
        }

        private static Texture2D LoadDiagnosticsIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow").image as Texture2D;
            }

            return icon;
        }

        private static Texture2D LoadDocsIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("_Help").image as Texture2D;
            if (icon == null)
            {
                icon = EditorGUIUtility.IconContent("console.infoicon").image as Texture2D;
            }

            return icon;
        }
    }
}
