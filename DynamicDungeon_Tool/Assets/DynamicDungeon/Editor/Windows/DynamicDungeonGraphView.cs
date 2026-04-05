using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonGraphView : GraphView
    {
        private readonly GridBackground _gridBackground;
        private readonly Dictionary<string, GenNodeView> _nodeViewsById = new Dictionary<string, GenNodeView>();
        private readonly VisualElement _generationOverlay;
        private readonly IEdgeConnectorListener _edgeConnectorListener;

        private NodeSearchWindow _nodeSearchWindow;
        private GenGraph _graph;
        private GenerationOrchestrator _generationOrchestrator;
        private bool _suppressGraphMutationCallbacks;
        private Vector2 _lastGraphLocalMousePosition;

        public GenGraph Graph
        {
            get
            {
                return _graph;
            }
        }

        public DynamicDungeonGraphView()
        {
            GraphView currentGraphView = this;

            style.flexGrow = 1.0f;
            focusable = true;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            currentGraphView.AddManipulator(new ContentDragger());
            currentGraphView.AddManipulator(new SelectionDragger());
            currentGraphView.AddManipulator(new RectangleSelector());
            graphViewChanged = OnGraphViewChanged;

            _gridBackground = new GridBackground();
            _edgeConnectorListener = new GenEdgeConnectorListener();
            Insert(0, _gridBackground);
            _gridBackground.style.position = Position.Absolute;
            _gridBackground.style.left = 0.0f;
            _gridBackground.style.top = 0.0f;
            _gridBackground.style.right = 0.0f;
            _gridBackground.style.bottom = 0.0f;

            _generationOverlay = BuildGenerationOverlay();
            Add(_generationOverlay);

            _nodeSearchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();
            _nodeSearchWindow.Initialise(this);

            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<ContextualMenuPopulateEvent>(OnGraphContextualMenuPopulate, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatiblePorts = new List<Port>();
            NodePortDefinition startPortDefinition;
            if (!GenPortUtility.TryGetPortDefinition(startPort, out startPortDefinition))
            {
                return compatiblePorts;
            }

            foreach (Port candidatePort in ports)
            {
                if (ReferenceEquals(candidatePort, startPort) || candidatePort.node == startPort.node)
                {
                    continue;
                }

                if (GenPortUtility.CanConnectTo(startPort, candidatePort))
                {
                    compatiblePorts.Add(candidatePort);
                }
            }

            return compatiblePorts;
        }

        public void SetGenerationOrchestrator(GenerationOrchestrator generationOrchestrator)
        {
            _generationOrchestrator = generationOrchestrator;
        }

        public void SetGenerationOverlayVisible(bool isVisible)
        {
            if (_generationOverlay == null)
            {
                return;
            }

            _generationOverlay.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void ClearNodePreviews()
        {
            foreach (KeyValuePair<string, GenNodeView> nodePair in _nodeViewsById)
            {
                nodePair.Value.ClearPreview();
            }
        }

        public void MarkNodePreviewStale(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            GenNodeView nodeView;
            if (_nodeViewsById.TryGetValue(nodeId, out nodeView))
            {
                nodeView.MarkStale();
            }
        }

        public void SetNodePreview(string nodeId, Texture2D texture)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                return;
            }

            GenNodeView nodeView;
            if (_nodeViewsById.TryGetValue(nodeId, out nodeView))
            {
                nodeView.SetPreview(texture);
                return;
            }

            if (texture != null)
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        public bool SelectAndFrameNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            GenNodeView nodeView;
            if (!_nodeViewsById.TryGetValue(nodeId, out nodeView))
            {
                return false;
            }

            ClearSelection();
            AddToSelection(nodeView);
            FrameSelection();
            return true;
        }

        public void LoadGraph(GenGraph graph)
        {
            _suppressGraphMutationCallbacks = true;
            ClearGraph();
            _graph = graph;

            if (_graph == null)
            {
                _suppressGraphMutationCallbacks = false;
                return;
            }

            BuildNodeViews();
            BuildEdgeViews();
            _suppressGraphMutationCallbacks = false;
        }

        public void ClearGraph()
        {
            ClearNodePreviews();

            List<GraphElement> elementsToRemove = new List<GraphElement>();

            foreach (GraphElement element in graphElements)
            {
                elementsToRemove.Add(element);
            }

            DeleteElements(elementsToRemove);
            _nodeViewsById.Clear();
            _graph = null;
        }

        public void CreateNodeFromSearch(Type nodeType, Vector2 graphLocalPosition)
        {
            if (_graph == null || nodeType == null)
            {
                return;
            }

            Vector2 graphContentPosition = ConvertGraphLocalToContentPosition(graphLocalPosition);
            string displayName = NodeDiscovery.GetNodeDisplayName(nodeType);

            Undo.RecordObject(_graph, "Add Graph Node");
            GenNodeData nodeData = _graph.AddNode(nodeType.FullName, displayName, graphContentPosition);

            IGenNode prototypeNodeInstance;
            string prototypeErrorMessage;
            if (!GenNodeInstantiationUtility.TryCreatePrototypeNodeInstance(nodeType, nodeData.NodeId, nodeData.NodeName, out prototypeNodeInstance, out prototypeErrorMessage))
            {
                _graph.RemoveNode(nodeData.NodeId);
                EditorUtility.SetDirty(_graph);
                Debug.LogError("Failed to create node '" + displayName + "': " + prototypeErrorMessage);
                return;
            }

            PopulatePortData(nodeData, prototypeNodeInstance);
            GenNodeInstantiationUtility.PopulateDefaultParameters(nodeData, nodeType);

            IGenNode nodeInstance;
            string nodeInstanceErrorMessage;
            if (!GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out nodeInstanceErrorMessage))
            {
                _graph.RemoveNode(nodeData.NodeId);
                EditorUtility.SetDirty(_graph);
                Debug.LogError("Failed to initialise graph node '" + displayName + "': " + nodeInstanceErrorMessage);
                return;
            }

            EditorUtility.SetDirty(_graph);

            GenNodeView nodeView = new GenNodeView(_graph, nodeData, nodeInstance, _generationOrchestrator, _edgeConnectorListener);
            _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
            AddElement(nodeView);

            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }
        }

        public void OpenNodeSearch(Vector2 graphLocalPosition)
        {
            _lastGraphLocalMousePosition = graphLocalPosition;
            _nodeSearchWindow.SetGraphLocalSearchPosition(graphLocalPosition);
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(graphLocalPosition)), _nodeSearchWindow);
        }

        private void BuildEdgeViews()
        {
            List<GenConnectionData> connections = _graph.Connections ?? new List<GenConnectionData>();

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                GenNodeView fromNodeView;
                GenNodeView toNodeView;
                if (!_nodeViewsById.TryGetValue(connection.FromNodeId ?? string.Empty, out fromNodeView) ||
                    !_nodeViewsById.TryGetValue(connection.ToNodeId ?? string.Empty, out toNodeView))
                {
                    continue;
                }

                Port fromPortView;
                Port toPortView;
                if (!fromNodeView.TryGetPort(connection.FromPortName, out fromPortView) ||
                    !toNodeView.TryGetPort(connection.ToPortName, out toPortView))
                {
                    continue;
                }

                if (!GenPortUtility.CanConnectTo(fromPortView, toPortView))
                {
                    continue;
                }

                Edge edgeView = CreateEdgeView(
                    connection.FromNodeId,
                    fromPortView,
                    connection.FromPortName,
                    connection.ToNodeId,
                    toPortView,
                    connection.ToPortName);
                AddElement(edgeView);
            }
        }

        private void BuildNodeViews()
        {
            List<GenNodeData> nodes = _graph.Nodes ?? new List<GenNodeData>();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodes[nodeIndex];
                if (nodeData == null)
                {
                    continue;
                }

                IGenNode nodeInstance;
                string errorMessage;
                if (!GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out errorMessage))
                {
                    Debug.LogWarning("Graph node view skipped for '" + nodeData.NodeName + "': " + errorMessage);
                    continue;
                }

                GenNodeView nodeView = new GenNodeView(_graph, nodeData, nodeInstance, _generationOrchestrator, _edgeConnectorListener);
                _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
                AddElement(nodeView);
            }
        }

        private Vector2 ConvertGraphLocalToContentPosition(Vector2 graphLocalPosition)
        {
            GraphView currentGraphView = this;
            Vector2 worldPosition = currentGraphView.LocalToWorld(graphLocalPosition);
            return contentViewContainer.WorldToLocal(worldPosition);
        }

        private static GraphElement FindOwningGraphElement(VisualElement element)
        {
            VisualElement currentElement = element;
            while (currentElement != null)
            {
                GraphElement graphElement = currentElement as GraphElement;
                if (graphElement != null)
                {
                    return graphElement;
                }

                currentElement = currentElement.parent;
            }

            return null;
        }

        private VisualElement BuildGenerationOverlay()
        {
            VisualElement overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0.0f;
            overlay.style.top = 0.0f;
            overlay.style.right = 0.0f;
            overlay.style.bottom = 0.0f;
            overlay.style.display = DisplayStyle.None;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.alignItems = Align.Center;
            overlay.style.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.18f);
            overlay.pickingMode = PickingMode.Ignore;

            Label overlayLabel = new Label("Generating...");
            overlayLabel.style.paddingLeft = 10.0f;
            overlayLabel.style.paddingRight = 10.0f;
            overlayLabel.style.paddingTop = 6.0f;
            overlayLabel.style.paddingBottom = 6.0f;
            overlayLabel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.92f);
            overlayLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            overlay.Add(overlayLabel);

            return overlay;
        }

        private void ConfigureEdgeCallbacks(Edge edge)
        {
            edge.RegisterCallback<ContextualMenuPopulateEvent>(OnEdgeContextualMenuPopulate);
        }

        private Edge CreateEdgeView(
            string fromNodeId,
            Port fromPortView,
            string fromPortName,
            string toNodeId,
            Port toPortView,
            string toPortName)
        {
            bool isCastEdge = GenPortUtility.RequiresCast(fromPortView, toPortView);
            Color edgeColour = ResolveEdgeColour(fromPortView, toPortView, isCastEdge);
            Edge edgeView = new Edge();

            edgeView.output = fromPortView;
            edgeView.input = toPortView;
            edgeView.output.Connect(edgeView);
            edgeView.input.Connect(edgeView);
            edgeView.userData = new GenConnectionData(fromNodeId, fromPortName, toNodeId, toPortName);

            if (edgeView.edgeControl != null)
            {
                edgeView.edgeControl.inputColor = edgeColour;
                edgeView.edgeControl.outputColor = edgeColour;
            }

            ConfigureEdgeCallbacks(edgeView);
            return edgeView;
        }

        private void OnEdgeContextualMenuPopulate(ContextualMenuPopulateEvent contextEvent)
        {
            Edge edge = contextEvent.currentTarget as Edge;
            if (edge == null)
            {
                return;
            }

            contextEvent.menu.AppendAction(
                "Delete",
                action =>
                {
                    List<GraphElement> elementsToDelete = new List<GraphElement>();
                    elementsToDelete.Add(edge);
                    DeleteElements(elementsToDelete);
                },
                DropdownMenuAction.AlwaysEnabled);
        }

        private void OnMouseMove(MouseMoveEvent moveEvent)
        {
            _lastGraphLocalMousePosition = moveEvent.localMousePosition;
        }

        private void OnMouseDown(MouseDownEvent mouseDownEvent)
        {
            _lastGraphLocalMousePosition = mouseDownEvent.localMousePosition;
            Focus();
        }

        private void OnGraphContextualMenuPopulate(ContextualMenuPopulateEvent contextEvent)
        {
            VisualElement targetElement = contextEvent.target as VisualElement;
            if (targetElement == null || FindOwningGraphElement(targetElement) != null)
            {
                return;
            }

            OpenNodeSearch(_lastGraphLocalMousePosition);
            contextEvent.StopImmediatePropagation();
        }

        private void OnKeyDown(KeyDownEvent keyDownEvent)
        {
            if (keyDownEvent.keyCode == KeyCode.Space)
            {
                Vector2 localPosition = _lastGraphLocalMousePosition;
                if (localPosition == Vector2.zero)
                {
                    localPosition = layout.center;
                }

                OpenNodeSearch(localPosition);
                keyDownEvent.StopImmediatePropagation();
                return;
            }

            if (keyDownEvent.keyCode != KeyCode.Delete && keyDownEvent.keyCode != KeyCode.Backspace)
            {
                return;
            }

            List<GraphElement> elementsToDelete = new List<GraphElement>();

            foreach (ISelectable selectable in selection)
            {
                GraphElement graphElement = selectable as GraphElement;
                if (graphElement != null)
                {
                    elementsToDelete.Add(graphElement);
                }
            }

            if (elementsToDelete.Count == 0)
            {
                return;
            }

            DeleteElements(elementsToDelete);
            keyDownEvent.StopImmediatePropagation();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (_suppressGraphMutationCallbacks || _graph == null)
            {
                return graphViewChange;
            }

            bool graphStructureChanged = false;

            if (graphViewChange.edgesToCreate != null && graphViewChange.edgesToCreate.Count > 0)
            {
                List<Edge> validEdges = new List<Edge>();
                bool recordedUndo = false;

                int edgeIndex;
                for (edgeIndex = 0; edgeIndex < graphViewChange.edgesToCreate.Count; edgeIndex++)
                {
                    Edge edge = graphViewChange.edgesToCreate[edgeIndex];
                    Port outputPort = edge.output;
                    Port inputPort = edge.input;

                    if (outputPort == null || inputPort == null || !GenPortUtility.CanConnectTo(outputPort, inputPort))
                    {
                        continue;
                    }

                    GenNodeView fromNodeView = outputPort.node as GenNodeView;
                    GenNodeView toNodeView = inputPort.node as GenNodeView;
                    if (fromNodeView == null || toNodeView == null)
                    {
                        continue;
                    }

                    if (!recordedUndo)
                    {
                        Undo.RecordObject(_graph, "Create Graph Connection");
                        recordedUndo = true;
                    }

                    NodePortDefinition outputPortDefinition;
                    NodePortDefinition inputPortDefinition;
                    if (!GenPortUtility.TryGetPortDefinition(outputPort, out outputPortDefinition) ||
                        !GenPortUtility.TryGetPortDefinition(inputPort, out inputPortDefinition))
                    {
                        continue;
                    }

                    if (!_graph.AddConnection(fromNodeView.NodeData.NodeId, outputPortDefinition.Name, toNodeView.NodeData.NodeId, inputPortDefinition.Name))
                    {
                        continue;
                    }

                    graphStructureChanged = true;
                    edge.userData = new GenConnectionData(
                        fromNodeView.NodeData.NodeId,
                        outputPortDefinition.Name,
                        toNodeView.NodeData.NodeId,
                        inputPortDefinition.Name);

                    Color edgeColour = ResolveEdgeColour(outputPort, inputPort, GenPortUtility.RequiresCast(outputPort, inputPort));
                    if (edge.edgeControl != null)
                    {
                        edge.edgeControl.inputColor = edgeColour;
                        edge.edgeControl.outputColor = edgeColour;
                    }

                    ConfigureEdgeCallbacks(edge);
                    validEdges.Add(edge);
                }

                if (recordedUndo)
                {
                    EditorUtility.SetDirty(_graph);
                }

                graphViewChange.edgesToCreate = validEdges;
            }

            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
            {
                List<string> nodeIdsToRemove = new List<string>();
                List<GenConnectionData> connectionsToRemove = new List<GenConnectionData>();

                int elementIndex;
                for (elementIndex = 0; elementIndex < graphViewChange.elementsToRemove.Count; elementIndex++)
                {
                    GenNodeView nodeView = graphViewChange.elementsToRemove[elementIndex] as GenNodeView;
                    if (nodeView == null)
                    {
                        continue;
                    }

                    string nodeId = nodeView.NodeData.NodeId ?? string.Empty;
                    if (!nodeIdsToRemove.Contains(nodeId))
                    {
                        nodeView.ClearPreview();
                        nodeIdsToRemove.Add(nodeId);
                    }
                }

                for (elementIndex = 0; elementIndex < graphViewChange.elementsToRemove.Count; elementIndex++)
                {
                    Edge edge = graphViewChange.elementsToRemove[elementIndex] as Edge;
                    if (edge == null)
                    {
                        continue;
                    }

                    GenConnectionData connectionData;
                    if (!TryGetConnectionData(edge, out connectionData))
                    {
                        continue;
                    }

                    if (nodeIdsToRemove.Contains(connectionData.FromNodeId) || nodeIdsToRemove.Contains(connectionData.ToNodeId))
                    {
                        continue;
                    }

                    if (!ContainsConnection(connectionsToRemove, connectionData))
                    {
                        connectionsToRemove.Add(connectionData);
                    }
                }

                if (nodeIdsToRemove.Count > 0 || connectionsToRemove.Count > 0)
                {
                    Undo.RecordObject(_graph, "Delete Graph Elements");

                    int nodeIndex;
                    for (nodeIndex = 0; nodeIndex < nodeIdsToRemove.Count; nodeIndex++)
                    {
                        string nodeId = nodeIdsToRemove[nodeIndex];
                        if (_graph.RemoveNode(nodeId))
                        {
                            _nodeViewsById.Remove(nodeId);
                        }
                    }

                    int connectionIndex;
                    for (connectionIndex = 0; connectionIndex < connectionsToRemove.Count; connectionIndex++)
                    {
                        GenConnectionData connectionData = connectionsToRemove[connectionIndex];
                        _graph.RemoveConnection(connectionData.FromNodeId, connectionData.FromPortName, connectionData.ToNodeId, connectionData.ToPortName);
                    }

                    graphStructureChanged = true;
                    EditorUtility.SetDirty(_graph);
                }
            }

            if (graphStructureChanged && _generationOrchestrator != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }

            return graphViewChange;
        }

        private static bool ContainsConnection(IReadOnlyList<GenConnectionData> connections, GenConnectionData candidate)
        {
            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData existingConnection = connections[connectionIndex];
                if (existingConnection.FromNodeId == candidate.FromNodeId &&
                    existingConnection.FromPortName == candidate.FromPortName &&
                    existingConnection.ToNodeId == candidate.ToNodeId &&
                    existingConnection.ToPortName == candidate.ToPortName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PopulatePortData(GenNodeData nodeData, IGenNode nodeInstance)
        {
            nodeData.Ports.Clear();

            IReadOnlyList<NodePortDefinition> ports = nodeInstance.Ports;
            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition portDefinition = ports[portIndex];
                nodeData.Ports.Add(new GenPortData(portDefinition.Name, portDefinition.Direction, portDefinition.Type));
            }
        }

        private static bool TryGetConnectionData(Edge edge, out GenConnectionData connectionData)
        {
            connectionData = null;

            GenConnectionData cachedConnectionData = edge != null ? edge.userData as GenConnectionData : null;
            if (cachedConnectionData != null)
            {
                connectionData = new GenConnectionData(
                    cachedConnectionData.FromNodeId,
                    cachedConnectionData.FromPortName,
                    cachedConnectionData.ToNodeId,
                    cachedConnectionData.ToPortName);
                return true;
            }

            GenNodeView fromNodeView = edge.output != null ? edge.output.node as GenNodeView : null;
            GenNodeView toNodeView = edge.input != null ? edge.input.node as GenNodeView : null;
            Port fromPortView = edge.output;
            Port toPortView = edge.input;
            NodePortDefinition fromPortDefinition;
            NodePortDefinition toPortDefinition;

            if (fromNodeView == null ||
                toNodeView == null ||
                !GenPortUtility.TryGetPortDefinition(fromPortView, out fromPortDefinition) ||
                !GenPortUtility.TryGetPortDefinition(toPortView, out toPortDefinition))
            {
                return false;
            }

            connectionData = new GenConnectionData(
                fromNodeView.NodeData.NodeId,
                fromPortDefinition.Name,
                toNodeView.NodeData.NodeId,
                toPortDefinition.Name);
            return true;
        }

        private static Color ResolveEdgeColour(Port fromPortView, Port toPortView, bool isCastEdge)
        {
            if (!isCastEdge)
            {
                return GenPortUtility.GetPortColour(fromPortView);
            }

            Color fromColour = GenPortUtility.GetPortColour(fromPortView);
            Color toColour = GenPortUtility.GetPortColour(toPortView);
            return new Color(
                (fromColour.r + toColour.r) * 0.5f,
                (fromColour.g + toColour.g) * 0.5f,
                (fromColour.b + toColour.b) * 0.5f,
                1.0f);
        }
    }
}
