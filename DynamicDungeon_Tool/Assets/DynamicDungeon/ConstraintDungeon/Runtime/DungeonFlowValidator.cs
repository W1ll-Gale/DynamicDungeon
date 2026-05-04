using System.Collections.Generic;
using System.Linq;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public static class DungeonFlowValidator
    {
        public sealed class Result
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<Issue> Issues = new List<Issue>();
            public readonly ValidationReport Report = new ValidationReport();
            public bool IsValid => Errors.Count == 0;
        }

        public sealed class Issue
        {
            public bool IsError;
            public string Message;
            public string NodeId;
            public string FromNodeId;
            public string ToNodeId;
        }

        public static Result Validate(DungeonFlow flow)
        {
            Result result = new Result();

            if (flow == null)
            {
                AddError(result, "No Dungeon Flow assigned.");
                return result;
            }

            if (flow.nodes == null || flow.nodes.Count == 0)
            {
                AddError(result, "Dungeon Flow has no nodes.");
                return result;
            }

            TemplateCatalog preparation = TemplatePreparer.PrepareForFlow(flow);
            foreach (string error in preparation.Errors)
            {
                AddError(result, error);
            }

            foreach (string warning in preparation.Warnings)
            {
                AddWarning(result, warning);
            }

            ValidateGraphReferences(flow, result);
            ValidateConnectivity(flow, result);
            ValidateTemplatesForNodes(flow, result);
            ValidateSocketCompatibility(flow, preparation, result);

            return result;
        }

        private static void ValidateGraphReferences(DungeonFlow flow, Result result)
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (RoomNode node in flow.nodes)
            {
                if (string.IsNullOrWhiteSpace(node.id))
                {
                    AddError(result, $"Node '{node.displayName}' has an empty id.", node.id);
                    continue;
                }

                if (!ids.Add(node.id))
                {
                    AddError(result, $"Duplicate node id '{node.id}'.", node.id);
                }
            }

            foreach (RoomEdge edge in flow.edges ?? new List<RoomEdge>())
            {
                if (!ids.Contains(edge.fromId))
                {
                    AddError(result, $"Edge references missing from-node '{edge.fromId}'.", null, edge.fromId, edge.toId);
                }

                if (!ids.Contains(edge.toId))
                {
                    AddError(result, $"Edge references missing to-node '{edge.toId}'.", null, edge.fromId, edge.toId);
                }
            }
        }

        private static void ValidateConnectivity(DungeonFlow flow, Result result)
        {
            Dictionary<string, List<string>> adjacency = new Dictionary<string, List<string>>();
            foreach (RoomNode node in flow.nodes)
            {
                if (string.IsNullOrEmpty(node.id))
                {
                    continue;
                }

                adjacency[node.id] = new List<string>();
            }

            if (adjacency.Count == 0)
            {
                return;
            }

            foreach (RoomEdge edge in flow.edges ?? new List<RoomEdge>())
            {
                if (!adjacency.ContainsKey(edge.fromId) || !adjacency.ContainsKey(edge.toId))
                {
                    continue;
                }

                adjacency[edge.fromId].Add(edge.toId);
                adjacency[edge.toId].Add(edge.fromId);
            }

            HashSet<string> visited = new HashSet<string>();
            Queue<string> queue = new Queue<string>();
            string rootId = adjacency.Keys.First();
            queue.Enqueue(rootId);
            visited.Add(rootId);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string next in adjacency[current])
                {
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            if (visited.Count != flow.nodes.Count)
            {
                RoomNode disconnected = flow.nodes.FirstOrDefault(n => !string.IsNullOrEmpty(n.id) && !visited.Contains(n.id));
                AddError(result, $"Flow graph is disconnected ({visited.Count}/{flow.nodes.Count} nodes reachable).", disconnected?.id);
            }
        }

        private static void ValidateTemplatesForNodes(DungeonFlow flow, Result result)
        {
            foreach (RoomNode node in flow.nodes)
            {
                List<GameObject> templates = node.allowedTemplates != null && node.allowedTemplates.Count > 0
                    ? node.allowedTemplates
                    : flow.GetTemplatesForType(node.type);

                if (templates == null || templates.All(t => t == null))
                {
                    AddError(result, $"Node '{node.displayName}' has no templates and no default templates for {node.type}.", node.id);
                }
            }

            foreach (DefaultTemplateMapping mapping in flow.defaultTemplates ?? new List<DefaultTemplateMapping>())
            {
                if (mapping.templates == null || mapping.templates.All(t => t == null))
                {
                    AddWarning(result, $"Default template mapping for {mapping.type} is empty.");
                }
            }
        }

        private static void ValidateSocketCompatibility(DungeonFlow flow, TemplateCatalog preparation, Result result)
        {
            DungeonFlow solverFlow = flow.CreateSolverFlow(true);
            try
            {
                foreach (RoomEdge edge in solverFlow.edges)
                {
                    RoomNode from = solverFlow.nodes.Find(n => n.id == edge.fromId);
                    RoomNode to = solverFlow.nodes.Find(n => n.id == edge.toId);
                    if (from == null || to == null)
                    {
                        continue;
                    }

                    if (!HasCompatibleTemplatePair(solverFlow, from, to, preparation))
                    {
                        AddWarning(result, $"No obvious compatible door sockets between '{from.displayName}' and '{to.displayName}'.", null, from.id, to.id);
                    }
                }
            }
            finally
            {
                DungeonFlow.DestroyTemporarySolverFlow(solverFlow);
            }
        }

        private static void AddError(Result result, string message, string nodeId = null, string fromNodeId = null, string toNodeId = null)
        {
            result.Errors.Add(message);
            result.Issues.Add(new Issue { IsError = true, Message = message, NodeId = nodeId, FromNodeId = fromNodeId, ToNodeId = toNodeId });
            result.Report.AddError(message, nodeId, fromNodeId, toNodeId);
        }

        private static void AddWarning(Result result, string message, string nodeId = null, string fromNodeId = null, string toNodeId = null)
        {
            result.Warnings.Add(message);
            result.Issues.Add(new Issue { IsError = false, Message = message, NodeId = nodeId, FromNodeId = fromNodeId, ToNodeId = toNodeId });
            result.Report.AddWarning(message, nodeId, fromNodeId, toNodeId);
        }

        private static bool HasCompatibleTemplatePair(DungeonFlow flow, RoomNode from, RoomNode to, TemplateCatalog preparation)
        {
            foreach (GameObject fromTemplate in GetTemplates(flow, from))
            {
                if (fromTemplate == null || !preparation.Cache.TryGetValue(fromTemplate, out List<RoomVariant> fromVariants))
                {
                    continue;
                }

                foreach (GameObject toTemplate in GetTemplates(flow, to))
                {
                    if (toTemplate == null || !preparation.Cache.TryGetValue(toTemplate, out List<RoomVariant> toVariants))
                    {
                        continue;
                    }

                    if (HasCompatibleVariantPair(fromVariants, toVariants))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<GameObject> GetTemplates(DungeonFlow flow, RoomNode node)
        {
            return node.allowedTemplates != null && node.allowedTemplates.Count > 0
                ? node.allowedTemplates
                : flow.GetTemplatesForType(node.type);
        }

        private static bool HasCompatibleVariantPair(List<RoomVariant> aVariants, List<RoomVariant> bVariants)
        {
            foreach (RoomVariant aVariant in aVariants)
            {
                foreach (RoomVariant bVariant in bVariants)
                {
                    foreach (DoorAnchor a in aVariant.anchors)
                    {
                        foreach (DoorAnchor b in bVariant.anchors)
                        {
                            if (a.socketType == b.socketType &&
                                a.size == b.size &&
                                a.direction == b.GetOppositeDirection())
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }
    }
}
