using UnityEngine;
using System.Collections.Generic;
using System;
using System.Linq;

namespace DynamicDungeon.ConstraintDungeon
{
    public enum CorridorPlacementMode { Fixed, Dynamic }

    [Serializable]
    public class RoomNode
    {
        public string id;
        public string displayName;
        public Vector2 position; // For the graph editor view
        public RoomType type = RoomType.Room;
        public List<GameObject> allowedTemplates = new List<GameObject>(); // Changed from RoomTemplate to GameObject

        public RoomNode(string id)
        {
            this.id = id;
            this.displayName = id;
        }
    }

    [Serializable]
    public class RoomEdge
    {
        public string fromId;
        public string toId;

        public RoomEdge(string from, string to)
        {
            this.fromId = from;
            this.toId = to;
        }
    }

    [Serializable]
    public class DefaultTemplateMapping
    {
        public RoomType type;
        public List<GameObject> templates = new List<GameObject>(); // Changed from RoomTemplate to GameObject
    }

    [CreateAssetMenu(fileName = "NewDungeonFlow", menuName = "Dynamic Dungeon/Constraint Dungeon/Dungeon Flow")]
    public class DungeonFlow : ScriptableObject
    {
        public List<RoomNode> nodes = new List<RoomNode>();
        public List<RoomEdge> edges = new List<RoomEdge>();
        public List<DefaultTemplateMapping> defaultTemplates = new List<DefaultTemplateMapping>();

        [Header("Corridor Links")]
        public CorridorPlacementMode corridorPlacementMode = CorridorPlacementMode.Fixed;
        [Min(0)] public int fixedCorridorCount = 1;
        [Min(1f)] public float dynamicCorridorSpacing = 450f;
        [Min(0)] public int maxDynamicCorridorCount = 6;

        public List<GameObject> GetTemplatesForType(RoomType type)
        {
            DefaultTemplateMapping mapping = defaultTemplates.Find(m => m.type == type);
            return mapping?.templates ?? new List<GameObject>();
        }

        public int GetCorridorCountForConnection(Vector2 from, Vector2 to)
        {
            if (corridorPlacementMode == CorridorPlacementMode.Fixed)
                return Mathf.Max(0, fixedCorridorCount);

            return GetDynamicCorridorCount(from, to);
        }

        public int GetDynamicCorridorCount(Vector2 from, Vector2 to)
        {
            float spacing = Mathf.Max(1f, dynamicCorridorSpacing);
            int count = Mathf.CeilToInt(Vector2.Distance(from, to) / spacing) - 1;
            return Mathf.Clamp(Mathf.Max(0, count), 0, Mathf.Max(0, maxDynamicCorridorCount));
        }

        public DungeonFlow CreateSolverFlow(bool preserveCorridorCounts = true)
        {
            DungeonFlow solverFlow = CreateInstance<DungeonFlow>();
            solverFlow.hideFlags = HideFlags.HideAndDontSave;
            solverFlow.corridorPlacementMode = corridorPlacementMode;
            solverFlow.fixedCorridorCount = fixedCorridorCount;
            solverFlow.dynamicCorridorSpacing = dynamicCorridorSpacing;
            solverFlow.maxDynamicCorridorCount = maxDynamicCorridorCount;

            foreach (DefaultTemplateMapping mapping in defaultTemplates)
            {
                solverFlow.defaultTemplates.Add(new DefaultTemplateMapping
                {
                    type = mapping.type,
                    templates = new List<GameObject>(mapping.templates)
                });
            }

            Dictionary<string, RoomNode> roomNodes = nodes
                .Where(n => n.type != RoomType.Corridor)
                .GroupBy(n => n.id)
                .ToDictionary(g => g.Key, g => g.First());

            HashSet<string> keptNodeIds = new HashSet<string>();
            foreach (RoomNode room in roomNodes.Values)
            {
                solverFlow.nodes.Add(CopyNode(room));
                keptNodeIds.Add(room.id);
            }

            HashSet<string> uniqueLinks = new HashSet<string>();
            HashSet<string> processedCorridors = new HashSet<string>();

            foreach (RoomNode room in roomNodes.Values)
            {
                foreach (RoomEdge edge in edges.Where(e => e.fromId == room.id || e.toId == room.id))
                {
                    string nextId = GetOtherNodeId(edge, room.id);
                    if (string.IsNullOrEmpty(nextId)) continue;

                    if (roomNodes.ContainsKey(nextId))
                    {
                        string key = LinkKey(room.id, nextId);
                        if (uniqueLinks.Add(key))
                            solverFlow.edges.Add(new RoomEdge(room.id, nextId));
                        continue;
                    }

                    CorridorChain chain = TraceCorridorChain(room.id, nextId, roomNodes);
                    if (chain == null || chain.corridors.Any(c => processedCorridors.Contains(c.id))) continue;

                    string chainKey = LinkKey(chain.fromRoomId, chain.toRoomId);
                    if (!uniqueLinks.Add(chainKey))
                    {
                        foreach (RoomNode corridor in chain.corridors)
                            processedCorridors.Add(corridor.id);
                        continue;
                    }

                    if (preserveCorridorCounts)
                    {
                        string previousId = chain.fromRoomId;
                        foreach (RoomNode corridor in chain.corridors)
                        {
                            if (keptNodeIds.Add(corridor.id))
                                solverFlow.nodes.Add(CopyNode(corridor));

                            solverFlow.edges.Add(new RoomEdge(previousId, corridor.id));
                            previousId = corridor.id;
                        }

                        solverFlow.edges.Add(new RoomEdge(previousId, chain.toRoomId));
                    }
                    else
                    {
                        RoomNode solverCorridor = CopyNode(chain.corridors[0]);
                        solverCorridor.displayName = "Connector";
                        solverCorridor.position = (roomNodes[chain.fromRoomId].position + roomNodes[chain.toRoomId].position) * 0.5f;

                        if (keptNodeIds.Add(solverCorridor.id))
                            solverFlow.nodes.Add(solverCorridor);

                        solverFlow.edges.Add(new RoomEdge(chain.fromRoomId, solverCorridor.id));
                        solverFlow.edges.Add(new RoomEdge(solverCorridor.id, chain.toRoomId));
                    }

                    foreach (RoomNode corridor in chain.corridors)
                        processedCorridors.Add(corridor.id);
                }
            }

            return solverFlow;
        }

        public static void DestroyTemporarySolverFlow(DungeonFlow flow)
        {
            if (flow == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(flow);
                return;
            }
#endif

            UnityEngine.Object.Destroy(flow);
        }

        public bool HasExpandedCorridorLinks()
        {
            Dictionary<string, RoomNode> roomNodes = nodes
                .Where(n => n.type != RoomType.Corridor)
                .GroupBy(n => n.id)
                .ToDictionary(g => g.Key, g => g.First());

            HashSet<string> processedCorridors = new HashSet<string>();
            HashSet<string> uniqueLinks = new HashSet<string>();

            foreach (RoomNode room in roomNodes.Values)
            {
                foreach (RoomEdge edge in edges.Where(e => e.fromId == room.id || e.toId == room.id))
                {
                    string nextId = GetOtherNodeId(edge, room.id);
                    if (string.IsNullOrEmpty(nextId) || roomNodes.ContainsKey(nextId)) continue;

                    CorridorChain chain = TraceCorridorChain(room.id, nextId, roomNodes);
                    if (chain == null || chain.corridors.Any(c => processedCorridors.Contains(c.id))) continue;

                    string chainKey = LinkKey(chain.fromRoomId, chain.toRoomId);
                    if (uniqueLinks.Add(chainKey) && chain.corridors.Count > 1)
                        return true;

                    foreach (RoomNode corridor in chain.corridors)
                        processedCorridors.Add(corridor.id);
                }
            }

            return false;
        }

        private class CorridorChain
        {
            public string fromRoomId;
            public string toRoomId;
            public List<RoomNode> corridors = new List<RoomNode>();
        }

        private static RoomNode CopyNode(RoomNode node)
        {
            return new RoomNode(node.id)
            {
                displayName = node.displayName,
                position = node.position,
                type = node.type,
                allowedTemplates = new List<GameObject>(node.allowedTemplates)
            };
        }

        private static string LinkKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }

        private static string GetOtherNodeId(RoomEdge edge, string id)
        {
            if (edge.fromId == id) return edge.toId;
            if (edge.toId == id) return edge.fromId;
            return null;
        }

        private CorridorChain TraceCorridorChain(string startRoomId, string firstCorridorId, Dictionary<string, RoomNode> roomNodes)
        {
            string previousId = startRoomId;
            string currentId = firstCorridorId;
            HashSet<string> visited = new HashSet<string>();
            List<RoomNode> corridors = new List<RoomNode>();

            while (true)
            {
                RoomNode corridor = nodes.Find(n => n.id == currentId && n.type == RoomType.Corridor);
                if (corridor == null || !visited.Add(currentId)) return null;

                corridors.Add(corridor);

                List<string> nextIds = edges
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

        public void AddNode(string id, Vector2 pos)
        {
            nodes.Add(new RoomNode(id) { position = pos });
        }

        public void AddEdge(string from, string to)
        {
            // Avoid duplicates
            if (edges.Exists(e => (e.fromId == from && e.toId == to) || (e.fromId == to && e.toId == from)))
                return;
            
            edges.Add(new RoomEdge(from, to));
        }
    }
}
