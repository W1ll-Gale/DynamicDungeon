using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEngine;
using DynamicDungeon.ConstraintDungeon;
using DynamicDungeon.Editor.Shared;
using System.Linq;
using System;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonGraphView : GraphView
    {
        public DungeonFlow activeFlow;
        public bool IsLoading;
        public Action<List<RoomNode>, DungeonEdge> OnSelectionChanged;
        private Node _lastSelectedNode;
        private DungeonSearchWindowProvider _searchWindow;
        private EditorWindow _editorWindow;
        private Action _afterMutation;
        private Action _viewTransformChanged;

        public DungeonGraphView(EditorWindow window)
        {
            _editorWindow = window;
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(ConstraintDungeonAssetPaths.DungeonGraphStylesheet));
            
            GraphViewShellUtility.ConfigureDefaultGraphView(this);

            GridBackground background = new GridBackground();
            background.AddToClassList("dungeon-graph-background");
            Insert(0, background);
            background.StretchToParentSize();

            AddSearchWindow();

            RegisterCallback<PointerDownEvent>(OnPointerDown);

            graphViewChanged = OnGraphViewChanged;
            viewTransformChanged = OnViewTransformChanged;
        }

        public void SetAfterMutationCallback(Action afterMutation)
        {
            _afterMutation = afterMutation;
        }

        public void SetViewTransformChangedCallback(Action viewTransformChanged)
        {
            _viewTransformChanged = viewTransformChanged;
        }

        public void GetViewportState(out Vector3 scrollOffset, out float zoomScale)
        {
            GraphViewShellUtility.GetViewportState(this, out scrollOffset, out zoomScale);
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            _viewTransformChanged?.Invoke();
        }

        private void MarkActiveFlowDirty()
        {
            if (activeFlow != null)
            {
                EditorUtility.SetDirty(activeFlow);
            }

            _afterMutation?.Invoke();
            _viewTransformChanged?.Invoke();
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

            OnSelectionChanged?.Invoke(selectedItems, selectedEdge);
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
                            MarkActiveFlowDirty();
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
                        MarkActiveFlowDirty();
                    }
                    else if (element is Edge rawEdge)
                    {
                        Undo.RecordObject(activeFlow, "Delete Edge");
                        string fromId = rawEdge.output?.node?.viewDataKey;
                        string toId = rawEdge.input?.node?.viewDataKey;
                        activeFlow.edges.RemoveAll(e => e.fromId == fromId && e.toId == toId);
                        MarkActiveFlowDirty();
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
                    MarkActiveFlowDirty();
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

        public bool FocusElement(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return false;
            }

            GraphElement target = FindElementBySharedId(elementId);
            if (target == null)
            {
                return false;
            }

            ClearSelection();
            AddToSelection(target);
            target.BringToFront();
            FrameSelection();
            NotifySelectionChanged();
            return true;
        }

        public string ResolveElementName(string elementId)
        {
            if (activeFlow == null || string.IsNullOrWhiteSpace(elementId))
            {
                return "Dungeon Flow";
            }

            if (TryParseLinkElementId(elementId, out string fromId, out string toId))
            {
                RoomNode from = activeFlow.nodes.Find(n => n.id == fromId);
                RoomNode to = activeFlow.nodes.Find(n => n.id == toId);
                string fromName = from != null && !string.IsNullOrWhiteSpace(from.displayName) ? from.displayName : fromId;
                string toName = to != null && !string.IsNullOrWhiteSpace(to.displayName) ? to.displayName : toId;
                return fromName + " -> " + toName;
            }

            RoomNode node = activeFlow.nodes.Find(n => n.id == elementId);
            if (node == null)
            {
                return "Dungeon Flow";
            }

            return string.IsNullOrWhiteSpace(node.displayName) ? node.id : node.displayName;
        }

        public static string BuildLinkElementId(string fromId, string toId)
        {
            return "link:" + LinkKey(fromId ?? string.Empty, toId ?? string.Empty);
        }

        private GraphElement FindElementBySharedId(string elementId)
        {
            if (TryParseLinkElementId(elementId, out string fromId, out string toId))
            {
                return graphElements
                    .OfType<DungeonEdge>()
                    .FirstOrDefault(e => LinkKey(e.fromRoomId, e.toRoomId) == LinkKey(fromId, toId));
            }

            return GetNodeByGuid(elementId) ?? FindVisibleNodeNear(elementId);
        }

        private static bool TryParseLinkElementId(string elementId, out string fromId, out string toId)
        {
            fromId = null;
            toId = null;

            if (string.IsNullOrWhiteSpace(elementId) || !elementId.StartsWith("link:", StringComparison.Ordinal))
            {
                return false;
            }

            string payload = elementId.Substring("link:".Length);
            int separatorIndex = payload.IndexOf('|');
            if (separatorIndex < 0)
            {
                return false;
            }

            fromId = payload.Substring(0, separatorIndex);
            toId = payload.Substring(separatorIndex + 1);
            return !string.IsNullOrWhiteSpace(fromId) && !string.IsNullOrWhiteSpace(toId);
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
            MarkActiveFlowDirty();
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
            MarkActiveFlowDirty();
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
            MarkActiveFlowDirty();
        }

        public void ApplyDynamicCorridorCount(DungeonEdge edge)
        {
            if (edge == null || activeFlow == null) return;
            RoomNode roomA = activeFlow.nodes.Find(n => n.id == edge.fromRoomId);
            RoomNode roomB = activeFlow.nodes.Find(n => n.id == edge.toRoomId);
            if (roomA == null || roomB == null) return;

            SetCorridorLinkCount(edge, activeFlow.GetDynamicCorridorCount(roomA.position, roomB.position));
        }

        public void ApplyCurrentCorridorPlacementToAllLinks()
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

    }
}
