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

        private NodeSearchWindow _nodeSearchWindow;
        private GenGraph _graph;
        private GenerationOrchestrator _generationOrchestrator;
        private Action _afterMutation;

        // Callback fired when a sub-graph node's Enter button is activated.
        // Arguments: the nested GenGraph to navigate into, the label to show
        // in the breadcrumb bar.
        private Action<GenGraph, string> _onEnterSubGraph;

        private string _expandedPreviewNodeId;
        private Vector2 _expandedPreviewPanOffset;
        private Vector2 _expandedPreviewPanOffsetAtDragStart;
        private Vector2 _expandedPreviewPanMousePositionAtDragStart;
        private float _expandedPreviewZoom = 1.0f;
        private bool _expandedPreviewNeedsFit;
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

                if (_expandedPreviewNodeId == nodeId)
                {
                    if (texture == null)
                    {
                        HideExpandedPreview();
                    }
                    else
                    {
                        ShowExpandedPreview(nodeId, texture, nodeView.title);
                    }
                }

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
            _afterMutation?.Invoke();

            GenNodeView nodeView = CreateNodeView(nodeData, nodeInstance);
            ProtectOutputNode(nodeView);
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
            _afterMutation?.Invoke();

            StickyNoteView noteView = new StickyNoteView(_graph, noteData, _afterMutation);
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
            _afterMutation?.Invoke();

            GroupView groupView = new GroupView(_graph, groupData, _afterMutation);
            _groupViewsById[groupData.GroupId] = groupView;
            AddElement(groupView);
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

                StickyNoteView noteView = new StickyNoteView(_graph, noteData, _afterMutation);
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

                GroupView groupView = new GroupView(_graph, groupData, _afterMutation);
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
                    ShowExpandedPreview,
                    _afterMutation,
                    subGraphAttribute.NestedGraphParameterName,
                    _onEnterSubGraph);
            }

            return new GenNodeView(
                _graph,
                nodeData,
                nodeInstance,
                _generationOrchestrator,
                _edgeConnectorListener,
                ShowExpandedPreview,
                _afterMutation);
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
            if (!_isPanningExpandedPreview || !_expandedPreviewOverlay.HasMouseCapture())
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

        private void OnExpandedPreviewViewportMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (_expandedPreviewImage.image == null || !IsExpandedPreviewPanButton(mouseDownEvent.button))
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
            if (!_isPanningExpandedPreview || !_expandedPreviewOverlay.HasMouseCapture())
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
            Texture texture = _expandedPreviewImage.image;
            if (texture == null)
            {
                return;
            }

            float zoomFactor = wheelEvent.delta.y < 0.0f ? ExpandedPreviewZoomStep : 1.0f / ExpandedPreviewZoomStep;
            float newZoom = Mathf.Clamp(_expandedPreviewZoom * zoomFactor, MinExpandedPreviewZoom, MaxExpandedPreviewZoom);
            if (Mathf.Approximately(newZoom, _expandedPreviewZoom))
            {
                return;
            }

            float oldScaledWidth = texture.width * _expandedPreviewZoom;
            float oldScaledHeight = texture.height * _expandedPreviewZoom;
            float oldBaseX = (_expandedPreviewViewport.resolvedStyle.width - oldScaledWidth) * 0.5f;
            float oldBaseY = (_expandedPreviewViewport.resolvedStyle.height - oldScaledHeight) * 0.5f;
            Vector2 oldTopLeft = new Vector2(oldBaseX, oldBaseY) + _expandedPreviewPanOffset;
            Vector2 textureSpacePoint = (wheelEvent.localMousePosition - oldTopLeft) / _expandedPreviewZoom;

            _expandedPreviewZoom = newZoom;

            float newScaledWidth = texture.width * _expandedPreviewZoom;
            float newScaledHeight = texture.height * _expandedPreviewZoom;
            float newBaseX = (_expandedPreviewViewport.resolvedStyle.width - newScaledWidth) * 0.5f;
            float newBaseY = (_expandedPreviewViewport.resolvedStyle.height - newScaledHeight) * 0.5f;
            Vector2 newTopLeft = wheelEvent.localMousePosition - (textureSpacePoint * _expandedPreviewZoom);
            _expandedPreviewPanOffset = newTopLeft - new Vector2(newBaseX, newBaseY);

            ApplyExpandedPreviewTransform();
            wheelEvent.StopPropagation();
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

                    outputPort.Connect(edge);
                    inputPort.Connect(edge);
                    Color edgeColour = ResolveEdgeColour(outputPort, inputPort, GenPortUtility.RequiresCast(outputPort, inputPort));
                    if (edge.edgeControl != null)
                    {
                        edge.edgeControl.inputColor = edgeColour;
                        edge.edgeControl.outputColor = edgeColour;
                    }

                    outputPort.portColor = GenPortUtility.GetPortColour(outputPort);
                    inputPort.portColor = GenPortUtility.GetPortColour(inputPort);
                    fromNodeView.RefreshPorts();
                    toNodeView.RefreshPorts();
                    ConfigureEdgeCallbacks(edge);
                    validEdges.Add(edge);
                }

                if (recordedUndo)
                {
                    EditorUtility.SetDirty(_graph);
                    _afterMutation?.Invoke();
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
                    _afterMutation?.Invoke();
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
                _afterMutation?.Invoke();
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
                _afterMutation?.Invoke();
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

        private void ShowExpandedPreview(string nodeId, Texture2D texture, string titleText)
        {
            if (texture == null)
            {
                HideExpandedPreview();
                return;
            }

            EnsureExpandedPreviewOverlayAttached();
            _expandedPreviewNodeId = nodeId ?? string.Empty;
            _expandedPreviewImage.image = texture;
            _expandedPreviewImage.style.width = texture.width;
            _expandedPreviewImage.style.height = texture.height;
            _expandedPreviewTitle.text = string.IsNullOrWhiteSpace(titleText) ? "Preview" : titleText;
            _expandedPreviewOverlay.style.display = DisplayStyle.Flex;
            _expandedPreviewOverlay.BringToFront();
            _expandedPreviewNeedsFit = true;
            _isPanningExpandedPreview = false;
            Focus();
            UpdateExpandedPreviewTransformIfNeeded();
        }

        private void HideExpandedPreview()
        {
            _expandedPreviewOverlay.style.display = DisplayStyle.None;
            _expandedPreviewImage.image = null;
            _expandedPreviewTitle.text = string.Empty;
            _expandedPreviewNodeId = null;
            _expandedPreviewPanOffset = Vector2.zero;
            _expandedPreviewPanOffsetAtDragStart = Vector2.zero;
            _expandedPreviewPanMousePositionAtDragStart = Vector2.zero;
            _expandedPreviewZoom = 1.0f;
            _expandedPreviewNeedsFit = false;
            _isPanningExpandedPreview = false;

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
                return;
            }

            if (_expandedPreviewViewport.resolvedStyle.width <= 0.0f || _expandedPreviewViewport.resolvedStyle.height <= 0.0f)
            {
                return;
            }

            if (_expandedPreviewNeedsFit)
            {
                Texture texture = _expandedPreviewImage.image;
                float zoomByWidth = _expandedPreviewViewport.resolvedStyle.width / texture.width;
                float zoomByHeight = _expandedPreviewViewport.resolvedStyle.height / texture.height;
                _expandedPreviewZoom = Mathf.Clamp(Mathf.Min(zoomByWidth, zoomByHeight), MinExpandedPreviewZoom, MaxExpandedPreviewZoom);
                _expandedPreviewPanOffset = Vector2.zero;
                _expandedPreviewNeedsFit = false;
            }

            ApplyExpandedPreviewTransform();
        }

        private void ApplyExpandedPreviewTransform()
        {
            Texture texture = _expandedPreviewImage.image;
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
            _isPanningExpandedPreview = true;
            _expandedPreviewPanMousePositionAtDragStart = viewportLocalMousePosition;
            _expandedPreviewPanOffsetAtDragStart = _expandedPreviewPanOffset;
            _expandedPreviewOverlay.CaptureMouse();
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
    }
}
