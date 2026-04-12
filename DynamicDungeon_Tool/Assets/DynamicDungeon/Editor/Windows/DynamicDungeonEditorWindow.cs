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

        private ObjectField _graphField;
        private Label _statusLabel;
        private ToolbarToggle _autoSaveToggle;
        private DynamicDungeonGraphView _graphView;
        private GenerationOrchestrator _generationOrchestrator;
        private DiagnosticsPanel _diagnosticsPanel;

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

            _graphView = new DynamicDungeonGraphView();
            rootVisualElement.Add(_graphView);

            _diagnosticsPanel = BuildDiagnosticsPanel();
            rootVisualElement.Add(_diagnosticsPanel);

            _generationOrchestrator = new GenerationOrchestrator(_graphView, SetStatus, OnDiagnosticsUpdated);
            _graphView.SetGenerationOrchestrator(_generationOrchestrator);
            _graphView.SetAfterMutationCallback(OnAfterGraphMutation);

            _graphField.SetValueWithoutNotify(_loadedGraph);

            if (_loadedGraph != null)
            {
                _generationOrchestrator.SetGraph(_loadedGraph);
                _graphView.LoadGraph(_loadedGraph);
                _diagnosticsPanel.SetGraphContext(_graphView, _loadedGraph);
                _generationOrchestrator.RequestPreviewRefresh();
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
                    _generationOrchestrator.RequestPreviewRefresh();
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

        private void SetStatus(string statusText)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = statusText;
            }
        }
    }
}
