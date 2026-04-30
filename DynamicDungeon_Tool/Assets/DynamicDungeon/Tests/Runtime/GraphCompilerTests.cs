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
        private const int DefaultFillValue = 6;
        private const string SharedOutputChannelName = "LogicalIds";
        private const string CopyOutputChannelName = "CopiedLogicalIds";
        private const string MergeOutputChannelName = "MergedLogicalIds";

        [Test]
        public void ValidSingleNodeGraphCompilesSuccessfullyAndBuildsPlan()
        {
            GenGraph graph = CreateGraph();
            try
            {
                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(result.HasConnectedOutput, Is.False);
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
                node.Ports.Add(new GenPortData(SharedOutputChannelName, PortDirection.Output, ChannelType.Int));
                graph.Nodes.Add(node);
                ConnectToOutput(graph, node.NodeId, SharedOutputChannelName);

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
                ConnectToOutput(graph, "copy-a", "ChannelA");

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
                ConnectToOutput(graph, "copy-node", CopyOutputChannelName);

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
        public async Task DuplicateVisibleOutputNamesCompileAndExecuteWhenInternalNamesAreUnique()
        {
            GenGraph graph = CreateGraph();
            try
            {
                string firstInternalOutputName = GraphPortNameUtility.CreateGeneratedOutputPortName("fill-a", GraphPortNameUtility.LegacyGenericOutputDisplayName);
                string secondInternalOutputName = GraphPortNameUtility.CreateGeneratedOutputPortName("fill-b", GraphPortNameUtility.LegacyGenericOutputDisplayName);

                AddIntFillNode(graph, "fill-a", "Fill A", firstInternalOutputName, 1, GraphPortNameUtility.LegacyGenericOutputDisplayName);
                AddIntFillNode(graph, "fill-b", "Fill B", secondInternalOutputName, 2, GraphPortNameUtility.LegacyGenericOutputDisplayName);
                AddMergeNode(graph, "merge-node", "Merge", MergeOutputChannelName);
                graph.Connections.Add(new GenConnectionData("fill-a", firstInternalOutputName, "merge-node", "Left"));
                graph.Connections.Add(new GenConnectionData("fill-b", secondInternalOutputName, "merge-node", "Right"));
                ConnectToOutput(graph, "merge-node", MergeOutputChannelName);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(ContainsError(compileResult.Diagnostics, "is owned"), Is.False);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(GetIntChannel(executionResult.Snapshot, firstInternalOutputName), Is.Not.Null);
                Assert.That(GetIntChannel(executionResult.Snapshot, secondInternalOutputName), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void DisconnectedInvalidBranchIsIgnoredWhenOutputPathIsValid()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddIntFillNode(graph, "fill-node", "Fill", SharedOutputChannelName, DefaultFillValue);
                AddCopyNode(graph, "disconnected-copy", "Disconnected Copy", "MissingInput", "UnusedOutput");
                ConnectToOutput(graph, "fill-node", SharedOutputChannelName);

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(ContainsError(result.Diagnostics, "MissingInput"), Is.False);

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileForPreviewIgnoresDisconnectedInvalidBranchWhenOutputPathIsValid()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddIntFillNode(graph, "fill-node", "Fill", SharedOutputChannelName, DefaultFillValue);

                GenNodeData disconnectedQueryNode = new GenNodeData(
                    "contextual-query",
                    typeof(ContextualQueryNode).FullName,
                    "Contextual Query",
                    Vector2.zero);
                disconnectedQueryNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Int));
                disconnectedQueryNode.Ports.Add(new GenPortData("Matches", PortDirection.Output, ChannelType.PointList));
                graph.Nodes.Add(disconnectedQueryNode);

                ConnectToOutput(graph, "fill-node", SharedOutputChannelName);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(ContainsError(result.Diagnostics, "Required input port 'Input'"), Is.False);

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileForPreviewIncludesConnectedDeadEndBranchEvenWhenOutputNodeExists()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddIntFillNode(graph, "fill-node", "Fill", SharedOutputChannelName, DefaultFillValue);
                AddIntFillNode(graph, "dead-end-source", "Dead End Source", "DeadEndSource", 9);
                AddCopyNode(graph, "dead-end-copy", "Dead End Copy", "DeadEndSource", "DeadEndOutput");

                graph.Connections.Add(new GenConnectionData("dead-end-source", "DeadEndSource", "dead-end-copy", "DeadEndSource"));
                ConnectToOutput(graph, "fill-node", SharedOutputChannelName);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(result.Plan.AllocatedWorld.HasIntChannel("DeadEndSource"), Is.True);
                Assert.That(result.Plan.AllocatedWorld.HasIntChannel("DeadEndOutput"), Is.True);

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileForPreviewIncludesDisconnectedStandaloneNoiseNode()
        {
            GenGraph graph = CreateGraph();
            try
            {
                string noiseNodeId = "surface-noise";
                string outputChannelName = GraphPortNameUtility.CreateGeneratedOutputPortName(noiseNodeId, "Output");
                GenNodeData noiseNode = new GenNodeData(noiseNodeId, typeof(SurfaceNoiseNode).FullName, "Surface Noise", Vector2.zero);
                noiseNode.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.Float, "Output"));
                graph.Nodes.Add(noiseNode);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Plan, Is.Not.Null);
                Assert.That(result.Plan.AllocatedWorld.HasFloatChannel(outputChannelName), Is.True);

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileForPreviewReportsErrorsOnConnectedInvalidDeadEndBranch()
        {
            GenGraph graph = CreateGraph();
            try
            {
                AddIntFillNode(graph, "fill-node", "Fill", SharedOutputChannelName, DefaultFillValue);
                AddCopyNode(graph, "invalid-copy", "Invalid Copy", "MissingInput", "UnusedOutput");
                AddCopyNode(graph, "downstream-copy", "Downstream Copy", "UnusedOutput", "DownstreamOutput");

                graph.Connections.Add(new GenConnectionData("invalid-copy", "UnusedOutput", "downstream-copy", "UnusedOutput"));
                ConnectToOutput(graph, "fill-node", SharedOutputChannelName);

                GraphCompileResult result = GraphCompiler.CompileForPreview(graph);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Plan, Is.Null);
                Assert.That(ContainsError(result.Diagnostics, "Required input port 'MissingInput'"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileSeedsInitialBlackboardValuesByPropertyId()
        {
            GenGraph graph = CreateGraph();
            try
            {
                ExposedProperty property = graph.AddExposedProperty("Seed Strength", ChannelType.Float, "4.5");

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.Plan.InitialNumericBlackboardValues, Is.Not.Null);
                Assert.That(
                    compileResult.Plan.InitialNumericBlackboardValues.ContainsKey(property.PropertyId),
                    Is.True);
                Assert.That(
                    compileResult.Plan.InitialNumericBlackboardValues.ContainsKey(property.PropertyName),
                    Is.False);
                Assert.That(
                    compileResult.Plan.InitialNumericBlackboardValues[property.PropertyId],
                    Is.EqualTo(4.5f));

                compileResult.Plan.Dispose();
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
                AddIntFillNode(graph, "fill-node", "Fill", SharedOutputChannelName, DefaultFillValue);
                AddCopyNode(graph, "copy-node", "Copy Node", SharedOutputChannelName, CopyOutputChannelName);
                graph.Connections.Add(new GenConnectionData("fill-node", SharedOutputChannelName, "copy-node", SharedOutputChannelName));
                ConnectToOutput(graph, "copy-node", CopyOutputChannelName);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.OutputChannelName, Is.EqualTo(CopyOutputChannelName));
                Assert.That(compileResult.HasConnectedOutput, Is.True);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.WasCancelled, Is.False);
                Assert.That(executionResult.ErrorMessage, Is.Null);
                Assert.That(executionResult.Snapshot, Is.Not.Null);

                WorldSnapshot.IntChannelSnapshot filledOutput = GetIntChannel(executionResult.Snapshot, SharedOutputChannelName);
                WorldSnapshot.IntChannelSnapshot copiedOutput = GetIntChannel(executionResult.Snapshot, CopyOutputChannelName);

                Assert.That(filledOutput, Is.Not.Null);
                Assert.That(copiedOutput, Is.Not.Null);
                AssertAllValuesEqual(filledOutput.Data, DefaultFillValue);
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
            node.Ports.Add(new GenPortData(inputPortName, PortDirection.Input, ChannelType.Int));
            node.Ports.Add(new GenPortData(outputPortName, PortDirection.Output, ChannelType.Int));
            graph.Nodes.Add(node);
        }

        private static void AddIntFillNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, int fillValue, string displayName = null)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(GraphCompilerIntFillNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.Int, displayName));
            node.Parameters.Add(new SerializedParameter("fillValue", fillValue.ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(node);
        }

        private static void AddMergeNode(GenGraph graph, string nodeId, string nodeName, string outputPortName)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(GraphCompilerMergeNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData("Left", PortDirection.Input, ChannelType.Int));
            node.Ports.Add(new GenPortData("Right", PortDirection.Input, ChannelType.Int));
            node.Ports.Add(new GenPortData(outputPortName, PortDirection.Output, ChannelType.Int));
            graph.Nodes.Add(node);
        }

        private static void ConnectToOutput(GenGraph graph, string fromNodeId, string fromPortName)
        {
            GenNodeData outputNode = GraphOutputUtility.FindOutputNode(graph);
            Assert.That(outputNode, Is.Not.Null);
            graph.Connections.Add(new GenConnectionData(fromNodeId, fromPortName, outputNode.NodeId, GraphOutputUtility.OutputInputPortName));
        }

        private static void AssertAllValuesEqual(int[] values, int expectedValue)
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
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 4;
            graph.WorldHeight = 3;
            graph.DefaultSeed = 12345L;
            GraphOutputUtility.EnsureSingleOutputNode(graph, false);
            return graph;
        }

        private static WorldSnapshot.IntChannelSnapshot GetIntChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels[index];
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
                new NodePortDefinition(_inputChannelName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_inputChannelName, ChannelType.Int, false),
                new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
            };
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> input = context.GetIntChannel(_inputChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);
            CopyIntJob job = new CopyIntJob
            {
                Input = input,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private struct CopyIntJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }
    }

    internal sealed class GraphCompilerIntFillNode : IGenNode
    {
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly int _fillValue;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public GraphCompilerIntFillNode(string nodeId, string nodeName, string outputChannelName, int fillValue)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _fillValue = fillValue;
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int)
            };
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
            };
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);
            FillIntJob job = new FillIntJob
            {
                Output = output,
                FillValue = _fillValue
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private struct FillIntJob : IJobParallelFor
        {
            public NativeArray<int> Output;
            public int FillValue;

            public void Execute(int index)
            {
                Output[index] = FillValue;
            }
        }
    }

    internal sealed class GraphCompilerMergeNode : IGenNode, IInputConnectionReceiver
    {
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;

        private string _leftChannelName = "Left";
        private string _rightChannelName = "Right";
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public GraphCompilerMergeNode(string nodeId, string nodeName, string outputChannelName)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
            _ports = new[]
            {
                new NodePortDefinition("Left", PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition("Right", PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string leftChannelName;
            string rightChannelName;
            _leftChannelName = inputConnections != null && inputConnections.TryGetValue("Left", out leftChannelName)
                ? leftChannelName ?? string.Empty
                : string.Empty;
            _rightChannelName = inputConnections != null && inputConnections.TryGetValue("Right", out rightChannelName)
                ? rightChannelName ?? string.Empty
                : string.Empty;
            RefreshChannelDeclarations();
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> left = context.GetIntChannel(_leftChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);
            CopyIntJob job = new CopyIntJob
            {
                Input = left,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_leftChannelName, ChannelType.Int, false),
                new ChannelDeclaration(_rightChannelName, ChannelType.Int, false),
                new ChannelDeclaration(_outputChannelName, ChannelType.Int, true)
            };
        }

        private struct CopyIntJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> Input;

            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }
    }
}
