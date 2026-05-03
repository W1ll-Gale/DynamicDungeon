using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEditor;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class TerrariaDemoGraphAssetTests
    {
        private const string DemoGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph.asset";
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
    }
}
