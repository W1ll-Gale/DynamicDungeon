using System;
using System.Collections.Generic;
using System.Threading;
using DynamicDungeon.Runtime.Component;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    public class DungeonSolver : IDungeonSolver
    {
        public class SolverSettings
        {
            public int maxSearchSteps = 50000;
            public bool useRandomisation = true;
            public long seed = 0L;
            public bool enableDiagnostics;
        }

        private const int DefaultMaxSearchSteps = 50000;

        private readonly DungeonFlow flow;
        private readonly SolverSettings settings;
        private readonly Dictionary<GameObject, List<RoomVariant>> templateCache;
        private readonly TemplateCatalog templateCatalog;
        private readonly Action<float> progressCallback;
        private readonly CancellationToken cancellationToken;
        private readonly System.Random random;
        private readonly DungeonGenerationDiagnostics diagnostics;

        private readonly Dictionary<string, RoomNode> nodesById = new Dictionary<string, RoomNode>();
        private readonly Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, PlacedRoom> placedRoomsByNodeId = new Dictionary<string, PlacedRoom>();

        private DungeonLayout currentLayout;
        private PlacementEngine placementEngine;
        private List<RoomNode> sequence;
        private int backtrackSteps;
        public string LastFailureReason { get; private set; }

        public DungeonSolver(DungeonFlow flow, Dictionary<GameObject, List<RoomVariant>> cache, SolverSettings settings = null, Action<float> progress = null, CancellationToken token = default, DungeonGenerationDiagnostics diagnostics = null)
        {
            this.flow = flow;
            this.templateCache = cache;
            this.settings = settings ?? new SolverSettings();
            this.progressCallback = progress;
            this.cancellationToken = token;
            this.random = new System.Random(GenerationSeedUtility.ToRandomSeed(this.settings.seed));
            this.diagnostics = diagnostics;
            this.templateCatalog = null;
        }

        public DungeonSolver(DungeonFlow flow, TemplateCatalog catalog, SolverSettings settings = null, Action<float> progress = null, CancellationToken token = default, DungeonGenerationDiagnostics diagnostics = null)
            : this(flow, catalog?.Cache, settings, progress, token, diagnostics)
        {
            this.templateCatalog = catalog;
        }

        public DungeonLayout Generate()
        {
            currentLayout = new DungeonLayout();
            placementEngine = new PlacementEngine(currentLayout, templateCatalog, diagnostics);
            placedRoomsByNodeId.Clear();
            backtrackSteps = 0;
            LastFailureReason = null;

            if (flow == null || flow.nodes.Count == 0)
            {
                LastFailureReason = "Flow has no rooms.";
                return null;
            }

            BuildGraphCache();
            sequence = BuildPlacementSequence();
            return Backtrack(0) ? currentLayout : null;
        }

        private void BuildGraphCache()
        {
            nodesById.Clear();
            adjacency.Clear();

            foreach (RoomNode node in flow.nodes)
            {
                nodesById[node.id] = node;
                if (!adjacency.ContainsKey(node.id))
                {
                    adjacency[node.id] = new List<string>();
                }
            }

            foreach (RoomEdge edge in flow.edges)
            {
                if (!adjacency.TryGetValue(edge.fromId, out List<string> fromNeighbours))
                {
                    fromNeighbours = new List<string>();
                    adjacency[edge.fromId] = fromNeighbours;
                }

                if (!adjacency.TryGetValue(edge.toId, out List<string> toNeighbours))
                {
                    toNeighbours = new List<string>();
                    adjacency[edge.toId] = toNeighbours;
                }

                fromNeighbours.Add(edge.toId);
                toNeighbours.Add(edge.fromId);
            }
        }

        private List<RoomNode> BuildPlacementSequence()
        {
            List<RoomNode> result = new List<RoomNode>(flow.nodes.Count);
            HashSet<string> visited = new HashSet<string>();

            RoomNode root = PickBestRootNode();
            if (root == null)
            {
                return result;
            }

            result.Add(root);
            visited.Add(root.id);

            while (result.Count < flow.nodes.Count)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return result;
                }

                RoomNode next = PickBestFrontierNode(visited);
                if (next == null)
                {
                    break;
                }

                result.Add(next);
                visited.Add(next.id);
            }

            return result;
        }

        private RoomNode PickBestRootNode()
        {
            RoomNode best = null;
            foreach (RoomNode node in flow.nodes)
            {
                if (best == null || ComparePlacementPriority(node, best) < 0)
                {
                    best = node;
                }
            }

            return best;
        }

        private RoomNode PickBestFrontierNode(HashSet<string> visited)
        {
            RoomNode best = null;
            foreach (RoomNode placedNode in flow.nodes)
            {
                if (!visited.Contains(placedNode.id) || !adjacency.TryGetValue(placedNode.id, out List<string> neighbours))
                {
                    continue;
                }

                foreach (string neighbourId in neighbours)
                {
                    if (visited.Contains(neighbourId) || !nodesById.TryGetValue(neighbourId, out RoomNode candidate))
                    {
                        continue;
                    }

                    if (best == null || ComparePlacementPriority(candidate, best) < 0)
                    {
                        best = candidate;
                    }
                }
            }

            return best;
        }

        private int ComparePlacementPriority(RoomNode a, RoomNode b)
        {
            int degreeCompare = GetNeighbourCount(b.id).CompareTo(GetNeighbourCount(a.id));
            if (degreeCompare != 0)
            {
                return degreeCompare;
            }

            int templateCompare = GetTemplateOptionCount(a).CompareTo(GetTemplateOptionCount(b));
            if (templateCompare != 0)
            {
                return templateCompare;
            }

            int typeCompare = GetTypePriority(a.type).CompareTo(GetTypePriority(b.type));
            if (typeCompare != 0)
            {
                return typeCompare;
            }

            return string.CompareOrdinal(a.id, b.id);
        }

        private int GetTemplateOptionCount(RoomNode node)
        {
            List<GameObject> templates = GetTemplatesForNode(node);
            return templates != null && templates.Count > 0 ? templates.Count : int.MaxValue;
        }

        private int GetTypePriority(RoomType type)
        {
            return type switch
            {
                RoomType.Entrance => 0,
                RoomType.Boss => 1,
                RoomType.Room => 2,
                RoomType.Corridor => 3,
                _ => 4
            };
        }

        private bool Backtrack(int index)
        {
            if (!AdvanceStep())
            {
                return false;
            }

            if (index == sequence.Count)
            {
                return true;
            }

            progressCallback?.Invoke((float)index / sequence.Count);

            RoomNode node = sequence[index];
            List<GameObject> templatesToUse = GetTemplatesForNode(node);
            if (templatesToUse.Count == 0)
            {
                LastFailureReason = $"Node '{node.displayName}' has no allowed templates.";
                if (settings.enableDiagnostics)
                {
                    Debug.LogWarning($"[Solver] {LastFailureReason}");
                }

                return false;
            }

            PlacedRoom neighbourToConnect = GetFirstPlacedNeighbour(node.id);
            bool placed = neighbourToConnect == null
                ? TryPlaceRootNode(node, index, templatesToUse)
                : TryPlaceConnectedNode(node, index, templatesToUse, neighbourToConnect);

            if (!placed && string.IsNullOrEmpty(LastFailureReason))
            {
                LastFailureReason = neighbourToConnect == null
                    ? $"Could not place root node '{node.displayName}'."
                    : $"Could not connect node '{node.displayName}' to placed neighbour '{neighbourToConnect.node.displayName}'. Check socket types, door sizes, and overlap constraints.";
            }

            return placed;
        }

        private bool TryPlaceRootNode(RoomNode node, int index, List<GameObject> templatesToUse)
        {
            int neighboursToSatisfy = GetNeighbourCount(node.id);

            foreach (GameObject templateObj in templatesToUse)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                List<RoomVariant> variants = GetCandidateVariants(templateObj, neighboursToSatisfy);
                try
                {
                    foreach (RoomVariant variant in variants)
                    {
                        if (!AdvanceStep())
                        {
                            return false;
                        }

                        if (!placementEngine.TryPlaceRoot(node, templateObj, variant, Vector2Int.zero, out PlacedRoom room))
                        {
                            continue;
                        }

                        RegisterPlacedRoom(room);
                        if (Backtrack(index + 1))
                        {
                            return true;
                        }

                        RemovePlacedRoom(room);
                    }
                }
                finally
                {
                    ListPool<RoomVariant>.Release(variants);
                }
            }

            return false;
        }

        private bool TryPlaceConnectedNode(RoomNode node, int index, List<GameObject> templatesToUse, PlacedRoom neighbourToConnect)
        {
            int neighboursToSatisfy = GetNeighbourCount(node.id);

            foreach (GameObject templateObj in templatesToUse)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                List<RoomVariant> variants = GetCandidateVariants(templateObj, neighboursToSatisfy);
                try
                {
                    foreach (RoomVariant variant in variants)
                    {
                        List<DoorAnchor> neighbourAnchors = GetUnusedNeighbourAnchors(neighbourToConnect);
                        try
                        {
                            foreach (DoorAnchor neighbourDoor in neighbourAnchors)
                            {
                                List<DoorConnection> compatibleConnections = ListPool<DoorConnection>.Get();
                                try
                                {
                                    placementEngine.FillCompatibleDoorConnections(
                                        variant,
                                        neighbourDoor,
                                        compatibleConnections,
                                        settings.useRandomisation,
                                        random);

                                    if (compatibleConnections.Count == 0)
                                    {
                                        diagnostics?.RecordSocketRejection();
                                        continue;
                                    }

                                    foreach (DoorConnection connection in compatibleConnections)
                                    {
                                        if (TryPlaceAtDoorPair(node, index, templateObj, variant, connection.Anchor, neighbourToConnect, neighbourDoor))
                                        {
                                            return true;
                                        }
                                    }
                                }
                                finally
                                {
                                    ListPool<DoorConnection>.Release(compatibleConnections);
                                }
                            }
                        }
                        finally
                        {
                            ListPool<DoorAnchor>.Release(neighbourAnchors);
                        }
                    }
                }
                finally
                {
                    ListPool<RoomVariant>.Release(variants);
                }
            }

            return false;
        }

        private bool TryPlaceAtDoorPair(RoomNode node, int index, GameObject templateObj, RoomVariant variant, DoorAnchor myDoor, PlacedRoom neighbourToConnect, DoorAnchor neighbourDoor)
        {
            List<Vector2Int> myPositions = GetOrderedDoorPositions(myDoor);
            List<Vector2Int> neighbourPositions = GetOrderedDoorPositions(neighbourDoor);

            try
            {
                foreach (Vector2Int myBasePos in myPositions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    foreach (Vector2Int neighbourBasePos in neighbourPositions)
                    {
                        if (!AdvanceStep())
                        {
                            return false;
                        }

                        if (placementEngine.TryPlaceConnected(
                            node,
                            templateObj,
                            variant,
                            myDoor,
                            myBasePos,
                            neighbourToConnect,
                            neighbourDoor,
                            neighbourBasePos,
                            false,
                            false,
                            out PlacedRoom newRoom,
                            out PlacementMutation mutation))
                        {
                            RegisterPlacedRoom(newRoom);
                            if (Backtrack(index + 1))
                            {
                                mutation.Commit();
                                return true;
                            }

                            RemovePlacedRoom(newRoom);
                            mutation.Rollback();
                            currentLayout.RebuildOccupancy();
                        }
                    }
                }
            }
            finally
            {
                ListPool<Vector2Int>.Release(myPositions);
                ListPool<Vector2Int>.Release(neighbourPositions);
            }

            return false;
        }

        private List<GameObject> GetTemplatesForNode(RoomNode node)
        {
            return node.allowedTemplates != null && node.allowedTemplates.Count > 0
                ? node.allowedTemplates
                : flow.GetTemplatesForType(node.type);
        }

        private PlacedRoom GetFirstPlacedNeighbour(string nodeId)
        {
            if (!adjacency.TryGetValue(nodeId, out List<string> neighbours))
            {
                return null;
            }

            foreach (string neighbourId in neighbours)
            {
                if (placedRoomsByNodeId.TryGetValue(neighbourId, out PlacedRoom room))
                {
                    return room;
                }
            }

            return null;
        }

        private int GetNeighbourCount(string nodeId)
        {
            return adjacency.TryGetValue(nodeId, out List<string> neighbours) ? neighbours.Count : 0;
        }

        private List<RoomVariant> GetCandidateVariants(GameObject templateObj, int minimumAnchorCount)
        {
            List<RoomVariant> candidates = ListPool<RoomVariant>.Get();
            bool hasTemplateReference = !ReferenceEquals(templateObj, null);
            if (!hasTemplateReference || !templateCache.TryGetValue(templateObj, out List<RoomVariant> variants))
            {
                LastFailureReason = !hasTemplateReference
                    ? "Encountered a null template while building candidates."
                    : "A template was not prepared successfully.";
                return candidates;
            }

            foreach (RoomVariant variant in variants)
            {
                if (variant.anchors.Count >= minimumAnchorCount)
                {
                    candidates.Add(variant);
                }
            }

            ShuffleIfNeeded(candidates);
            return candidates;
        }

        private List<DoorAnchor> GetUnusedNeighbourAnchors(PlacedRoom room)
        {
            List<DoorAnchor> anchors = ListPool<DoorAnchor>.Get();
            foreach (DoorAnchor anchor in room.variant.anchors)
            {
                if (!room.usedAnchors.Contains(anchor))
                {
                    anchors.Add(anchor);
                }
            }

            ShuffleIfNeeded(anchors);
            return anchors;
        }

        private List<Vector2Int> GetOrderedDoorPositions(DoorAnchor anchor)
        {
            List<Vector2Int> positions = ListPool<Vector2Int>.Get();
            placementEngine.FillDoorBasePositions(anchor, positions, settings.useRandomisation, random);
            return positions;
        }

        private List<T> GetOrderedCopy<T>(List<T> source)
        {
            List<T> copy = new List<T>(source);
            ShuffleIfNeeded(copy);
            return copy;
        }

        private void AddPlacedRoom(PlacedRoom room)
        {
            currentLayout.AddRoom(room);
            RegisterPlacedRoom(room);
        }

        private void RegisterPlacedRoom(PlacedRoom room)
        {
            placedRoomsByNodeId[room.node.id] = room;
        }

        private void RemovePlacedRoom(PlacedRoom room)
        {
            currentLayout.RemoveRoom(room);
            placedRoomsByNodeId.Remove(room.node.id);
        }

        private bool AdvanceStep()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LastFailureReason = "Generation was cancelled.";
                return false;
            }

            backtrackSteps++;
            if (diagnostics != null)
            {
                diagnostics.searchSteps = backtrackSteps;
            }
            int maxSearchSteps = settings != null ? Mathf.Max(1, settings.maxSearchSteps) : DefaultMaxSearchSteps;
            if (backtrackSteps <= maxSearchSteps)
            {
                return true;
            }

            LastFailureReason = $"Search step limit reached ({maxSearchSteps}).";
            return false;
        }

        private void ShuffleIfNeeded<T>(List<T> list)
        {
            if (!settings.useRandomisation || list.Count <= 1)
            {
                return;
            }

            SolverPlacementUtility.Shuffle(list, random);
        }
    }
}
