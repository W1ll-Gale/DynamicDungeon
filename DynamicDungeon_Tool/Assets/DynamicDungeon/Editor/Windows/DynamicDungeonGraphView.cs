using System;
using System.Collections.Generic;
using System.Linq;
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
            scrollOffset = contentViewContainer.resolvedStyle.translate;
            zoomScale = contentViewContainer.resolvedStyle.scale.value.x;
        }

        /// <summary>
        /// Restores a previously saved canvas scroll offset and zoom scale.
        /// Used when the user navigates back to a parent graph via the breadcrumb.
        /// </summary>
        public void RestoreViewportState(Vector3 scrollOffset, float zoomScale)
        {
            float safeZoom = Mathf.Clamp(
                zoomScale,
                ContentZoomer.DefaultMinScale,
                ContentZoomer.DefaultMaxScale);

            UpdateViewTransform(scrollOffset, new Vector3(safeZoom, safeZoom, 1.0f));
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

                UpdateExpandedPreviewForCurrentNode(nodeId, texture, nodeView.title);

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

            return selectedGroups;
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
            if (!HasBlackboardPropertyDrag())
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dragUpdatedEvent.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent dragPerformEvent)
        {
            string propertyId = DragAndDrop.GetGenericData(BlackboardPropertyDragDataKey) as string;
            if (string.IsNullOrWhiteSpace(propertyId))
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            CreateExposedPropertyNodeFromBlackboard(propertyId, dragPerformEvent.localMousePosition);
            DragAndDrop.SetGenericData(BlackboardPropertyDragDataKey, null);
            dragPerformEvent.StopPropagation();
        }

        private static bool HasBlackboardPropertyDrag()
        {
            return !string.IsNullOrWhiteSpace(
                DragAndDrop.GetGenericData(BlackboardPropertyDragDataKey) as string);
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
