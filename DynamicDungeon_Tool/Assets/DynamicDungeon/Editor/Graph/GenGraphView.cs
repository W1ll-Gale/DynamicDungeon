using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GenGraphView : GraphView
{
    private GenGraph _graph;
    private readonly DynamicDungeonEditorWindow _window;
    private readonly Dictionary<string, GenNodeView> _nodeViews = new Dictionary<string, GenNodeView>();

    private NodeSearchWindow _searchWindow;
    private MiniMap _miniMap;
    private bool _minimapVisible = true;
    private IVisualElementScheduledItem _pendingRefresh;
    private bool _isLoading;

    private const long DebounceMs = 120L;

    public GenGraphView(DynamicDungeonEditorWindow window)
    {
        _window = window;

        SetupZoomAndPan();
        SetupBackground();
        SetupMinimap();
        RegisterChangeCallbacks();
        SetupNodeCreationRequest();
    }

    public bool IsMinimapVisible => _minimapVisible;

    private void SetupZoomAndPan()
    {
        this.AddManipulator(new ContentZoomer());
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
    }

    private void SetupBackground()
    {
        GridBackground grid = new GridBackground();
        grid.StretchToParentSize();
        Insert(0, grid);
    }

    private void SetupMinimap()
    {
        _miniMap = new MiniMap { anchored = true };
        _miniMap.SetPosition(new Rect(10f, 30f, 200f, 140f));
        Add(_miniMap);
    }

    private void RegisterChangeCallbacks()
    {
        graphViewChanged = OnGraphViewChanged;
    }

    private void SetupNodeCreationRequest()
    {
        _searchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();
        _searchWindow.Initialise(_window, this);

        nodeCreationRequest = context =>
            SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchWindow);
    }

    public void ToggleMinimap()
    {
        _minimapVisible = !_minimapVisible;
        _miniMap.style.display = _minimapVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void SetMinimapVisible(bool visible)
    {
        _minimapVisible = visible;
        _miniMap.style.display = _minimapVisible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void LoadGraph(GenGraph graph)
    {
        _graph = graph;
        ClearViewOnly();

        if (_graph == null) return;

        foreach (GenNodeBase node in _graph.Nodes)
        {
            if (node == null) continue;
            GenNodeView view = CreateNodeView(node);
            _nodeViews[node.NodeId] = view;
        }

        foreach (PortConnection connection in _graph.Connections)
            ReconstructEdge(connection);

        schedule.Execute(AutoRefreshPreviews).ExecuteLater(100L);
    }

    public void CreateNode(System.Type nodeType, Vector2 screenPosition)
    {
        if (_graph == null)
        {
            Debug.LogWarning("[GenGraphView] No graph is loaded. Cannot create node.");
            return;
        }

        Vector2 worldPos = _window.rootVisualElement.ChangeCoordinatesTo(
            _window.rootVisualElement.parent,
            screenPosition - _window.position.position);
        Vector2 localPos = contentViewContainer.WorldToLocal(worldPos);

        GenNodeBase nodeInstance = ScriptableObject.CreateInstance(nodeType) as GenNodeBase;
        if (nodeInstance == null)
        {
            Debug.LogError($"[GenGraphView] Could not create node of type {nodeType.Name}.");
            return;
        }

        nodeInstance.name = nodeType.Name;
        nodeInstance.EditorPosition = localPos;

        AssetDatabase.AddObjectToAsset(nodeInstance, _graph);
        AssetDatabase.SaveAssets();

        _graph.AddNode(nodeInstance);
        EditorUtility.SetDirty(_graph);
        AssetDatabase.SaveAssets();

        GenNodeView view = CreateNodeView(nodeInstance);
        _nodeViews[nodeInstance.NodeId] = view;

        SchedulePreviewRefresh();
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        List<Port> compatible = new List<Port>();

        foreach (Port port in ports.ToList())
        {
            if (port == startPort) continue;
            if (port.node == startPort.node) continue;
            if (port.direction == startPort.direction) continue;

            if (!(startPort.node is GenNodeView startNodeView)) continue;
            if (!(port.node is GenNodeView candidateNodeView)) continue;

            NodePort startMetadata = startNodeView.GetNodePort(startPort);
            NodePort candidateMetadata = candidateNodeView.GetNodePort(port);

            if (startMetadata == null || candidateMetadata == null) continue;
            if (startMetadata.DataKind != candidateMetadata.DataKind) continue;

            compatible.Add(port);
        }

        return compatible;
    }

    public void SchedulePreviewRefresh()
    {
        _pendingRefresh?.Pause();
        _pendingRefresh = schedule.Execute(AutoRefreshPreviews);
        _pendingRefresh.ExecuteLater(DebounceMs);
    }

    public void AutoRefreshPreviews()
    {
        if (_graph == null || _graph.Nodes.Count == 0) return;

        GraphProcessor processor = new GraphProcessor(_graph);
        GraphProcessorResult result = processor.Execute(_window.GetPreviewExecutionContext());

        if (!result.IsSuccess)
        {
            Debug.LogWarning($"[GenGraphView] Auto-preview: {result.ErrorMessage}");
            return;
        }

        RefreshAllPreviews(result);
    }

    public void RefreshAllPreviews(GraphProcessorResult result)
    {
        foreach (KeyValuePair<string, GenNodeView> pair in _nodeViews)
        {
            string nodeId = pair.Key;
            GenNodeView view = pair.Value;

            NodeValue previewValue = null;

            string preferredPortName = view.BoundNode.PreferredPreviewPortName;
            if (!string.IsNullOrEmpty(preferredPortName))
                result.TryGetNodeOutput(nodeId, preferredPortName, out previewValue);

            if (previewValue == null)
            {
                string preferredInputPortName = view.BoundNode.PreferredPreviewInputPortName;
                if (!string.IsNullOrEmpty(preferredInputPortName))
                    previewValue = ResolvePreviewFromInput(nodeId, preferredInputPortName, result);
            }

            if (previewValue == null)
            {
                foreach (NodePort outputPort in view.BoundNode.OutputPorts)
                {
                    if (result.TryGetNodeOutput(nodeId, outputPort.PortName, out previewValue))
                        break;
                }
            }

            Texture2D preview = previewValue != null
                ? NodePreviewUtility.GeneratePreview(previewValue, 80, 80, _graph.TileRuleset)
                : null;

            view.SetPreviewTexture(preview);
        }
    }

    private NodeValue ResolvePreviewFromInput(string nodeId, string inputPortName, GraphProcessorResult result)
    {
        GenNodeBase node = _graph.FindNodeById(nodeId);
        if (node == null || !node.TryGetInputPort(inputPortName, out NodePort inputPort) || inputPort == null)
            return null;

        foreach (PortConnection connection in _graph.GetConnectionsToNode(nodeId))
        {
            if (connection.InputPortId != inputPort.PortId)
                continue;

            GenNodeBase upstreamNode = _graph.FindNodeById(connection.OutputNodeId);
            if (upstreamNode == null)
                continue;

            NodePort outputPort = upstreamNode.GetOutputPortById(connection.OutputPortId);
            if (outputPort == null)
                continue;

            if (result.TryGetNodeOutput(connection.OutputNodeId, outputPort.PortName, out NodeValue previewValue))
                return previewValue;
        }

        return null;
    }

    private GenNodeView CreateNodeView(GenNodeBase node)
    {
        GenNodeView view = new GenNodeView(node, this);
        view.SetPosition(new Rect(node.EditorPosition, Vector2.zero));
        AddElement(view);
        return view;
    }

    private void ReconstructEdge(PortConnection connection)
    {
        if (!_nodeViews.TryGetValue(connection.OutputNodeId, out GenNodeView outputView)) return;
        if (!_nodeViews.TryGetValue(connection.InputNodeId, out GenNodeView inputView)) return;

        Port outputPort = outputView.GetOutputPort(connection.OutputPortId);
        Port inputPort = inputView.GetInputPort(connection.InputPortId);
        if (outputPort == null || inputPort == null) return;

        Edge edge = outputPort.ConnectTo(inputPort);
        AddElement(edge);
    }

    private void ClearViewOnly()
    {
        _isLoading = true;
        _nodeViews.Clear();
        DeleteElements(graphElements.ToList());
        _isLoading = false;
    }

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (_isLoading || _graph == null) return change;

        if (change.edgesToCreate != null)
        {
            foreach (Edge edge in change.edgesToCreate)
                HandleEdgeCreated(edge);
        }

        if (change.elementsToRemove != null)
        {
            foreach (GraphElement element in change.elementsToRemove)
            {
                if (element is Edge edge) HandleEdgeRemoved(edge);
                else if (element is GenNodeView nodeView) HandleNodeRemoved(nodeView);
            }
        }

        if (change.movedElements != null)
        {
            foreach (GraphElement element in change.movedElements)
            {
                if (element is GenNodeView nodeView)
                {
                    nodeView.SyncPositionToData();
                    EditorUtility.SetDirty(_graph);
                }
            }
        }

        SchedulePreviewRefresh();
        return change;
    }

    private void HandleEdgeCreated(Edge edge)
    {
        if (!(edge.output.node is GenNodeView outputNodeView)) return;
        if (!(edge.input.node is GenNodeView inputNodeView)) return;

        string outputPortId = outputNodeView.GetPortId(edge.output);
        string inputPortId = inputNodeView.GetPortId(edge.input);

        _graph.AddConnection(
            outputNodeView.BoundNode.NodeId,
            outputPortId,
            inputNodeView.BoundNode.NodeId,
            inputPortId);

        EditorUtility.SetDirty(_graph);
    }

    private void HandleEdgeRemoved(Edge edge)
    {
        if (!(edge.output.node is GenNodeView outputNodeView)) return;
        if (!(edge.input.node is GenNodeView inputNodeView)) return;

        string outputPortId = outputNodeView.GetPortId(edge.output);
        string inputPortId = inputNodeView.GetPortId(edge.input);

        foreach (PortConnection connection in _graph.Connections)
        {
            if (connection.OutputNodeId == outputNodeView.BoundNode.NodeId &&
                connection.OutputPortId == outputPortId &&
                connection.InputNodeId == inputNodeView.BoundNode.NodeId &&
                connection.InputPortId == inputPortId)
            {
                _graph.RemoveConnection(connection);
                EditorUtility.SetDirty(_graph);
                break;
            }
        }
    }

    private void HandleNodeRemoved(GenNodeView nodeView)
    {
        _graph.RemoveNode(nodeView.BoundNode);
        _nodeViews.Remove(nodeView.BoundNode.NodeId);

        AssetDatabase.RemoveObjectFromAsset(nodeView.BoundNode);
        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(_graph);
    }
}
