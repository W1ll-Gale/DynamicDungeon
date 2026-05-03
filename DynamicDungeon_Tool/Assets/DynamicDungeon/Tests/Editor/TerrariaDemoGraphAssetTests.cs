using System;
using System.Linq;
using System.Threading;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEditor;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class TerrariaDemoGraphAssetTests
    {
        private const string DemoGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph.asset";
        private const string OrganizedGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph_Organized.asset";
        private const int OrganizedWrapperCount = 8;
        private const int MaxFidelityNodes = 170;
        private const int MaxFidelityEdges = 240;
        private const int MaxNodesPerGroup = 50;

        [Test]
        public void TerrariaDemoGraphLoadsCompilesAndKeepsFidelityAuthoringShape()
        {
            GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(DemoGraphPath);

            Assert.That(graph, Is.Not.Null);
            Assert.That(graph.SchemaVersion, Is.EqualTo(GraphSchemaVersion.Current));
            Assert.That(GraphOutputUtility.CountOutputNodes(graph), Is.EqualTo(1));
            Assert.That(graph.Nodes.Count, Is.LessThanOrEqualTo(MaxFidelityNodes));
            Assert.That(graph.Connections.Count, Is.LessThanOrEqualTo(MaxFidelityEdges));
            Assert.That(graph.StickyNotes.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(CountNodesOfType(graph, "DynamicDungeon.Runtime.Nodes.LogicalIdRuleStackNode"), Is.GreaterThanOrEqualTo(2));
            Assert.That(CountNonOutputSinks(graph), Is.EqualTo(0));

            int groupIndex;
            for (groupIndex = 0; groupIndex < graph.Groups.Count; groupIndex++)
            {
                GenGroupData group = graph.Groups[groupIndex];
                int nodeCount = group != null && group.ContainedNodeIds != null ? group.ContainedNodeIds.Count : 0;
                Assert.That(nodeCount, Is.LessThanOrEqualTo(MaxNodesPerGroup), group != null ? group.Title : string.Empty);
            }

            GraphCompileResult result = GraphCompiler.Compile(graph);

            Assert.That(result.IsSuccess, Is.True, JoinDiagnostics(result));
            Assert.That(result.Plan, Is.Not.Null);
            result.Plan.Dispose();
        }

        [Test]
        public void OrganizedTerrariaDemoGraphLoadsCompilesAndUsesSubgraphs()
        {
            GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(OrganizedGraphPath);

            Assert.That(graph, Is.Not.Null);
            Assert.That(graph.SchemaVersion, Is.EqualTo(GraphSchemaVersion.Current));
            Assert.That(GraphOutputUtility.CountOutputNodes(graph), Is.EqualTo(1));
            Assert.That(graph.Nodes.Count, Is.LessThanOrEqualTo(12));
            Assert.That(graph.Connections.Count, Is.LessThanOrEqualTo(60));
            Assert.That(CountNodesOfType(graph, typeof(SubGraphNode).FullName), Is.EqualTo(OrganizedWrapperCount));
            Assert.That(CountNonOutputSinks(graph), Is.EqualTo(0));

            foreach (GenNodeData wrapper in graph.Nodes.Where(node => node != null && node.NodeTypeName == typeof(SubGraphNode).FullName))
            {
                SerializedParameter nestedParameter = wrapper.Parameters.FirstOrDefault(parameter => parameter.Name == SubGraphNode.NestedGraphParameterName);
                Assert.That(nestedParameter, Is.Not.Null, wrapper.NodeName);
                Assert.That(nestedParameter.ObjectReference as GenGraph, Is.Not.Null, wrapper.NodeName);
            }

            GraphCompileResult result = GraphCompiler.Compile(graph);

            Assert.That(result.IsSuccess, Is.True, JoinDiagnostics(result));
            Assert.That(result.Plan, Is.Not.Null);
            result.Plan.Dispose();
        }

        [Test]
        public void OrganizedTerrariaDemoGraphExecutesLikeOriginalGraph()
        {
            GenGraph originalGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(DemoGraphPath);
            GenGraph organizedGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(OrganizedGraphPath);

            Assert.That(originalGraph, Is.Not.Null);
            Assert.That(organizedGraph, Is.Not.Null);

            ExecutionResult originalResult = Execute(originalGraph);
            ExecutionResult organizedResult = Execute(organizedGraph);

            Assert.That(organizedResult.IsSuccess, Is.True, organizedResult.ErrorMessage);
            CollectionAssert.AreEqual(ReadIntChannel(originalResult.Snapshot, "FinalLogicalIds"), ReadIntChannel(organizedResult.Snapshot, "FinalLogicalIds"));
            CollectionAssert.AreEqual(ReadIntChannel(originalResult.Snapshot, "Biome"), ReadIntChannel(organizedResult.Snapshot, "Biome"));
            AssertPrefabPlacementChannelsEqual(originalResult.Snapshot, organizedResult.Snapshot);
        }

        private static int CountNodesOfType(GenGraph graph, string nodeTypeName)
        {
            int count = 0;
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node != null && node.NodeTypeName == nodeTypeName)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountNonOutputSinks(GenGraph graph)
        {
            int count = 0;
            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData node = graph.Nodes[nodeIndex];
                if (node == null || GraphOutputUtility.IsOutputNode(node))
                {
                    continue;
                }

                if (!HasOutgoingConnection(graph, node.NodeId))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasOutgoingConnection(GenGraph graph, string nodeId)
        {
            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < graph.Connections.Count; connectionIndex++)
            {
                GenConnectionData connection = graph.Connections[connectionIndex];
                if (connection != null && connection.FromNodeId == nodeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string JoinDiagnostics(GraphCompileResult result)
        {
            if (result == null || result.Diagnostics == null)
            {
                return string.Empty;
            }

            string[] messages = new string[result.Diagnostics.Count];
            int index;
            for (index = 0; index < result.Diagnostics.Count; index++)
            {
                messages[index] = result.Diagnostics[index].Message;
            }

            return string.Join("\n", messages);
        }

        private static ExecutionResult Execute(GenGraph graph)
        {
            GraphCompileResult compileResult = GraphCompiler.Compile(graph);
            try
            {
                Assert.That(compileResult.IsSuccess, Is.True, JoinDiagnostics(compileResult));
                Assert.That(compileResult.Plan, Is.Not.Null);
                Executor executor = new Executor();
                return executor.Execute(compileResult.Plan, CancellationToken.None);
            }
            finally
            {
                if (compileResult.Plan != null)
                {
                    compileResult.Plan.Dispose();
                }
            }
        }

        private static int[] ReadIntChannel(WorldSnapshot snapshot, string channelName)
        {
            WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels.FirstOrDefault(candidate => string.Equals(candidate.Name, channelName, StringComparison.Ordinal));
            Assert.That(channel, Is.Not.Null, channelName);
            return channel.Data;
        }

        private static void AssertPrefabPlacementChannelsEqual(WorldSnapshot expected, WorldSnapshot actual)
        {
            Assert.That(actual.PrefabPlacementChannels.Length, Is.EqualTo(expected.PrefabPlacementChannels.Length));

            int channelIndex;
            for (channelIndex = 0; channelIndex < expected.PrefabPlacementChannels.Length; channelIndex++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot expectedChannel = expected.PrefabPlacementChannels[channelIndex];
                WorldSnapshot.PrefabPlacementListChannelSnapshot actualChannel = actual.PrefabPlacementChannels.FirstOrDefault(candidate => string.Equals(candidate.Name, expectedChannel.Name, StringComparison.Ordinal));
                Assert.That(actualChannel, Is.Not.Null, expectedChannel.Name);
                CollectionAssert.AreEqual(expectedChannel.Data, actualChannel.Data, expectedChannel.Name);
            }
        }
    }
}
