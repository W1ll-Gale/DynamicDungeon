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
        private const string WindowTitle = "Dynamic Dungeon Graph";
        private const float DiagnosticsPanelHeight = 120.0f;
        private const string AutoSavePrefsKey = "DynamicDungeon.AutoSave";
        private const string CanvasScrollXPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollX";
        private const string CanvasScrollYPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollY";
        private const string CanvasScrollZPrefsKey = "DynamicDungeon.EditorWindow.CanvasScrollZ";
        private const string CanvasZoomPrefsKey = "DynamicDungeon.EditorWindow.CanvasZoom";
        private const string CanvasViewportGraphGuidPrefsKey = "DynamicDungeon.EditorWindow.CanvasViewportGraphGuid";
        private const string PanelViewSettingsPrefsKey = "DynamicDungeon.EditorWindow.PanelViewSettings";
        private const string BlackboardLayoutPrefsKey = "DynamicDungeon.EditorWindow.BlackboardLayout";
        private const string GraphSettingsLayoutPrefsKey = "DynamicDungeon.EditorWindow.GraphSettingsLayout";
        private const string MiniMapLayoutPrefsKey = "DynamicDungeon.EditorWindow.MiniMapLayout";

        private ObjectField _graphField;
        private Label _statusLabel;
        private ToolbarToggle _autoSaveToggle;
        private ToolbarToggle _blackboardToggle;
        private ToolbarToggle _settingsToggle;
        private ToolbarToggle _miniMapToggle;
        private ToolbarToggle _diagnosticsToggle;
        private ToolbarButton _docsButton;
        private BreadcrumbBar _breadcrumbBar;
        private DynamicDungeonGraphView _graphView;
        private BlackboardWindow _blackboardWindow;
        private GraphSettingsWindow _graphSettingsWindow;
        private MiniMapWindow _miniMapWindow;
        private GenerationOrchestrator _generationOrchestrator;
        private DiagnosticsPanel _diagnosticsPanel;
        private bool _skipNextPreviewRefresh;

        [SerializeField]
        private GenGraph _loadedGraph;

        [SerializeField]
        private bool _isDiagnosticsPanelCollapsed;

        [SerializeField]
        private float _diagnosticsPanelExpandedHeight = DiagnosticsPanelHeight;

        private PanelViewSettings _panelViewSettings;
        private FloatingWindowLayout _blackboardLayout;
        private FloatingWindowLayout _graphSettingsLayout;
        private FloatingWindowLayout _miniMapLayout;

        [Serializable]
        internal sealed class PanelViewSettings
        {
            public bool IsBlackboardVisible = true;
            public bool IsGraphSettingsVisible = true;
            public bool IsMiniMapVisible = true;
            public bool IsDiagnosticsVisible = true;
            public bool IsBlackboardCollapsed;
            public bool IsGraphSettingsCollapsed;
            public bool IsMiniMapCollapsed;
        }

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
            LoadPanelState();

            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;

            Toolbar toolbar = BuildToolbar();
            rootVisualElement.Add(toolbar);

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

            _miniMapWindow = new MiniMapWindow(_miniMapLayout, _graphView);
            _miniMapWindow.SetLayoutChangedCallback(SavePanelState);
            _miniMapWindow.SetCollapsed(_panelViewSettings.IsMiniMapCollapsed);
            _graphView.Add(_miniMapWindow);

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
                _blackboardWindow.SetGraph(null);
                _graphSettingsWindow.SetGraph(null);
            }

            ApplyPanelVisibility();
            ApplyPanelLayouts();
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
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.style.height = 24.0f;
            toolbar.style.minHeight = 24.0f;
            toolbar.style.paddingLeft = 6.0f;
            toolbar.style.paddingRight = 4.0f;
            toolbar.style.paddingTop = 0.0f;
            toolbar.style.paddingBottom = 0.0f;
            toolbar.style.alignItems = Align.Center;

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

            _autoSaveToggle = new ToolbarToggle();
            _autoSaveToggle.text = "Auto Save";
            _autoSaveToggle.SetValueWithoutNotify(AutoSave);
            _autoSaveToggle.RegisterValueChangedCallback(OnAutoSaveToggleChanged);
            _autoSaveToggle.style.marginRight = 8.0f;
            toolbar.Add(_autoSaveToggle);

            _statusLabel = new Label(IdleStatusText);
            _statusLabel.style.fontSize = 11.0f;
            _statusLabel.style.color = new Color(0.76f, 0.76f, 0.76f, 1.0f);
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(_statusLabel);

            VisualElement toolbarSpacer = new VisualElement();
            toolbarSpacer.style.flexGrow = 1.0f;
            toolbar.Add(toolbarSpacer);

            VisualElement panelToggleGroup = new VisualElement();
            panelToggleGroup.style.flexDirection = FlexDirection.Row;
            panelToggleGroup.style.alignItems = Align.Center;
            toolbar.Add(panelToggleGroup);

            _blackboardToggle = BuildPanelToggle(
                LoadBlackboardIcon(),
                "Toggle Blackboard window",
                _panelViewSettings.IsBlackboardVisible,
                OnBlackboardToggleChanged);
            panelToggleGroup.Add(_blackboardToggle);

            _settingsToggle = BuildPanelToggle(
                LoadInspectorIcon(),
                "Toggle Graph Settings window",
                _panelViewSettings.IsGraphSettingsVisible,
                OnSettingsToggleChanged);
            panelToggleGroup.Add(_settingsToggle);

            _miniMapToggle = BuildPanelToggle(
                LoadMiniMapIcon(),
                "Toggle MiniMap window",
                _panelViewSettings.IsMiniMapVisible,
                OnMiniMapToggleChanged);
            panelToggleGroup.Add(_miniMapToggle);

            _diagnosticsToggle = BuildPanelToggle(
                LoadDiagnosticsIcon(),
                "Toggle Diagnostics panel",
                _panelViewSettings.IsDiagnosticsVisible,
                OnDiagnosticsToggleChanged);
            panelToggleGroup.Add(_diagnosticsToggle);

            _docsButton = BuildIconButton(
                LoadDocsIcon(),
                "Documentation (coming later)",
                OnDocsButtonClicked);
            panelToggleGroup.Add(_docsButton);

            return toolbar;
        }

        private ToolbarToggle BuildPanelToggle(Texture icon, string tooltip, bool initialValue, EventCallback<ChangeEvent<bool>> callback)
        {
            ToolbarToggle toggle = new ToolbarToggle();
            toggle.text = string.Empty;
            toggle.tooltip = tooltip;
            toggle.style.width = 24.0f;
            toggle.style.minWidth = 24.0f;
            toggle.style.height = 20.0f;
            toggle.style.justifyContent = Justify.Center;
            toggle.style.alignItems = Align.Center;
            toggle.style.paddingLeft = 0.0f;
            toggle.style.paddingRight = 0.0f;
            toggle.style.paddingTop = 0.0f;
            toggle.style.paddingBottom = 0.0f;
            toggle.style.marginLeft = 3.0f;
            toggle.style.marginRight = 0.0f;
            toggle.style.borderLeftWidth = 0.0f;
            toggle.style.borderTopWidth = 0.0f;
            toggle.style.borderRightWidth = 0.0f;
            toggle.style.borderBottomWidth = 0.0f;
            toggle.style.backgroundColor = Color.clear;
            toggle.style.unityBackgroundImageTintColor = Color.clear;
            toggle.style.borderTopLeftRadius = 0.0f;
            toggle.style.borderTopRightRadius = 0.0f;
            toggle.style.borderBottomLeftRadius = 0.0f;
            toggle.style.borderBottomRightRadius = 0.0f;

            Image image = new Image();
            image.image = icon;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.width = 16.0f;
            image.style.height = 16.0f;
            image.pickingMode = PickingMode.Ignore;
            toggle.Add(image);

            toggle.SetValueWithoutNotify(initialValue);
            ApplyPanelToggleVisualState(toggle, image, initialValue);
            toggle.RegisterValueChangedCallback(
                evt => ApplyPanelToggleVisualState(toggle, image, evt.newValue));
            toggle.RegisterValueChangedCallback(callback);
            return toggle;
        }

        private ToolbarButton BuildIconButton(Texture icon, string tooltip, System.Action clicked)
        {
            ToolbarButton button = new ToolbarButton(clicked);
            button.text = string.Empty;
            button.tooltip = tooltip;
            button.style.width = 24.0f;
            button.style.minWidth = 24.0f;
            button.style.height = 20.0f;
            button.style.justifyContent = Justify.Center;
            button.style.alignItems = Align.Center;
            button.style.paddingLeft = 0.0f;
            button.style.paddingRight = 0.0f;
            button.style.paddingTop = 0.0f;
            button.style.paddingBottom = 0.0f;
            button.style.marginLeft = 3.0f;
            button.style.marginRight = 0.0f;
            button.style.borderLeftWidth = 0.0f;
            button.style.borderTopWidth = 0.0f;
            button.style.borderRightWidth = 0.0f;
            button.style.borderBottomWidth = 0.0f;
            button.style.backgroundColor = Color.clear;
            button.style.unityBackgroundImageTintColor = Color.clear;

            Image image = new Image();
            image.image = icon;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.width = 16.0f;
            image.style.height = 16.0f;
            image.pickingMode = PickingMode.Ignore;
            image.tintColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            button.Add(image);
            return button;
        }

        private static void ApplyPanelToggleVisualState(ToolbarToggle toggle, Image image, bool isActive)
        {
            toggle.style.opacity = isActive ? 1.0f : 0.65f;
            image.tintColor = isActive
                ? new Color(0.92f, 0.92f, 0.92f, 1.0f)
                : new Color(0.72f, 0.72f, 0.72f, 1.0f);
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
            if (graph != null && !TryPrepareGraphForEditing(graph))
            {
                return;
            }

            _loadedGraph = graph;

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(graph);
            }

            LoadGraphInCanvas(graph, Vector3.zero, 1.0f);
        }

        private bool TryPrepareGraphForEditing(GenGraph graph)
        {
            bool changed;
            string errorMessage;
            if (!GraphOutputUtility.TryUpgradeToCurrentSchema(graph, out changed, out errorMessage))
            {
                Debug.LogError("Failed to upgrade graph '" + graph.name + "': " + errorMessage);
                return false;
            }

            if (changed)
            {
                EditorUtility.SetDirty(graph);
                if (AutoSave)
                {
                    AssetDatabase.SaveAssets();
                }
            }

            return true;
        }

        private void LoadGraphInCanvas(GenGraph graph, Vector3 scrollOffset, float zoomScale)
        {
            _blackboardWindow?.SetGraph(graph);
            _graphSettingsWindow?.SetGraph(graph);

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
                _diagnosticsPanel.Populate(Array.Empty<GraphDiagnostic>());
            }

            SetStatus(IdleStatusText);
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
        }

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
            titleContent = new GUIContent(WindowTitle);
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

        private void OnGraphViewGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            ApplyPanelLayouts();
        }

        private void ApplyPanelVisibility()
        {
            _blackboardWindow?.SetVisible(_panelViewSettings.IsBlackboardVisible);
            _graphSettingsWindow?.SetVisible(_panelViewSettings.IsGraphSettingsVisible);
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

            if (_miniMapWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _miniMapWindow.CaptureCurrentLayout();
                _panelViewSettings.IsMiniMapCollapsed = _miniMapWindow.IsCollapsedForTesting;
            }

            EditorUserSettings.SetConfigValue(
                PanelViewSettingsPrefsKey,
                JsonUtility.ToJson(_panelViewSettings ?? new PanelViewSettings()));
            EditorUserSettings.SetConfigValue(
                BlackboardLayoutPrefsKey,
                JsonUtility.ToJson(_blackboardLayout ?? CreateDefaultBlackboardLayout()));
            EditorUserSettings.SetConfigValue(
                GraphSettingsLayoutPrefsKey,
                JsonUtility.ToJson(_graphSettingsLayout ?? CreateDefaultGraphSettingsLayout()));
            EditorUserSettings.SetConfigValue(
                MiniMapLayoutPrefsKey,
                JsonUtility.ToJson(_miniMapLayout ?? CreateDefaultMiniMapLayout()));
        }

        private static PanelViewSettings LoadPanelViewSettings()
        {
            string json = EditorUserSettings.GetConfigValue(PanelViewSettingsPrefsKey);
            return string.IsNullOrWhiteSpace(json)
                ? new PanelViewSettings()
                : (JsonUtility.FromJson<PanelViewSettings>(json) ?? new PanelViewSettings());
        }

        private static FloatingWindowLayout LoadLayout(string prefsKey, FloatingWindowLayout defaultLayout)
        {
            string json = EditorUserSettings.GetConfigValue(prefsKey);
            return string.IsNullOrWhiteSpace(json)
                ? defaultLayout
                : (JsonUtility.FromJson<FloatingWindowLayout>(json) ?? defaultLayout);
        }

        internal static PanelViewSettings LoadPanelViewSettingsForTesting()
        {
            return LoadPanelViewSettings();
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

        internal static FloatingWindowLayout CreateDefaultMiniMapLayoutForTesting()
        {
            return CreateDefaultMiniMapLayout();
        }

        internal static void SavePanelViewSettingsForTesting(PanelViewSettings panelViewSettings)
        {
            EditorUserSettings.SetConfigValue(PanelViewSettingsPrefsKey, JsonUtility.ToJson(panelViewSettings));
        }

        internal static void SaveBlackboardLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorUserSettings.SetConfigValue(BlackboardLayoutPrefsKey, JsonUtility.ToJson(layout));
        }

        internal static void SaveGraphSettingsLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorUserSettings.SetConfigValue(GraphSettingsLayoutPrefsKey, JsonUtility.ToJson(layout));
        }

        internal static void SaveMiniMapLayoutForTesting(FloatingWindowLayout layout)
        {
            EditorUserSettings.SetConfigValue(MiniMapLayoutPrefsKey, JsonUtility.ToJson(layout));
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
