using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Shared;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonGraphView : GraphView
    {
        private const float MinExpandedPreviewZoom = 0.1f;
        private const float MaxExpandedPreviewZoom = 16.0f;
        private const float ExpandedPreviewZoomStep = 1.15f;
        private const float DefaultGroupWidth = 300.0f;
        private const float DefaultGroupHeight = 200.0f;
        private const float DefaultNoteWidth = 200.0f;
        private const float DefaultNoteHeight = 150.0f;
        private const float AutoLayoutColumnSpacing = 280.0f;
        private const float AutoLayoutRowSpacing = 140.0f;
        private const string BlackboardPropertyDragDataKey = BlackboardPanel.PropertyDragDataKey;

        private readonly GridBackground _gridBackground;
        private readonly Dictionary<string, GenNodeView> _nodeViewsById = new Dictionary<string, GenNodeView>();
        private readonly Dictionary<string, StickyNoteView> _stickyNoteViewsById = new Dictionary<string, StickyNoteView>();
        private readonly Dictionary<string, GroupView> _groupViewsById = new Dictionary<string, GroupView>();
        private readonly VisualElement _expandedPreviewOverlay;
        private readonly VisualElement _expandedPreviewViewport;
        private readonly Image _expandedPreviewImage;
        private readonly Label _expandedPreviewTitle;
        private readonly VisualElement _generationOverlay;
        private readonly IEdgeConnectorListener _edgeConnectorListener;
        private IVisualElementScheduledItem _expandedPreviewDeferredFitSchedule;

        private NodeSearchWindow _nodeSearchWindow;
        private GenGraph _graph;
        private GenerationOrchestrator _generationOrchestrator;
        private Action _afterMutation;
        private Action _viewTransformChanged;

        // Callback fired when a sub-graph node's Enter button is activated.
        // Arguments: the nested GenGraph to navigate into, the label to show
        // in the breadcrumb bar.
        private Action<GenGraph, string> _onEnterSubGraph;

        private string _expandedPreviewNodeId;
        private Texture2D _expandedPreviewTexture;
        private Vector2 _expandedPreviewPanOffset;
        private Vector2 _expandedPreviewPanOffsetAtDragStart;
        private Vector2 _expandedPreviewPanMousePositionAtDragStart;
        private Vector2 _expandedPreviewLastAutoFitViewportSize = new Vector2(-1.0f, -1.0f);
        private float _expandedPreviewZoom = 1.0f;
        private bool _expandedPreviewNeedsFit;
        private bool _expandedPreviewAutoFitUntilInteraction;
        private bool _isPanningExpandedPreview;
        private bool _suppressGraphMutationCallbacks;
        private bool _subGraphPortReloadQueued;
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

            GraphViewShellUtility.ConfigureDefaultGraphView(currentGraphView);
            graphViewChanged = OnGraphViewChanged;
            viewTransformChanged = OnViewTransformChanged;

            elementsAddedToGroup += OnElementsAddedToGroup;
            elementsRemovedFromGroup += OnElementsRemovedFromGroup;

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

            _expandedPreviewOverlay = BuildExpandedPreviewOverlay(out _expandedPreviewViewport, out _expandedPreviewImage, out _expandedPreviewTitle);
            _expandedPreviewOverlay.RegisterCallback<MouseDownEvent>(OnExpandedPreviewOverlayMouseDown);
            _expandedPreviewOverlay.RegisterCallback<MouseMoveEvent>(OnExpandedPreviewOverlayMouseMove);
            _expandedPreviewOverlay.RegisterCallback<MouseUpEvent>(OnExpandedPreviewOverlayMouseUp);
            _expandedPreviewOverlay.RegisterCallback<WheelEvent>(OnExpandedPreviewOverlayWheel);
            _expandedPreviewViewport.RegisterCallback<MouseDownEvent>(OnExpandedPreviewViewportMouseDown);
            _expandedPreviewViewport.RegisterCallback<MouseMoveEvent>(OnExpandedPreviewViewportMouseMove);
            _expandedPreviewViewport.RegisterCallback<MouseUpEvent>(OnExpandedPreviewViewportMouseUp);
            _expandedPreviewViewport.RegisterCallback<WheelEvent>(OnExpandedPreviewViewportWheel);
            _expandedPreviewViewport.RegisterCallback<GeometryChangedEvent>(OnExpandedPreviewViewportGeometryChanged);

            _nodeSearchWindow = ScriptableObject.CreateInstance<NodeSearchWindow>();
            _nodeSearchWindow.Initialise(this);

            RegisterCallback<MouseMoveEvent>(OnMouseMove);
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<MouseDownEvent>(OnMouseDownCapture, TrickleDown.TrickleDown);
            RegisterCallback<ContextualMenuPopulateEvent>(OnGraphContextualMenuPopulate, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
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

        public void SetAfterMutationCallback(Action afterMutation)
        {
            _afterMutation = afterMutation;
        }

        public void SetViewTransformChangedCallback(Action viewTransformChanged)
        {
            _viewTransformChanged = viewTransformChanged;
        }

        /// <summary>
        /// Registers the callback that is invoked when a sub-graph node's
        /// "↓ Enter" action is triggered.  The breadcrumb bar wires this up so
        /// it can push a new level onto the trail.
        /// </summary>
        public void SetSubGraphEnterCallback(Action<GenGraph, string> onEnterSubGraph)
        {
            _onEnterSubGraph = onEnterSubGraph;
        }

        /// <summary>
        /// Returns the current canvas scroll offset and zoom scale so the caller
        /// can save them before navigating away to a sub-graph.
        /// </summary>
        public void GetViewportState(out Vector3 scrollOffset, out float zoomScale)
        {
            GraphViewShellUtility.GetViewportState(this, out scrollOffset, out zoomScale);
        }

        /// <summary>
        /// Restores a previously saved canvas scroll offset and zoom scale.
        /// Used when the user navigates back to a parent graph via the breadcrumb.
        /// </summary>
        public void RestoreViewportState(Vector3 scrollOffset, float zoomScale)
        {
            GraphViewShellUtility.RestoreViewportState(this, scrollOffset, zoomScale);
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
            HideExpandedPreview();

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

            nodeId = ResolveVisiblePreviewNodeId(nodeId);

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
            string visibleNodeId = ResolveVisiblePreviewNodeId(nodeId);
            if (_nodeViewsById.TryGetValue(nodeId, out nodeView))
            {
                nodeView.SetPreview(texture);

                UpdateExpandedPreviewForCurrentNode(nodeId, texture, nodeView.title);

                return;
            }

            if (!string.Equals(visibleNodeId, nodeId, StringComparison.Ordinal) &&
                _nodeViewsById.TryGetValue(visibleNodeId, out nodeView))
            {
                nodeView.SetPreview(texture);
                UpdateExpandedPreviewForCurrentNode(visibleNodeId, texture, nodeView.title);
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

        public GenNodeData GetSingleSelectedNodeData()
        {
            List<GenNodeView> selectedNodeViews = GetSelectedNodeViews();
            if (selectedNodeViews.Count != 1)
            {
                return null;
            }

            GenNodeView selectedNodeView = selectedNodeViews[0];
            return selectedNodeView != null ? selectedNodeView.NodeData : null;
        }

        public int GetSelectedNodeCount()
        {
            return GetSelectedNodeViews().Count;
        }

        private static string ResolveVisiblePreviewNodeId(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return string.Empty;
            }

            int separatorIndex = nodeId.IndexOf("::", StringComparison.Ordinal);
            return separatorIndex > 0 ? nodeId.Substring(0, separatorIndex) : nodeId;
        }

        public bool SelectAndFrameGroup(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            GroupView groupView;
            if (!_groupViewsById.TryGetValue(groupId, out groupView))
            {
                return false;
            }

            ClearSelection();
            AddToSelection(groupView);
            FrameSelection();
            return true;
        }

        internal IReadOnlyList<GroupNavigationItem> GetGroupNavigationItems()
        {
            List<GroupNavigationItem> items = new List<GroupNavigationItem>();
            if (_graph == null || _graph.Groups == null)
            {
                return items;
            }

            int groupIndex;
            for (groupIndex = 0; groupIndex < _graph.Groups.Count; groupIndex++)
            {
                GenGroupData groupData = _graph.Groups[groupIndex];
                if (groupData == null || string.IsNullOrWhiteSpace(groupData.GroupId))
                {
                    continue;
                }

                int nodeCount = groupData.ContainedNodeIds != null ? groupData.ContainedNodeIds.Count : 0;
                items.Add(new GroupNavigationItem(
                    groupData.GroupId,
                    groupData.Title,
                    nodeCount,
                    groupData.Position));
            }

            return items;
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

            EnsureBoundaryNodesForSubGraphAsset(_graph);
            SyncSubGraphWrappersInGraph(_graph, false);
            BuildNodeViews();
            BuildEdgeViews();
            BuildStickyNoteViews();
            BuildGroupViews();
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
            _stickyNoteViewsById.Clear();
            _groupViewsById.Clear();
            _graph = null;
        }

        public Edge NormaliseCreatedEdge(Edge edge)
        {
            GenEdgeView existingGenEdgeView = edge as GenEdgeView;
            if (existingGenEdgeView != null)
            {
                return existingGenEdgeView;
            }

            if (edge == null || edge.output == null || edge.input == null)
            {
                return edge;
            }

            GenNodeView fromNodeView = edge.output.node as GenNodeView;
            GenNodeView toNodeView = edge.input.node as GenNodeView;
            NodePortDefinition fromPortDefinition;
            NodePortDefinition toPortDefinition;
            if (fromNodeView == null ||
                toNodeView == null ||
                !GenPortUtility.TryGetPortDefinition(edge.output, out fromPortDefinition) ||
                !GenPortUtility.TryGetPortDefinition(edge.input, out toPortDefinition))
            {
                return edge;
            }

            CastMode castMode = CastMode.None;
            GenConnectionData connectionData = edge.userData as GenConnectionData;
            if (connectionData != null)
            {
                castMode = connectionData.CastMode;
            }
            else if (GenPortUtility.RequiresCast(edge.output, edge.input))
            {
                CastRegistry.CanCast(fromPortDefinition.Type, toPortDefinition.Type, out castMode);
            }

            return CreateEdgeView(
                fromNodeView.NodeData.NodeId,
                edge.output,
                fromPortDefinition.Name,
                toNodeView.NodeData.NodeId,
                edge.input,
                toPortDefinition.Name,
                castMode);
        }

        public void CreateNodeFromSearch(Type nodeType, Vector2 graphLocalPosition)
        {
            if (_graph == null || nodeType == null)
            {
                return;
            }

            if (GraphOutputUtility.IsOutputNodeType(nodeType))
            {
                GenNodeData existingOutputNode = GraphOutputUtility.FindOutputNode(_graph);
                if (existingOutputNode != null)
                {
                    SelectAndFrameNode(existingOutputNode.NodeId);
                    return;
                }
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
            NotifyGraphMutated();

            GenNodeView nodeView = CreateNodeView(nodeData, nodeInstance);
            ProtectOutputNode(nodeView);
            _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
            AddElement(nodeView);

            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }
        }

        public void CreateExposedPropertyNodeFromBlackboard(string propertyId, Vector2 graphLocalPosition)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(propertyId))
            {
                return;
            }

            ExposedProperty property = _graph.GetExposedProperty(propertyId);
            if (property == null)
            {
                return;
            }

            Vector2 graphContentPosition = ConvertGraphLocalToContentPosition(graphLocalPosition);

            Undo.RecordObject(_graph, "Add Exposed Property Node");
            GenNodeData nodeData = _graph.AddNode(
                ExposedPropertyNodeUtility.NodeTypeName,
                property.PropertyName ?? string.Empty,
                graphContentPosition);
            ExposedPropertyNodeUtility.ConfigureNodeData(nodeData, property);

            IGenNode nodeInstance;
            string nodeInstanceErrorMessage;
            if (!GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out nodeInstanceErrorMessage))
            {
                _graph.RemoveNode(nodeData.NodeId);
                EditorUtility.SetDirty(_graph);
                Debug.LogError(
                    "Failed to initialise exposed property node '" +
                    (property.PropertyName ?? string.Empty) +
                    "': " +
                    nodeInstanceErrorMessage);
                return;
            }

            PopulatePortData(nodeData, nodeInstance);
            EditorUtility.SetDirty(_graph);
            NotifyGraphMutated();

            GenNodeView nodeView = CreateNodeView(nodeData, nodeInstance);
            _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
            AddElement(nodeView);
            ClearSelection();
            AddToSelection(nodeView);

            _generationOrchestrator?.RequestPreviewRefresh();
        }

        public void ReloadGraphFromModel()
        {
            Vector3 scrollOffset;
            float zoomScale;
            GetViewportState(out scrollOffset, out zoomScale);

            LoadGraph(_graph);
            RestoreViewportState(scrollOffset, zoomScale);
            _generationOrchestrator?.RequestPreviewRefresh();
        }

        public void RequestPreviewRefresh()
        {
            _generationOrchestrator?.RequestPreviewRefresh();
        }

        public void MarkNodeDirty(string nodeId)
        {
            _generationOrchestrator?.MarkNodeDirty(nodeId);
        }

        public void AddSubGraphBoundaryPort(GenNodeData boundaryNode, ChannelType channelType)
        {
            if (_graph == null || boundaryNode == null)
            {
                return;
            }

            bool isInputBoundary = IsSubGraphInputNodeData(boundaryNode);
            bool isOutputBoundary = IsSubGraphOutputNodeData(boundaryNode);
            if (!isInputBoundary && !isOutputBoundary)
            {
                return;
            }

            if (boundaryNode.Ports == null)
            {
                boundaryNode.Ports = new List<GenPortData>();
            }

            HashSet<string> usedPortNames = new HashSet<string>(StringComparer.Ordinal);
            for (int portIndex = 0; portIndex < boundaryNode.Ports.Count; portIndex++)
            {
                GenPortData port = boundaryNode.Ports[portIndex];
                if (port != null && !string.IsNullOrWhiteSpace(port.PortName))
                {
                    usedPortNames.Add(port.PortName);
                }
            }

            string baseName = isInputBoundary ? "Input" : "Output";
            string portName = CreateUniqueBoundaryPortName(baseName, usedPortNames);
            PortDirection internalDirection = isInputBoundary ? PortDirection.Output : PortDirection.Input;

            Undo.RecordObject(_graph, isInputBoundary ? "Add Sub-Graph Input Port" : "Add Sub-Graph Output Port");
            boundaryNode.Ports.Add(new GenPortData(portName, internalDirection, channelType, portName));
            EditorUtility.SetDirty(_graph);

            SyncSubGraphPortsForGraphMutation();
            NotifyGraphMutated();
            RequestDeferredSubGraphPortReload();
        }

        public void DeleteSubGraphBoundaryPort(GenNodeData boundaryNode, string portName)
        {
            if (_graph == null || boundaryNode == null || string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            bool isInputBoundary = IsSubGraphInputNodeData(boundaryNode);
            bool isOutputBoundary = IsSubGraphOutputNodeData(boundaryNode);
            if (!isInputBoundary && !isOutputBoundary)
            {
                return;
            }

            List<GenPortData> ports = boundaryNode.Ports;
            if (ports == null)
            {
                return;
            }

            int portIndex = ports.FindIndex(port => port != null && string.Equals(port.PortName, portName, StringComparison.Ordinal));
            if (portIndex < 0)
            {
                return;
            }

            Undo.RecordObject(_graph, isInputBoundary ? "Delete Sub-Graph Input Port" : "Delete Sub-Graph Output Port");
            ports.RemoveAt(portIndex);
            RemoveConnectionsForBoundaryPort(boundaryNode.NodeId, portName, isInputBoundary);
            EditorUtility.SetDirty(_graph);

            SyncSubGraphPortsForGraphMutation();
            NotifyGraphMutated();
            RequestDeferredSubGraphPortReload();
        }

        private void RemoveConnectionsForBoundaryPort(string boundaryNodeId, string portName, bool inputBoundary)
        {
            List<GenConnectionData> connections = _graph != null ? _graph.Connections : null;
            if (connections == null)
            {
                return;
            }

            for (int connectionIndex = connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                bool matches = inputBoundary
                    ? string.Equals(connection.FromNodeId, boundaryNodeId, StringComparison.Ordinal) &&
                      string.Equals(connection.FromPortName, portName, StringComparison.Ordinal)
                    : string.Equals(connection.ToNodeId, boundaryNodeId, StringComparison.Ordinal) &&
                      string.Equals(connection.ToPortName, portName, StringComparison.Ordinal);
                if (matches)
                {
                    connections.RemoveAt(connectionIndex);
                }
            }
        }

        private void RequestDeferredSubGraphPortReload()
        {
            if (_subGraphPortReloadQueued)
            {
                return;
            }

            _subGraphPortReloadQueued = true;
            schedule.Execute(() =>
            {
                _subGraphPortReloadQueued = false;
                ReloadGraphFromModel();
            }).StartingIn(0);
        }

        public void OpenNodeSearch(Vector2 graphLocalPosition)
        {
            _lastGraphLocalMousePosition = graphLocalPosition;
            _nodeSearchWindow.ClearChannelTypeFilter();
            _nodeSearchWindow.SetGraphLocalSearchPosition(graphLocalPosition);
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(graphLocalPosition)), _nodeSearchWindow);
        }

        public void OpenFilteredNodeSearch(Vector2 graphLocalPosition, Port anchorPort)
        {
            _lastGraphLocalMousePosition = graphLocalPosition;

            NodePortDefinition anchorDefinition;
            if (anchorPort != null && GenPortUtility.TryGetPortDefinition(anchorPort, out anchorDefinition))
            {
                PortDirection requiredDirection = anchorDefinition.Direction == PortDirection.Output
                    ? PortDirection.Input
                    : PortDirection.Output;
                _nodeSearchWindow.SetChannelTypeFilter(anchorDefinition.Type, requiredDirection);
            }
            else
            {
                _nodeSearchWindow.ClearChannelTypeFilter();
            }

            _nodeSearchWindow.SetGraphLocalSearchPosition(graphLocalPosition);
            SearchWindow.Open(new SearchWindowContext(GUIUtility.GUIToScreenPoint(graphLocalPosition)), _nodeSearchWindow);
        }

        public void CreateStickyNote(Vector2 graphLocalPosition, string initialText = "")
        {
            if (_graph == null)
            {
                return;
            }

            Vector2 contentPosition = ConvertGraphLocalToContentPosition(graphLocalPosition);
            Rect noteRect = new Rect(contentPosition.x, contentPosition.y, DefaultNoteWidth, DefaultNoteHeight);

            Undo.RecordObject(_graph, "Add Sticky Note");
            GenStickyNoteData noteData = _graph.AddStickyNote(initialText ?? string.Empty, noteRect);
            EditorUtility.SetDirty(_graph);
            NotifyGraphMutated();

            StickyNoteView noteView = new StickyNoteView(_graph, noteData, NotifyGraphMutated);
            _stickyNoteViewsById[noteData.NoteId] = noteView;
            AddElement(noteView);
        }

        public void CreateGroup(Vector2 graphLocalPosition)
        {
            if (_graph == null)
            {
                return;
            }

            Vector2 contentPosition = ConvertGraphLocalToContentPosition(graphLocalPosition);
            Rect groupRect = new Rect(contentPosition.x, contentPosition.y, DefaultGroupWidth, DefaultGroupHeight);

            Undo.RecordObject(_graph, "Add Group");
            GenGroupData groupData = _graph.AddGroup("Group", groupRect);
            EditorUtility.SetDirty(_graph);
            NotifyGraphMutated();

            GroupView groupView = new GroupView(_graph, groupData, NotifyGraphMutated);
            _groupViewsById[groupData.GroupId] = groupView;
            AddElement(groupView);
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            _viewTransformChanged?.Invoke();
        }

        private void NotifyGraphMutated()
        {
            _afterMutation?.Invoke();
            _viewTransformChanged?.Invoke();
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
                    connection.ToPortName,
                    connection.CastMode);
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

                Type nodeType = GenNodeInstantiationUtility.ResolveNodeType(nodeData.NodeTypeName);
                if (nodeType != null)
                {
                    GenNodeInstantiationUtility.PopulateDefaultParameters(nodeData, nodeType);
                }

                IGenNode nodeInstance;
                string errorMessage;
                if (!GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out errorMessage))
                {
                    Debug.LogWarning("Graph node view skipped for '" + nodeData.NodeName + "': " + errorMessage);
                    continue;
                }

                GenNodeView nodeView = CreateNodeView(nodeData, nodeInstance);
                ProtectOutputNode(nodeView);
                _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
                AddElement(nodeView);
            }
        }

        private void BuildStickyNoteViews()
        {
            List<GenStickyNoteData> stickyNotes = _graph.StickyNotes ?? new List<GenStickyNoteData>();

            int noteIndex;
            for (noteIndex = 0; noteIndex < stickyNotes.Count; noteIndex++)
            {
                GenStickyNoteData noteData = stickyNotes[noteIndex];
                if (noteData == null || string.IsNullOrEmpty(noteData.NoteId))
                {
                    continue;
                }

                StickyNoteView noteView = new StickyNoteView(_graph, noteData, NotifyGraphMutated);
                _stickyNoteViewsById[noteData.NoteId] = noteView;
                AddElement(noteView);
            }
        }

        private void BuildGroupViews()
        {
            List<GenGroupData> groups = _graph.Groups ?? new List<GenGroupData>();

            int groupIndex;
            for (groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                GenGroupData groupData = groups[groupIndex];
                if (groupData == null || string.IsNullOrEmpty(groupData.GroupId))
                {
                    continue;
                }

                GroupView groupView = new GroupView(_graph, groupData, NotifyGraphMutated);
                _groupViewsById[groupData.GroupId] = groupView;
                AddElement(groupView);
            }

            for (groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                GenGroupData groupData = groups[groupIndex];
                if (groupData == null ||
                    string.IsNullOrEmpty(groupData.GroupId) ||
                    groupData.ContainedNodeIds == null)
                {
                    continue;
                }

                GroupView groupView;
                if (!_groupViewsById.TryGetValue(groupData.GroupId, out groupView))
                {
                    continue;
                }

                int memberIndex;
                for (memberIndex = 0; memberIndex < groupData.ContainedNodeIds.Count; memberIndex++)
                {
                    string nodeId = groupData.ContainedNodeIds[memberIndex];
                    GenNodeView nodeView;
                    if (!string.IsNullOrEmpty(nodeId) && _nodeViewsById.TryGetValue(nodeId, out nodeView))
                    {
                        groupView.AddElement(nodeView);
                    }
                }
            }
        }

        /// <summary>
        /// Instantiates the correct node view type for <paramref name="nodeData"/>.
        /// Returns a <see cref="SubGraphNodeView"/> when the resolved node type
        /// carries a <see cref="SubGraphNodeAttribute"/>, and a plain
        /// <see cref="GenNodeView"/> otherwise.
        /// </summary>
        private GenNodeView CreateNodeView(GenNodeData nodeData, IGenNode nodeInstance)
        {
            Type nodeType = nodeInstance != null ? nodeInstance.GetType() : null;
            if (nodeType == typeof(SubGraphInputNode) || nodeType == typeof(SubGraphOutputNode))
            {
                return new SubGraphBoundaryNodeView(
                    _graph,
                    nodeData,
                    nodeInstance,
                    _generationOrchestrator,
                    _edgeConnectorListener,
                    OpenExpandedPreview,
                    NotifyGraphMutated);
            }

            SubGraphNodeAttribute subGraphAttribute = nodeType != null
                ? Attribute.GetCustomAttribute(nodeType, typeof(SubGraphNodeAttribute)) as SubGraphNodeAttribute
                : null;

            if (subGraphAttribute != null)
            {
                return new SubGraphNodeView(
                    _graph,
                    nodeData,
                    nodeInstance,
                    _generationOrchestrator,
                    _edgeConnectorListener,
                    OpenExpandedPreview,
                    NotifyGraphMutated,
                    subGraphAttribute.NestedGraphParameterName,
                    _onEnterSubGraph);
            }

            return new GenNodeView(
                _graph,
                nodeData,
                nodeInstance,
                _generationOrchestrator,
                _edgeConnectorListener,
                OpenExpandedPreview,
                NotifyGraphMutated);
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

        private static VisualElement BuildExpandedPreviewOverlay(out VisualElement viewport, out Image image, out Label titleLabel)
        {
            VisualElement overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0.0f;
            overlay.style.top = 0.0f;
            overlay.style.right = 0.0f;
            overlay.style.bottom = 0.0f;
            overlay.style.display = DisplayStyle.None;
            overlay.style.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.92f);
            overlay.style.justifyContent = Justify.Center;
            overlay.style.alignItems = Align.Center;
            overlay.pickingMode = PickingMode.Position;

            titleLabel = new Label();
            titleLabel.style.position = Position.Absolute;
            titleLabel.style.left = 16.0f;
            titleLabel.style.top = 12.0f;
            titleLabel.style.paddingLeft = 10.0f;
            titleLabel.style.paddingRight = 10.0f;
            titleLabel.style.paddingTop = 6.0f;
            titleLabel.style.paddingBottom = 6.0f;
            titleLabel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            titleLabel.style.color = new Color(0.95f, 0.95f, 0.95f, 1.0f);
            overlay.Add(titleLabel);

            viewport = new VisualElement();
            viewport.style.position = Position.Absolute;
            viewport.style.left = 24.0f;
            viewport.style.top = 56.0f;
            viewport.style.right = 24.0f;
            viewport.style.bottom = 24.0f;
            viewport.style.overflow = Overflow.Hidden;
            viewport.style.unityBackgroundImageTintColor = Color.white;
            viewport.pickingMode = PickingMode.Position;
            overlay.Add(viewport);

            image = new Image();
            image.scaleMode = ScaleMode.StretchToFill;
            image.pickingMode = PickingMode.Ignore;
            image.style.position = Position.Absolute;
            image.style.left = 0.0f;
            image.style.top = 0.0f;
            viewport.Add(image);

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
            string toPortName,
            CastMode castMode = CastMode.None)
        {
            Color outputEdgeColour = ResolveOutputEdgeColour(fromPortView);
            Color inputEdgeColour = ResolveInputEdgeColour(fromPortView, toPortView, castMode != CastMode.None);
            GenEdgeView edgeView = new GenEdgeView(castMode, outputEdgeColour, inputEdgeColour);

            edgeView.output = fromPortView;
            edgeView.input = toPortView;
            edgeView.output.Connect(edgeView);
            edgeView.input.Connect(edgeView);

            GenConnectionData connectionData = new GenConnectionData(fromNodeId, fromPortName, toNodeId, toPortName);
            connectionData.CastMode = castMode;
            edgeView.userData = connectionData;

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

            GenEdgeView genEdge = edge as GenEdgeView;
            if (genEdge != null && genEdge.IsCastEdge)
            {
                ChannelType fromType;
                ChannelType toType;
                GetPortTypesFromCastMode(genEdge.ActiveCastMode, out fromType, out toType);

                List<CastMode> validModes = GetValidCastModesForPortPair(fromType, toType);
                CastMode currentMode = genEdge.ActiveCastMode;

                int modeIndex;
                for (modeIndex = 0; modeIndex < validModes.Count; modeIndex++)
                {
                    CastMode mode = validModes[modeIndex];
                    string menuItemLabel = "Cast Mode/" + GetCastModeDisplayName(mode);

                    CastMode capturedMode = mode;
                    Edge capturedEdge = edge;

                    DropdownMenuAction.Status itemStatus = mode == currentMode
                        ? DropdownMenuAction.Status.Checked
                        : DropdownMenuAction.Status.Normal;

                    contextEvent.menu.AppendAction(
                        menuItemLabel,
                        _ => ApplyCastModeChange(capturedEdge, capturedMode),
                        _ => itemStatus);
                }

                contextEvent.menu.AppendSeparator();
            }

            contextEvent.menu.AppendAction(
                "Delete",
                _ =>
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

        private void OnMouseDownCapture(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent.shiftKey && mouseDownEvent.button == 0)
            {
                VisualElement targetElement = mouseDownEvent.target as VisualElement;
                if (targetElement != null && IsInsideTextField(targetElement))
                {
                    return;
                }

                GraphElement clickedElement = FindOwningGraphElement(targetElement);
                if (clickedElement != null && (clickedElement.capabilities & Capabilities.Selectable) != 0)
                {
                    if (selection.Contains(clickedElement))
                    {
                        RemoveFromSelection(clickedElement);
                    }
                    else
                    {
                        AddToSelection(clickedElement);
                    }
                    
                    mouseDownEvent.StopPropagation();
                }
            }
        }

        private void OnMouseDown(MouseDownEvent mouseDownEvent)
        {
            _lastGraphLocalMousePosition = mouseDownEvent.localMousePosition;

            // Don't steal focus when the click originated inside a text field —
            // doing so would immediately unfocus the field before the user can type.
            VisualElement targetElement = mouseDownEvent.target as VisualElement;
            if (targetElement != null && IsInsideTextField(targetElement))
            {
                return;
            }

            Focus();
        }

        private static bool IsInsideTextField(VisualElement element)
        {
            VisualElement current = element;
            while (current != null)
            {
                if (current is TextField)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private void OnExpandedPreviewOverlayMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent.button == 0 && mouseDownEvent.clickCount == 2)
            {
                HideExpandedPreview();
                mouseDownEvent.StopImmediatePropagation();
                return;
            }

            if (!IsExpandedPreviewPanButton(mouseDownEvent.button))
            {
                return;
            }

            BeginExpandedPreviewPan(ToExpandedPreviewViewportLocal(mouseDownEvent.localMousePosition));
            mouseDownEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewOverlayMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!_isPanningExpandedPreview)
            {
                return;
            }

            UpdateExpandedPreviewPan(ToExpandedPreviewViewportLocal(mouseMoveEvent.localMousePosition));
            mouseMoveEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewOverlayMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (!IsExpandedPreviewPanButton(mouseUpEvent.button))
            {
                return;
            }

            EndExpandedPreviewPan();
            mouseUpEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewOverlayWheel(WheelEvent wheelEvent)
        {
            if (_expandedPreviewTexture == null)
            {
                return;
            }

            HandleExpandedPreviewWheel(ToExpandedPreviewViewportLocal(wheelEvent.localMousePosition), wheelEvent.delta.y);
            wheelEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewViewportMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (_expandedPreviewTexture == null || !IsExpandedPreviewPanButton(mouseDownEvent.button))
            {
                return;
            }

            if (mouseDownEvent.clickCount == 2)
            {
                HideExpandedPreview();
                mouseDownEvent.StopImmediatePropagation();
                return;
            }

            BeginExpandedPreviewPan(mouseDownEvent.localMousePosition);
            mouseDownEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewViewportMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!_isPanningExpandedPreview)
            {
                return;
            }

            UpdateExpandedPreviewPan(mouseMoveEvent.localMousePosition);
            mouseMoveEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewViewportMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (!IsExpandedPreviewPanButton(mouseUpEvent.button))
            {
                return;
            }

            EndExpandedPreviewPan();
            mouseUpEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewViewportWheel(WheelEvent wheelEvent)
        {
            if (_expandedPreviewTexture == null)
            {
                return;
            }

            HandleExpandedPreviewWheel(wheelEvent.localMousePosition, wheelEvent.delta.y);
            wheelEvent.StopImmediatePropagation();
        }

        private void OnExpandedPreviewViewportGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            UpdateExpandedPreviewTransformIfNeeded();
        }

        private void OnGraphContextualMenuPopulate(ContextualMenuPopulateEvent contextEvent)
        {
            VisualElement targetElement = contextEvent.target as VisualElement;
            if (targetElement == null || FindOwningGraphElement(targetElement) != null)
            {
                return;
            }

            Vector2 localPosition = _lastGraphLocalMousePosition;
            contextEvent.menu.AppendAction("Add Node...", _ => OpenNodeSearch(localPosition));
            contextEvent.menu.AppendAction("Add Sticky Note", _ => CreateStickyNote(localPosition));
            contextEvent.menu.AppendAction("Add Group", _ => CreateGroup(localPosition));
            contextEvent.menu.AppendAction(
                "Group Selection",
                _ => GroupSelection(),
                _ => CanGroupSelection() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            contextEvent.menu.AppendAction(
                "Ungroup Selection",
                _ => UngroupSelection(),
                _ => CanUngroupSelection() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            contextEvent.menu.AppendAction(
                "Convert Selection To Subgraph",
                _ => ConvertSelectionToSubGraph(),
                _ => CanConvertSelectionToSubGraph() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            contextEvent.menu.AppendAction(
                "Auto Layout Selection",
                _ => AutoLayoutSelection(),
                _ => CanAutoLayoutSelection() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private void OnKeyDown(KeyDownEvent keyDownEvent)
        {
            if (keyDownEvent.keyCode == KeyCode.Escape && _expandedPreviewOverlay.style.display == DisplayStyle.Flex)
            {
                HideExpandedPreview();
                keyDownEvent.StopImmediatePropagation();
                return;
            }

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

            bool isGroupingShortcut = (keyDownEvent.ctrlKey || keyDownEvent.commandKey) && keyDownEvent.keyCode == KeyCode.G;
            if (isGroupingShortcut)
            {
                if (keyDownEvent.shiftKey)
                {
                    if (CanUngroupSelection())
                    {
                        UngroupSelection();
                    }
                }
                else if (CanGroupSelection())
                {
                    GroupSelection();
                }

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
                if (graphElement != null && CanDeleteGraphElement(graphElement))
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

                    if (TryCreateSubGraphPortFromAutoConnection(
                            fromNodeView,
                            outputPortDefinition,
                            toNodeView,
                            inputPortDefinition))
                    {
                        graphStructureChanged = true;
                        continue;
                    }

                    if (!_graph.AddConnection(fromNodeView.NodeData.NodeId, outputPortDefinition.Name, toNodeView.NodeData.NodeId, inputPortDefinition.Name))
                    {
                        continue;
                    }

                    graphStructureChanged = true;

                    bool isNewEdgeCast = GenPortUtility.RequiresCast(outputPort, inputPort);
                    CastMode newEdgeCastMode = CastMode.None;

                    if (isNewEdgeCast)
                    {
                        CastMode resolvedDefault;
                        CastRegistry.CanCast(outputPortDefinition.Type, inputPortDefinition.Type, out resolvedDefault);
                        newEdgeCastMode = resolvedDefault;

                        // Persist the default cast mode on the graph connection that was just added.
                        GenConnectionData graphConnection = FindConnectionInGraph(
                            fromNodeView.NodeData.NodeId,
                            outputPortDefinition.Name,
                            toNodeView.NodeData.NodeId,
                            inputPortDefinition.Name);

                        if (graphConnection != null)
                        {
                            graphConnection.CastMode = newEdgeCastMode;
                        }
                    }

                    GenConnectionData newConnectionData = new GenConnectionData(
                        fromNodeView.NodeData.NodeId,
                        outputPortDefinition.Name,
                        toNodeView.NodeData.NodeId,
                        inputPortDefinition.Name);
                    newConnectionData.CastMode = newEdgeCastMode;

                    Color outputEdgeColour = ResolveOutputEdgeColour(outputPort);
                    Color inputEdgeColour = ResolveInputEdgeColour(outputPort, inputPort, isNewEdgeCast);
                    GenEdgeView genEdgeView = new GenEdgeView(newEdgeCastMode, outputEdgeColour, inputEdgeColour);
                    genEdgeView.output = outputPort;
                    genEdgeView.input = inputPort;
                    outputPort.Connect(genEdgeView);
                    inputPort.Connect(genEdgeView);
                    genEdgeView.userData = newConnectionData;

                    outputPort.portColor = GenPortUtility.GetPortColour(outputPort);
                    inputPort.portColor = GenPortUtility.GetPortColour(inputPort);
                    fromNodeView.RefreshPorts();
                    toNodeView.RefreshPorts();
                    ConfigureEdgeCallbacks(genEdgeView);
                    validEdges.Add(genEdgeView);
                }

                if (recordedUndo)
                {
                    EditorUtility.SetDirty(_graph);
                    NotifyGraphMutated();
                }

                graphViewChange.edgesToCreate = validEdges;
            }

            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
            {
                List<string> nodeIdsToRemove = new List<string>();
                List<string> noteIdsToRemove = new List<string>();
                List<string> groupIdsToRemove = new List<string>();
                List<GenConnectionData> connectionsToRemove = new List<GenConnectionData>();

                int elementIndex;
                for (elementIndex = 0; elementIndex < graphViewChange.elementsToRemove.Count; elementIndex++)
                {
                    GenNodeView nodeView = graphViewChange.elementsToRemove[elementIndex] as GenNodeView;
                    if (nodeView != null)
                    {
                        if (GraphOutputUtility.IsOutputNode(nodeView.NodeData))
                        {
                            continue;
                        }

                        string nodeId = nodeView.NodeData.NodeId ?? string.Empty;
                        if (!nodeIdsToRemove.Contains(nodeId))
                        {
                            HideExpandedPreviewIfShowing(nodeId);
                            nodeView.ClearPreview();
                            nodeIdsToRemove.Add(nodeId);
                        }

                        continue;
                    }

                    StickyNoteView noteView = graphViewChange.elementsToRemove[elementIndex] as StickyNoteView;
                    if (noteView != null)
                    {
                        string noteId = noteView.NoteId;
                        if (!string.IsNullOrEmpty(noteId) && !noteIdsToRemove.Contains(noteId))
                        {
                            noteIdsToRemove.Add(noteId);
                        }

                        continue;
                    }

                    GroupView groupView = graphViewChange.elementsToRemove[elementIndex] as GroupView;
                    if (groupView != null)
                    {
                        string groupId = groupView.GroupId;
                        if (!string.IsNullOrEmpty(groupId) && !groupIdsToRemove.Contains(groupId))
                        {
                            groupIdsToRemove.Add(groupId);
                            _groupViewsById.Remove(groupId);
                        }
                    }
                }

                if (nodeIdsToRemove.Count > 0)
                {
                    List<GraphElement> connectedEdgesToRemove = new List<GraphElement>();

                    int nodeIndex;
                    for (nodeIndex = 0; nodeIndex < nodeIdsToRemove.Count; nodeIndex++)
                    {
                        string nodeId = nodeIdsToRemove[nodeIndex];
                        GenNodeView nodeView;
                        if (!_nodeViewsById.TryGetValue(nodeId, out nodeView) || nodeView == null)
                        {
                            continue;
                        }

                        CollectConnectedEdges(nodeView.inputContainer, connectedEdgesToRemove, graphViewChange.elementsToRemove);
                        CollectConnectedEdges(nodeView.outputContainer, connectedEdgesToRemove, graphViewChange.elementsToRemove);
                    }

                    if (connectedEdgesToRemove.Count > 0)
                    {
                        graphViewChange.elementsToRemove.AddRange(connectedEdgesToRemove);
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

                bool anyRemoved = nodeIdsToRemove.Count > 0 ||
                    noteIdsToRemove.Count > 0 ||
                    groupIdsToRemove.Count > 0 ||
                    connectionsToRemove.Count > 0;

                if (anyRemoved)
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

                    int noteIndex;
                    for (noteIndex = 0; noteIndex < noteIdsToRemove.Count; noteIndex++)
                    {
                        string noteId = noteIdsToRemove[noteIndex];
                        if (_graph.RemoveStickyNote(noteId))
                        {
                            _stickyNoteViewsById.Remove(noteId);
                        }
                    }

                    int groupIndex;
                    for (groupIndex = 0; groupIndex < groupIdsToRemove.Count; groupIndex++)
                    {
                        _graph.RemoveGroup(groupIdsToRemove[groupIndex]);
                    }

                    int connectionIndex;
                    for (connectionIndex = 0; connectionIndex < connectionsToRemove.Count; connectionIndex++)
                    {
                        GenConnectionData connectionData = connectionsToRemove[connectionIndex];
                        _graph.RemoveConnection(connectionData.FromNodeId, connectionData.FromPortName, connectionData.ToNodeId, connectionData.ToPortName);
                    }

                    graphStructureChanged = true;
                    EditorUtility.SetDirty(_graph);
                    NotifyGraphMutated();
                }
            }

            if (graphStructureChanged && _generationOrchestrator != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }

            return graphViewChange;
        }

        private static void ProtectOutputNode(GenNodeView nodeView)
        {
            if (nodeView == null || !GraphOutputUtility.IsOutputNode(nodeView.NodeData))
            {
                return;
            }

            nodeView.capabilities &= ~Capabilities.Deletable;
        }

        private static bool CanDeleteGraphElement(GraphElement graphElement)
        {
            return graphElement != null && (graphElement.capabilities & Capabilities.Deletable) != 0;
        }

        private bool TryCreateSubGraphPortFromAutoConnection(
            GenNodeView fromNodeView,
            NodePortDefinition outputPortDefinition,
            GenNodeView toNodeView,
            NodePortDefinition inputPortDefinition)
        {
            if (_graph == null || fromNodeView == null || toNodeView == null)
            {
                return false;
            }

            bool createsWrapperInput =
                IsSubGraphNodeData(toNodeView.NodeData) &&
                string.Equals(inputPortDefinition.Name, SubGraphNodeView.AutoInputPortName, StringComparison.Ordinal);
            bool createsBoundaryInput =
                IsSubGraphInputNodeData(fromNodeView.NodeData) &&
                string.Equals(outputPortDefinition.Name, SubGraphBoundaryNodeView.AutoInputBoundaryPortName, StringComparison.Ordinal);
            bool createsBoundaryOutput =
                IsSubGraphOutputNodeData(toNodeView.NodeData) &&
                string.Equals(inputPortDefinition.Name, SubGraphBoundaryNodeView.AutoOutputBoundaryPortName, StringComparison.Ordinal);
            if (createsBoundaryInput || createsBoundaryOutput)
            {
                return TryCreateSubGraphBoundaryPortFromAutoConnection(
                    fromNodeView,
                    outputPortDefinition,
                    toNodeView,
                    inputPortDefinition,
                    createsBoundaryInput);
            }

            if (!createsWrapperInput)
            {
                return false;
            }

            GenNodeData wrapperNode = toNodeView.NodeData;
            GenGraph nestedGraph = ResolveNestedGraphForWrapper(_graph, wrapperNode);
            if (nestedGraph == null)
            {
                Debug.LogWarning("Cannot auto-create a sub-graph port because '" + wrapperNode.NodeName + "' has no nested graph.");
                return true;
            }

            EnsureBoundaryNodesForSubGraphAsset(nestedGraph);
            GenNodeData boundaryNode = EnsureSubGraphBoundaryNode(nestedGraph, true);
            if (boundaryNode == null)
            {
                Debug.LogWarning("Cannot auto-create a sub-graph port because the nested graph boundary node could not be created.");
                return true;
            }

            if (HasConnectionIntoNodeFromPort(_graph, fromNodeView.NodeData.NodeId, outputPortDefinition.Name, wrapperNode.NodeId))
            {
                return true;
            }

            ChannelType channelType = outputPortDefinition.Type;
            string baseName = ResolveBoundaryPortBaseNameFromDefinition(outputPortDefinition, "Input");
            string portName = CreateUniqueBoundaryPortName(baseName, CollectPortNames(boundaryNode.Ports));
            PortDirection boundaryDirection = PortDirection.Output;
            PortDirection wrapperDirection = PortDirection.Input;

            if (!ReferenceEquals(nestedGraph, _graph))
            {
                Undo.RecordObject(nestedGraph, "Add Sub-Graph Input Port");
            }

            if (boundaryNode.Ports == null)
            {
                boundaryNode.Ports = new List<GenPortData>();
            }

            boundaryNode.Ports.Add(new GenPortData(portName, boundaryDirection, channelType, portName));

            if (wrapperNode.Ports == null)
            {
                wrapperNode.Ports = new List<GenPortData>();
            }

            wrapperNode.Ports.Add(new GenPortData(portName, wrapperDirection, channelType, portName));

            GenConnectionData connectionData = new GenConnectionData(
                fromNodeView.NodeData.NodeId,
                outputPortDefinition.Name,
                wrapperNode.NodeId,
                portName);
            _graph.Connections.Add(connectionData);

            EditorUtility.SetDirty(_graph);
            EditorUtility.SetDirty(nestedGraph);
            SyncSubGraphPortsForGraphMutation();
            RequestDeferredSubGraphPortReload();
            return true;
        }

        private bool TryCreateSubGraphBoundaryPortFromAutoConnection(
            GenNodeView fromNodeView,
            NodePortDefinition outputPortDefinition,
            GenNodeView toNodeView,
            NodePortDefinition inputPortDefinition,
            bool createsInputBoundaryPort)
        {
            GenNodeData boundaryNode = createsInputBoundaryPort ? fromNodeView.NodeData : toNodeView.NodeData;
            if (boundaryNode == null)
            {
                return true;
            }

            if (GenPortUtility.IsSubGraphAutoPort(createsInputBoundaryPort ? inputPortDefinition : outputPortDefinition))
            {
                return true;
            }

            bool existingConnection = createsInputBoundaryPort
                ? HasConnectionFromNodeToPort(_graph, boundaryNode.NodeId, toNodeView.NodeData.NodeId, inputPortDefinition.Name)
                : HasConnectionIntoNodeFromPort(_graph, fromNodeView.NodeData.NodeId, outputPortDefinition.Name, boundaryNode.NodeId);
            if (existingConnection)
            {
                return true;
            }

            ChannelType channelType = createsInputBoundaryPort ? inputPortDefinition.Type : outputPortDefinition.Type;
            string baseName = createsInputBoundaryPort
                ? ResolveBoundaryPortBaseNameFromDefinition(inputPortDefinition, "Input")
                : ResolveBoundaryPortBaseNameFromDefinition(outputPortDefinition, "Output");
            string portName = CreateUniqueBoundaryPortName(baseName, CollectPortNames(boundaryNode.Ports));
            PortDirection boundaryDirection = createsInputBoundaryPort ? PortDirection.Output : PortDirection.Input;

            Undo.RecordObject(_graph, createsInputBoundaryPort ? "Add Sub-Graph Input Port" : "Add Sub-Graph Output Port");
            if (boundaryNode.Ports == null)
            {
                boundaryNode.Ports = new List<GenPortData>();
            }

            boundaryNode.Ports.Add(new GenPortData(portName, boundaryDirection, channelType, portName));

            GenConnectionData connectionData = createsInputBoundaryPort
                ? new GenConnectionData(boundaryNode.NodeId, portName, toNodeView.NodeData.NodeId, inputPortDefinition.Name)
                : new GenConnectionData(fromNodeView.NodeData.NodeId, outputPortDefinition.Name, boundaryNode.NodeId, portName);
            _graph.Connections.Add(connectionData);

            EditorUtility.SetDirty(_graph);
            SyncSubGraphPortsForGraphMutation();
            RequestDeferredSubGraphPortReload();
            return true;
        }

        private static GenNodeData EnsureSubGraphBoundaryNode(GenGraph nestedGraph, bool inputBoundary)
        {
            List<GenNodeData> nodes = nestedGraph != null ? nestedGraph.Nodes : null;
            if (nodes == null)
            {
                return null;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if ((inputBoundary && IsSubGraphInputNodeData(node)) ||
                    (!inputBoundary && IsSubGraphOutputNodeData(node)))
                {
                    return node;
                }
            }

            GenNodeData boundaryNode = new GenNodeData(
                Guid.NewGuid().ToString(),
                inputBoundary ? typeof(SubGraphInputNode).FullName : typeof(SubGraphOutputNode).FullName,
                inputBoundary ? SubGraphInputNode.DefaultNodeName : SubGraphOutputNode.DefaultNodeName,
                inputBoundary ? new Vector2(-AutoLayoutColumnSpacing, -80.0f) : new Vector2(AutoLayoutColumnSpacing, -80.0f));
            nodes.Add(boundaryNode);
            return boundaryNode;
        }

        private static HashSet<string> CollectPortNames(IReadOnlyList<GenPortData> ports)
        {
            HashSet<string> portNames = new HashSet<string>(StringComparer.Ordinal);
            if (ports == null)
            {
                return portNames;
            }

            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null && !string.IsNullOrWhiteSpace(port.PortName))
                {
                    portNames.Add(port.PortName);
                }
            }

            return portNames;
        }

        private static bool HasConnectionIntoNodeFromPort(GenGraph graph, string fromNodeId, string fromPortName, string toNodeId)
        {
            List<GenConnectionData> connections = graph != null ? graph.Connections : null;
            if (connections == null)
            {
                return false;
            }

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection != null &&
                    string.Equals(connection.FromNodeId, fromNodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.FromPortName, fromPortName, StringComparison.Ordinal) &&
                    string.Equals(connection.ToNodeId, toNodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasConnectionFromNodeToPort(GenGraph graph, string fromNodeId, string toNodeId, string toPortName)
        {
            List<GenConnectionData> connections = graph != null ? graph.Connections : null;
            if (connections == null)
            {
                return false;
            }

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection != null &&
                    string.Equals(connection.FromNodeId, fromNodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.ToNodeId, toNodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.ToPortName, toPortName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveBoundaryPortBaseNameFromDefinition(NodePortDefinition portDefinition, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(portDefinition.DisplayName) &&
                !string.Equals(portDefinition.DisplayName, "+ Input", StringComparison.Ordinal) &&
                !string.Equals(portDefinition.DisplayName, "+ Output", StringComparison.Ordinal))
            {
                return portDefinition.DisplayName;
            }

            return string.IsNullOrWhiteSpace(portDefinition.Name) ? fallback : portDefinition.Name;
        }

        private void SyncSubGraphPortsForGraphMutation()
        {
            if (_graph == null)
            {
                return;
            }

            SyncSubGraphWrappersInGraph(_graph, true);
            if (ContainsSubGraphBoundaryNode(_graph))
            {
                SyncParentSubGraphWrappers(_graph);
            }
        }

        private static bool SyncParentSubGraphWrappers(GenGraph nestedGraph)
        {
            if (nestedGraph == null)
            {
                return false;
            }

            bool anyChanged = false;
            List<GenGraph> candidateGraphs = FindCandidateParentGraphs(nestedGraph);
            for (int graphIndex = 0; graphIndex < candidateGraphs.Count; graphIndex++)
            {
                GenGraph candidateGraph = candidateGraphs[graphIndex];
                if (candidateGraph == null || ReferenceEquals(candidateGraph, nestedGraph))
                {
                    continue;
                }

                if (SyncSubGraphWrappersInGraph(candidateGraph, nestedGraph, true))
                {
                    anyChanged = true;
                }
            }

            return anyChanged;
        }

        private static List<GenGraph> FindCandidateParentGraphs(GenGraph nestedGraph)
        {
            List<GenGraph> graphs = new List<GenGraph>();
            string nestedAssetPath = AssetDatabase.GetAssetPath(nestedGraph);
            if (string.IsNullOrWhiteSpace(nestedAssetPath))
            {
                AddGraphsFromGuids(graphs, AssetDatabase.FindAssets("t:GenGraph"));
                return graphs;
            }

            string normalisedPath = nestedAssetPath.Replace("\\", "/");
            int subGraphsIndex = normalisedPath.LastIndexOf("/SubGraphs/", StringComparison.OrdinalIgnoreCase);
            if (subGraphsIndex <= 0)
            {
                AddGraphsFromGuids(graphs, AssetDatabase.FindAssets("t:GenGraph"));
                return graphs;
            }

            string parentFolder = normalisedPath.Substring(0, subGraphsIndex);
            if (Directory.Exists(parentFolder))
            {
                string[] assetPaths = Directory.GetFiles(parentFolder, "*.asset", SearchOption.TopDirectoryOnly);
                for (int assetIndex = 0; assetIndex < assetPaths.Length; assetIndex++)
                {
                    string assetPath = assetPaths[assetIndex].Replace("\\", "/");
                    GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
                    if (graph != null)
                    {
                        graphs.Add(graph);
                    }
                }
            }

            if (graphs.Count == 0 && AssetDatabase.IsValidFolder(parentFolder))
            {
                AddGraphsFromGuids(graphs, AssetDatabase.FindAssets("t:GenGraph", new[] { parentFolder }));
            }

            if (graphs.Count == 0)
            {
                AddGraphsFromGuids(graphs, AssetDatabase.FindAssets("t:GenGraph"));
            }

            return graphs;
        }

        private static void AddGraphsFromGuids(List<GenGraph> graphs, string[] graphGuids)
        {
            if (graphs == null || graphGuids == null)
            {
                return;
            }

            for (int graphIndex = 0; graphIndex < graphGuids.Length; graphIndex++)
            {
                string graphPath = AssetDatabase.GUIDToAssetPath(graphGuids[graphIndex]);
                if (string.IsNullOrWhiteSpace(graphPath))
                {
                    continue;
                }

                GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(graphPath);
                if (graph != null && !graphs.Contains(graph))
                {
                    graphs.Add(graph);
                }
            }
        }

        private static bool SyncSubGraphWrappersInGraph(GenGraph parentGraph, bool recordUndo)
        {
            return SyncSubGraphWrappersInGraph(parentGraph, null, recordUndo);
        }

        private static bool SyncSubGraphWrappersInGraph(GenGraph parentGraph, GenGraph requiredNestedGraph, bool recordUndo)
        {
            if (parentGraph == null || parentGraph.Nodes == null)
            {
                return false;
            }

            bool changed = false;
            bool recordedUndo = false;
            for (int nodeIndex = 0; nodeIndex < parentGraph.Nodes.Count; nodeIndex++)
            {
                GenNodeData wrapperNode = parentGraph.Nodes[nodeIndex];
                if (!IsSubGraphNodeData(wrapperNode))
                {
                    continue;
                }

                GenGraph nestedGraph = ResolveNestedGraphForWrapper(parentGraph, wrapperNode);
                if (nestedGraph == null ||
                    (requiredNestedGraph != null && !ReferenceEquals(nestedGraph, requiredNestedGraph)))
                {
                    continue;
                }

                List<GenPortData> desiredPorts = BuildWrapperPortsFromNestedGraph(nestedGraph);
                if (PortsMatch(wrapperNode.Ports, desiredPorts))
                {
                    continue;
                }

                if (recordUndo && !recordedUndo)
                {
                    Undo.RecordObject(parentGraph, "Sync Sub-Graph Ports");
                    recordedUndo = true;
                }

                wrapperNode.Ports = ClonePorts(desiredPorts);
                PruneInvalidWrapperConnections(parentGraph, wrapperNode);
                EditorUtility.SetDirty(parentGraph);
                changed = true;
            }

            return changed;
        }

        private static List<GenPortData> BuildWrapperPortsFromNestedGraph(GenGraph nestedGraph)
        {
            List<GenPortData> wrapperPorts = new List<GenPortData>();
            List<GenNodeData> nestedNodes = nestedGraph != null ? nestedGraph.Nodes : null;
            if (nestedNodes == null)
            {
                return wrapperPorts;
            }

            for (int nodeIndex = 0; nodeIndex < nestedNodes.Count; nodeIndex++)
            {
                GenNodeData node = nestedNodes[nodeIndex];
                if (IsSubGraphInputNodeData(node))
                {
                    AddBoundaryPortsAsWrapperPorts(node.Ports, PortDirection.Output, PortDirection.Input, wrapperPorts);
                }
            }

            for (int nodeIndex = 0; nodeIndex < nestedNodes.Count; nodeIndex++)
            {
                GenNodeData node = nestedNodes[nodeIndex];
                if (IsSubGraphOutputNodeData(node))
                {
                    AddBoundaryPortsAsWrapperPorts(node.Ports, PortDirection.Input, PortDirection.Output, wrapperPorts);
                }
            }

            return wrapperPorts;
        }

        private static void AddBoundaryPortsAsWrapperPorts(
            IReadOnlyList<GenPortData> boundaryPorts,
            PortDirection expectedBoundaryDirection,
            PortDirection wrapperDirection,
            List<GenPortData> wrapperPorts)
        {
            if (boundaryPorts == null || wrapperPorts == null)
            {
                return;
            }

            for (int portIndex = 0; portIndex < boundaryPorts.Count; portIndex++)
            {
                GenPortData boundaryPort = boundaryPorts[portIndex];
                if (boundaryPort == null ||
                    boundaryPort.Direction != expectedBoundaryDirection ||
                    string.IsNullOrWhiteSpace(boundaryPort.PortName))
                {
                    continue;
                }

                wrapperPorts.Add(new GenPortData(
                    boundaryPort.PortName,
                    wrapperDirection,
                    boundaryPort.Type,
                    string.IsNullOrWhiteSpace(boundaryPort.DisplayName) ? boundaryPort.PortName : boundaryPort.DisplayName));
            }
        }

        private static bool PortsMatch(IReadOnlyList<GenPortData> currentPorts, IReadOnlyList<GenPortData> desiredPorts)
        {
            IReadOnlyList<GenPortData> safeCurrentPorts = currentPorts ?? Array.Empty<GenPortData>();
            IReadOnlyList<GenPortData> safeDesiredPorts = desiredPorts ?? Array.Empty<GenPortData>();
            if (safeCurrentPorts.Count != safeDesiredPorts.Count)
            {
                return false;
            }

            for (int portIndex = 0; portIndex < safeCurrentPorts.Count; portIndex++)
            {
                GenPortData currentPort = safeCurrentPorts[portIndex];
                GenPortData desiredPort = safeDesiredPorts[portIndex];
                if (currentPort == null || desiredPort == null)
                {
                    if (currentPort != desiredPort)
                    {
                        return false;
                    }

                    continue;
                }

                if (!string.Equals(currentPort.PortName, desiredPort.PortName, StringComparison.Ordinal) ||
                    !string.Equals(currentPort.DisplayName, desiredPort.DisplayName, StringComparison.Ordinal) ||
                    currentPort.Direction != desiredPort.Direction ||
                    currentPort.Type != desiredPort.Type)
                {
                    return false;
                }
            }

            return true;
        }

        private static void PruneInvalidWrapperConnections(GenGraph parentGraph, GenNodeData wrapperNode)
        {
            if (parentGraph == null || parentGraph.Connections == null || wrapperNode == null)
            {
                return;
            }

            HashSet<string> inputPortNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> outputPortNames = new HashSet<string>(StringComparer.Ordinal);
            List<GenPortData> ports = wrapperNode.Ports ?? new List<GenPortData>();
            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port == null || string.IsNullOrWhiteSpace(port.PortName))
                {
                    continue;
                }

                if (port.Direction == PortDirection.Input)
                {
                    inputPortNames.Add(port.PortName);
                }
                else
                {
                    outputPortNames.Add(port.PortName);
                }
            }

            string wrapperNodeId = wrapperNode.NodeId ?? string.Empty;
            for (int connectionIndex = parentGraph.Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = parentGraph.Connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                bool staleInput = string.Equals(connection.ToNodeId, wrapperNodeId, StringComparison.Ordinal) &&
                    !inputPortNames.Contains(connection.ToPortName ?? string.Empty);
                bool staleOutput = string.Equals(connection.FromNodeId, wrapperNodeId, StringComparison.Ordinal) &&
                    !outputPortNames.Contains(connection.FromPortName ?? string.Empty);
                if (staleInput || staleOutput)
                {
                    parentGraph.Connections.RemoveAt(connectionIndex);
                }
            }
        }

        private static GenGraph ResolveNestedGraphForWrapper(GenGraph parentGraph, GenNodeData wrapperNode)
        {
            List<SerializedParameter> parameters = wrapperNode != null ? wrapperNode.Parameters : null;
            return ResolveNestedGraphFromParameter(parameters, SubGraphNode.NestedGraphParameterName);
        }

        private static GenGraph ResolveNestedGraphFromParameter(IReadOnlyList<SerializedParameter> parameters, string parameterName)
        {
            if (parameters == null)
            {
                return null;
            }

            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter == null ||
                    !string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                GenGraph objectReferenceGraph = parameter.ObjectReference as GenGraph;
                if (objectReferenceGraph != null)
                {
                    return objectReferenceGraph;
                }

                string assetPath = ResolveGraphAssetPath(parameter.Value);
                if (!string.IsNullOrWhiteSpace(assetPath))
                {
                    GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
                    if (graph != null)
                    {
                        return graph;
                    }
                }
            }

            return null;
        }

        private static string ResolveGraphAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmedValue = value.Trim();
            string guidPath = AssetDatabase.GUIDToAssetPath(trimmedValue);
            if (!string.IsNullOrWhiteSpace(guidPath))
            {
                return guidPath;
            }

            return string.Empty;
        }

        private static bool ContainsSubGraphBoundaryNode(GenGraph graph)
        {
            List<GenNodeData> nodes = graph != null ? graph.Nodes : null;
            if (nodes == null)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if (IsSubGraphInputNodeData(node) || IsSubGraphOutputNodeData(node))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureBoundaryNodesForSubGraphAsset(GenGraph graph)
        {
            if (graph == null || graph.Nodes == null || !IsSubGraphAsset(graph))
            {
                return;
            }

            bool hasInputBoundary = false;
            bool hasOutputBoundary = false;
            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                hasInputBoundary = hasInputBoundary || IsSubGraphInputNodeData(node);
                hasOutputBoundary = hasOutputBoundary || IsSubGraphOutputNodeData(node);
            }

            if (hasInputBoundary && hasOutputBoundary)
            {
                return;
            }

            if (!hasInputBoundary)
            {
                graph.Nodes.Add(new GenNodeData(
                    Guid.NewGuid().ToString(),
                    typeof(SubGraphInputNode).FullName,
                    SubGraphInputNode.DefaultNodeName,
                    new Vector2(-AutoLayoutColumnSpacing, -80.0f)));
            }

            if (!hasOutputBoundary)
            {
                graph.Nodes.Add(new GenNodeData(
                    Guid.NewGuid().ToString(),
                    typeof(SubGraphOutputNode).FullName,
                    SubGraphOutputNode.DefaultNodeName,
                    new Vector2(AutoLayoutColumnSpacing, -80.0f)));
            }

            EditorUtility.SetDirty(graph);
        }

        private static bool IsSubGraphAsset(GenGraph graph)
        {
            string assetPath = AssetDatabase.GetAssetPath(graph);
            return !string.IsNullOrWhiteSpace(assetPath) &&
                   assetPath.IndexOf("/SubGraphs/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSubGraphNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphNode).FullName, StringComparison.Ordinal);
        }

        private static bool IsSubGraphInputNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphInputNode).FullName, StringComparison.Ordinal);
        }

        private static bool IsSubGraphOutputNodeData(GenNodeData nodeData)
        {
            return nodeData != null && string.Equals(nodeData.NodeTypeName, typeof(SubGraphOutputNode).FullName, StringComparison.Ordinal);
        }

        public bool CanGroupSelection()
        {
            return GetSelectedNodeViews().Count > 0;
        }

        public bool CanUngroupSelection()
        {
            if (GetSelectedGroupViews().Count > 0)
            {
                return true;
            }

            foreach (GenNodeView nodeView in GetSelectedNodeViews())
            {
                if (GetContainingGroups(nodeView).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanCollapseSelection()
        {
            foreach (GenNodeView nodeView in GetSelectedNodeViews())
            {
                if (!nodeView.IsCollapsed())
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanExpandSelection()
        {
            foreach (GenNodeView nodeView in GetSelectedNodeViews())
            {
                if (nodeView.IsCollapsed())
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanAutoLayoutSelection()
        {
            return GetSelectedNodeViews().Count > 0 || GetSelectedGroupViews().Count > 0;
        }

        public bool CanConvertSelectionToSubGraph()
        {
            if (_graph == null || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(_graph)))
            {
                return false;
            }

            List<GenNodeView> selectedNodeViews = ResolveSubGraphConversionNodeSelection();
            if (selectedNodeViews.Count == 0)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < selectedNodeViews.Count; nodeIndex++)
            {
                GenNodeView nodeView = selectedNodeViews[nodeIndex];
                if (nodeView != null && GraphOutputUtility.IsOutputNode(nodeView.NodeData))
                {
                    return false;
                }
            }

            return true;
        }

        public void ConvertSelectionToSubGraph()
        {
            if (_graph == null)
            {
                return;
            }

            List<GenNodeView> selectedNodeViews = ResolveSubGraphConversionNodeSelection();
            if (selectedNodeViews.Count == 0)
            {
                return;
            }

            for (int nodeIndex = 0; nodeIndex < selectedNodeViews.Count; nodeIndex++)
            {
                GenNodeView nodeView = selectedNodeViews[nodeIndex];
                if (nodeView != null && GraphOutputUtility.IsOutputNode(nodeView.NodeData))
                {
                    Debug.LogWarning("Cannot convert the graph output node into a sub-graph.");
                    return;
                }
            }

            string parentAssetPath = AssetDatabase.GetAssetPath(_graph);
            if (string.IsNullOrWhiteSpace(parentAssetPath))
            {
                Debug.LogWarning("Save the graph asset before converting nodes into a sub-graph.");
                return;
            }

            HashSet<string> selectedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            for (int nodeIndex = 0; nodeIndex < selectedNodeViews.Count; nodeIndex++)
            {
                selectedNodeIds.Add(selectedNodeViews[nodeIndex].NodeData.NodeId ?? string.Empty);
            }

            List<GenConnectionData> internalConnections;
            List<BoundaryInputDefinition> boundaryInputs;
            List<BoundaryOutputDefinition> boundaryOutputs;
            string boundaryError;
            if (!TryBuildBoundaryDefinitions(selectedNodeIds, out internalConnections, out boundaryInputs, out boundaryOutputs, out boundaryError))
            {
                Debug.LogError(boundaryError);
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Selection To Subgraph");

            Rect selectionBounds = CalculateNodeViewsRect(selectedNodeViews);
            string subGraphName = ResolveSubGraphName(selectedNodeViews);
            string nestedGraphGuid;
            string nestedGraphPath;
            GenGraph nestedGraph = CreateNestedGraphAsset(parentAssetPath, subGraphName, out nestedGraphGuid, out nestedGraphPath);
            if (nestedGraph == null)
            {
                Undo.CollapseUndoOperations(undoGroup);
                return;
            }

            Undo.RegisterCreatedObjectUndo(nestedGraph, "Create Subgraph Asset");
            Undo.RecordObject(_graph, "Convert Selection To Subgraph");
            Undo.RecordObject(nestedGraph, "Populate Subgraph");

            PopulateNestedGraph(nestedGraph, selectedNodeIds, internalConnections, boundaryInputs, boundaryOutputs, selectionBounds);

            GenNodeData wrapperNode = CreateSubGraphWrapperNode(subGraphName, nestedGraph, nestedGraphGuid, boundaryInputs, boundaryOutputs, selectionBounds);
            RewriteParentGraphForSubGraph(selectedNodeIds, wrapperNode, boundaryInputs, boundaryOutputs);

            EditorUtility.SetDirty(_graph);
            EditorUtility.SetDirty(nestedGraph);
            AssetDatabase.SaveAssets();

            Vector3 scrollOffset;
            float zoomScale;
            GetViewportState(out scrollOffset, out zoomScale);
            LoadGraph(_graph);
            RestoreViewportState(scrollOffset, zoomScale);
            SelectAndFrameNode(wrapperNode.NodeId);

            NotifyGraphMutated();
            _generationOrchestrator?.RequestPreviewRefresh();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private void CreateSubGraphNodeFromDroppedGraph(GenGraph nestedGraph, Vector2 graphLocalPosition)
        {
            if (_graph == null || nestedGraph == null)
            {
                return;
            }

            if (ReferenceEquals(_graph, nestedGraph))
            {
                Debug.LogWarning("Cannot add a graph as a sub-graph of itself.");
                return;
            }

            if (WouldCreateRecursiveSubGraphReference(_graph, nestedGraph))
            {
                Debug.LogWarning("Cannot add '" + nestedGraph.name + "' as a sub-graph because it would create a recursive graph reference.");
                return;
            }

            string nestedGraphPath = AssetDatabase.GetAssetPath(nestedGraph);
            if (string.IsNullOrWhiteSpace(nestedGraphPath))
            {
                Debug.LogWarning("Save the graph asset before adding it as a sub-graph.");
                return;
            }

            string nestedGraphGuid = AssetDatabase.AssetPathToGUID(nestedGraphPath);
            if (string.IsNullOrWhiteSpace(nestedGraphGuid))
            {
                Debug.LogWarning("Cannot add '" + nestedGraph.name + "' as a sub-graph because its asset GUID could not be resolved.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Graph Asset As Subgraph");

            Undo.RecordObject(nestedGraph, "Convert Graph Asset To Subgraph");
            bool convertedNestedGraph = ConvertGraphAssetToSubGraphIfNeeded(nestedGraph);

            Undo.RecordObject(_graph, "Add Subgraph Node");
            Vector2 contentPosition = ConvertGraphLocalToContentPosition(graphLocalPosition);
            string nodeName = string.IsNullOrWhiteSpace(nestedGraph.name) ? "Subgraph" : nestedGraph.name;
            GenNodeData wrapperNode = new GenNodeData(
                Guid.NewGuid().ToString(),
                typeof(SubGraphNode).FullName,
                nodeName,
                contentPosition);
            wrapperNode.Ports = BuildWrapperPortsFromNestedGraph(nestedGraph);
            wrapperNode.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphParameterName, nestedGraphGuid, nestedGraph));
            _graph.Nodes.Add(wrapperNode);

            EditorUtility.SetDirty(_graph);
            if (convertedNestedGraph)
            {
                EditorUtility.SetDirty(nestedGraph);
            }

            AssetDatabase.SaveAssets();

            Vector3 scrollOffset;
            float zoomScale;
            GetViewportState(out scrollOffset, out zoomScale);
            LoadGraph(_graph);
            RestoreViewportState(scrollOffset, zoomScale);
            SelectAndFrameNode(wrapperNode.NodeId);

            NotifyGraphMutated();
            _generationOrchestrator?.RequestPreviewRefresh();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static bool ConvertGraphAssetToSubGraphIfNeeded(GenGraph graph)
        {
            if (graph == null)
            {
                return false;
            }

            EnsureGraphCollections(graph);

            bool hadBoundaryNodes = ContainsSubGraphBoundaryNode(graph);
            bool changed = EnsureBoundaryNodes(graph);
            if (hadBoundaryNodes)
            {
                return changed;
            }

            GenNodeData outputNode = GraphOutputUtility.FindOutputNode(graph);
            GenNodeData subGraphOutputNode = EnsureSubGraphBoundaryNode(graph, false);
            if (outputNode == null || subGraphOutputNode == null)
            {
                return true;
            }

            if (subGraphOutputNode.Ports == null)
            {
                subGraphOutputNode.Ports = new List<GenPortData>();
            }

            HashSet<string> usedOutputPortNames = CollectPortNames(subGraphOutputNode.Ports);
            List<GenConnectionData> connections = graph.Connections;
            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null ||
                    !string.Equals(connection.ToNodeId, outputNode.NodeId, StringComparison.Ordinal))
                {
                    continue;
                }

                GenPortData outputInputPort = FindPortData(outputNode, connection.ToPortName);
                string baseName = ResolveBoundaryPortBaseName(outputInputPort, connection.ToPortName);
                string portName = CreateUniqueBoundaryPortName(baseName, usedOutputPortNames);
                ChannelType portType = outputInputPort != null ? outputInputPort.Type : ChannelType.Int;
                string displayName = outputInputPort != null && !string.IsNullOrWhiteSpace(outputInputPort.DisplayName)
                    ? outputInputPort.DisplayName
                    : portName;

                subGraphOutputNode.Ports.Add(new GenPortData(portName, PortDirection.Input, portType, displayName));
                connection.ToNodeId = subGraphOutputNode.NodeId;
                connection.ToPortName = portName;
            }

            for (int connectionIndex = connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection != null &&
                    (string.Equals(connection.FromNodeId, outputNode.NodeId, StringComparison.Ordinal) ||
                     string.Equals(connection.ToNodeId, outputNode.NodeId, StringComparison.Ordinal)))
                {
                    connections.RemoveAt(connectionIndex);
                }
            }

            graph.Nodes.Remove(outputNode);
            return true;
        }

        private static bool EnsureBoundaryNodes(GenGraph graph)
        {
            if (graph == null)
            {
                return false;
            }

            EnsureGraphCollections(graph);

            bool changed = false;
            if (!HasSubGraphBoundaryNode(graph, true) && EnsureSubGraphBoundaryNode(graph, true) != null)
            {
                changed = true;
            }

            if (!HasSubGraphBoundaryNode(graph, false) && EnsureSubGraphBoundaryNode(graph, false) != null)
            {
                changed = true;
            }

            return changed;
        }

        private static bool HasSubGraphBoundaryNode(GenGraph graph, bool inputBoundary)
        {
            List<GenNodeData> nodes = graph != null ? graph.Nodes : null;
            if (nodes == null)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if ((inputBoundary && IsSubGraphInputNodeData(node)) ||
                    (!inputBoundary && IsSubGraphOutputNodeData(node)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureGraphCollections(GenGraph graph)
        {
            if (graph.Nodes == null)
            {
                graph.Nodes = new List<GenNodeData>();
            }

            if (graph.Connections == null)
            {
                graph.Connections = new List<GenConnectionData>();
            }

            if (graph.StickyNotes == null)
            {
                graph.StickyNotes = new List<GenStickyNoteData>();
            }

            if (graph.Groups == null)
            {
                graph.Groups = new List<GenGroupData>();
            }

            if (graph.ExposedProperties == null)
            {
                graph.ExposedProperties = new List<ExposedProperty>();
            }
        }

        private static bool WouldCreateRecursiveSubGraphReference(GenGraph parentGraph, GenGraph nestedGraph)
        {
            if (parentGraph == null || nestedGraph == null)
            {
                return false;
            }

            return GraphReferencesGraph(nestedGraph, parentGraph, new HashSet<GenGraph>());
        }

        private static bool GraphReferencesGraph(GenGraph sourceGraph, GenGraph targetGraph, HashSet<GenGraph> visitedGraphs)
        {
            if (sourceGraph == null || targetGraph == null)
            {
                return false;
            }

            if (ReferenceEquals(sourceGraph, targetGraph))
            {
                return true;
            }

            if (!visitedGraphs.Add(sourceGraph))
            {
                return false;
            }

            List<GenNodeData> nodes = sourceGraph.Nodes;
            if (nodes == null)
            {
                return false;
            }

            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData node = nodes[nodeIndex];
                if (!IsSubGraphNodeData(node))
                {
                    continue;
                }

                GenGraph nestedGraph = ResolveNestedGraphForWrapper(sourceGraph, node);
                if (GraphReferencesGraph(nestedGraph, targetGraph, visitedGraphs))
                {
                    return true;
                }
            }

            return false;
        }

        public void AutoLayoutSelection()
        {
            if (_graph == null)
            {
                return;
            }

            List<GenNodeView> selectedNodeViews = ResolveAutoLayoutNodeSelection();
            if (selectedNodeViews.Count == 0)
            {
                return;
            }

            Undo.RecordObject(_graph, "Auto Layout Selection");

            List<GenNodeView> orderedNodes = SortNodesByDependency(selectedNodeViews);
            Dictionary<string, int> columnsByNodeId = CalculateDependencyColumns(orderedNodes);
            Dictionary<int, int> rowsByColumn = new Dictionary<int, int>();

            Rect selectionBounds = CalculateNodeViewsRect(selectedNodeViews);
            Vector2 origin = selectionBounds.position;
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < orderedNodes.Count; nodeIndex++)
            {
                GenNodeView nodeView = orderedNodes[nodeIndex];
                int column;
                if (!columnsByNodeId.TryGetValue(nodeView.NodeData.NodeId, out column))
                {
                    column = 0;
                }

                int row;
                rowsByColumn.TryGetValue(column, out row);
                rowsByColumn[column] = row + 1;

                Vector2 position = new Vector2(
                    origin.x + column * AutoLayoutColumnSpacing,
                    origin.y + row * AutoLayoutRowSpacing);

                Rect currentRect = nodeView.GetPosition();
                currentRect.position = position;
                nodeView.SetPosition(currentRect);
                nodeView.NodeData.Position = position;
            }

            RefreshSelectedGroupBounds(selectedNodeViews);
            EditorUtility.SetDirty(_graph);
            NotifyGraphMutated();
        }

        public void GroupSelection()
        {
            if (_graph == null)
            {
                return;
            }

            List<GenNodeView> selectedNodeViews = GetSelectedNodeViews();
            if (selectedNodeViews.Count == 0)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Group Selection");

            Rect groupRect = CalculateGroupRect(selectedNodeViews);

            Undo.RecordObject(_graph, "Group Selection");
            GenGroupData groupData = _graph.AddGroup("Group", groupRect);
            EditorUtility.SetDirty(_graph);

            GroupView groupView = new GroupView(_graph, groupData, NotifyGraphMutated);
            _groupViewsById[groupData.GroupId] = groupView;
            AddElement(groupView);
            groupView.AddElements(selectedNodeViews.Cast<GraphElement>().ToList());

            ClearSelection();
            AddToSelection(groupView);

            NotifyGraphMutated();
            Undo.CollapseUndoOperations(undoGroup);
        }

        public void CollapseSelection()
        {
            SetSelectionCollapsedState(true);
        }

        public void ExpandSelection()
        {
            SetSelectionCollapsedState(false);
        }

        public void UngroupSelection()
        {
            if (_graph == null)
            {
                return;
            }

            List<GroupView> selectedGroups = GetSelectedGroupViews();
            List<GenNodeView> selectedNodeViews = GetSelectedNodeViews();
            if (selectedGroups.Count == 0 && selectedNodeViews.Count == 0)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Ungroup Selection");

            if (selectedGroups.Count > 0)
            {
                foreach (GroupView groupView in selectedGroups)
                {
                    if (groupView == null)
                    {
                        continue;
                    }

                    List<GraphElement> containedElements = groupView.containedElements
                        .OfType<GraphElement>()
                        .ToList();
                    if (containedElements.Count > 0)
                    {
                        groupView.RemoveElements(containedElements);
                    }
                }

                DeleteElements(selectedGroups.Cast<GraphElement>().ToList());
            }

            if (selectedNodeViews.Count > 0)
            {
                foreach (GroupView groupView in _groupViewsById.Values.ToList())
                {
                    List<GraphElement> nodesToRemove = selectedNodeViews
                        .Where(nodeView => groupView.ContainsElement(nodeView))
                        .Cast<GraphElement>()
                        .ToList();
                    if (nodesToRemove.Count > 0)
                    {
                        groupView.RemoveElements(nodesToRemove);
                    }
                }
            }

            NotifyGraphMutated();
            Undo.CollapseUndoOperations(undoGroup);
        }

        private List<GenNodeView> GetSelectedNodeViews()
        {
            List<GenNodeView> selectedNodeViews = new List<GenNodeView>();

            foreach (ISelectable selectable in selection)
            {
                GenNodeView nodeView = selectable as GenNodeView;
                if (nodeView != null && !selectedNodeViews.Contains(nodeView))
                {
                    selectedNodeViews.Add(nodeView);
                }
            }

            foreach (GenNodeView nodeView in graphElements.OfType<GenNodeView>())
            {
                if (nodeView != null && nodeView.selected && !selectedNodeViews.Contains(nodeView))
                {
                    selectedNodeViews.Add(nodeView);
                }
            }

            return selectedNodeViews;
        }

        private List<GroupView> GetSelectedGroupViews()
        {
            List<GroupView> selectedGroups = new List<GroupView>();

            foreach (ISelectable selectable in selection)
            {
                GroupView groupView = selectable as GroupView;
                if (groupView != null && !selectedGroups.Contains(groupView))
                {
                    selectedGroups.Add(groupView);
                }
            }

            foreach (GroupView groupView in graphElements.OfType<GroupView>())
            {
                if (groupView != null && groupView.selected && !selectedGroups.Contains(groupView))
                {
                    selectedGroups.Add(groupView);
                }
            }

            return selectedGroups;
        }

        private sealed class BoundaryInputDefinition
        {
            public GenConnectionData ParentConnection;
            public string PortName;
            public string DisplayName;
            public ChannelType Type;
        }

        private sealed class BoundaryOutputDefinition
        {
            public GenConnectionData SourceConnection;
            public readonly List<GenConnectionData> ParentConnections = new List<GenConnectionData>();
            public string PortName;
            public string DisplayName;
            public ChannelType Type;
        }

        private List<GenNodeView> ResolveSubGraphConversionNodeSelection()
        {
            HashSet<string> selectedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            List<GenNodeView> selectedNodeViews = new List<GenNodeView>();

            List<GenNodeView> directNodeViews = GetSelectedNodeViews();
            for (int nodeIndex = 0; nodeIndex < directNodeViews.Count; nodeIndex++)
            {
                AddUniqueNodeView(directNodeViews[nodeIndex], selectedNodeIds, selectedNodeViews);
            }

            List<GroupView> selectedGroups = GetSelectedGroupViews();
            for (int groupIndex = 0; groupIndex < selectedGroups.Count; groupIndex++)
            {
                GroupView groupView = selectedGroups[groupIndex];
                if (groupView == null)
                {
                    continue;
                }

                foreach (GraphElement element in groupView.containedElements)
                {
                    AddUniqueNodeView(element as GenNodeView, selectedNodeIds, selectedNodeViews);
                }
            }

            return selectedNodeViews;
        }

        private static void AddUniqueNodeView(GenNodeView nodeView, HashSet<string> selectedNodeIds, List<GenNodeView> selectedNodeViews)
        {
            if (nodeView == null || nodeView.NodeData == null)
            {
                return;
            }

            string nodeId = nodeView.NodeData.NodeId ?? string.Empty;
            if (selectedNodeIds.Add(nodeId))
            {
                selectedNodeViews.Add(nodeView);
            }
        }

        private bool TryBuildBoundaryDefinitions(
            HashSet<string> selectedNodeIds,
            out List<GenConnectionData> internalConnections,
            out List<BoundaryInputDefinition> boundaryInputs,
            out List<BoundaryOutputDefinition> boundaryOutputs,
            out string errorMessage)
        {
            internalConnections = new List<GenConnectionData>();
            boundaryInputs = new List<BoundaryInputDefinition>();
            boundaryOutputs = new List<BoundaryOutputDefinition>();
            errorMessage = null;

            Dictionary<string, BoundaryOutputDefinition> boundaryOutputsBySource = new Dictionary<string, BoundaryOutputDefinition>(StringComparer.Ordinal);
            HashSet<string> internallyConsumedOutputKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedInputPortNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedOutputPortNames = new HashSet<string>(StringComparer.Ordinal);
            List<GenConnectionData> connections = _graph.Connections ?? new List<GenConnectionData>();

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                bool fromSelected = selectedNodeIds.Contains(connection.FromNodeId ?? string.Empty);
                bool toSelected = selectedNodeIds.Contains(connection.ToNodeId ?? string.Empty);

                if (fromSelected && toSelected)
                {
                    internalConnections.Add(CloneConnectionData(connection));
                    internallyConsumedOutputKeys.Add(CreateSourcePortKey(connection.FromNodeId, connection.FromPortName));
                    continue;
                }

                if (!fromSelected && toSelected)
                {
                    GenPortData targetPort = FindPortData(connection.ToNodeId, connection.ToPortName);
                    if (targetPort == null)
                    {
                        errorMessage = "Could not resolve target port '" + connection.ToPortName + "' while converting selection to a sub-graph.";
                        return false;
                    }

                    string portName = CreateUniqueBoundaryPortName("Input", usedInputPortNames);
                    boundaryInputs.Add(new BoundaryInputDefinition
                    {
                        ParentConnection = CloneConnectionData(connection),
                        PortName = portName,
                        DisplayName = portName,
                        Type = targetPort.Type
                    });
                    continue;
                }

                if (fromSelected && !toSelected)
                {
                    GenPortData sourcePort = FindPortData(connection.FromNodeId, connection.FromPortName);
                    if (sourcePort == null)
                    {
                        errorMessage = "Could not resolve source port '" + connection.FromPortName + "' while converting selection to a sub-graph.";
                        return false;
                    }

                    string key = CreateSourcePortKey(connection.FromNodeId, connection.FromPortName);
                    BoundaryOutputDefinition outputDefinition;
                    if (!boundaryOutputsBySource.TryGetValue(key, out outputDefinition))
                    {
                        string portName = CreateUniqueBoundaryPortName(ResolveBoundaryPortBaseName(sourcePort, "Output"), usedOutputPortNames);
                        outputDefinition = new BoundaryOutputDefinition
                        {
                            SourceConnection = CloneConnectionData(connection),
                            PortName = portName,
                            DisplayName = portName,
                            Type = sourcePort.Type
                        };
                        boundaryOutputsBySource.Add(key, outputDefinition);
                        boundaryOutputs.Add(outputDefinition);
                    }

                    outputDefinition.ParentConnections.Add(CloneConnectionData(connection));
                }
            }

            AddTerminalOutputDefinitions(
                selectedNodeIds,
                internallyConsumedOutputKeys,
                boundaryOutputsBySource,
                usedOutputPortNames,
                boundaryOutputs);

            return true;
        }

        private void AddTerminalOutputDefinitions(
            HashSet<string> selectedNodeIds,
            HashSet<string> internallyConsumedOutputKeys,
            Dictionary<string, BoundaryOutputDefinition> boundaryOutputsBySource,
            HashSet<string> usedOutputPortNames,
            List<BoundaryOutputDefinition> boundaryOutputs)
        {
            foreach (string selectedNodeId in selectedNodeIds)
            {
                GenNodeData node = _graph.GetNode(selectedNodeId);
                List<GenPortData> ports = node != null ? node.Ports : null;
                if (ports == null)
                {
                    continue;
                }

                for (int portIndex = 0; portIndex < ports.Count; portIndex++)
                {
                    GenPortData port = ports[portIndex];
                    if (port == null || port.Direction != PortDirection.Output)
                    {
                        continue;
                    }

                    string sourceKey = CreateSourcePortKey(selectedNodeId, port.PortName);
                    if (internallyConsumedOutputKeys.Contains(sourceKey) ||
                        boundaryOutputsBySource.ContainsKey(sourceKey))
                    {
                        continue;
                    }

                    string portName = CreateUniqueBoundaryPortName(ResolveBoundaryPortBaseName(port, "Output"), usedOutputPortNames);
                    GenConnectionData sourceConnection = new GenConnectionData(selectedNodeId, port.PortName, string.Empty, string.Empty);
                    BoundaryOutputDefinition outputDefinition = new BoundaryOutputDefinition
                    {
                        SourceConnection = sourceConnection,
                        PortName = portName,
                        DisplayName = portName,
                        Type = port.Type
                    };
                    boundaryOutputsBySource.Add(sourceKey, outputDefinition);
                    boundaryOutputs.Add(outputDefinition);
                }
            }
        }

        private GenGraph CreateNestedGraphAsset(string parentAssetPath, string subGraphName, out string nestedGraphGuid, out string nestedGraphPath)
        {
            nestedGraphGuid = string.Empty;
            nestedGraphPath = string.Empty;

            string parentFolder = Path.GetDirectoryName(parentAssetPath);
            if (string.IsNullOrWhiteSpace(parentFolder))
            {
                parentFolder = "Assets";
            }

            parentFolder = parentFolder.Replace("\\", "/");
            string subGraphsFolder = parentFolder + "/SubGraphs";
            if (!AssetDatabase.IsValidFolder(subGraphsFolder))
            {
                AssetDatabase.CreateFolder(parentFolder, "SubGraphs");
            }

            string safeName = MakeSafeAssetFileName(subGraphName);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(subGraphsFolder + "/" + safeName + ".asset");
            nestedGraphPath = assetPath;
            GenGraph nestedGraph = ScriptableObject.CreateInstance<GenGraph>();
            nestedGraph.name = Path.GetFileNameWithoutExtension(assetPath);
            CopyGraphSettings(_graph, nestedGraph);
            AssetDatabase.CreateAsset(nestedGraph, assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            nestedGraphGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(nestedGraphGuid))
            {
                Debug.LogError("Failed to resolve GUID for generated sub-graph asset at '" + assetPath + "'.");
                return null;
            }

            return nestedGraph;
        }

        private void PopulateNestedGraph(
            GenGraph nestedGraph,
            HashSet<string> selectedNodeIds,
            List<GenConnectionData> internalConnections,
            List<BoundaryInputDefinition> boundaryInputs,
            List<BoundaryOutputDefinition> boundaryOutputs,
            Rect selectionBounds)
        {
            nestedGraph.Nodes.Clear();
            nestedGraph.Connections.Clear();
            nestedGraph.Groups.Clear();
            nestedGraph.StickyNotes.Clear();

            List<GenNodeData> parentNodes = _graph.Nodes ?? new List<GenNodeData>();
            for (int nodeIndex = 0; nodeIndex < parentNodes.Count; nodeIndex++)
            {
                GenNodeData node = parentNodes[nodeIndex];
                if (node != null && selectedNodeIds.Contains(node.NodeId ?? string.Empty))
                {
                    nestedGraph.Nodes.Add(CloneNodeData(node));
                }
            }

            GenNodeData inputNode = new GenNodeData(
                Guid.NewGuid().ToString(),
                typeof(SubGraphInputNode).FullName,
                SubGraphInputNode.DefaultNodeName,
                new Vector2(selectionBounds.xMin - AutoLayoutColumnSpacing, selectionBounds.center.y - 80.0f));

            for (int inputIndex = 0; inputIndex < boundaryInputs.Count; inputIndex++)
            {
                BoundaryInputDefinition input = boundaryInputs[inputIndex];
                inputNode.Ports.Add(new GenPortData(input.PortName, PortDirection.Output, input.Type, input.DisplayName));
                GenConnectionData nestedConnection = new GenConnectionData(
                    inputNode.NodeId,
                    input.PortName,
                    input.ParentConnection.ToNodeId,
                    input.ParentConnection.ToPortName);
                nestedGraph.Connections.Add(nestedConnection);
            }

            nestedGraph.Nodes.Add(inputNode);

            GenNodeData outputNode = new GenNodeData(
                Guid.NewGuid().ToString(),
                typeof(SubGraphOutputNode).FullName,
                SubGraphOutputNode.DefaultNodeName,
                new Vector2(selectionBounds.xMax + AutoLayoutColumnSpacing, selectionBounds.center.y - 80.0f));

            for (int outputIndex = 0; outputIndex < boundaryOutputs.Count; outputIndex++)
            {
                BoundaryOutputDefinition output = boundaryOutputs[outputIndex];
                outputNode.Ports.Add(new GenPortData(output.PortName, PortDirection.Input, output.Type, output.DisplayName));
                GenConnectionData nestedConnection = new GenConnectionData(
                    output.SourceConnection.FromNodeId,
                    output.SourceConnection.FromPortName,
                    outputNode.NodeId,
                    output.PortName);
                nestedGraph.Connections.Add(nestedConnection);
            }

            nestedGraph.Nodes.Add(outputNode);

            for (int connectionIndex = 0; connectionIndex < internalConnections.Count; connectionIndex++)
            {
                nestedGraph.Connections.Add(CloneConnectionData(internalConnections[connectionIndex]));
            }

            MoveFullySelectedGroupsToNestedGraph(nestedGraph, selectedNodeIds);
        }

        private GenNodeData CreateSubGraphWrapperNode(
            string subGraphName,
            GenGraph nestedGraph,
            string nestedGraphGuid,
            List<BoundaryInputDefinition> boundaryInputs,
            List<BoundaryOutputDefinition> boundaryOutputs,
            Rect selectionBounds)
        {
            GenNodeData wrapperNode = new GenNodeData(
                Guid.NewGuid().ToString(),
                typeof(SubGraphNode).FullName,
                subGraphName,
                selectionBounds.center);

            for (int inputIndex = 0; inputIndex < boundaryInputs.Count; inputIndex++)
            {
                BoundaryInputDefinition input = boundaryInputs[inputIndex];
                wrapperNode.Ports.Add(new GenPortData(input.PortName, PortDirection.Input, input.Type, input.DisplayName));
            }

            for (int outputIndex = 0; outputIndex < boundaryOutputs.Count; outputIndex++)
            {
                BoundaryOutputDefinition output = boundaryOutputs[outputIndex];
                wrapperNode.Ports.Add(new GenPortData(output.PortName, PortDirection.Output, output.Type, output.DisplayName));
            }

            wrapperNode.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphParameterName, nestedGraphGuid, nestedGraph));
            return wrapperNode;
        }

        private void RewriteParentGraphForSubGraph(
            HashSet<string> selectedNodeIds,
            GenNodeData wrapperNode,
            List<BoundaryInputDefinition> boundaryInputs,
            List<BoundaryOutputDefinition> boundaryOutputs)
        {
            for (int nodeIndex = _graph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                GenNodeData node = _graph.Nodes[nodeIndex];
                if (node != null && selectedNodeIds.Contains(node.NodeId ?? string.Empty))
                {
                    _graph.Nodes.RemoveAt(nodeIndex);
                }
            }

            for (int connectionIndex = _graph.Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = _graph.Connections[connectionIndex];
                if (connection == null ||
                    selectedNodeIds.Contains(connection.FromNodeId ?? string.Empty) ||
                    selectedNodeIds.Contains(connection.ToNodeId ?? string.Empty))
                {
                    _graph.Connections.RemoveAt(connectionIndex);
                }
            }

            _graph.Nodes.Add(wrapperNode);

            for (int inputIndex = 0; inputIndex < boundaryInputs.Count; inputIndex++)
            {
                BoundaryInputDefinition input = boundaryInputs[inputIndex];
                GenConnectionData parentConnection = new GenConnectionData(
                    input.ParentConnection.FromNodeId,
                    input.ParentConnection.FromPortName,
                    wrapperNode.NodeId,
                    input.PortName);
                parentConnection.CastMode = input.ParentConnection.CastMode;
                _graph.Connections.Add(parentConnection);
            }

            for (int outputIndex = 0; outputIndex < boundaryOutputs.Count; outputIndex++)
            {
                BoundaryOutputDefinition output = boundaryOutputs[outputIndex];
                for (int connectionIndex = 0; connectionIndex < output.ParentConnections.Count; connectionIndex++)
                {
                    GenConnectionData originalConnection = output.ParentConnections[connectionIndex];
                    GenConnectionData parentConnection = new GenConnectionData(
                        wrapperNode.NodeId,
                        output.PortName,
                        originalConnection.ToNodeId,
                        originalConnection.ToPortName);
                    parentConnection.CastMode = originalConnection.CastMode;
                    _graph.Connections.Add(parentConnection);
                }
            }

            UpdateParentGroupsAfterSubGraphConversion(selectedNodeIds, wrapperNode.NodeId);
        }

        private void MoveFullySelectedGroupsToNestedGraph(GenGraph nestedGraph, HashSet<string> selectedNodeIds)
        {
            List<GenGroupData> groups = _graph.Groups ?? new List<GenGroupData>();
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                GenGroupData group = groups[groupIndex];
                if (group == null || group.ContainedNodeIds == null || group.ContainedNodeIds.Count == 0)
                {
                    continue;
                }

                bool allSelected = true;
                for (int nodeIndex = 0; nodeIndex < group.ContainedNodeIds.Count; nodeIndex++)
                {
                    if (!selectedNodeIds.Contains(group.ContainedNodeIds[nodeIndex] ?? string.Empty))
                    {
                        allSelected = false;
                        break;
                    }
                }

                if (allSelected)
                {
                    nestedGraph.Groups.Add(CloneGroupData(group));
                }
            }
        }

        private void UpdateParentGroupsAfterSubGraphConversion(HashSet<string> selectedNodeIds, string wrapperNodeId)
        {
            List<GenGroupData> groups = _graph.Groups ?? new List<GenGroupData>();
            for (int groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
            {
                GenGroupData group = groups[groupIndex];
                if (group == null || group.ContainedNodeIds == null)
                {
                    continue;
                }

                int originalCount = group.ContainedNodeIds.Count;
                group.ContainedNodeIds.RemoveAll(nodeId => selectedNodeIds.Contains(nodeId ?? string.Empty));
                if (group.ContainedNodeIds.Count == 0 && originalCount > 0)
                {
                    groups.RemoveAt(groupIndex);
                }
                else if (group.ContainedNodeIds.Count != originalCount && !group.ContainedNodeIds.Contains(wrapperNodeId))
                {
                    group.ContainedNodeIds.Add(wrapperNodeId);
                }
            }
        }

        private GenPortData FindPortData(string nodeId, string portName)
        {
            GenNodeData node = _graph.GetNode(nodeId);
            return FindPortData(node, portName);
        }

        private static GenPortData FindPortData(GenNodeData node, string portName)
        {
            List<GenPortData> ports = node != null ? node.Ports : null;
            if (ports == null)
            {
                return null;
            }

            for (int portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                GenPortData port = ports[portIndex];
                if (port != null && string.Equals(port.PortName, portName, StringComparison.Ordinal))
                {
                    return port;
                }
            }

            return null;
        }

        private static string CreateUniqueBoundaryPortName(string prefix, HashSet<string> usedNames)
        {
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "Port" : prefix.Trim();
            if (usedNames.Add(safePrefix))
            {
                return safePrefix;
            }

            int index = 2;
            string candidate;
            do
            {
                candidate = safePrefix + " " + index.ToString();
                index++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
        }

        private static string CreateSourcePortKey(string nodeId, string portName)
        {
            return (nodeId ?? string.Empty) + "\n" + (portName ?? string.Empty);
        }

        private static string ResolveBoundaryPortBaseName(GenPortData port, string fallback)
        {
            if (port == null)
            {
                return fallback;
            }

            if (!string.IsNullOrWhiteSpace(port.DisplayName))
            {
                return port.DisplayName;
            }

            return string.IsNullOrWhiteSpace(port.PortName) ? fallback : port.PortName;
        }

        private static string ResolveSubGraphName(IReadOnlyList<GenNodeView> selectedNodeViews)
        {
            if (selectedNodeViews != null && selectedNodeViews.Count == 1 && selectedNodeViews[0] != null)
            {
                string nodeName = selectedNodeViews[0].NodeData != null ? selectedNodeViews[0].NodeData.NodeName : selectedNodeViews[0].title;
                if (!string.IsNullOrWhiteSpace(nodeName))
                {
                    return nodeName + " Subgraph";
                }
            }

            return "Subgraph";
        }

        private static string MakeSafeAssetFileName(string name)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? "Subgraph" : name.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int charIndex = 0; charIndex < invalidChars.Length; charIndex++)
            {
                safeName = safeName.Replace(invalidChars[charIndex], '_');
            }

            return string.IsNullOrWhiteSpace(safeName) ? "Subgraph" : safeName;
        }

        private static void CopyGraphSettings(GenGraph source, GenGraph target)
        {
            target.SchemaVersion = source.SchemaVersion;
            target.WorldWidth = source.WorldWidth;
            target.WorldHeight = source.WorldHeight;
            target.DefaultSeed = source.DefaultSeed;
            target.DefaultSeedMode = source.DefaultSeedMode;
            target.MaxValidationRetries = source.MaxValidationRetries;
            target.Biome = source.Biome;
            target.TileSemanticRegistry = source.TileSemanticRegistry;
            target.PromoteBlackboardToParentScope = source.PromoteBlackboardToParentScope;
            target.ExposedProperties = CloneExposedProperties(source.ExposedProperties);
        }

        private static List<ExposedProperty> CloneExposedProperties(IReadOnlyList<ExposedProperty> source)
        {
            List<ExposedProperty> properties = new List<ExposedProperty>();
            if (source == null)
            {
                return properties;
            }

            for (int propertyIndex = 0; propertyIndex < source.Count; propertyIndex++)
            {
                ExposedProperty property = source[propertyIndex];
                if (property == null)
                {
                    continue;
                }

                properties.Add(new ExposedProperty
                {
                    PropertyId = property.PropertyId,
                    PropertyName = property.PropertyName,
                    Type = property.Type,
                    DefaultValue = property.DefaultValue,
                    Description = property.Description
                });
            }

            return properties;
        }

        private static GenNodeData CloneNodeData(GenNodeData source)
        {
            GenNodeData clone = new GenNodeData(source.NodeId, source.NodeTypeName, source.NodeName, source.Position);
            clone.Ports = ClonePorts(source.Ports);
            clone.Parameters = CloneParameters(source.Parameters);
            return clone;
        }

        private static List<GenPortData> ClonePorts(IReadOnlyList<GenPortData> source)
        {
            List<GenPortData> ports = new List<GenPortData>();
            if (source == null)
            {
                return ports;
            }

            for (int portIndex = 0; portIndex < source.Count; portIndex++)
            {
                GenPortData port = source[portIndex];
                if (port != null)
                {
                    ports.Add(new GenPortData(port.PortName, port.Direction, port.Type, port.DisplayName));
                }
            }

            return ports;
        }

        private static List<SerializedParameter> CloneParameters(IReadOnlyList<SerializedParameter> source)
        {
            List<SerializedParameter> parameters = new List<SerializedParameter>();
            if (source == null)
            {
                return parameters;
            }

            for (int parameterIndex = 0; parameterIndex < source.Count; parameterIndex++)
            {
                SerializedParameter parameter = source[parameterIndex];
                if (parameter != null)
                {
                    parameters.Add(new SerializedParameter(parameter.Name, parameter.Value, parameter.ObjectReference));
                }
            }

            return parameters;
        }

        private static GenConnectionData CloneConnectionData(GenConnectionData source)
        {
            GenConnectionData clone = new GenConnectionData(
                source.FromNodeId,
                source.FromPortName,
                source.ToNodeId,
                source.ToPortName);
            clone.CastMode = source.CastMode;
            return clone;
        }

        private static GenGroupData CloneGroupData(GenGroupData source)
        {
            GenGroupData clone = new GenGroupData();
            clone.GroupId = source.GroupId;
            clone.Title = source.Title;
            clone.Position = source.Position;
            clone.BackgroundColor = source.BackgroundColor;
            clone.ContainedNodeIds = source.ContainedNodeIds != null
                ? new List<string>(source.ContainedNodeIds)
                : new List<string>();
            return clone;
        }

        private List<GenNodeView> ResolveAutoLayoutNodeSelection()
        {
            List<GenNodeView> selectedNodeViews = GetSelectedNodeViews();
            HashSet<string> selectedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            List<GenNodeView> result = new List<GenNodeView>();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < selectedNodeViews.Count; nodeIndex++)
            {
                GenNodeView nodeView = selectedNodeViews[nodeIndex];
                if (nodeView != null && selectedNodeIds.Add(nodeView.NodeData.NodeId ?? string.Empty))
                {
                    result.Add(nodeView);
                }
            }

            List<GroupView> selectedGroupViews = GetSelectedGroupViews();
            int groupIndex;
            for (groupIndex = 0; groupIndex < selectedGroupViews.Count; groupIndex++)
            {
                GroupView groupView = selectedGroupViews[groupIndex];
                if (groupView == null)
                {
                    continue;
                }

                foreach (GraphElement element in groupView.containedElements)
                {
                    GenNodeView nodeView = element as GenNodeView;
                    if (nodeView != null && selectedNodeIds.Add(nodeView.NodeData.NodeId ?? string.Empty))
                    {
                        result.Add(nodeView);
                    }
                }
            }

            return result;
        }

        private List<GenNodeView> SortNodesByDependency(List<GenNodeView> nodeViews)
        {
            Dictionary<string, GenNodeView> nodeViewsById = new Dictionary<string, GenNodeView>(StringComparer.Ordinal);
            Dictionary<string, int> incomingCountsByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, List<string>> outgoingByNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodeViews.Count; nodeIndex++)
            {
                GenNodeView nodeView = nodeViews[nodeIndex];
                string nodeId = nodeView.NodeData.NodeId ?? string.Empty;
                nodeViewsById[nodeId] = nodeView;
                incomingCountsByNodeId[nodeId] = 0;
                outgoingByNodeId[nodeId] = new List<string>();
            }

            if (_graph != null && _graph.Connections != null)
            {
                int connectionIndex;
                for (connectionIndex = 0; connectionIndex < _graph.Connections.Count; connectionIndex++)
                {
                    GenConnectionData connection = _graph.Connections[connectionIndex];
                    if (connection == null ||
                        !nodeViewsById.ContainsKey(connection.FromNodeId ?? string.Empty) ||
                        !nodeViewsById.ContainsKey(connection.ToNodeId ?? string.Empty))
                    {
                        continue;
                    }

                    outgoingByNodeId[connection.FromNodeId].Add(connection.ToNodeId);
                    incomingCountsByNodeId[connection.ToNodeId] = incomingCountsByNodeId[connection.ToNodeId] + 1;
                }
            }

            Queue<string> ready = new Queue<string>();
            foreach (KeyValuePair<string, int> entry in incomingCountsByNodeId)
            {
                if (entry.Value == 0)
                {
                    ready.Enqueue(entry.Key);
                }
            }

            List<GenNodeView> ordered = new List<GenNodeView>();
            while (ready.Count > 0)
            {
                string nodeId = ready.Dequeue();
                GenNodeView nodeView;
                if (nodeViewsById.TryGetValue(nodeId, out nodeView))
                {
                    ordered.Add(nodeView);
                }

                List<string> outgoing;
                if (!outgoingByNodeId.TryGetValue(nodeId, out outgoing))
                {
                    continue;
                }

                int outgoingIndex;
                for (outgoingIndex = 0; outgoingIndex < outgoing.Count; outgoingIndex++)
                {
                    string toNodeId = outgoing[outgoingIndex];
                    incomingCountsByNodeId[toNodeId] = incomingCountsByNodeId[toNodeId] - 1;
                    if (incomingCountsByNodeId[toNodeId] == 0)
                    {
                        ready.Enqueue(toNodeId);
                    }
                }
            }

            if (ordered.Count < nodeViews.Count)
            {
                for (nodeIndex = 0; nodeIndex < nodeViews.Count; nodeIndex++)
                {
                    GenNodeView nodeView = nodeViews[nodeIndex];
                    if (!ordered.Contains(nodeView))
                    {
                        ordered.Add(nodeView);
                    }
                }
            }

            return ordered;
        }

        private Dictionary<string, int> CalculateDependencyColumns(List<GenNodeView> orderedNodes)
        {
            HashSet<string> selectedNodeIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> columnsByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < orderedNodes.Count; nodeIndex++)
            {
                string nodeId = orderedNodes[nodeIndex].NodeData.NodeId ?? string.Empty;
                selectedNodeIds.Add(nodeId);
                columnsByNodeId[nodeId] = 0;
            }

            if (_graph == null || _graph.Connections == null)
            {
                return columnsByNodeId;
            }

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < _graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connection = _graph.Connections[connectionIndex];
                if (connection == null ||
                    !selectedNodeIds.Contains(connection.FromNodeId ?? string.Empty) ||
                    !selectedNodeIds.Contains(connection.ToNodeId ?? string.Empty))
                {
                    continue;
                }

                int fromColumn;
                columnsByNodeId.TryGetValue(connection.FromNodeId, out fromColumn);
                int toColumn;
                columnsByNodeId.TryGetValue(connection.ToNodeId, out toColumn);
                columnsByNodeId[connection.ToNodeId] = Mathf.Max(toColumn, fromColumn + 1);
            }

            return columnsByNodeId;
        }

        private void RefreshSelectedGroupBounds(List<GenNodeView> autoLayoutNodeViews)
        {
            HashSet<string> autoLayoutNodeIds = new HashSet<string>(StringComparer.Ordinal);
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < autoLayoutNodeViews.Count; nodeIndex++)
            {
                autoLayoutNodeIds.Add(autoLayoutNodeViews[nodeIndex].NodeData.NodeId ?? string.Empty);
            }

            List<GroupView> selectedGroups = GetSelectedGroupViews();
            int groupIndex;
            for (groupIndex = 0; groupIndex < selectedGroups.Count; groupIndex++)
            {
                GroupView groupView = selectedGroups[groupIndex];
                if (groupView == null)
                {
                    continue;
                }

                List<GenNodeView> groupNodes = groupView.containedElements
                    .OfType<GenNodeView>()
                    .Where(nodeView => autoLayoutNodeIds.Contains(nodeView.NodeData.NodeId ?? string.Empty))
                    .ToList();
                if (groupNodes.Count == 0)
                {
                    continue;
                }

                Rect groupRect = CalculateGroupRect(groupNodes);
                groupView.SetPosition(groupRect);
            }
        }

        private List<GroupView> GetContainingGroups(GraphElement graphElement)
        {
            List<GroupView> containingGroups = new List<GroupView>();

            foreach (GroupView groupView in _groupViewsById.Values)
            {
                if (groupView != null && groupView.ContainsElement(graphElement))
                {
                    containingGroups.Add(groupView);
                }
            }

            return containingGroups;
        }

        private void SetSelectionCollapsedState(bool isCollapsed)
        {
            List<GenNodeView> selectedNodeViews = GetSelectedNodeViews();
            if (selectedNodeViews.Count == 0)
            {
                return;
            }

            foreach (GenNodeView nodeView in selectedNodeViews)
            {
                nodeView.SetCollapsed(isCollapsed);
            }
        }

        private static Rect CalculateGroupRect(IReadOnlyList<GenNodeView> nodeViews)
        {
            const float padding = 40.0f;

            if (nodeViews == null || nodeViews.Count == 0)
            {
                return new Rect(0.0f, 0.0f, DefaultGroupWidth, DefaultGroupHeight);
            }

            Rect selectionBounds = nodeViews[0].GetPosition();
            for (int nodeIndex = 1; nodeIndex < nodeViews.Count; nodeIndex++)
            {
                selectionBounds = UnionRect(selectionBounds, nodeViews[nodeIndex].GetPosition());
            }

            selectionBounds.xMin -= padding;
            selectionBounds.yMin -= padding;
            selectionBounds.xMax += padding;
            selectionBounds.yMax += padding;
            return selectionBounds;
        }

        private static Rect CalculateNodeViewsRect(IReadOnlyList<GenNodeView> nodeViews)
        {
            if (nodeViews == null || nodeViews.Count == 0)
            {
                return new Rect(0.0f, 0.0f, DefaultGroupWidth, DefaultGroupHeight);
            }

            Rect bounds = nodeViews[0].GetPosition();
            int nodeIndex;
            for (nodeIndex = 1; nodeIndex < nodeViews.Count; nodeIndex++)
            {
                bounds = UnionRect(bounds, nodeViews[nodeIndex].GetPosition());
            }

            return bounds;
        }

        private static Rect UnionRect(Rect first, Rect second)
        {
            float xMin = Mathf.Min(first.xMin, second.xMin);
            float yMin = Mathf.Min(first.yMin, second.yMin);
            float xMax = Mathf.Max(first.xMax, second.xMax);
            float yMax = Mathf.Max(first.yMax, second.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static void CollectConnectedEdges(
            VisualElement portContainer,
            ICollection<GraphElement> edgesToRemove,
            ICollection<GraphElement> existingElementsToRemove)
        {
            if (portContainer == null)
            {
                return;
            }

            foreach (Port port in portContainer.Children().OfType<Port>())
            {
                if (port.connections == null)
                {
                    continue;
                }

                foreach (Edge edge in port.connections)
                {
                    if (edge == null ||
                        edgesToRemove.Contains(edge) ||
                        existingElementsToRemove.Contains(edge))
                    {
                        continue;
                    }

                    edgesToRemove.Add(edge);
                }
            }
        }

        private void OnElementsAddedToGroup(Group group, System.Collections.Generic.IEnumerable<GraphElement> elements)
        {
            if (_suppressGraphMutationCallbacks || _graph == null)
            {
                return;
            }

            GroupView groupView = group as GroupView;
            if (groupView == null)
            {
                return;
            }

            GenGroupData groupData = _graph.GetGroup(groupView.GroupId);
            if (groupData == null)
            {
                return;
            }

            bool changed = false;
            Undo.RecordObject(_graph, "Add Nodes to Group");

            foreach (GraphElement element in elements)
            {
                GenNodeView nodeView = element as GenNodeView;
                if (nodeView == null || nodeView.NodeData == null || string.IsNullOrEmpty(nodeView.NodeData.NodeId))
                {
                    continue;
                }

                string nodeId = nodeView.NodeData.NodeId;
                if (!groupData.ContainedNodeIds.Contains(nodeId))
                {
                    groupData.ContainedNodeIds.Add(nodeId);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(_graph);
                NotifyGraphMutated();
            }
        }

        private void OnElementsRemovedFromGroup(Group group, System.Collections.Generic.IEnumerable<GraphElement> elements)
        {
            if (_suppressGraphMutationCallbacks || _graph == null)
            {
                return;
            }

            GroupView groupView = group as GroupView;
            if (groupView == null)
            {
                return;
            }

            if (!_groupViewsById.ContainsKey(groupView.GroupId))
            {
                return;
            }

            GenGroupData groupData = _graph.GetGroup(groupView.GroupId);
            if (groupData == null)
            {
                return;
            }

            bool changed = false;
            Undo.RecordObject(_graph, "Remove Nodes from Group");

            foreach (GraphElement element in elements)
            {
                GenNodeView nodeView = element as GenNodeView;
                if (nodeView == null || nodeView.NodeData == null || string.IsNullOrEmpty(nodeView.NodeData.NodeId))
                {
                    continue;
                }

                if (groupData.ContainedNodeIds.Remove(nodeView.NodeData.NodeId))
                {
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(_graph);
                NotifyGraphMutated();
            }
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
                nodeData.Ports.Add(new GenPortData(portDefinition.Name, portDefinition.Direction, portDefinition.Type, portDefinition.DisplayName));
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

        private static Color ResolveOutputEdgeColour(Port fromPortView)
        {
            return GenPortUtility.GetPortColour(fromPortView);
        }

        private static Color ResolveInputEdgeColour(Port fromPortView, Port toPortView, bool isCastEdge)
        {
            if (!isCastEdge)
            {
                return GenPortUtility.GetPortColour(fromPortView);
            }

            return GenPortUtility.GetPortColour(toPortView);
        }

        private void OpenExpandedPreview(string nodeId, Texture2D texture, string titleText)
        {
            if (texture == null)
            {
                HideExpandedPreview();
                return;
            }

            EnsureExpandedPreviewOverlayAttached();
            EndExpandedPreviewPan();
            _expandedPreviewPanOffset = Vector2.zero;
            _expandedPreviewPanOffsetAtDragStart = Vector2.zero;
            _expandedPreviewPanMousePositionAtDragStart = Vector2.zero;
            _expandedPreviewLastAutoFitViewportSize = new Vector2(-1.0f, -1.0f);
            SetExpandedPreviewContent(nodeId, texture, titleText);
            _expandedPreviewOverlay.style.display = DisplayStyle.Flex;
            _expandedPreviewOverlay.BringToFront();
            _expandedPreviewNeedsFit = true;
            _expandedPreviewAutoFitUntilInteraction = true;
            Focus();
            EnsureExpandedPreviewDeferredFitScheduled();
        }

        private void RefreshExpandedPreview(string nodeId, Texture2D texture, string titleText)
        {
            if (texture == null)
            {
                HideExpandedPreview();
                return;
            }

            bool isVisibleSameNode =
                IsExpandedPreviewVisible() &&
                string.Equals(_expandedPreviewNodeId, nodeId, StringComparison.Ordinal) &&
                _expandedPreviewImage.image != null;
            bool shouldFit = _expandedPreviewNeedsFit ||
                             _expandedPreviewAutoFitUntilInteraction ||
                             !isVisibleSameNode;

            EnsureExpandedPreviewOverlayAttached();
            SetExpandedPreviewContent(nodeId, texture, titleText);
            _expandedPreviewOverlay.style.display = DisplayStyle.Flex;
            _expandedPreviewOverlay.BringToFront();
            _expandedPreviewNeedsFit = shouldFit;
            if (shouldFit)
            {
                _expandedPreviewAutoFitUntilInteraction = true;
                if (!isVisibleSameNode)
                {
                    _expandedPreviewLastAutoFitViewportSize = new Vector2(-1.0f, -1.0f);
                }
            }
            Focus();
            if (shouldFit)
            {
                EnsureExpandedPreviewDeferredFitScheduled();
                return;
            }

            UpdateExpandedPreviewTransformIfNeeded();
        }

        private void HideExpandedPreview()
        {
            _expandedPreviewOverlay.style.display = DisplayStyle.None;
            DestroyExpandedPreviewTexture();
            _expandedPreviewImage.image = null;
            _expandedPreviewTitle.text = string.Empty;
            _expandedPreviewNodeId = null;
            _expandedPreviewPanOffset = Vector2.zero;
            _expandedPreviewPanOffsetAtDragStart = Vector2.zero;
            _expandedPreviewPanMousePositionAtDragStart = Vector2.zero;
            _expandedPreviewLastAutoFitViewportSize = new Vector2(-1.0f, -1.0f);
            _expandedPreviewZoom = 1.0f;
            _expandedPreviewNeedsFit = false;
            _expandedPreviewAutoFitUntilInteraction = false;
            _isPanningExpandedPreview = false;
            PauseExpandedPreviewDeferredFitSchedule();

            if (_expandedPreviewViewport.HasMouseCapture())
            {
                _expandedPreviewViewport.ReleaseMouse();
            }

            if (_expandedPreviewOverlay.HasMouseCapture())
            {
                _expandedPreviewOverlay.ReleaseMouse();
            }
        }

        private void HideExpandedPreviewIfShowing(string nodeId)
        {
            if (!string.IsNullOrWhiteSpace(nodeId) && string.Equals(_expandedPreviewNodeId, nodeId, StringComparison.Ordinal))
            {
                HideExpandedPreview();
            }
        }

        private void EnsureExpandedPreviewOverlayAttached()
        {
            VisualElement desiredParent = parent;
            if (desiredParent == null)
            {
                desiredParent = this;
            }

            if (_expandedPreviewOverlay.parent == desiredParent)
            {
                return;
            }

            _expandedPreviewOverlay.RemoveFromHierarchy();
            desiredParent.Add(_expandedPreviewOverlay);
        }

        private void UpdateExpandedPreviewTransformIfNeeded()
        {
            if (_expandedPreviewOverlay.style.display != DisplayStyle.Flex || _expandedPreviewImage.image == null)
            {
                PauseExpandedPreviewDeferredFitSchedule();
                return;
            }

            if (_expandedPreviewViewport.resolvedStyle.width <= 0.0f || _expandedPreviewViewport.resolvedStyle.height <= 0.0f)
            {
                EnsureExpandedPreviewDeferredFitScheduled();
                return;
            }

            Vector2 viewportSize = new Vector2(
                _expandedPreviewViewport.resolvedStyle.width,
                _expandedPreviewViewport.resolvedStyle.height);
            bool viewportSizeChangedSinceLastAutoFit =
                _expandedPreviewLastAutoFitViewportSize.x < 0.0f ||
                _expandedPreviewLastAutoFitViewportSize.y < 0.0f ||
                !Mathf.Approximately(_expandedPreviewLastAutoFitViewportSize.x, viewportSize.x) ||
                !Mathf.Approximately(_expandedPreviewLastAutoFitViewportSize.y, viewportSize.y);

            if (_expandedPreviewNeedsFit || (_expandedPreviewAutoFitUntilInteraction && viewportSizeChangedSinceLastAutoFit))
            {
                _expandedPreviewZoom = CalculateExpandedPreviewFitZoom(
                    viewportSize.x,
                    viewportSize.y,
                    _expandedPreviewTexture.width,
                    _expandedPreviewTexture.height);
                _expandedPreviewPanOffset = Vector2.zero;
                _expandedPreviewNeedsFit = false;
                _expandedPreviewLastAutoFitViewportSize = viewportSize;
            }

            ApplyExpandedPreviewTransform();
            PauseExpandedPreviewDeferredFitSchedule();
        }

        private void ApplyExpandedPreviewTransform()
        {
            Texture texture = _expandedPreviewTexture;
            if (texture == null)
            {
                return;
            }

            float scaledWidth = texture.width * _expandedPreviewZoom;
            float scaledHeight = texture.height * _expandedPreviewZoom;
            float baseX = (_expandedPreviewViewport.resolvedStyle.width - scaledWidth) * 0.5f;
            float baseY = (_expandedPreviewViewport.resolvedStyle.height - scaledHeight) * 0.5f;

            _expandedPreviewImage.style.width = scaledWidth;
            _expandedPreviewImage.style.height = scaledHeight;
            _expandedPreviewImage.style.translate = new Translate(baseX + _expandedPreviewPanOffset.x, baseY + _expandedPreviewPanOffset.y, 0.0f);
        }

        private void BeginExpandedPreviewPan(Vector2 viewportLocalMousePosition)
        {
            _expandedPreviewAutoFitUntilInteraction = false;
            _isPanningExpandedPreview = true;
            _expandedPreviewPanMousePositionAtDragStart = viewportLocalMousePosition;
            _expandedPreviewPanOffsetAtDragStart = _expandedPreviewPanOffset;
            _expandedPreviewOverlay.CaptureMouse();
            _expandedPreviewViewport.CaptureMouse();
        }

        private void UpdateExpandedPreviewPan(Vector2 viewportLocalMousePosition)
        {
            _expandedPreviewPanOffset = _expandedPreviewPanOffsetAtDragStart + (viewportLocalMousePosition - _expandedPreviewPanMousePositionAtDragStart);
            ApplyExpandedPreviewTransform();
        }

        private void EndExpandedPreviewPan()
        {
            _isPanningExpandedPreview = false;
            if (_expandedPreviewOverlay.HasMouseCapture())
            {
                _expandedPreviewOverlay.ReleaseMouse();
            }

            if (_expandedPreviewViewport.HasMouseCapture())
            {
                _expandedPreviewViewport.ReleaseMouse();
            }
        }

        private Vector2 ToExpandedPreviewViewportLocal(Vector2 overlayLocalMousePosition)
        {
            Vector2 worldMousePosition = _expandedPreviewOverlay.LocalToWorld(overlayLocalMousePosition);
            return _expandedPreviewViewport.WorldToLocal(worldMousePosition);
        }

        private static bool IsExpandedPreviewPanButton(int mouseButton)
        {
            return mouseButton == 0 || mouseButton == 2;
        }

        private void UpdateExpandedPreviewForCurrentNode(string nodeId, Texture2D texture, string titleText)
        {
            if (!string.Equals(_expandedPreviewNodeId, nodeId, StringComparison.Ordinal))
            {
                return;
            }

            if (texture == null)
            {
                HideExpandedPreview();
                return;
            }

            if (IsExpandedPreviewVisible())
            {
                RefreshExpandedPreview(nodeId, texture, titleText);
                return;
            }

            OpenExpandedPreview(nodeId, texture, titleText);
        }

        private void SetExpandedPreviewContent(string nodeId, Texture2D texture, string titleText)
        {
            _expandedPreviewNodeId = nodeId ?? string.Empty;
            ReplaceExpandedPreviewTexture(texture);
            _expandedPreviewImage.image = _expandedPreviewTexture;
            _expandedPreviewTitle.text = string.IsNullOrWhiteSpace(titleText) ? "Preview" : titleText;
        }

        private bool IsExpandedPreviewVisible()
        {
            return _expandedPreviewOverlay.style.display == DisplayStyle.Flex;
        }

        internal static float CalculateExpandedPreviewFitZoom(float viewportWidth, float viewportHeight, float textureWidth, float textureHeight)
        {
            if (viewportWidth <= 0.0f || viewportHeight <= 0.0f || textureWidth <= 0.0f || textureHeight <= 0.0f)
            {
                return 1.0f;
            }

            float zoomByWidth = viewportWidth / textureWidth;
            float zoomByHeight = viewportHeight / textureHeight;
            return Mathf.Clamp(Mathf.Min(zoomByWidth, zoomByHeight), MinExpandedPreviewZoom, MaxExpandedPreviewZoom);
        }

        internal void OpenExpandedPreviewForTesting(string nodeId, Texture2D texture, string titleText)
        {
            OpenExpandedPreview(nodeId, texture, titleText);
        }

        internal void CreateExposedPropertyNodeFromBlackboardForTesting(string propertyId, Vector2 graphLocalPosition)
        {
            CreateExposedPropertyNodeFromBlackboard(propertyId, graphLocalPosition);
        }

        internal void UpdateExpandedPreviewForCurrentNodeForTesting(string nodeId, Texture2D texture, string titleText)
        {
            UpdateExpandedPreviewForCurrentNode(nodeId, texture, titleText);
        }

        internal void SetExpandedPreviewTransformStateForTesting(float zoom, Vector2 panOffset, bool needsFit)
        {
            _expandedPreviewZoom = zoom;
            _expandedPreviewPanOffset = panOffset;
            _expandedPreviewNeedsFit = needsFit;
            _expandedPreviewAutoFitUntilInteraction = needsFit;
        }

        internal bool IsExpandedPreviewVisibleForTesting
        {
            get
            {
                return IsExpandedPreviewVisible();
            }
        }

        internal string ExpandedPreviewNodeIdForTesting
        {
            get
            {
                return _expandedPreviewNodeId;
            }
        }

        internal float ExpandedPreviewZoomForTesting
        {
            get
            {
                return _expandedPreviewZoom;
            }
        }

        internal Vector2 ExpandedPreviewPanOffsetForTesting
        {
            get
            {
                return _expandedPreviewPanOffset;
            }
        }

        internal bool ExpandedPreviewNeedsFitForTesting
        {
            get
            {
                return _expandedPreviewNeedsFit;
            }
        }

        private void HandleExpandedPreviewWheel(Vector2 viewportLocalMousePosition, float wheelDeltaY)
        {
            if (_expandedPreviewTexture == null)
            {
                return;
            }

            _expandedPreviewAutoFitUntilInteraction = false;
            float zoomFactor = wheelDeltaY < 0.0f ? ExpandedPreviewZoomStep : 1.0f / ExpandedPreviewZoomStep;
            float newZoom = Mathf.Clamp(_expandedPreviewZoom * zoomFactor, MinExpandedPreviewZoom, MaxExpandedPreviewZoom);
            if (Mathf.Approximately(newZoom, _expandedPreviewZoom))
            {
                return;
            }

            float oldScaledWidth = _expandedPreviewTexture.width * _expandedPreviewZoom;
            float oldScaledHeight = _expandedPreviewTexture.height * _expandedPreviewZoom;
            float oldBaseX = (_expandedPreviewViewport.resolvedStyle.width - oldScaledWidth) * 0.5f;
            float oldBaseY = (_expandedPreviewViewport.resolvedStyle.height - oldScaledHeight) * 0.5f;
            Vector2 oldTopLeft = new Vector2(oldBaseX, oldBaseY) + _expandedPreviewPanOffset;
            Vector2 textureSpacePoint = (viewportLocalMousePosition - oldTopLeft) / _expandedPreviewZoom;

            _expandedPreviewZoom = newZoom;

            float newScaledWidth = _expandedPreviewTexture.width * _expandedPreviewZoom;
            float newScaledHeight = _expandedPreviewTexture.height * _expandedPreviewZoom;
            float newBaseX = (_expandedPreviewViewport.resolvedStyle.width - newScaledWidth) * 0.5f;
            float newBaseY = (_expandedPreviewViewport.resolvedStyle.height - newScaledHeight) * 0.5f;
            Vector2 newTopLeft = viewportLocalMousePosition - (textureSpacePoint * _expandedPreviewZoom);
            _expandedPreviewPanOffset = newTopLeft - new Vector2(newBaseX, newBaseY);

            ApplyExpandedPreviewTransform();
        }

        private void OnDragUpdated(DragUpdatedEvent dragUpdatedEvent)
        {
            if (!HasBlackboardPropertyDrag() && !HasGenGraphAssetDrag())
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dragUpdatedEvent.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent dragPerformEvent)
        {
            string propertyId = DragAndDrop.GetGenericData(BlackboardPropertyDragDataKey) as string;
            if (!string.IsNullOrWhiteSpace(propertyId))
            {
                DragAndDrop.AcceptDrag();
                CreateExposedPropertyNodeFromBlackboard(propertyId, dragPerformEvent.localMousePosition);
                DragAndDrop.SetGenericData(BlackboardPropertyDragDataKey, null);
                dragPerformEvent.StopPropagation();
                return;
            }

            GenGraph draggedGraph = GetDraggedGenGraphAsset();
            if (draggedGraph == null)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            CreateSubGraphNodeFromDroppedGraph(draggedGraph, dragPerformEvent.localMousePosition);
            dragPerformEvent.StopPropagation();
        }

        private static bool HasBlackboardPropertyDrag()
        {
            return !string.IsNullOrWhiteSpace(
                DragAndDrop.GetGenericData(BlackboardPropertyDragDataKey) as string);
        }

        private static bool HasGenGraphAssetDrag()
        {
            return GetDraggedGenGraphAsset() != null;
        }

        private static GenGraph GetDraggedGenGraphAsset()
        {
            UnityEngine.Object[] objectReferences = DragAndDrop.objectReferences;
            if (objectReferences == null)
            {
                return null;
            }

            for (int objectIndex = 0; objectIndex < objectReferences.Length; objectIndex++)
            {
                GenGraph graph = objectReferences[objectIndex] as GenGraph;
                if (graph != null)
                {
                    return graph;
                }
            }

            return null;
        }

        private void EnsureExpandedPreviewDeferredFitScheduled()
        {
            if (!_expandedPreviewNeedsFit)
            {
                return;
            }

            if (_expandedPreviewDeferredFitSchedule == null)
            {
                _expandedPreviewDeferredFitSchedule = schedule.Execute(UpdateExpandedPreviewTransformIfNeeded).Every(16);
                return;
            }

            _expandedPreviewDeferredFitSchedule.Resume();
        }

        private void PauseExpandedPreviewDeferredFitSchedule()
        {
            _expandedPreviewDeferredFitSchedule?.Pause();
        }

        private void ReplaceExpandedPreviewTexture(Texture2D sourceTexture)
        {
            DestroyExpandedPreviewTexture();

            if (sourceTexture == null)
            {
                _expandedPreviewTexture = null;
                return;
            }

            _expandedPreviewTexture = UnityEngine.Object.Instantiate(sourceTexture);
            _expandedPreviewTexture.name = sourceTexture.name + "_ExpandedPreview";
            _expandedPreviewTexture.hideFlags = HideFlags.HideAndDontSave;
        }

        private void DestroyExpandedPreviewTexture()
        {
            if (_expandedPreviewTexture == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(_expandedPreviewTexture);
            _expandedPreviewTexture = null;
        }

        private void ApplyCastModeChange(Edge edge, CastMode newMode)
        {
            if (_graph == null || edge == null)
            {
                return;
            }

            GenEdgeView genEdge = edge as GenEdgeView;
            GenConnectionData userData = edge.userData as GenConnectionData;
            if (userData == null)
            {
                return;
            }

            GenConnectionData graphConnection = FindConnectionInGraph(
                userData.FromNodeId,
                userData.FromPortName,
                userData.ToNodeId,
                userData.ToPortName);

            if (graphConnection == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Change Cast Mode");
            graphConnection.CastMode = newMode;
            userData.CastMode = newMode;
            EditorUtility.SetDirty(_graph);

            if (genEdge != null)
            {
                genEdge.ApplyCastMode(newMode);
            }

            // Force a full recompile so the new cast mode is reflected in the implicit cast node.
            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.RequestPreviewRefresh();
            }

            NotifyGraphMutated();
        }

        private GenConnectionData FindConnectionInGraph(
            string fromNodeId,
            string fromPortName,
            string toNodeId,
            string toPortName)
        {
            if (_graph == null || _graph.Connections == null)
            {
                return null;
            }

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < _graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connection = _graph.Connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                if (string.Equals(connection.FromNodeId, fromNodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.FromPortName, fromPortName, StringComparison.Ordinal) &&
                    string.Equals(connection.ToNodeId, toNodeId, StringComparison.Ordinal) &&
                    string.Equals(connection.ToPortName, toPortName, StringComparison.Ordinal))
                {
                    return connection;
                }
            }

            return null;
        }

        private static void GetPortTypesFromCastMode(CastMode mode, out ChannelType fromType, out ChannelType toType)
        {
            switch (mode)
            {
                case CastMode.FloatToIntFloor:
                case CastMode.FloatToIntRound:
                    fromType = ChannelType.Float;
                    toType = ChannelType.Int;
                    return;
                case CastMode.FloatToBoolMask:
                    fromType = ChannelType.Float;
                    toType = ChannelType.BoolMask;
                    return;
                case CastMode.IntToBoolMask:
                    fromType = ChannelType.Int;
                    toType = ChannelType.BoolMask;
                    return;
                default:
                    fromType = ChannelType.Float;
                    toType = ChannelType.Float;
                    return;
            }
        }

        private static List<CastMode> GetValidCastModesForPortPair(ChannelType fromType, ChannelType toType)
        {
            List<CastMode> modes = new List<CastMode>();

            if (fromType == ChannelType.Float && toType == ChannelType.Int)
            {
                modes.Add(CastMode.FloatToIntFloor);
                modes.Add(CastMode.FloatToIntRound);
                return modes;
            }

            if (fromType == ChannelType.Float && toType == ChannelType.BoolMask)
            {
                modes.Add(CastMode.FloatToBoolMask);
                return modes;
            }

            if (fromType == ChannelType.Int && toType == ChannelType.BoolMask)
            {
                modes.Add(CastMode.IntToBoolMask);
                return modes;
            }

            return modes;
        }

        private static string GetCastModeDisplayName(CastMode mode)
        {
            switch (mode)
            {
                case CastMode.FloatToIntFloor:
                    return "Floor";
                case CastMode.FloatToIntRound:
                    return "Round";
                case CastMode.FloatToBoolMask:
                    return "Float to Bool Mask";
                case CastMode.IntToBoolMask:
                    return "Int to Bool Mask";
                default:
                    return mode.ToString();
            }
        }
    }
}
