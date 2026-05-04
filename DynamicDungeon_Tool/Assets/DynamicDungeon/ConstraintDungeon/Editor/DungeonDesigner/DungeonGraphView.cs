using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEngine;
using DynamicDungeon.ConstraintDungeon;
using System.Linq;
using System;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonGraphView : GraphView
    {
        public DungeonFlow activeFlow;
        public bool IsLoading;
        public Action<List<RoomNode>, DungeonEdge> OnSelectionChanged;
        public Blackboard Blackboard { get; private set; }
        public Blackboard ValidationBlackboard { get; private set; }
        private Node _lastSelectedNode;
        private bool _hasPositioned;
        private DungeonSearchWindowProvider _searchWindow;
        private EditorWindow _editorWindow;
        public Blackboard SettingsBlackboard { get; private set; }

        public DungeonGraphView(EditorWindow window)
        {
            _editorWindow = window;
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ConstraintDungeonAssetPaths.DungeonGraphStylesheet));
            
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            AddSearchWindow();

            SettingsBlackboard = new Blackboard(this)
            {
                title = "Dungeon Defaults",
                subTitle = "Global settings",
                scrollable = true
            };
            SettingsBlackboard.SetPosition(new Rect(15, 15, 300, 400));
            SettingsBlackboard.capabilities |= Capabilities.Resizable;
            SettingsBlackboard.Add(new Resizer());
            
            VisualElement settingsAddBtn = SettingsBlackboard.Q("addButton");
            if (settingsAddBtn != null) settingsAddBtn.style.display = DisplayStyle.None;

            VisualElement settingsHeader = SettingsBlackboard.Q("header");
            Button settingsCollapseBtn = new Button(() => ToggleBlackboardCollapse(SettingsBlackboard)) { text = "▼", name = "collapseButton" };
            SetupBlackboardButton(settingsCollapseBtn);
            settingsHeader.Add(settingsCollapseBtn);
            Add(SettingsBlackboard);

            Blackboard = new Blackboard(this)
            {
                title = "Node Settings",
                subTitle = "Details",
                scrollable = true
            };
            Blackboard.SetPosition(new Rect(0, 0, 350, 270));
            Blackboard.capabilities |= Capabilities.Resizable;
            Blackboard.Add(new Resizer());
            
            VisualElement inspectorAddBtn = Blackboard.Q("addButton");
            if (inspectorAddBtn != null) inspectorAddBtn.style.display = DisplayStyle.None;

            VisualElement inspectorHeader = Blackboard.Q("header");
            Button inspectorCollapseBtn = new Button(() => ToggleBlackboardCollapse(Blackboard)) { text = "▼", name = "collapseButton" };
            SetupBlackboardButton(inspectorCollapseBtn);
            inspectorHeader.Add(inspectorCollapseBtn);
            Add(Blackboard);

            ValidationBlackboard = new Blackboard(this)
            {
                title = "Validation",
                subTitle = "Run validation",
                scrollable = true
            };
            ValidationBlackboard.SetPosition(new Rect(15, 430, 380, 260));
            ValidationBlackboard.capabilities |= Capabilities.Resizable;
            ValidationBlackboard.Add(new Resizer());

            VisualElement validationAddBtn = ValidationBlackboard.Q("addButton");
            if (validationAddBtn != null) validationAddBtn.style.display = DisplayStyle.None;

            VisualElement validationHeader = ValidationBlackboard.Q("header");
            Button validationCollapseBtn = new Button(() => ToggleBlackboardCollapse(ValidationBlackboard)) { text = "▼", name = "collapseButton" };
            SetupBlackboardButton(validationCollapseBtn);
            validationHeader.Add(validationCollapseBtn);
            Add(ValidationBlackboard);

            // Position Panels after layout (Inspector to right)
            RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (!_hasPositioned && Blackboard.parent != null)
                {
                    Rect bRect = Blackboard.GetPosition();
                    Blackboard.SetPosition(new Rect(evt.newRect.width - bRect.width - 15, 15, bRect.width, bRect.height));
                    _hasPositioned = true;
                }
            });

            RegisterCallback<PointerDownEvent>(OnPointerDown);

            graphViewChanged = OnGraphViewChanged;
        }

        private void SetupBlackboardButton(Button btn)
        {
            btn.style.width = 20;
            btn.style.height = 20;
            btn.style.paddingLeft = btn.style.paddingRight = 0;
            btn.style.paddingTop = btn.style.paddingBottom = 0;
            btn.style.fontSize = 10;
            btn.style.marginLeft = 5;
            btn.style.backgroundColor = new Color(0, 0, 0, 0); 
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
        }

        private void ToggleBlackboardCollapse(Blackboard target)
        {
            VisualElement container = target.Q("contentContainer");
            Resizer resizer = target.Q<Resizer>();
            Button collapseBtn = target.Q<Button>("collapseButton");
            Rect rect = target.GetPosition();
            
            if (container.style.display == DisplayStyle.None)
            {
                container.style.display = DisplayStyle.Flex;
                if (resizer != null) resizer.style.display = DisplayStyle.Flex;
                target.SetPosition(new Rect(rect.x, rect.y, rect.width, 300));
                collapseBtn.text = "▼";
            }
            else
            {
                container.style.display = DisplayStyle.None;
                if (resizer != null) resizer.style.display = DisplayStyle.None;
                target.SetPosition(new Rect(rect.x, rect.y, rect.width, 55));
                collapseBtn.text = "►";
            }
        }


        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            NotifySelectionChanged();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            NotifySelectionChanged();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            if (activeFlow == null) return;
            
            List<RoomNode> selectedItems = new List<RoomNode>();
            DungeonEdge selectedEdge = null;

            foreach (ISelectable item in selection)
            {
                if (item is Node node)
                {
                    RoomNode roomNode = activeFlow.nodes.Find(rn => rn.id == node.viewDataKey);
                    if (roomNode != null) selectedItems.Add(roomNode);
                }
                else if (item is DungeonEdge edge)
                {
                    selectedEdge = edge;
                    if (edge.associatedCorridors.Count > 0)
                        selectedItems.AddRange(edge.associatedCorridors);
                    else if (edge.associatedCorridor != null)
                        selectedItems.Add(edge.associatedCorridor);
                }
            }

            // Sticky Selection: Only update if there is a new selection
            if (selectedItems.Count > 0 || selectedEdge != null)
            {
                OnSelectionChanged?.Invoke(selectedItems, selectedEdge);
            }
        }



        private void OnPointerDown(PointerDownEvent evt)
        {
            // Instant Right-Click Search
            if (evt.button == 1 && (evt.target is GraphView || evt.target is GridBackground))
            {
                Vector2 screenPos = GUIUtility.GUIToScreenPoint(evt.localPosition);
                SearchWindow.Open(new SearchWindowContext(screenPos), _searchWindow);
                evt.StopPropagation();
                return;
            }

            if (evt.shiftKey && evt.target is Node clickedNode)
            {
                if (_lastSelectedNode != null && _lastSelectedNode != clickedNode)
                {
                    ConnectNodes(_lastSelectedNode, clickedNode);
                    _lastSelectedNode = clickedNode;
                    evt.StopPropagation();
                }
                else
                {
                    _lastSelectedNode = clickedNode;
                }
            }
            else if (evt.target is Node node)
            {
                _lastSelectedNode = node;
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            if (IsLoading) return graphViewChange;
            if (activeFlow == null) return graphViewChange;

            if (graphViewChange.edgesToCreate != null)
            {
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    if (edge.output?.node is Node fromNode && edge.input?.node is Node toNode)
                    {
                        // Intercept port connection to create our Corridor-based logic
                        ConnectNodes(fromNode, toNode);
                    }
                }
                // We clear the list because ConnectNodes manually AddElement(smartEdge) and handles its own UI lifecycle
                graphViewChange.edgesToCreate.Clear();
            }

            if (graphViewChange.elementsToRemove != null)
            {
                foreach (GraphElement element in graphViewChange.elementsToRemove)
                {
                    if (element is Node node)
                    {
                        RoomNode roomNode = activeFlow.nodes.Find(rn => rn.id == node.viewDataKey);
                        if (roomNode != null)
                        {
                            Undo.RecordObject(activeFlow, "Delete Node");
                            activeFlow.nodes.Remove(roomNode);
                            // Also remove edges connected to this node
                            activeFlow.edges.RemoveAll(e => e.fromId == roomNode.id || e.toId == roomNode.id);
                            EditorUtility.SetDirty(activeFlow);
                        }
                    }
                    else if (element is DungeonEdge dungeonEdge)
                    {
                        Undo.RecordObject(activeFlow, "Delete Edge");
                        HashSet<string> corridorIds = new HashSet<string>(dungeonEdge.associatedCorridors.Select(c => c.id));
                        if (dungeonEdge.associatedCorridor != null) corridorIds.Add(dungeonEdge.associatedCorridor.id);

                        activeFlow.nodes.RemoveAll(n => corridorIds.Contains(n.id));
                        activeFlow.edges.RemoveAll(e =>
                            corridorIds.Contains(e.fromId) ||
                            corridorIds.Contains(e.toId) ||
                            (e.fromId == dungeonEdge.fromRoomId && e.toId == dungeonEdge.toRoomId) ||
                            (e.fromId == dungeonEdge.toRoomId && e.toId == dungeonEdge.fromRoomId));
                        EditorUtility.SetDirty(activeFlow);
                    }
                    else if (element is Edge rawEdge)
                    {
                        Undo.RecordObject(activeFlow, "Delete Edge");
                        string fromId = rawEdge.output?.node?.viewDataKey;
                        string toId = rawEdge.input?.node?.viewDataKey;
                        activeFlow.edges.RemoveAll(e => e.fromId == fromId && e.toId == toId);
                        EditorUtility.SetDirty(activeFlow);
                    }
                }
            }

            if (graphViewChange.movedElements != null)
            {
                foreach (GraphElement element in graphViewChange.movedElements)
                {
                    Node node = element as Node;
                    if (node == null)
                    {
                        continue;
                    }

                    RoomNode roomNode = activeFlow.nodes.Find(n => n.id == node.viewDataKey);
                    if (roomNode == null)
                    {
                        continue;
                    }

                    Undo.RecordObject(activeFlow, "Move Node");
                    roomNode.position = node.GetPosition().position;
                    EditorUtility.SetDirty(activeFlow);
                }
            }

            return graphViewChange;
        }


        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)

        {
            List<Port> compatiblePorts = new List<Port>();
            ports.ForEach(port =>
            {
                if (startPort != port && startPort.node != port.node)
                    compatiblePorts.Add(port);
            });
            return compatiblePorts;
        }

        private class CorridorChain
        {
            public string fromRoomId;
            public string toRoomId;
            public List<RoomNode> corridors = new List<RoomNode>();
        }

        private static string LinkKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }

        private string GetOtherNodeId(RoomEdge edge, string id)
        {
            if (edge.fromId == id) return edge.toId;
            if (edge.toId == id) return edge.fromId;
            return null;
        }

        private Port GetOutputOrAny(Node node)
        {
            return node.outputContainer.Q<Port>() ?? node.inputContainer.Q<Port>();
        }

        private Port GetInputOrAny(Node node)
        {
            return node.inputContainer.Q<Port>() ?? node.outputContainer.Q<Port>();
        }

        private List<CorridorChain> FindCorridorChains(bool includeDuplicateLinks = false)
        {
            List<CorridorChain> chains = new List<CorridorChain>();
            if (activeFlow == null) return chains;

            Dictionary<string, RoomNode> roomNodes = activeFlow.nodes
                .Where(n => n.type != RoomType.Corridor)
                .ToDictionary(n => n.id, n => n);

            HashSet<string> processedCorridors = new HashSet<string>();
            HashSet<string> processedDirectLinks = new HashSet<string>();
            HashSet<string> processedLogicalLinks = new HashSet<string>();

            foreach (RoomNode room in roomNodes.Values)
            {
                List<RoomEdge> connectedEdges = activeFlow.edges
                    .Where(e => e.fromId == room.id || e.toId == room.id)
                    .ToList();

                foreach (RoomEdge edge in connectedEdges)
                {
                    string nextId = GetOtherNodeId(edge, room.id);
                    if (string.IsNullOrEmpty(nextId)) continue;

                    if (roomNodes.ContainsKey(nextId))
                    {
                        string key = LinkKey(room.id, nextId);
                        if (processedDirectLinks.Add(key) && (includeDuplicateLinks || processedLogicalLinks.Add(key)))
                        {
                            chains.Add(new CorridorChain
                            {
                                fromRoomId = room.id,
                                toRoomId = nextId
                            });
                        }
                        continue;
                    }

                    RoomNode corridor = activeFlow.nodes.Find(n => n.id == nextId && n.type == RoomType.Corridor);
                    if (corridor == null || processedCorridors.Contains(corridor.id)) continue;

                    CorridorChain chain = TraceCorridorChain(room.id, corridor.id, roomNodes);
                    if (chain == null) continue;

                    string chainKey = LinkKey(chain.fromRoomId, chain.toRoomId);
                    if (includeDuplicateLinks || processedLogicalLinks.Add(chainKey))
                        chains.Add(chain);

                    foreach (RoomNode chainCorridor in chain.corridors)
                        processedCorridors.Add(chainCorridor.id);
                }
            }

            return chains;
        }

        private CorridorChain TraceCorridorChain(string startRoomId, string firstCorridorId, Dictionary<string, RoomNode> roomNodes)
        {
            string previousId = startRoomId;
            string currentId = firstCorridorId;
            HashSet<string> localVisited = new HashSet<string>();
            List<RoomNode> corridors = new List<RoomNode>();

            while (true)
            {
                RoomNode corridor = activeFlow.nodes.Find(n => n.id == currentId && n.type == RoomType.Corridor);
                if (corridor == null || !localVisited.Add(currentId)) return null;

                corridors.Add(corridor);

                List<string> nextIds = activeFlow.edges
                    .Where(e => e.fromId == currentId || e.toId == currentId)
                    .Select(e => GetOtherNodeId(e, currentId))
                    .Where(id => !string.IsNullOrEmpty(id) && id != previousId)
                    .Distinct()
                    .ToList();

                if (nextIds.Count != 1) return null;

                string nextId = nextIds[0];
                if (roomNodes.ContainsKey(nextId))
                {
                    return new CorridorChain
                    {
                        fromRoomId = startRoomId,
                        toRoomId = nextId,
                        corridors = corridors
                    };
                }

                previousId = currentId;
                currentId = nextId;
            }
        }

        public void LoadFlow(DungeonFlow flow)
        {
            IsLoading = true;
            this.activeFlow = flow;
            DeleteElements(graphElements.ToList());
            
            if (flow == null) {
                IsLoading = false;
                return;
            }

            PopulateSettingsPanel();

            Dictionary<string, Node> nodeMap = new Dictionary<string, Node>();

            foreach (RoomNode roomNode in flow.nodes.Where(n => n.type != RoomType.Corridor))
            {
                Node node = CreateNode(roomNode);
                nodeMap.Add(roomNode.id, node);
                AddElement(node);
            }

            foreach (CorridorChain chain in FindCorridorChains())
            {
                if (!nodeMap.ContainsKey(chain.fromRoomId) || !nodeMap.ContainsKey(chain.toRoomId))
                    continue;

                Port portA = GetOutputOrAny(nodeMap[chain.fromRoomId]);
                Port portB = GetInputOrAny(nodeMap[chain.toRoomId]);

                if (portA == null || portB == null)
                    continue;

                if (chain.corridors.Count == 0)
                {
                    DungeonEdge directEdge = new DungeonEdge
                    {
                        output = portA,
                        input = portB,
                        fromRoomId = chain.fromRoomId,
                        toRoomId = chain.toRoomId
                    };
                    directEdge.SetCorridorCount(0);
                    directEdge.output.Connect(directEdge);
                    directEdge.input.Connect(directEdge);
                    AddElement(directEdge);
                    continue;
                }

                DungeonEdge smartEdge = new DungeonEdge
                {
                    output = portA,
                    input = portB,
                    associatedCorridor = chain.corridors[0],
                    fromRoomId = chain.fromRoomId,
                    toRoomId = chain.toRoomId
                };

                smartEdge.associatedCorridors.AddRange(chain.corridors);
                smartEdge.SetCorridorCount(chain.corridors.Count);
                smartEdge.output.Connect(smartEdge);
                smartEdge.input.Connect(smartEdge);
                AddElement(smartEdge);
            }
            
            IsLoading = false;
        }

        public void ShowValidationResults(DungeonFlowValidator.Result result)
        {
            if (ValidationBlackboard == null)
            {
                return;
            }

            ValidationBlackboard.Clear();
            ValidationBlackboard.subTitle = result == null
                ? "No results"
                : $"{result.Errors.Count} errors, {result.Warnings.Count} warnings";

            VisualElement container = new VisualElement
            {
                style =
                {
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 8
                }
            };
            ValidationBlackboard.Add(container);

            if (result == null || result.Issues.Count == 0)
            {
                Label okLabel = new Label("No validation issues.");
                okLabel.style.color = new Color(0.55f, 1f, 0.65f);
                okLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                container.Add(okLabel);
                return;
            }

            foreach (DungeonFlowValidator.Issue issue in result.Issues)
            {
                Button row = new Button(() => FocusValidationIssue(issue))
                {
                    text = (issue.IsError ? "ERROR: " : "WARN: ") + issue.Message
                };
                row.style.whiteSpace = WhiteSpace.Normal;
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.style.marginBottom = 4;
                row.style.paddingTop = 5;
                row.style.paddingBottom = 5;
                row.style.color = issue.IsError ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.82f, 0.35f);
                row.tooltip = "Click to select the related graph item.";
                container.Add(row);
            }
        }

        private void FocusValidationIssue(DungeonFlowValidator.Issue issue)
        {
            if (issue == null)
            {
                return;
            }

            GraphElement target = FindIssueTarget(issue);
            if (target == null)
            {
                Debug.LogWarning($"[DungeonDesigner] Could not locate graph item for: {issue.Message}", activeFlow);
                return;
            }

            ClearSelection();
            AddToSelection(target);
            target.BringToFront();
            FrameSelection();
            NotifySelectionChanged();
        }

        private GraphElement FindIssueTarget(DungeonFlowValidator.Issue issue)
        {
            if (!string.IsNullOrEmpty(issue.NodeId))
            {
                Node node = GetNodeByGuid(issue.NodeId) ?? FindVisibleNodeNear(issue.NodeId);
                if (node != null)
                {
                    return node;
                }
            }

            if (!string.IsNullOrEmpty(issue.FromNodeId) && !string.IsNullOrEmpty(issue.ToNodeId))
            {
                DungeonEdge edge = graphElements
                    .OfType<DungeonEdge>()
                    .FirstOrDefault(e => LinkKey(e.fromRoomId, e.toRoomId) == LinkKey(issue.FromNodeId, issue.ToNodeId));
                if (edge != null)
                {
                    return edge;
                }
            }

            if (!string.IsNullOrEmpty(issue.FromNodeId))
            {
                Node fromNode = GetNodeByGuid(issue.FromNodeId) ?? FindVisibleNodeNear(issue.FromNodeId);
                if (fromNode != null)
                {
                    return fromNode;
                }
            }

            if (!string.IsNullOrEmpty(issue.ToNodeId))
            {
                Node toNode = GetNodeByGuid(issue.ToNodeId) ?? FindVisibleNodeNear(issue.ToNodeId);
                if (toNode != null)
                {
                    return toNode;
                }
            }

            return null;
        }

        private Node FindVisibleNodeNear(string nodeId)
        {
            if (activeFlow == null || string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            RoomNode directNode = activeFlow.nodes.Find(n => n.id == nodeId);
            if (directNode != null && directNode.type != RoomType.Corridor)
            {
                return GetNodeByGuid(directNode.id);
            }

            Queue<string> queue = new Queue<string>();
            HashSet<string> visited = new HashSet<string>();
            queue.Enqueue(nodeId);
            visited.Add(nodeId);

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                foreach (RoomEdge edge in activeFlow.edges.Where(e => e.fromId == currentId || e.toId == currentId))
                {
                    string nextId = GetOtherNodeId(edge, currentId);
                    if (string.IsNullOrEmpty(nextId) || !visited.Add(nextId))
                    {
                        continue;
                    }

                    RoomNode nextNode = activeFlow.nodes.Find(n => n.id == nextId);
                    if (nextNode != null && nextNode.type != RoomType.Corridor)
                    {
                        Node visibleNode = GetNodeByGuid(nextNode.id);
                        if (visibleNode != null)
                        {
                            return visibleNode;
                        }
                    }

                    queue.Enqueue(nextId);
                }
            }

            return null;
        }

        public Node CreateNode(RoomNode roomNode)
        {
            Node node = new Node
            {
                viewDataKey = roomNode.id
            };
            
            node.AddToClassList("dungeon-node");
            RefreshNodeStyleInternal(node, roomNode.type);

            // Rooms are the only nodes shown now
            node.SetPosition(new Rect(roomNode.position, new Vector2(390, 180)));
            
            // Port Setup - Constrained by Type
            if (roomNode.type != RoomType.Exit)
            {
                Port outputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
                outputPort.portName = "";
                node.outputContainer.Add(outputPort);
            }

            if (roomNode.type != RoomType.Entrance)
            {
                Port inputPort = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
                inputPort.portName = "";
                node.inputContainer.Add(inputPort);
            }

            // Two-Tone Structure - Force INSIDE the border
            VisualElement border = node.Q("node-border");
            border.style.flexDirection = FlexDirection.Column;
            
            VisualElement topArea = new VisualElement { name = "node-top-area" };
            topArea.style.flexGrow = 1;
            topArea.style.flexDirection = FlexDirection.Row; // For ports
            topArea.style.justifyContent = Justify.SpaceBetween;
            
            // Move ports into top area
            topArea.Add(node.inputContainer);
            topArea.Add(node.outputContainer);
            border.Add(topArea);

            VisualElement footer = new VisualElement { name = "node-footer" };
            footer.AddToClassList("dungeon-node-footer");
            
            Label label = new Label(roomNode.displayName.ToUpper());
            label.AddToClassList("dungeon-node-label");
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 32;
            label.style.color = Color.white;
            footer.Add(label);
            border.Add(footer);

            node.mainContainer.Remove(node.Q("title"));
            node.RefreshExpandedState();
            node.RefreshPorts();

            return node;
        }

        private void AddSearchWindow()
        {
            _searchWindow = ScriptableObject.CreateInstance<DungeonSearchWindowProvider>();
            _searchWindow.Initialise(this, _editorWindow);
            
            nodeCreationRequest = context => 
                SearchWindow.Open(new SearchWindowContext(context.screenMousePosition), _searchWindow);
        }

        public void RefreshNodeDisplayName(string id, string newName)
        {
            Node node = GetNodeByGuid(id);
            if (node != null)
            {
                Label label = node.Q<Label>(className: "dungeon-node-label");
                if (label != null) label.text = newName.ToUpper();
            }
        }

        public void RefreshNodeStyle(string id, RoomType type)
        {
            Node node = GetNodeByGuid(id);
            if (node != null) RefreshNodeStyleInternal(node, type);
        }

        private void RefreshNodeStyleInternal(Node node, RoomType type)
        {
            node.RemoveFromClassList("room-entrance");
            node.RemoveFromClassList("room-exit");
            node.RemoveFromClassList("room-reward");
            node.RemoveFromClassList("room-shop");
            node.RemoveFromClassList("room-hub");
            node.RemoveFromClassList("room-boss");
            node.RemoveFromClassList("room-bossfoyer");
            node.RemoveFromClassList("room-normal");
            node.RemoveFromClassList("room-corridor");

            switch (type)
            {
                case RoomType.Entrance: node.AddToClassList("room-entrance"); break;
                case RoomType.Exit: node.AddToClassList("room-exit"); break;
                case RoomType.Reward: node.AddToClassList("room-reward"); break;
                case RoomType.Shop: node.AddToClassList("room-shop"); break;
                case RoomType.Hub: node.AddToClassList("room-hub"); break;
                case RoomType.Boss: node.AddToClassList("room-boss"); break;
                case RoomType.BossFoyer: node.AddToClassList("room-bossfoyer"); break;
                case RoomType.Corridor: node.AddToClassList("room-corridor"); break;
                default: node.AddToClassList("room-normal"); break;
            }
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (activeFlow == null) return;
            
            // Clean up the menu entirely if clicking background 
            // to ensure our instant search is the only thing that happens.
            if (evt.target is GraphView || evt.target is GridBackground)
            {
                // We don't add anything here because OnPointerDown handles the search window
                // If we want to allow other menu items, we would add them here.
            }
        }

        public void AddNode(RoomType type, Vector2 pos)
        {
            string id = Guid.NewGuid().ToString().Substring(0, 8);
            Undo.RecordObject(activeFlow, "Add Node");
            RoomNode newNode = new RoomNode(id) { position = pos, type = type, displayName = type.ToString().ToUpper() };
            
            // Auto-populate with current Dungeon Defaults
            newNode.allowedTemplates.AddRange(activeFlow.GetTemplatesForType(type));
            
            activeFlow.nodes.Add(newNode);
            LoadFlow(activeFlow);
            EditorUtility.SetDirty(activeFlow);
        }
    
        public void ConnectNodes(Node from, Node to)
        {
            if (from == null || to == null || from == to) return;
            
            RoomNode roomA = activeFlow.nodes.Find(n => n.id == from.viewDataKey);
            RoomNode roomB = activeFlow.nodes.Find(n => n.id == to.viewDataKey);
            if (roomA == null || roomB == null) return;
            if (FindCorridorChains().Any(c => LinkKey(c.fromRoomId, c.toRoomId) == LinkKey(roomA.id, roomB.id))) return;

            Undo.RecordObject(activeFlow, "Connect Rooms");
            CreateCorridorChain(roomA, roomB, activeFlow.GetCorridorCountForConnection(roomA.position, roomB.position));

            LoadFlow(activeFlow);
            EditorUtility.SetDirty(activeFlow);
        }

        public void SetCorridorLinkCount(DungeonEdge edge, int corridorCount)
        {
            if (edge == null || activeFlow == null) return;
            RoomNode roomA = activeFlow.nodes.Find(n => n.id == edge.fromRoomId);
            RoomNode roomB = activeFlow.nodes.Find(n => n.id == edge.toRoomId);
            if (roomA == null || roomB == null) return;

            Undo.RecordObject(activeFlow, "Change Corridor Count");
            string linkKey = LinkKey(roomA.id, roomB.id);
            List<RoomNode> currentCorridors = FindCorridorChains(true)
                .Where(c => LinkKey(c.fromRoomId, c.toRoomId) == linkKey)
                .SelectMany(c => c.corridors)
                .ToList();

            if (currentCorridors.Count == 0)
                currentCorridors = edge.associatedCorridors;

            ReplaceCorridorChain(roomA, roomB, currentCorridors, corridorCount);
            LoadFlow(activeFlow);
            EditorUtility.SetDirty(activeFlow);
        }

        public void ApplyDynamicCorridorCount(DungeonEdge edge)
        {
            if (edge == null || activeFlow == null) return;
            RoomNode roomA = activeFlow.nodes.Find(n => n.id == edge.fromRoomId);
            RoomNode roomB = activeFlow.nodes.Find(n => n.id == edge.toRoomId);
            if (roomA == null || roomB == null) return;

            SetCorridorLinkCount(edge, activeFlow.GetDynamicCorridorCount(roomA.position, roomB.position));
        }

        private void ApplyCurrentCorridorPlacementToAllLinks()
        {
            if (activeFlow == null) return;

            List<IGrouping<string, CorridorChain>> chainsByLink = FindCorridorChains(true)
                .GroupBy(c => LinkKey(c.fromRoomId, c.toRoomId))
                .ToList();

            foreach (IGrouping<string, CorridorChain> group in chainsByLink)
            {
                CorridorChain chain = group.First();
                RoomNode roomA = activeFlow.nodes.Find(n => n.id == chain.fromRoomId);
                RoomNode roomB = activeFlow.nodes.Find(n => n.id == chain.toRoomId);
                if (roomA == null || roomB == null) continue;

                List<RoomNode> existingCorridors = group.SelectMany(c => c.corridors).ToList();
                int corridorCount = activeFlow.GetCorridorCountForConnection(roomA.position, roomB.position);
                ReplaceCorridorChain(roomA, roomB, existingCorridors, corridorCount);
            }
        }

        private void ReplaceCorridorChain(RoomNode roomA, RoomNode roomB, List<RoomNode> existingCorridors, int corridorCount)
        {
            HashSet<string> corridorIds = new HashSet<string>(existingCorridors.Select(c => c.id));
            activeFlow.nodes.RemoveAll(n => corridorIds.Contains(n.id));
            activeFlow.edges.RemoveAll(e =>
                corridorIds.Contains(e.fromId) ||
                corridorIds.Contains(e.toId) ||
                (e.fromId == roomA.id && e.toId == roomB.id) ||
                (e.fromId == roomB.id && e.toId == roomA.id));

            CreateCorridorChain(roomA, roomB, corridorCount, existingCorridors);
        }

        private void CreateCorridorChain(RoomNode roomA, RoomNode roomB, int corridorCount, List<RoomNode> templatesSource = null)
        {
            corridorCount = Mathf.Max(0, corridorCount);
            string previousId = roomA.id;

            for (int i = 0; i < corridorCount; i++)
            {
                string corridorId = "corr_" + Guid.NewGuid().ToString().Substring(0, 4);
                float t = (i + 1f) / (corridorCount + 1f);
                RoomNode corridorNode = new RoomNode(corridorId)
                {
                    type = RoomType.Corridor,
                    displayName = corridorCount == 1 ? "Connector" : $"Connector {i + 1}/{corridorCount}",
                    position = Vector2.Lerp(roomA.position, roomB.position, t)
                };

                if (templatesSource != null && i < templatesSource.Count && templatesSource[i].allowedTemplates != null && templatesSource[i].allowedTemplates.Count > 0)
                    corridorNode.allowedTemplates.AddRange(templatesSource[i].allowedTemplates);
                else
                    corridorNode.allowedTemplates.AddRange(activeFlow.GetTemplatesForType(RoomType.Corridor));

                activeFlow.nodes.Add(corridorNode);
                activeFlow.edges.Add(new RoomEdge(previousId, corridorId));
                previousId = corridorId;
            }

            activeFlow.edges.Add(new RoomEdge(previousId, roomB.id));
        }

        private void PopulateSettingsPanel()
        {
            if (activeFlow == null || SettingsBlackboard == null) return;
            SettingsBlackboard.Clear();

            VisualElement corridorSettings = new VisualElement();
            corridorSettings.AddToClassList("settings-type-group");
            corridorSettings.style.marginBottom = 10;

            Label corridorHeader = new Label("CORRIDOR LINKS");
            corridorHeader.AddToClassList("settings-type-header");
            corridorSettings.Add(corridorHeader);

            UnityEngine.UIElements.EnumField modeField = new UnityEngine.UIElements.EnumField("Placement", activeFlow.corridorPlacementMode);
            corridorSettings.Add(modeField);

            UnityEngine.UIElements.IntegerField fixedCountField = new UnityEngine.UIElements.IntegerField("Fixed Count") { value = activeFlow.fixedCorridorCount };
            corridorSettings.Add(fixedCountField);

            UnityEngine.UIElements.FloatField dynamicSpacingField = new UnityEngine.UIElements.FloatField("Dynamic Spacing") { value = activeFlow.dynamicCorridorSpacing };
            corridorSettings.Add(dynamicSpacingField);

            UnityEngine.UIElements.IntegerField dynamicMaxField = new UnityEngine.UIElements.IntegerField("Dynamic Max") { value = activeFlow.maxDynamicCorridorCount };
            corridorSettings.Add(dynamicMaxField);

            Action refreshCorridorFieldState = () =>
            {
                bool fixedMode = activeFlow.corridorPlacementMode == CorridorPlacementMode.Fixed;
                fixedCountField.SetEnabled(fixedMode);
                dynamicSpacingField.SetEnabled(!fixedMode);
                dynamicMaxField.SetEnabled(!fixedMode);
            };

            modeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Corridor Placement");
                activeFlow.corridorPlacementMode = (CorridorPlacementMode)evt.newValue;
                ApplyCurrentCorridorPlacementToAllLinks();
                refreshCorridorFieldState();
                LoadFlow(activeFlow);
                EditorUtility.SetDirty(activeFlow);
            });

            fixedCountField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Fixed Corridor Count");
                activeFlow.fixedCorridorCount = Mathf.Max(0, evt.newValue);
                fixedCountField.SetValueWithoutNotify(activeFlow.fixedCorridorCount);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Fixed)
                {
                    ApplyCurrentCorridorPlacementToAllLinks();
                    LoadFlow(activeFlow);
                }
                EditorUtility.SetDirty(activeFlow);
            });

            dynamicSpacingField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Dynamic Corridor Spacing");
                activeFlow.dynamicCorridorSpacing = Mathf.Max(1f, evt.newValue);
                dynamicSpacingField.SetValueWithoutNotify(activeFlow.dynamicCorridorSpacing);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Dynamic)
                {
                    ApplyCurrentCorridorPlacementToAllLinks();
                    LoadFlow(activeFlow);
                }
                EditorUtility.SetDirty(activeFlow);
            });

            dynamicMaxField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Dynamic Corridor Max");
                activeFlow.maxDynamicCorridorCount = Mathf.Max(0, evt.newValue);
                dynamicMaxField.SetValueWithoutNotify(activeFlow.maxDynamicCorridorCount);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Dynamic)
                {
                    ApplyCurrentCorridorPlacementToAllLinks();
                    LoadFlow(activeFlow);
                }
                EditorUtility.SetDirty(activeFlow);
            });

            refreshCorridorFieldState();
            SettingsBlackboard.Add(corridorSettings);

            foreach (RoomType type in Enum.GetValues(typeof(RoomType)))
            {
                VisualElement group = new VisualElement();
                group.AddToClassList("settings-type-group");

                VisualElement headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
                Label typeLabel = new Label(type.ToString().ToUpper());
                typeLabel.AddToClassList("settings-type-header");
                headerRow.Add(typeLabel);

                Button addBtn = new Button(() => AddDefaultTemplate(type)) { text = "+", style = { width = 20, height = 20 } };
                headerRow.Add(addBtn);
                group.Add(headerRow);

                DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
                if (mapping != null)
                {
                    for (int i = 0; i < mapping.templates.Count; i++)
                    {
                        int index = i;
                        VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                        
                        UnityEditor.UIElements.ObjectField field = new UnityEditor.UIElements.ObjectField { objectType = typeof(GameObject), value = mapping.templates[i] };
                        field.style.flexGrow = 1;
                        field.RegisterValueChangedCallback(evt => {
                            Undo.RecordObject(activeFlow, "Change Default Template");
                            mapping.templates[index] = (GameObject)evt.newValue;
                            EditorUtility.SetDirty(activeFlow);
                        });
                        row.Add(field);

                        Button removeBtn = new Button(() => RemoveDefaultTemplate(type, index)) { text = "×", style = { width = 20 } };
                        row.Add(removeBtn);
                        group.Add(row);
                    }
                }

                SettingsBlackboard.Add(group);
            }
        }

        private void AddDefaultTemplate(RoomType type)
        {
            if (activeFlow == null) return;
            Undo.RecordObject(activeFlow, "Add Default Template");
            DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
            if (mapping == null)
            {
                mapping = new DefaultTemplateMapping { type = type };
                activeFlow.defaultTemplates.Add(mapping);
            }
            mapping.templates.Add(null);
            EditorUtility.SetDirty(activeFlow);
            PopulateSettingsPanel();
        }

        private void RemoveDefaultTemplate(RoomType type, int index)
        {
            if (activeFlow == null) return;
            Undo.RecordObject(activeFlow, "Remove Default Template");
            DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
            if (mapping != null && index < mapping.templates.Count)
            {
                mapping.templates.RemoveAt(index);
            }
            EditorUtility.SetDirty(activeFlow);
            PopulateSettingsPanel();
        }
    }
}
