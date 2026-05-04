using System;
using System.Linq;
using System.Threading;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class SubGraphConversionTests
    {
        private const string TestFolder = "Assets/__SubGraphConversionTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void ConvertSingleNodeCreatesWrapperNestedGraphAndBoundaryConnections()
        {
            GenGraph graph = CreateSavedGraph("SingleNodeGraph");
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();

            AddConstantNode(graph, "source", "Source", "Source", ChannelType.Float, 0.0f, 0, new Vector2(0.0f, 0.0f));
            AddThresholdNode(graph, "mask", "Mask", "Mask", new Vector2(300.0f, 0.0f));
            GenNodeData output = GraphOutputUtility.EnsureSingleOutputNode(graph);
            output.Position = new Vector2(620.0f, 0.0f);
            graph.Connections.Add(new GenConnectionData("source", "Source", "mask", "Input"));
            graph.Connections.Add(new GenConnectionData("mask", "Mask", output.NodeId, GraphOutputUtility.OutputInputPortName));
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();

            graphView.LoadGraph(graph);
            graphView.AddToSelection(FindNodeView(graphView, "mask"));

            graphView.ConvertSelectionToSubGraph();

            GenNodeData wrapper = graph.Nodes.Single(node => node.NodeTypeName == typeof(SubGraphNode).FullName);
            Assert.That(graph.GetNode("mask"), Is.Null);
            Assert.That(wrapper.Ports.Count(port => port.Direction == PortDirection.Input), Is.EqualTo(1));
            Assert.That(wrapper.Ports.Count(port => port.Direction == PortDirection.Output), Is.EqualTo(1));
            Assert.That(graph.Connections.Any(connection => connection.FromNodeId == "source" && connection.ToNodeId == wrapper.NodeId), Is.True);
            Assert.That(graph.Connections.Any(connection => connection.FromNodeId == wrapper.NodeId && connection.ToNodeId == output.NodeId), Is.True);

            GenGraph nestedGraph = wrapper.Parameters.Single(parameter => parameter.Name == SubGraphNode.NestedGraphParameterName).ObjectReference as GenGraph;
            Assert.That(nestedGraph, Is.Not.Null);
            Assert.That(wrapper.Parameters.Any(parameter => parameter.Name == "NestedGraphPath"), Is.False);
            Assert.That(nestedGraph.Nodes.Any(node => node.NodeId == "mask"), Is.True);
            Assert.That(nestedGraph.Nodes.Any(node => node.NodeTypeName == typeof(SubGraphInputNode).FullName), Is.True);
            Assert.That(nestedGraph.Nodes.Any(node => node.NodeTypeName == typeof(SubGraphOutputNode).FullName), Is.True);
            Assert.That(nestedGraph.Connections.Any(connection => connection.ToNodeId == "mask" && connection.ToPortName == "Input"), Is.True);
            Assert.That(nestedGraph.Connections.Any(connection => connection.FromNodeId == "mask" && connection.FromPortName == "Mask"), Is.True);
        }

        [Test]
        public void ConvertSingleOutputFeedingMultipleExternalTargetsCreatesOneWrapperOutput()
        {
            GenGraph graph = CreateSavedGraph("FanoutGraph");
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();

            AddConstantNode(graph, "source", "Source", "Source", ChannelType.Float, 0.25f, 0, new Vector2(0.0f, 0.0f));
            AddThresholdNode(graph, "mask-a", "Mask A", "MaskA", new Vector2(300.0f, -80.0f));
            AddThresholdNode(graph, "mask-b", "Mask B", "MaskB", new Vector2(300.0f, 80.0f));
            graph.Connections.Add(new GenConnectionData("source", "Source", "mask-a", "Input"));
            graph.Connections.Add(new GenConnectionData("source", "Source", "mask-b", "Input"));
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();

            graphView.LoadGraph(graph);
            graphView.AddToSelection(FindNodeView(graphView, "source"));

            graphView.ConvertSelectionToSubGraph();

            GenNodeData wrapper = graph.Nodes.Single(node => node.NodeTypeName == typeof(SubGraphNode).FullName);
            Assert.That(wrapper.Ports.Count(port => port.Direction == PortDirection.Output), Is.EqualTo(1));
            Assert.That(graph.Connections.Count(connection => connection.FromNodeId == wrapper.NodeId), Is.EqualTo(2));

            GenGraph nestedGraph = wrapper.Parameters.Single(parameter => parameter.Name == SubGraphNode.NestedGraphParameterName).ObjectReference as GenGraph;
            Assert.That(wrapper.Parameters.Any(parameter => parameter.Name == "NestedGraphPath"), Is.False);
            GenNodeData outputBoundary = nestedGraph.Nodes.Single(node => node.NodeTypeName == typeof(SubGraphOutputNode).FullName);
            Assert.That(outputBoundary.Ports.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConvertedGraphCompilesAndExecutesLikeFlatGraph()
        {
            GenGraph flatGraph = ScriptableObject.CreateInstance<GenGraph>();
            GenGraph convertedGraph = CreateSavedGraph("ExecutionGraph");
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();

            try
            {
                ConfigureConstantOutputGraph(flatGraph, "ids", 7);
                ConfigureConstantOutputGraph(convertedGraph, "ids", 7);
                graphView.LoadGraph(convertedGraph);
                graphView.AddToSelection(FindNodeView(graphView, "ids"));
                graphView.ConvertSelectionToSubGraph();

                ExecutionResult flatResult = Execute(flatGraph);
                ExecutionResult convertedResult = Execute(convertedGraph);

                Assert.That(convertedResult.IsSuccess, Is.True);
                Assert.That(ReadIntChannel(convertedResult.Snapshot, "Ids"), Is.EqualTo(ReadIntChannel(flatResult.Snapshot, "Ids")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(flatGraph);
            }
        }

        [Test]
        public void CompileReportsMissingNestedGraphReference()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                GenNodeData wrapper = new GenNodeData("wrapper", typeof(SubGraphNode).FullName, "Wrapper", Vector2.zero);
                wrapper.Ports.Add(new GenPortData("Output", PortDirection.Output, ChannelType.Int));
                wrapper.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphParameterName, string.Empty));
                graph.Nodes.Add(wrapper);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("no nested graph reference")), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileReportsRecursiveSubGraphReference()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                GenNodeData wrapper = new GenNodeData("wrapper", typeof(SubGraphNode).FullName, "Wrapper", Vector2.zero);
                wrapper.Parameters.Add(new SerializedParameter(SubGraphNode.NestedGraphParameterName, string.Empty, graph));
                graph.Nodes.Add(wrapper);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("Recursive sub-graph reference")), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static GenGraph CreateSavedGraph(string name)
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder("Assets", "__SubGraphConversionTests");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(TestFolder + "/" + name + ".asset");
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 4;
            graph.WorldHeight = 4;
            AssetDatabase.CreateAsset(graph, path);
            return graph;
        }

        private static void ConfigureConstantOutputGraph(GenGraph graph, string nodeId, int value)
        {
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 4;
            graph.WorldHeight = 4;
            AddConstantNode(graph, nodeId, "Ids", "Ids", ChannelType.Int, 0.0f, value, Vector2.zero);
            GenNodeData output = GraphOutputUtility.EnsureSingleOutputNode(graph);
            output.Position = new Vector2(300.0f, 0.0f);
            graph.Connections.Add(new GenConnectionData(nodeId, "Ids", output.NodeId, GraphOutputUtility.OutputInputPortName));
        }

        private static ExecutionResult Execute(GenGraph graph)
        {
            GraphCompileResult compileResult = GraphCompiler.Compile(graph);
            Assert.That(compileResult.IsSuccess, Is.True, string.Join("\n", compileResult.Diagnostics.Select(diagnostic => diagnostic.Message)));
            Executor executor = new Executor();
            return executor.Execute(compileResult.Plan, CancellationToken.None);
        }

        private static int[] ReadIntChannel(WorldSnapshot snapshot, string channelName)
        {
            return snapshot.IntChannels.Single(channel => channel.Name == channelName).Data;
        }

        private static GenNodeView FindNodeView(DynamicDungeonGraphView graphView, string nodeId)
        {
            return graphView.graphElements.OfType<GenNodeView>().Single(nodeView => nodeView.NodeData.NodeId == nodeId);
        }

        private static void AddConstantNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, ChannelType outputType, float floatValue, int intValue, Vector2 position)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(ConstantNode).FullName, nodeName, position);
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, outputType));
            node.Parameters.Add(new SerializedParameter("outputChannelName", outputChannelName));
            node.Parameters.Add(new SerializedParameter("outputType", outputType.ToString()));
            node.Parameters.Add(new SerializedParameter("floatValue", floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            node.Parameters.Add(new SerializedParameter("intValue", intValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            graph.Nodes.Add(node);
        }

        private static void AddThresholdNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, Vector2 position)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(ThresholdNode).FullName, nodeName, position);
            node.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.BoolMask));
            node.Parameters.Add(new SerializedParameter("outputChannelName", outputChannelName));
            node.Parameters.Add(new SerializedParameter("threshold", "0.5"));
            graph.Nodes.Add(node);
        }
    }
}
