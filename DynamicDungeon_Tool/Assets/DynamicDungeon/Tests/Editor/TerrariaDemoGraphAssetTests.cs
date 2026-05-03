using System.Linq;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEditor;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class TerrariaDemoGraphAssetTests
    {
        private const string OrganizedGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph_Organized.asset";
        private const int OrganizedWrapperCount = 8;

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
