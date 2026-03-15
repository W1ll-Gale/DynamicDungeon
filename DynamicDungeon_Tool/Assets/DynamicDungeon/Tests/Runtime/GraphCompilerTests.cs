using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GraphCompilerTests
    {
        private const float DefaultFillValue = 6.5f;
        private const string FlatOutputChannelName = "FlatOutput";
        private const string CopyOutputChannelName = "CopiedOutput";

        [Test]
        public void ValidSingleNodeGraphCompilesSuccessfullyAndBuildsPlan()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddFlatFillNode(graph, "flat-node", "Flat Fill", DefaultFillValue);

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(CountDiagnostics(result.Diagnostics, DiagnosticSeverity.Error), Is.EqualTo(0));

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void UnknownNodeTypeProducesErrorDiagnosticAndFailsCompilation()
        {
            GenGraph graph = CreateGraph();
            try
            {
                GenNodeData node = new GenNodeData("unknown-node", "DynamicDungeon.Runtime.Nodes.DoesNotExistNode", "Missing Node", Vector2.zero);
                graph.Nodes.Add(node);

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Plan, Is.Null);
                Assert.That(ContainsError(result.Diagnostics, "could not be found"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void GraphWithCycleProducesCycleDiagnostic()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddCopyNode(
                    graph,
                    "copy-a",
                    "Copy A",
                    "ChannelB",
                    "ChannelA");

                AddCopyNode(
                    graph,
                    "copy-b",
                    "Copy B",
                    "ChannelA",
                    "ChannelB");

                graph.Connections.Add(new GenConnectionData("copy-a", "ChannelA", "copy-b", "ChannelA"));
                graph.Connections.Add(new GenConnectionData("copy-b", "ChannelB", "copy-a", "ChannelB"));

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Plan, Is.Null);
                Assert.That(ContainsError(result.Diagnostics, "Cycle detected"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void RequiredInputLeftUnconnectedProducesErrorDiagnostic()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddCopyNode(
                    graph,
                    "copy-node",
                    "Copy Node",
                    "RequiredInput",
                    CopyOutputChannelName);

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Plan, Is.Null);
                Assert.That(ContainsError(result.Diagnostics, "Required input port 'RequiredInput'"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void DuplicateChannelOwnershipProducesErrorDiagnostic()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddFlatFillNode(graph, "flat-a", "Flat A", 1.0f);
                AddFlatFillNode(graph, "flat-b", "Flat B", 2.0f);

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Plan, Is.Null);
                Assert.That(ContainsError(result.Diagnostics, "Channel 'FlatOutput' is owned"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task ValidTwoNodeGraphCompilesAndExecutesThroughExecutor()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddFlatFillNode(graph, "flat-node", "Flat Fill", DefaultFillValue);
                AddCopyNode(graph, "copy-node", "Copy Node", FlatOutputChannelName, CopyOutputChannelName);
                graph.Connections.Add(new GenConnectionData("flat-node", FlatOutputChannelName, "copy-node", FlatOutputChannelName));

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.WasCancelled, Is.False);
                Assert.That(executionResult.ErrorMessage, Is.Null);
                Assert.That(executionResult.Snapshot, Is.Not.Null);

                WorldSnapshot.FloatChannelSnapshot flatOutput = GetFloatChannel(executionResult.Snapshot, FlatOutputChannelName);
                WorldSnapshot.FloatChannelSnapshot copiedOutput = GetFloatChannel(executionResult.Snapshot, CopyOutputChannelName);

                Assert.That(flatOutput, Is.Not.Null);
                Assert.That(copiedOutput, Is.Not.Null);
                AssertAllValuesEqual(flatOutput.Data, DefaultFillValue);
                AssertAllValuesEqual(copiedOutput.Data, DefaultFillValue);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static void AddCopyNode(GenGraph graph, string nodeId, string nodeName, string inputPortName, string outputPortName)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(GraphCompilerCopyNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData(inputPortName, PortDirection.Input, ChannelType.Float));
            node.Ports.Add(new GenPortData(outputPortName, PortDirection.Output, ChannelType.Float));
            graph.Nodes.Add(node);
        }

        private static void AddFlatFillNode(GenGraph graph, string nodeId, string nodeName, float fillValue)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(FlatFillNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData(FlatOutputChannelName, PortDirection.Output, ChannelType.Float));
            node.Parameters.Add(new SerializedParameter("fillValue", fillValue.ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(node);
        }

        private static void AssertAllValuesEqual(float[] values, float expectedValue)
        {
            int index;
            for (index = 0; index < values.Length; index++)
            {
                Assert.That(values[index], Is.EqualTo(expectedValue));
            }
        }

        private static bool ContainsError(IReadOnlyList<GraphDiagnostic> diagnostics, string messageFragment)
        {
            int index;
            for (index = 0; index < diagnostics.Count; index++)
            {
                GraphDiagnostic diagnostic = diagnostics[index];
                if (diagnostic.Severity == DiagnosticSeverity.Error &&
                    diagnostic.Message != null &&
                    diagnostic.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics, DiagnosticSeverity severity)
        {
            int count = 0;

            int index;
            for (index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }

        private static GenGraph CreateGraph()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.WorldWidth = 4;
            graph.WorldHeight = 3;
            graph.DefaultSeed = 12345L;
            return graph;
        }

        private static WorldSnapshot.FloatChannelSnapshot GetFloatChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.FloatChannels.Length; index++)
            {
                WorldSnapshot.FloatChannelSnapshot channel = snapshot.FloatChannels[index];
                if (channel.Name == channelName)
                {
                    return channel;
                }
            }

            return null;
        }
    }

    internal sealed class GraphCompilerCopyNode : IGenNode
    {
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _inputChannelName;
        private readonly string _outputChannelName;

        public IReadOnlyList<NodePortDefinition> Ports
        {
            get
            {
                return _ports;
            }
        }

        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
        {
            get
            {
                return _channelDeclarations;
            }
        }

        public IReadOnlyList<BlackboardKey> BlackboardDeclarations
        {
            get
            {
                return Array.Empty<BlackboardKey>();
            }
        }

        public string NodeId
        {
            get
            {
                return _nodeId;
            }
        }

        public string NodeName
        {
            get
            {
                return _nodeName;
            }
        }

        public GraphCompilerCopyNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _inputChannelName = inputChannelName;
            _outputChannelName = outputChannelName;
            _ports = new[]
            {
                new NodePortDefinition(_inputChannelName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_inputChannelName, ChannelType.Float, false),
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            CopyFloatJob job = new CopyFloatJob
            {
                Input = input,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private struct CopyFloatJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<float> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }
    }
}
