using System;
using System.Collections.Generic;
using System.IO;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    internal static class TerrariaDemoGraphOrganizer
    {
        public const string SourceGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph.asset";
        public const string OrganizedGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph_Organized.asset";
        public const string OrganizedSubGraphFolder = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/SubGraphs/TerrariaDemoGraph_Organized";

        private const float WrapperColumnSpacing = 320.0f;
        private const float WrapperRowSpacing = 210.0f;
        private const float BoundarySpacing = 260.0f;
        private const string PrefabReservedVoidNodeId = "prefab-reserved-void-overlay";

        private static readonly SubGraphSpec[] _specs =
        {
            new SubGraphSpec("TerrainShape", "Terrain Shape", new Vector2(0.0f, 0.0f)),
            new SubGraphSpec("CaveGeneration", "Cave Generation", new Vector2(WrapperColumnSpacing, 0.0f)),
            new SubGraphSpec("MaterialContext", "Material Context", new Vector2(WrapperColumnSpacing * 2.0f, 0.0f)),
            new SubGraphSpec("BiomeLayout", "Biome Layout", new Vector2(WrapperColumnSpacing * 2.0f, WrapperRowSpacing)),
            new SubGraphSpec("CaveFeatureOverrides", "Cave Feature Overrides", new Vector2(WrapperColumnSpacing * 3.0f, 0.0f)),
            new SubGraphSpec("OreFeatureOverrides", "Ore Feature Overrides", new Vector2(WrapperColumnSpacing * 4.0f, 0.0f)),
            new SubGraphSpec("SurfacePropPlacement", "Surface Prop Placement", new Vector2(WrapperColumnSpacing * 5.0f, 0.0f)),
            new SubGraphSpec("CaveHousePlacement", "Cave House Placement", new Vector2(WrapperColumnSpacing * 5.0f, WrapperRowSpacing))
        };

        [MenuItem("DynamicDungeon/Examples/Rebuild Organized Terraria Graph")]
        public static void RebuildOrganizedGraphMenu()
        {
            BuildOrganizedGraph();
        }

        public static GenGraph BuildOrganizedGraph()
        {
            GenGraph sourceGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(SourceGraphPath);
            if (sourceGraph == null)
            {
                throw new InvalidOperationException("Could not load Terraria demo graph at '" + SourceGraphPath + "'.");
            }

            DeleteAssetIfExists(OrganizedGraphPath);
            DeleteAssetIfExists(OrganizedSubGraphFolder);
            EnsureFolder(OrganizedSubGraphFolder);

            GenGraph organizedGraph = ScriptableObject.CreateInstance<GenGraph>();
            organizedGraph.name = Path.GetFileNameWithoutExtension(OrganizedGraphPath);
            CopyGraphSettings(sourceGraph, organizedGraph);
            organizedGraph.Nodes = CloneNodes(sourceGraph.Nodes);
            organizedGraph.Connections = CloneConnections(sourceGraph.Connections);
            organizedGraph.StickyNotes = CloneStickyNotes(sourceGraph.StickyNotes);
            organizedGraph.Groups = CloneGroups(sourceGraph.Groups);
            AssetDatabase.CreateAsset(organizedGraph, OrganizedGraphPath);

            Dictionary<string, GenGroupData> sourceGroupsByNodeId = BuildGroupLookup(sourceGraph);
            Dictionary<string, SubGraphSpec> specsByKey = BuildSpecLookup();
            AssignNodesToSpecs(sourceGraph, sourceGroupsByNodeId, specsByKey);

            for (int specIndex = 0; specIndex < _specs.Length; specIndex++)
            {
                SubGraphSpec spec = _specs[specIndex];
                if (spec.NodeIds.Count == 0)
                {
                    throw new InvalidOperationException("Sub-graph spec '" + spec.DisplayName + "' did not receive any nodes.");
                }

                ConvertNodesToSubGraph(organizedGraph, spec);
            }

            LayoutTopLevelGraph(organizedGraph);
            ValidateOrganizedGraphShape(organizedGraph);
            ValidateCompiledGraph(organizedGraph);

            EditorUtility.SetDirty(organizedGraph);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<GenGraph>(OrganizedGraphPath);
        }

        private static void AssignNodesToSpecs(
            GenGraph sourceGraph,
            IReadOnlyDictionary<string, GenGroupData> sourceGroupsByNodeId,
            IReadOnlyDictionary<string, SubGraphSpec> specsByKey)
        {
            for (int nodeIndex = 0; nodeIndex < sourceGraph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = sourceGraph.Nodes[nodeIndex];
                if (node == null || ShouldStayInParent(node))
                {
                    continue;
                }

                GenGroupData group;
                sourceGroupsByNodeId.TryGetValue(node.NodeId ?? string.Empty, out group);

                string specKey = ResolveSpecKey(node, group);
                SubGraphSpec spec;
                if (!specsByKey.TryGetValue(specKey, out spec))
                {
                    throw new InvalidOperationException("Node '" + node.NodeName + "' could not be assigned to an organized sub-graph.");
                }

                spec.NodeIds.Add(node.NodeId ?? string.Empty);
            }
        }

        private static string ResolveSpecKey(GenNodeData node, GenGroupData group)
        {
            string nodeName = node.NodeName ?? string.Empty;
            string groupTitle = group != null ? group.Title ?? string.Empty : string.Empty;

            if (string.Equals(groupTitle, "Terrain Shape", StringComparison.Ordinal))
            {
                return IsCaveGenerationNode(nodeName) ? "CaveGeneration" : "TerrainShape";
            }

            if (string.Equals(groupTitle, "Material Context", StringComparison.Ordinal))
            {
                return "MaterialContext";
            }

            if (string.Equals(groupTitle, "Biome Layout", StringComparison.Ordinal))
            {
                return "BiomeLayout";
            }

            if (string.Equals(groupTitle, "Feature Overrides", StringComparison.Ordinal))
            {
                return IsOreFeatureNode(nodeName) ? "OreFeatureOverrides" : "CaveFeatureOverrides";
            }

            if (string.Equals(groupTitle, "Props", StringComparison.Ordinal))
            {
                return IsHouseNode(nodeName) ? "CaveHousePlacement" : "SurfacePropPlacement";
            }

            if (IsHouseNode(nodeName))
            {
                return "CaveHousePlacement";
            }

            if (IsOreFeatureNode(nodeName))
            {
                return "OreFeatureOverrides";
            }

            if (IsCaveFeatureNode(nodeName))
            {
                return "CaveFeatureOverrides";
            }

            if (IsSurfacePropNode(nodeName))
            {
                return "SurfacePropPlacement";
            }

            return "CaveGeneration";
        }

        private static bool ShouldStayInParent(GenNodeData node)
        {
            return GraphOutputUtility.IsOutputNode(node) ||
                   string.Equals(node.NodeId, PrefabReservedVoidNodeId, StringComparison.Ordinal);
        }

        private static bool IsCaveGenerationNode(string nodeName)
        {
            return ContainsAny(nodeName, "Pocket", "Automata", "Open Cave", "Open Caves", "Worm", "Cavern") ||
                   string.Equals(nodeName, "Forest Deep Cave Automata", StringComparison.Ordinal);
        }

        private static bool IsCaveFeatureNode(string nodeName)
        {
            return ContainsAny(nodeName, "Moss", "Ice Cave", "Crystal", "Wall Mask", "Neighbourhood", "Pocket IDs", "Visual Override");
        }

        private static bool IsOreFeatureNode(string nodeName)
        {
            return ContainsAny(nodeName, "Ore", "Coal", "Iron", "Gold", "Diamond");
        }

        private static bool IsHouseNode(string nodeName)
        {
            return nodeName.IndexOf("House", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSurfacePropNode(string nodeName)
        {
            return ContainsAny(nodeName, "Prop", "Tree", "Cactus", "Cloud", "Surface Floor", "One", "Zero");
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            for (int index = 0; index < needles.Length; index++)
            {
                if (value.IndexOf(needles[index], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ConvertNodesToSubGraph(GenGraph parentGraph, SubGraphSpec spec)
        {
            HashSet<string> selectedNodeIds = new HashSet<string>(spec.NodeIds, StringComparer.Ordinal);
            BoundaryBuildResult boundaries = BuildBoundaryData(parentGraph, selectedNodeIds);
            Rect bounds = CalculateBounds(parentGraph, selectedNodeIds);

            string nestedGraphPath = OrganizedSubGraphFolder + "/" + spec.AssetName + ".asset";
            GenGraph nestedGraph = ScriptableObject.CreateInstance<GenGraph>();
            nestedGraph.name = spec.AssetName;
            CopyGraphSettings(parentGraph, nestedGraph);
            AssetDatabase.CreateAsset(nestedGraph, nestedGraphPath);
            AssetDatabase.ImportAsset(nestedGraphPath, ImportAssetOptions.ForceSynchronousImport);

            PopulateNestedGraph(parentGraph, nestedGraph, spec, selectedNodeIds, boundaries, bounds);

            string nestedGraphGuid = AssetDatabase.AssetPathToGUID(nestedGraphPath);
            GenNodeData wrapper = CreateWrapperNode(spec, nestedGraph, nestedGraphGuid, nestedGraphPath, boundaries);
            RewriteParentGraph(parentGraph, selectedNodeIds, wrapper, boundaries);

            EditorUtility.SetDirty(nestedGraph);
            EditorUtility.SetDirty(parentGraph);
        }

        private static BoundaryBuildResult BuildBoundaryData(GenGraph graph, HashSet<string> selectedNodeIds)
        {
            BoundaryBuildResult result = new BoundaryBuildResult();
            Dictionary<string, BoundaryInput> inputsBySource = new Dictionary<string, BoundaryInput>(StringComparer.Ordinal);
            Dictionary<string, BoundaryOutput> outputsBySource = new Dictionary<string, BoundaryOutput>(StringComparer.Ordinal);
            HashSet<string> usedInputNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> usedOutputNames = new HashSet<string>(StringComparer.Ordinal);

            for (int connectionIndex = 0; connectionIndex < graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connection = graph.Connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                bool fromSelected = selectedNodeIds.Contains(connection.FromNodeId ?? string.Empty);
                bool toSelected = selectedNodeIds.Contains(connection.ToNodeId ?? string.Empty);
                if (fromSelected && toSelected)
                {
                    result.InternalConnections.Add(CloneConnection(connection));
                }
                else if (!fromSelected && toSelected)
                {
                    AddBoundaryInput(graph, connection, inputsBySource, usedInputNames, result.Inputs);
                }
                else if (fromSelected && !toSelected)
                {
                    AddBoundaryOutput(graph, connection, outputsBySource, usedOutputNames, result.Outputs);
                }
            }

            return result;
        }

        private static void AddBoundaryInput(
            GenGraph graph,
            GenConnectionData connection,
            Dictionary<string, BoundaryInput> inputsBySource,
            HashSet<string> usedInputNames,
            List<BoundaryInput> inputs)
        {
            GenNodeData fromNode = graph.GetNode(connection.FromNodeId);
            GenNodeData toNode = graph.GetNode(connection.ToNodeId);
            GenPortData sourcePort = FindPort(fromNode, connection.FromPortName);
            GenPortData targetPort = FindPort(toNode, connection.ToPortName);
            if (targetPort == null)
            {
                throw new InvalidOperationException("Could not resolve target port '" + connection.ToPortName + "' on '" + (toNode != null ? toNode.NodeName : connection.ToNodeId) + "'.");
            }

            string key = CreateBoundaryInputKey(connection, targetPort.Type);
            BoundaryInput input;
            if (!inputsBySource.TryGetValue(key, out input))
            {
                string baseName = ResolveReadablePortName(sourcePort, targetPort, fromNode != null ? fromNode.NodeName : "Input");
                string portName = CreateUniquePortName(baseName, usedInputNames);
                input = new BoundaryInput
                {
                    PortName = portName,
                    DisplayName = portName,
                    Type = targetPort.Type,
                    ParentConnection = CloneConnection(connection)
                };
                inputsBySource.Add(key, input);
                inputs.Add(input);
            }

            input.NestedConnections.Add(CloneConnection(connection));
        }

        private static void AddBoundaryOutput(
            GenGraph graph,
            GenConnectionData connection,
            Dictionary<string, BoundaryOutput> outputsBySource,
            HashSet<string> usedOutputNames,
            List<BoundaryOutput> outputs)
        {
            GenNodeData fromNode = graph.GetNode(connection.FromNodeId);
            GenPortData sourcePort = FindPort(fromNode, connection.FromPortName);
            if (sourcePort == null)
            {
                throw new InvalidOperationException("Could not resolve source port '" + connection.FromPortName + "' on '" + (fromNode != null ? fromNode.NodeName : connection.FromNodeId) + "'.");
            }

            string key = connection.FromNodeId + "\n" + connection.FromPortName;
            BoundaryOutput output;
            if (!outputsBySource.TryGetValue(key, out output))
            {
                string baseName = ResolveReadablePortName(sourcePort, null, fromNode != null ? fromNode.NodeName : "Output");
                string portName = CreateUniquePortName(baseName, usedOutputNames);
                output = new BoundaryOutput
                {
                    PortName = portName,
                    DisplayName = portName,
                    Type = sourcePort.Type,
                    SourceConnection = CloneConnection(connection)
                };
                outputsBySource.Add(key, output);
                outputs.Add(output);
            }

            output.ParentConnections.Add(CloneConnection(connection));
        }

        private static string CreateBoundaryInputKey(GenConnectionData connection, ChannelType targetType)
        {
            return string.Join(
                "\n",
                connection.FromNodeId ?? string.Empty,
                connection.FromPortName ?? string.Empty,
                connection.CastMode.ToString(),
                targetType.ToString());
        }

        private static void PopulateNestedGraph(
            GenGraph parentGraph,
            GenGraph nestedGraph,
            SubGraphSpec spec,
            HashSet<string> selectedNodeIds,
            BoundaryBuildResult boundaries,
            Rect bounds)
        {
            nestedGraph.Nodes.Clear();
            nestedGraph.Connections.Clear();
            nestedGraph.Groups.Clear();
            nestedGraph.StickyNotes.Clear();

            for (int nodeIndex = 0; nodeIndex < parentGraph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = parentGraph.Nodes[nodeIndex];
                if (node != null && selectedNodeIds.Contains(node.NodeId ?? string.Empty))
                {
                    nestedGraph.Nodes.Add(CloneNode(node));
                }
            }

            GenNodeData inputBoundary = new GenNodeData(
                spec.Key + "-input",
                typeof(SubGraphInputNode).FullName,
                SubGraphInputNode.DefaultNodeName,
                new Vector2(bounds.xMin - BoundarySpacing, bounds.center.y));
            for (int inputIndex = 0; inputIndex < boundaries.Inputs.Count; inputIndex++)
            {
                BoundaryInput input = boundaries.Inputs[inputIndex];
                inputBoundary.Ports.Add(new GenPortData(input.PortName, PortDirection.Output, input.Type, input.DisplayName));
                for (int nestedIndex = 0; nestedIndex < input.NestedConnections.Count; nestedIndex++)
                {
                    GenConnectionData original = input.NestedConnections[nestedIndex];
                    nestedGraph.Connections.Add(new GenConnectionData(inputBoundary.NodeId, input.PortName, original.ToNodeId, original.ToPortName));
                }
            }

            nestedGraph.Nodes.Add(inputBoundary);

            GenNodeData outputBoundary = new GenNodeData(
                spec.Key + "-output",
                typeof(SubGraphOutputNode).FullName,
                SubGraphOutputNode.DefaultNodeName,
                new Vector2(bounds.xMax + BoundarySpacing, bounds.center.y));
            for (int outputIndex = 0; outputIndex < boundaries.Outputs.Count; outputIndex++)
            {
                BoundaryOutput output = boundaries.Outputs[outputIndex];
                outputBoundary.Ports.Add(new GenPortData(output.PortName, PortDirection.Input, output.Type, output.DisplayName));
                nestedGraph.Connections.Add(new GenConnectionData(output.SourceConnection.FromNodeId, output.SourceConnection.FromPortName, outputBoundary.NodeId, output.PortName));
            }

            nestedGraph.Nodes.Add(outputBoundary);

            for (int connectionIndex = 0; connectionIndex < boundaries.InternalConnections.Count; connectionIndex++)
            {
                nestedGraph.Connections.Add(CloneConnection(boundaries.InternalConnections[connectionIndex]));
            }

            nestedGraph.Groups.Add(new GenGroupData
            {
                GroupId = spec.Key + "-group",
                Title = spec.DisplayName,
                Position = Expand(bounds, 120.0f),
                ContainedNodeIds = new List<string>(selectedNodeIds)
            });
        }

        private static GenNodeData CreateWrapperNode(
            SubGraphSpec spec,
            GenGraph nestedGraph,
            string nestedGraphGuid,
            string nestedGraphPath,
            BoundaryBuildResult boundaries)
        {
            GenNodeData wrapper = new GenNodeData(
                "organized-subgraph-" + spec.Key,
                typeof(SubGraphNode).FullName,
                spec.DisplayName,
                spec.Position);

            for (int inputIndex = 0; inputIndex < boundaries.Inputs.Count; inputIndex++)
            {
                BoundaryInput input = boundaries.Inputs[inputIndex];
                wrapper.Ports.Add(new GenPortData(input.PortName, PortDirection.Input, input.Type, input.DisplayName));
            }

            for (int outputIndex = 0; outputIndex < boundaries.Outputs.Count; outputIndex++)
            {
                BoundaryOutput output = boundaries.Outputs[outputIndex];
                wrapper.Ports.Add(new GenPortData(output.PortName, PortDirection.Output, output.Type, output.DisplayName));
            }

            wrapper.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphParameterName, nestedGraphGuid, nestedGraph));
            wrapper.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphPathParameterName, nestedGraphPath, nestedGraph));
            return wrapper;
        }

        private static void RewriteParentGraph(
            GenGraph parentGraph,
            HashSet<string> selectedNodeIds,
            GenNodeData wrapper,
            BoundaryBuildResult boundaries)
        {
            for (int nodeIndex = parentGraph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                GenNodeData node = parentGraph.Nodes[nodeIndex];
                if (node != null && selectedNodeIds.Contains(node.NodeId ?? string.Empty))
                {
                    parentGraph.Nodes.RemoveAt(nodeIndex);
                }
            }

            for (int connectionIndex = parentGraph.Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = parentGraph.Connections[connectionIndex];
                if (connection == null ||
                    selectedNodeIds.Contains(connection.FromNodeId ?? string.Empty) ||
                    selectedNodeIds.Contains(connection.ToNodeId ?? string.Empty))
                {
                    parentGraph.Connections.RemoveAt(connectionIndex);
                }
            }

            parentGraph.Nodes.Add(wrapper);

            for (int inputIndex = 0; inputIndex < boundaries.Inputs.Count; inputIndex++)
            {
                BoundaryInput input = boundaries.Inputs[inputIndex];
                GenConnectionData parentConnection = new GenConnectionData(
                    input.ParentConnection.FromNodeId,
                    input.ParentConnection.FromPortName,
                    wrapper.NodeId,
                    input.PortName);
                parentConnection.CastMode = input.ParentConnection.CastMode;
                parentGraph.Connections.Add(parentConnection);
            }

            for (int outputIndex = 0; outputIndex < boundaries.Outputs.Count; outputIndex++)
            {
                BoundaryOutput output = boundaries.Outputs[outputIndex];
                for (int connectionIndex = 0; connectionIndex < output.ParentConnections.Count; connectionIndex++)
                {
                    GenConnectionData original = output.ParentConnections[connectionIndex];
                    GenConnectionData parentConnection = new GenConnectionData(wrapper.NodeId, output.PortName, original.ToNodeId, original.ToPortName);
                    parentConnection.CastMode = original.CastMode;
                    parentGraph.Connections.Add(parentConnection);
                }
            }
        }

        private static void LayoutTopLevelGraph(GenGraph graph)
        {
            Dictionary<string, GenNodeData> nodesById = BuildNodeLookup(graph);
            for (int specIndex = 0; specIndex < _specs.Length; specIndex++)
            {
                SubGraphSpec spec = _specs[specIndex];
                GenNodeData wrapper;
                if (nodesById.TryGetValue("organized-subgraph-" + spec.Key, out wrapper))
                {
                    wrapper.Position = spec.Position;
                }
            }

            GenNodeData prefabReserved;
            if (nodesById.TryGetValue(PrefabReservedVoidNodeId, out prefabReserved))
            {
                prefabReserved.Position = new Vector2(WrapperColumnSpacing * 6.0f, 0.0f);
            }

            GenNodeData output = GraphOutputUtility.FindOutputNode(graph);
            if (output != null)
            {
                output.Position = new Vector2(WrapperColumnSpacing * 7.0f, 0.0f);
            }

            graph.Groups.Clear();
            graph.Groups.Add(CreateTopLevelGroup("organized-core-generation", "Core Generation", -80.0f, -80.0f, WrapperColumnSpacing * 3.0f + 260.0f, WrapperRowSpacing + 200.0f, "TerrainShape", "CaveGeneration", "MaterialContext", "BiomeLayout"));
            graph.Groups.Add(CreateTopLevelGroup("organized-features", "Feature Overrides", WrapperColumnSpacing * 3.0f - 80.0f, -80.0f, WrapperColumnSpacing * 2.0f + 260.0f, 200.0f, "CaveFeatureOverrides", "OreFeatureOverrides"));
            graph.Groups.Add(CreateTopLevelGroup("organized-placement", "Placement", WrapperColumnSpacing * 5.0f - 80.0f, -80.0f, 280.0f, WrapperRowSpacing + 200.0f, "SurfacePropPlacement", "CaveHousePlacement"));
            graph.Groups.Add(new GenGroupData
            {
                GroupId = "organized-output",
                Title = "Output",
                Position = new Rect(WrapperColumnSpacing * 6.0f - 80.0f, -80.0f, WrapperColumnSpacing + 280.0f, 200.0f),
                ContainedNodeIds = new List<string> { PrefabReservedVoidNodeId, output != null ? output.NodeId : string.Empty }
            });
        }

        private static GenGroupData CreateTopLevelGroup(string groupId, string title, float x, float y, float width, float height, params string[] specKeys)
        {
            GenGroupData group = new GenGroupData
            {
                GroupId = groupId,
                Title = title,
                Position = new Rect(x, y, width, height),
                ContainedNodeIds = new List<string>()
            };

            for (int index = 0; index < specKeys.Length; index++)
            {
                group.ContainedNodeIds.Add("organized-subgraph-" + specKeys[index]);
            }

            return group;
        }

        private static void ValidateOrganizedGraphShape(GenGraph graph)
        {
            if (GraphOutputUtility.CountOutputNodes(graph) != 1)
            {
                throw new InvalidOperationException("Organized graph must contain exactly one output node.");
            }

            HashSet<string> expectedParentNodes = new HashSet<string>(StringComparer.Ordinal)
            {
                PrefabReservedVoidNodeId,
                GraphOutputUtility.FindOutputNode(graph).NodeId
            };
            for (int specIndex = 0; specIndex < _specs.Length; specIndex++)
            {
                expectedParentNodes.Add("organized-subgraph-" + _specs[specIndex].Key);
            }

            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node == null || !expectedParentNodes.Contains(node.NodeId ?? string.Empty))
                {
                    throw new InvalidOperationException("Unexpected top-level node in organized graph: '" + (node != null ? node.NodeName : "<null>") + "'.");
                }
            }
        }

        private static void ValidateCompiledGraph(GenGraph graph)
        {
            GraphCompileResult result = GraphCompiler.Compile(graph);
            try
            {
                if (!result.IsSuccess)
                {
                    throw new InvalidOperationException("Organized graph failed to compile:\n" + JoinDiagnostics(result));
                }
            }
            finally
            {
                if (result.Plan != null)
                {
                    result.Plan.Dispose();
                }
            }
        }

        private static string JoinDiagnostics(GraphCompileResult result)
        {
            if (result == null || result.Diagnostics == null)
            {
                return string.Empty;
            }

            string[] messages = new string[result.Diagnostics.Count];
            for (int index = 0; index < result.Diagnostics.Count; index++)
            {
                messages[index] = result.Diagnostics[index].Message;
            }

            return string.Join("\n", messages);
        }

        private static Dictionary<string, GenGroupData> BuildGroupLookup(GenGraph graph)
        {
            Dictionary<string, GenGroupData> groupsByNodeId = new Dictionary<string, GenGroupData>(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < graph.Groups.Count; groupIndex++)
            {
                GenGroupData group = graph.Groups[groupIndex];
                if (group == null || group.ContainedNodeIds == null)
                {
                    continue;
                }

                for (int nodeIndex = 0; nodeIndex < group.ContainedNodeIds.Count; nodeIndex++)
                {
                    groupsByNodeId[group.ContainedNodeIds[nodeIndex] ?? string.Empty] = group;
                }
            }

            return groupsByNodeId;
        }

        private static Dictionary<string, SubGraphSpec> BuildSpecLookup()
        {
            Dictionary<string, SubGraphSpec> specsByKey = new Dictionary<string, SubGraphSpec>(StringComparer.Ordinal);
            for (int index = 0; index < _specs.Length; index++)
            {
                _specs[index].NodeIds.Clear();
                specsByKey.Add(_specs[index].Key, _specs[index]);
            }

            return specsByKey;
        }

        private static Dictionary<string, GenNodeData> BuildNodeLookup(GenGraph graph)
        {
            Dictionary<string, GenNodeData> nodesById = new Dictionary<string, GenNodeData>(StringComparer.Ordinal);
            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node != null)
                {
                    nodesById[node.NodeId ?? string.Empty] = node;
                }
            }

            return nodesById;
        }

        private static Rect CalculateBounds(GenGraph graph, HashSet<string> selectedNodeIds)
        {
            bool found = false;
            float minX = 0.0f;
            float minY = 0.0f;
            float maxX = 0.0f;
            float maxY = 0.0f;
            for (int nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node == null || !selectedNodeIds.Contains(node.NodeId ?? string.Empty))
                {
                    continue;
                }

                if (!found)
                {
                    minX = maxX = node.Position.x;
                    minY = maxY = node.Position.y;
                    found = true;
                }
                else
                {
                    minX = Mathf.Min(minX, node.Position.x);
                    minY = Mathf.Min(minY, node.Position.y);
                    maxX = Mathf.Max(maxX, node.Position.x);
                    maxY = Mathf.Max(maxY, node.Position.y);
                }
            }

            if (!found)
            {
                return new Rect(Vector2.zero, Vector2.one * 220.0f);
            }

            return Rect.MinMaxRect(minX, minY, maxX + 220.0f, maxY + 160.0f);
        }

        private static Rect Expand(Rect rect, float padding)
        {
            return new Rect(rect.xMin - padding, rect.yMin - padding, rect.width + padding * 2.0f, rect.height + padding * 2.0f);
        }

        private static GenPortData FindPort(GenNodeData node, string portName)
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

        private static string ResolveReadablePortName(GenPortData primaryPort, GenPortData fallbackPort, string finalFallback)
        {
            string name = primaryPort != null && !string.IsNullOrWhiteSpace(primaryPort.DisplayName)
                ? primaryPort.DisplayName
                : primaryPort != null ? primaryPort.PortName : string.Empty;
            if (string.IsNullOrWhiteSpace(name) && fallbackPort != null)
            {
                name = !string.IsNullOrWhiteSpace(fallbackPort.DisplayName) ? fallbackPort.DisplayName : fallbackPort.PortName;
            }

            return string.IsNullOrWhiteSpace(name) ? finalFallback : name;
        }

        private static string CreateUniquePortName(string baseName, HashSet<string> usedNames)
        {
            string safeName = string.IsNullOrWhiteSpace(baseName) ? "Port" : baseName.Trim();
            if (usedNames.Add(safeName))
            {
                return safeName;
            }

            int index = 2;
            string candidate;
            do
            {
                candidate = safeName + " " + index.ToString();
                index++;
            }
            while (!usedNames.Add(candidate));

            return candidate;
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

        private static List<GenNodeData> CloneNodes(IReadOnlyList<GenNodeData> source)
        {
            List<GenNodeData> nodes = new List<GenNodeData>();
            for (int nodeIndex = 0; source != null && nodeIndex < source.Count; nodeIndex++)
            {
                nodes.Add(CloneNode(source[nodeIndex]));
            }

            return nodes;
        }

        private static GenNodeData CloneNode(GenNodeData source)
        {
            GenNodeData node = new GenNodeData(source.NodeId, source.NodeTypeName, source.NodeName, source.Position);
            node.Ports = ClonePorts(source.Ports);
            node.Parameters = CloneParameters(source.Parameters);
            return node;
        }

        private static List<GenPortData> ClonePorts(IReadOnlyList<GenPortData> source)
        {
            List<GenPortData> ports = new List<GenPortData>();
            for (int portIndex = 0; source != null && portIndex < source.Count; portIndex++)
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
            for (int parameterIndex = 0; source != null && parameterIndex < source.Count; parameterIndex++)
            {
                SerializedParameter parameter = source[parameterIndex];
                if (parameter != null)
                {
                    parameters.Add(new SerializedParameter(parameter.Name, parameter.Value, parameter.ObjectReference));
                }
            }

            return parameters;
        }

        private static List<GenConnectionData> CloneConnections(IReadOnlyList<GenConnectionData> source)
        {
            List<GenConnectionData> connections = new List<GenConnectionData>();
            for (int connectionIndex = 0; source != null && connectionIndex < source.Count; connectionIndex++)
            {
                connections.Add(CloneConnection(source[connectionIndex]));
            }

            return connections;
        }

        private static GenConnectionData CloneConnection(GenConnectionData source)
        {
            GenConnectionData connection = new GenConnectionData(source.FromNodeId, source.FromPortName, source.ToNodeId, source.ToPortName);
            connection.CastMode = source.CastMode;
            return connection;
        }

        private static List<GenStickyNoteData> CloneStickyNotes(IReadOnlyList<GenStickyNoteData> source)
        {
            List<GenStickyNoteData> notes = new List<GenStickyNoteData>();
            for (int noteIndex = 0; source != null && noteIndex < source.Count; noteIndex++)
            {
                GenStickyNoteData note = source[noteIndex];
                if (note != null)
                {
                    notes.Add(new GenStickyNoteData { NoteId = note.NoteId, Text = note.Text, Position = note.Position });
                }
            }

            return notes;
        }

        private static List<GenGroupData> CloneGroups(IReadOnlyList<GenGroupData> source)
        {
            List<GenGroupData> groups = new List<GenGroupData>();
            for (int groupIndex = 0; source != null && groupIndex < source.Count; groupIndex++)
            {
                GenGroupData group = source[groupIndex];
                if (group != null)
                {
                    groups.Add(new GenGroupData
                    {
                        GroupId = group.GroupId,
                        Title = group.Title,
                        Position = group.Position,
                        BackgroundColor = group.BackgroundColor,
                        ContainedNodeIds = group.ContainedNodeIds != null ? new List<string>(group.ContainedNodeIds) : new List<string>()
                    });
                }
            }

            return groups;
        }

        private static List<ExposedProperty> CloneExposedProperties(IReadOnlyList<ExposedProperty> source)
        {
            List<ExposedProperty> properties = new List<ExposedProperty>();
            for (int propertyIndex = 0; source != null && propertyIndex < source.Count; propertyIndex++)
            {
                ExposedProperty property = source[propertyIndex];
                if (property != null)
                {
                    properties.Add(new ExposedProperty
                    {
                        PropertyId = property.PropertyId,
                        PropertyName = property.PropertyName,
                        Type = property.Type,
                        DefaultValue = property.DefaultValue,
                        Description = property.Description
                    });
                }
            }

            return properties;
        }

        private static void EnsureFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int partIndex = 1; partIndex < parts.Length; partIndex++)
            {
                string next = current + "/" + parts[partIndex];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[partIndex]);
                }

                current = next;
            }
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath) && (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null || AssetDatabase.IsValidFolder(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private sealed class SubGraphSpec
        {
            public readonly string Key;
            public readonly string DisplayName;
            public readonly string AssetName;
            public readonly Vector2 Position;
            public readonly List<string> NodeIds = new List<string>();

            public SubGraphSpec(string key, string displayName, Vector2 position)
            {
                Key = key;
                DisplayName = displayName;
                AssetName = key;
                Position = position;
            }
        }

        private sealed class BoundaryBuildResult
        {
            public readonly List<GenConnectionData> InternalConnections = new List<GenConnectionData>();
            public readonly List<BoundaryInput> Inputs = new List<BoundaryInput>();
            public readonly List<BoundaryOutput> Outputs = new List<BoundaryOutput>();
        }

        private sealed class BoundaryInput
        {
            public GenConnectionData ParentConnection;
            public readonly List<GenConnectionData> NestedConnections = new List<GenConnectionData>();
            public string PortName;
            public string DisplayName;
            public ChannelType Type;
        }

        private sealed class BoundaryOutput
        {
            public GenConnectionData SourceConnection;
            public readonly List<GenConnectionData> ParentConnections = new List<GenConnectionData>();
            public string PortName;
            public string DisplayName;
            public ChannelType Type;
        }
    }
}
