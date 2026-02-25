// Assets/DynamicDungeon/Editor/Windows/DynamicDungeonEditorWindow.cs

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System;

/// <summary>
/// Main editor window for the DynamicDungeon PCG tool.
///
/// v2 changes:
///   • Minimap toggle button in the toolbar (syncs label with state).
///   • LoadGraph triggers an immediate auto-preview pass so the graph
///     looks populated on first open without pressing Generate.
/// </summary>
public sealed class DynamicDungeonEditorWindow : EditorWindow
{
    private static readonly string WindowTitle = "DynamicDungeon";
    private static readonly Vector2 MinWindowSize = new Vector2(800f, 500f);

    private GenGraph _activeGraph;
    private GenGraphView _graphView;
    private Label _graphNameLabel;
    private Button _generateButton;
    private Button _minimapToggleButton;
    private ScrollView _layerListView;
    private long _previewSeed;
    private bool _hasPreviewSeed;

    private const float LayerPanelWidth = 340f;

    // ── Open ──────────────────────────────────────────────────────────────────

    [MenuItem("Window/DynamicDungeon/Graph Editor")]
    public static void OpenEmpty()
    {
        DynamicDungeonEditorWindow window = GetWindow<DynamicDungeonEditorWindow>(WindowTitle);
        window.minSize = MinWindowSize;
        window.Show();
    }

    public static void OpenWithGraph(GenGraph graph)
    {
        DynamicDungeonEditorWindow window = GetWindow<DynamicDungeonEditorWindow>(WindowTitle);
        window.minSize = MinWindowSize;
        window.LoadGraph(graph);
        window.Show();
    }

    [OnOpenAsset(1)]
    public static bool OnOpenAsset(int instanceId, int line)
    {
        UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceId);
        if (obj is GenGraph genGraph) { OpenWithGraph(genGraph); return true; }
        return false;
    }

    // ── UI Construction ───────────────────────────────────────────────────────

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.flexDirection = FlexDirection.Column;

        BuildToolbar(root);
        BuildWorkspace(root);

        if (_activeGraph != null)
        {
            _graphView.LoadGraph(_activeGraph);
            RefreshLayerPanel();
            UpdateMinimapButtonLabel();
        }
    }

    private void BuildToolbar(VisualElement root)
    {
        VisualElement toolbar = new VisualElement();
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.flexShrink = 0;
        toolbar.style.height = 32;
        toolbar.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f));
        toolbar.style.paddingLeft = 8;
        toolbar.style.paddingRight = 8;
        toolbar.style.alignItems = Align.Center;
        toolbar.style.borderBottomWidth = 1;
        toolbar.style.borderBottomColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f));

        // Graph name.
        _graphNameLabel = new Label("No Graph Loaded");
        _graphNameLabel.style.flexGrow = 1;
        _graphNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        toolbar.Add(_graphNameLabel);

        // Separator helper.
        VisualElement Sep()
        {
            VisualElement s = new VisualElement();
            s.style.width = 1;
            s.style.height = 18;
            s.style.backgroundColor = new StyleColor(new Color(0.35f, 0.35f, 0.35f));
            s.style.marginLeft = 4;
            s.style.marginRight = 4;
            return s;
        }

        // New graph.
        Button newButton = new Button(OnNewGraphClicked) { text = "+ New" };
        newButton.style.marginRight = 2;
        toolbar.Add(newButton);

        // Save.
        Button saveButton = new Button(OnSaveClicked) { text = "Save" };
        toolbar.Add(saveButton);

        toolbar.Add(Sep());

        // Minimap toggle — label updates when toggled.
        _minimapToggleButton = new Button(OnMinimapToggleClicked) { text = "⊞ Minimap: ON" };
        _minimapToggleButton.style.marginRight = 2;
        toolbar.Add(_minimapToggleButton);

        toolbar.Add(Sep());

        // Generate.
        _generateButton = new Button(OnGenerateClicked) { text = "▶  Generate" };
        _generateButton.style.backgroundColor = new StyleColor(new Color(0.2f, 0.55f, 0.2f));
        _generateButton.style.color = new StyleColor(Color.white);
        _generateButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        toolbar.Add(_generateButton);

        root.Add(toolbar);
    }

    private void BuildWorkspace(VisualElement root)
    {
        VisualElement workspace = new VisualElement();
        workspace.style.flexDirection = FlexDirection.Row;
        workspace.style.flexGrow = 1;

        _graphView = new GenGraphView(this);
        _graphView.style.flexGrow = 1;
        workspace.Add(_graphView);

        VisualElement layerPanel = BuildLayerPanel();
        workspace.Add(layerPanel);

        root.Add(workspace);
    }

    private VisualElement BuildLayerPanel()
    {
        VisualElement panel = new VisualElement();
        panel.style.width = LayerPanelWidth;
        panel.style.flexShrink = 0;
        panel.style.backgroundColor = new StyleColor(new Color(0.13f, 0.13f, 0.13f));
        panel.style.borderLeftWidth = 1;
        panel.style.borderLeftColor = new StyleColor(new Color(0.09f, 0.09f, 0.09f));
        panel.style.paddingTop = 10;
        panel.style.paddingBottom = 10;
        panel.style.paddingLeft = 10;
        panel.style.paddingRight = 10;

        Label title = new Label("Graph Layers");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 13;
        panel.Add(title);

        Label subtitle = new Label("Like Unity layers: keep the graph's shared typed layers here.");
        subtitle.style.whiteSpace = WhiteSpace.Normal;
        subtitle.style.fontSize = 10;
        subtitle.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
        subtitle.style.marginBottom = 8;
        panel.Add(subtitle);

        ObjectField tilesetField = new ObjectField("Tile Ruleset")
        {
            objectType = typeof(TileRulesetAsset),
            allowSceneObjects = false
        };
        tilesetField.RegisterValueChangedCallback(evt =>
        {
            if (_activeGraph == null)
                return;

            _activeGraph.SetTileRuleset(evt.newValue as TileRulesetAsset);
            PersistGraphChanges(true);
        });
        panel.Add(tilesetField);

        VisualElement buttonGrid = new VisualElement();
        buttonGrid.style.flexDirection = FlexDirection.Column;
        buttonGrid.style.marginBottom = 8;

        VisualElement firstButtonRow = new VisualElement();
        firstButtonRow.style.flexDirection = FlexDirection.Row;

        VisualElement secondButtonRow = new VisualElement();
        secondButtonRow.style.flexDirection = FlexDirection.Row;
        secondButtonRow.style.marginTop = 4;

        Button addFloatButton = new Button(() => CreateLayer(PortDataKind.FloatLayer)) { text = "+ Float" };
        Button addIntButton = new Button(() => CreateLayer(PortDataKind.IntLayer)) { text = "+ Int" };
        Button addMaskButton = new Button(() => CreateLayer(PortDataKind.BoolMask)) { text = "+ Mask" };
        Button addMarkerButton = new Button(() => CreateLayer(PortDataKind.MarkerSet)) { text = "+ Markers" };

        addFloatButton.style.flexGrow = 1;
        addIntButton.style.flexGrow = 1;
        addMaskButton.style.flexGrow = 1;
        addMarkerButton.style.flexGrow = 1;

        firstButtonRow.Add(addFloatButton);
        firstButtonRow.Add(addIntButton);
        secondButtonRow.Add(addMaskButton);
        secondButtonRow.Add(addMarkerButton);

        buttonGrid.Add(firstButtonRow);
        buttonGrid.Add(secondButtonRow);
        panel.Add(buttonGrid);

        _layerListView = new ScrollView();
        _layerListView.style.flexGrow = 1;
        panel.Add(_layerListView);

        panel.userData = tilesetField;

        return panel;
    }

    // ── Graph Loading ─────────────────────────────────────────────────────────

    public void LoadGraph(GenGraph graph)
    {
        _activeGraph = graph;
        ResetPreviewSeed();

        if (_activeGraph != null)
            GraphAuthoringUtility.EnsureBootstrapGraph(_activeGraph);

        if (_graphView != null)
        {
            _graphView.LoadGraph(_activeGraph);
            // Previews are triggered inside LoadGraph via a short schedule delay.
        }

        RefreshLayerPanel();

        if (_graphNameLabel != null)
            _graphNameLabel.text = _activeGraph != null ? _activeGraph.name : "No Graph Loaded";
    }

    public GenGraph ActiveGraph => _activeGraph;

    // ── Toolbar Callbacks ─────────────────────────────────────────────────────

    private void OnNewGraphClicked()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create New Generation Graph", "NewGenGraph", "asset",
            "Choose a location for the new Generation Graph asset.");

        if (string.IsNullOrEmpty(path)) return;

        GenGraph newGraph = ScriptableObject.CreateInstance<GenGraph>();
        AssetDatabase.CreateAsset(newGraph, path);
        AssetDatabase.SaveAssets();
        GraphAuthoringUtility.EnsureBootstrapGraph(newGraph);
        LoadGraph(newGraph);
    }

    private void OnSaveClicked()
    {
        if (_activeGraph == null)
        {
            EditorUtility.DisplayDialog("Save", "No graph is loaded.", "OK");
            return;
        }
        EditorUtility.SetDirty(_activeGraph);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DynamicDungeon] Graph '{_activeGraph.name}' saved.");
    }

    private void OnMinimapToggleClicked()
    {
        if (_graphView == null) return;
        _graphView.ToggleMinimap();
        UpdateMinimapButtonLabel();
    }

    private void UpdateMinimapButtonLabel()
    {
        if (_minimapToggleButton == null || _graphView == null) return;
        _minimapToggleButton.text = _graphView.IsMinimapVisible
            ? "⊞ Minimap: ON"
            : "⊞ Minimap: OFF";
    }

    private void OnGenerateClicked()
    {
        if (_activeGraph == null)
        {
            EditorUtility.DisplayDialog("Generate", "No graph is loaded.", "OK");
            return;
        }

        GraphProcessor processor = new GraphProcessor(_activeGraph);
        GraphProcessorResult result = processor.Execute(GetPreviewExecutionContext(true));

        if (result.IsSuccess)
        {
            Debug.Log("[DynamicDungeon] Generation succeeded.");
            _graphView.RefreshAllPreviews(result);
        }
        else
        {
            Debug.LogError($"[DynamicDungeon] Generation failed: {result.ErrorMessage}");
            EditorUtility.DisplayDialog("Generation Failed", result.ErrorMessage, "OK");
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnDisable()
    {
        if (_activeGraph != null)
            EditorUtility.SetDirty(_activeGraph);
    }

    private void RefreshLayerPanel()
    {
        if (_layerListView == null)
            return;

        _layerListView.Clear();

        if (_activeGraph == null)
        {
            _layerListView.Add(new Label("Load a graph to edit its layers."));
            return;
        }

        ObjectField tilesetField = _layerListView.parent.userData as ObjectField;
        if (tilesetField != null)
            tilesetField.SetValueWithoutNotify(_activeGraph.TileRuleset);

        if (_activeGraph.Layers.Count == 0)
        {
            Label emptyLabel = new Label("No layers yet. Add one with the buttons above.");
            emptyLabel.style.color = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            _layerListView.Add(emptyLabel);
            return;
        }

        for (int index = 0; index < _activeGraph.Layers.Count; index++)
            _layerListView.Add(BuildLayerRow(_activeGraph.Layers[index], index));
    }

    private VisualElement BuildLayerRow(GraphLayerDefinition layer, int index)
    {
        VisualElement row = new VisualElement();
        row.style.marginBottom = 6;
        row.style.paddingTop = 6;
        row.style.paddingBottom = 6;
        row.style.paddingLeft = 6;
        row.style.paddingRight = 6;
        row.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.18f));
        row.style.borderTopLeftRadius = 4;
        row.style.borderTopRightRadius = 4;
        row.style.borderBottomLeftRadius = 4;
        row.style.borderBottomRightRadius = 4;

        VisualElement topRow = new VisualElement();
        topRow.style.flexDirection = FlexDirection.Row;
        topRow.style.alignItems = Align.Center;

        Label indexLabel = new Label((index + 1).ToString("00"));
        indexLabel.style.minWidth = 28;
        indexLabel.style.color = new StyleColor(new Color(0.78f, 0.78f, 0.78f));
        topRow.Add(indexLabel);

        TextField nameField = new TextField { value = layer.DisplayName };
        nameField.isDelayed = true;
        nameField.style.flexGrow = 1;
        nameField.RegisterValueChangedCallback(evt =>
        {
            if (_activeGraph == null)
                return;

            _activeGraph.TryRenameLayer(layer.LayerId, evt.newValue);
            PersistGraphChanges(false);
            nameField.SetValueWithoutNotify(_activeGraph.GetLayerDisplayName(layer.LayerId));
        });
        topRow.Add(nameField);

        Button upButton = new Button(() => MoveLayer(index, index - 1)) { text = "▲" };
        Button downButton = new Button(() => MoveLayer(index, index + 1)) { text = "▼" };
        Button deleteButton = new Button(() => DeleteLayer(layer.LayerId)) { text = "X" };

        upButton.SetEnabled(index > 0);
        downButton.SetEnabled(index < _activeGraph.Layers.Count - 1);

        topRow.Add(upButton);
        topRow.Add(downButton);
        topRow.Add(deleteButton);
        row.Add(topRow);

        EnumField kindField = new EnumField(layer.Kind);
        kindField.style.marginTop = 4;
        kindField.RegisterValueChangedCallback(evt =>
        {
            if (_activeGraph == null)
                return;

            if (evt.newValue is PortDataKind kind)
            {
                _activeGraph.TrySetLayerKind(layer.LayerId, kind);
                PersistGraphChanges(true);
                RefreshLayerPanel();
            }
        });
        row.Add(kindField);

        return row;
    }

    private void CreateLayer(PortDataKind kind)
    {
        if (_activeGraph == null)
            return;

        _activeGraph.AddLayer($"New {kind}", kind);
        PersistGraphChanges(true);
        RefreshLayerPanel();
    }

    private void MoveLayer(int fromIndex, int toIndex)
    {
        if (_activeGraph == null)
            return;

        if (_activeGraph.MoveLayer(fromIndex, toIndex))
        {
            PersistGraphChanges(false);
            RefreshLayerPanel();
        }
    }

    private void DeleteLayer(string layerId)
    {
        if (_activeGraph == null)
            return;

        if (_activeGraph.RemoveLayer(layerId))
        {
            PersistGraphChanges(true);
            RefreshLayerPanel();
        }
    }

    private void PersistGraphChanges(bool refreshPreviews)
    {
        if (_activeGraph == null)
            return;

        EditorUtility.SetDirty(_activeGraph);
        AssetDatabase.SaveAssets();

        if (refreshPreviews && _graphView != null)
            _graphView.SchedulePreviewRefresh();
    }

    public GraphExecutionContext GetPreviewExecutionContext(bool rerollSeed = false)
    {
        if (_activeGraph == null)
            return null;

        if (!_activeGraph.RandomizeSeedByDefault)
        {
            _previewSeed = _activeGraph.DefaultSeed;
            _hasPreviewSeed = true;
        }
        else
        {
            if (rerollSeed || !_hasPreviewSeed)
                _previewSeed = DateTime.UtcNow.Ticks;

            _hasPreviewSeed = true;
        }

        return new GraphExecutionContext(
            _activeGraph,
            _activeGraph.DefaultWidth,
            _activeGraph.DefaultHeight,
            _previewSeed);
    }

    private void ResetPreviewSeed()
    {
        _hasPreviewSeed = false;
        _previewSeed = 0L;
    }
}
