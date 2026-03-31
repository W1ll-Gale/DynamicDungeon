using System;
using DynamicDungeon.Runtime.Core;
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
        private const string GeneratingStatusText = "Generating…";
        private const string DoneStatusText = "Done";
        private const string FailedStatusText = "Failed";
        private const string NoGraphTitle = "No Graph";
        private const string DiagnosticsPlaceholderText = "Diagnostics panel placeholder";
        private const float DiagnosticsPanelHeight = 120.0f;

        private ObjectField _graphField;
        private Label _statusLabel;
        private DynamicDungeonGraphView _graphView;

        [SerializeField]
        private GenGraph _loadedGraph;

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

            VisualElement diagnosticsPlaceholder = BuildDiagnosticsPlaceholder();
            rootVisualElement.Add(diagnosticsPlaceholder);

            _graphField.SetValueWithoutNotify(_loadedGraph);

            if (_loadedGraph != null)
            {
                _graphView.LoadGraph(_loadedGraph);
            }

            SetStatus(IdleStatusText);
            RefreshWindowTitle();
        }

        private void OnEnable()
        {
            RefreshWindowTitle();
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

            _statusLabel = new Label(IdleStatusText);
            _statusLabel.style.marginLeft = 8.0f;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(_statusLabel);

            return toolbar;
        }

        private VisualElement BuildDiagnosticsPlaceholder()
        {
            VisualElement placeholder = new VisualElement();
            placeholder.style.height = DiagnosticsPanelHeight;
            placeholder.style.flexShrink = 0.0f;
            placeholder.style.borderTopWidth = 1.0f;
            placeholder.style.borderTopColor = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            placeholder.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            placeholder.style.paddingLeft = 8.0f;
            placeholder.style.paddingRight = 8.0f;
            placeholder.style.paddingTop = 6.0f;
            placeholder.style.paddingBottom = 6.0f;

            Label diagnosticsHeader = new Label("Diagnostics");
            diagnosticsHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            placeholder.Add(diagnosticsHeader);

            Label diagnosticsPlaceholder = new Label(DiagnosticsPlaceholderText);
            diagnosticsPlaceholder.style.marginTop = 4.0f;
            placeholder.Add(diagnosticsPlaceholder);

            return placeholder;
        }

        private void CancelGeneration()
        {
            SetStatus(IdleStatusText);
        }

        private void GenerateGraph()
        {
            if (_loadedGraph == null)
            {
                SetStatus(FailedStatusText);
                return;
            }

            SetStatus(GeneratingStatusText);

            try
            {
                GraphCompileResult compileResult = GraphCompiler.Compile(_loadedGraph);
                if (compileResult.IsSuccess && compileResult.Plan != null)
                {
                    compileResult.Plan.Dispose();
                    SetStatus(DoneStatusText);
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Graph preflight failed: " + exception.Message);
            }

            SetStatus(FailedStatusText);
        }

        private void LoadGraph(GenGraph graph)
        {
            _loadedGraph = graph;

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(graph);
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
                }
            }

            SetStatus(IdleStatusText);
            RefreshWindowTitle();
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
