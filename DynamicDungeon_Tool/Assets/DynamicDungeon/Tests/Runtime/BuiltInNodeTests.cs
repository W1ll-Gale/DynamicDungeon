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
    public sealed class BuiltInNodeTests
    {
        [Test]
        public async Task ThresholdNodeMarksOnlyValuesAboveThresholdAsTrue()
        {
            float[] inputValues =
            {
                0.0f,
                0.4f,
                0.5f,
                0.5001f,
                1.0f,
                0.2f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            ThresholdNode thresholdNode = new ThresholdNode("threshold-node", "Threshold", "Input", "Mask", 0.5f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, thresholdNode }, 3, 2, 123L);
            WorldSnapshot.BoolMaskChannelSnapshot outputMask = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(outputMask, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1, 1, 0 }, outputMask.Data);
        }

        [Test]
        public async Task InvertNodeForFloatOutputsOneMinusInput()
        {
            float[] inputValues =
            {
                0.0f,
                0.25f,
                0.5f,
                0.75f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            InvertNode invertNode = new InvertNode("invert-node", "Invert Float", "Input", "Output", ChannelType.Float);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, invertNode }, 2, 2, 321L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 1.0f, 0.75f, 0.5f, 0.25f }, output.Data);
        }

        [Test]
        public async Task InvertNodeForBoolMaskOutputsBitwiseInverse()
        {
            byte[] inputValues =
            {
                0,
                1,
                1,
                0,
                0,
                1
            };

            BoolMaskSourceNode sourceNode = new BoolMaskSourceNode("mask-source", "Input", inputValues);
            InvertNode invertNode = new InvertNode("invert-node", "Invert Mask", "Input", "Output", ChannelType.BoolMask);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, invertNode }, 3, 2, 654L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 1, 1, 0 }, output.Data);
        }

        [Test]
        public async Task CellularAutomataWithClassicCaveRulesRemovesIsolatedSingleTileRegions()
        {
            CellularAutomataNode cellularAutomataNode = new CellularAutomataNode(
                "cellular-node",
                "Cellular Automata",
                string.Empty,
                "Mask",
                "3",
                "23456",
                5,
                0.45f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { cellularAutomataNode }, 64, 64, 112233L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(output, Is.Not.Null);
            Assert.That(HasIsolatedSingleTileRegion(output.Data, snapshot.Width, snapshot.Height), Is.False);
        }

        [Test]
        public async Task CellularAutomataOutputIsDeterministicForSameLocalSeed()
        {
            CellularAutomataNode firstNode = new CellularAutomataNode(
                "cellular-node",
                "Cellular Automata",
                string.Empty,
                "Mask",
                "3",
                "23456",
                5,
                0.45f);

            CellularAutomataNode secondNode = new CellularAutomataNode(
                "cellular-node",
                "Cellular Automata",
                string.Empty,
                "Mask",
                "3",
                "23456",
                5,
                0.45f);

            WorldSnapshot firstSnapshot = await ExecuteNodesAsync(new IGenNode[] { firstNode }, 48, 48, 445566L);
            WorldSnapshot secondSnapshot = await ExecuteNodesAsync(new IGenNode[] { secondNode }, 48, 48, 445566L);
            WorldSnapshot.BoolMaskChannelSnapshot firstOutput = GetBoolMaskChannel(firstSnapshot, "Mask");
            WorldSnapshot.BoolMaskChannelSnapshot secondOutput = GetBoolMaskChannel(secondSnapshot, "Mask");

            Assert.That(firstOutput, Is.Not.Null);
            Assert.That(secondOutput, Is.Not.Null);
            CollectionAssert.AreEqual(firstOutput.Data, secondOutput.Data);
        }

        [Test]
        public async Task MathNodeAddOutputsPerTileSum()
        {
            float[] valuesA =
            {
                1.0f,
                2.0f,
                3.0f,
                4.0f
            };

            float[] valuesB =
            {
                10.0f,
                20.0f,
                30.0f,
                40.0f
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "AInput", valuesA);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "BInput", valuesB);
            MathNode mathNode = new MathNode("math-node", "Math", "AInput", "BInput", "Output", MathOperation.Add);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, mathNode }, 2, 2, 778899L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 11.0f, 22.0f, 33.0f, 44.0f }, output.Data);
        }

        [Test]
        public void CastRegistryFloatToIntUsesFloor()
        {
            NativeArray<float> input = new NativeArray<float>(new[] { -1.2f, -0.1f, 0.0f, 2.9f }, Allocator.Temp);
            NativeArray<int> output = default;

            try
            {
                output = CastRegistry.Cast<int>(input, ChannelType.Float, ChannelType.Int, CastMode.FloatToIntFloor, Allocator.Temp);
                CollectionAssert.AreEqual(new[] { -2, -1, 0, 2 }, output.ToArray());
            }
            finally
            {
                if (output.IsCreated)
                {
                    output.Dispose();
                }

                input.Dispose();
            }
        }

        [Test]
        public void CastRegistryFloatToBoolMaskMarksOnlyValuesAbovePointFiveAsTrue()
        {
            NativeArray<float> input = new NativeArray<float>(new[] { 0.0f, 0.5f, 0.5001f, 1.0f }, Allocator.Temp);
            NativeArray<byte> output = default;

            try
            {
                output = CastRegistry.Cast<byte>(input, ChannelType.Float, ChannelType.BoolMask, CastMode.FloatToBoolMask, Allocator.Temp);
                CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 1 }, output.ToArray());
            }
            finally
            {
                if (output.IsCreated)
                {
                    output.Dispose();
                }

                input.Dispose();
            }
        }

        [Test]
        public void CastRegistryReportsBoolMaskToFloatAsUnsupported()
        {
            bool canCast = CastRegistry.CanCast(ChannelType.BoolMask, ChannelType.Float);

            Assert.That(canCast, Is.False);
        }

        [Test]
        public async Task PerlinThresholdCellularPipelineCompilesAndExecutesWithoutErrors()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.WorldWidth = 32;
            graph.WorldHeight = 32;
            graph.DefaultSeed = 123456L;

            try
            {
                GenNodeData perlinNode = new GenNodeData("perlin-node", typeof(PerlinNoiseNode).FullName, "Perlin", Vector2.zero);
                perlinNode.Ports.Add(new GenPortData("Noise", PortDirection.Output, ChannelType.Float));
                perlinNode.Parameters.Add(new SerializedParameter("frequency", "0.08"));
                perlinNode.Parameters.Add(new SerializedParameter("amplitude", "1.0"));
                perlinNode.Parameters.Add(new SerializedParameter("offset", "0,0"));
                perlinNode.Parameters.Add(new SerializedParameter("octaves", "3"));
                graph.Nodes.Add(perlinNode);

                GenNodeData thresholdNode = new GenNodeData("threshold-node", typeof(ThresholdNode).FullName, "Threshold", Vector2.zero);
                thresholdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
                thresholdNode.Ports.Add(new GenPortData("Mask", PortDirection.Output, ChannelType.BoolMask));
                thresholdNode.Parameters.Add(new SerializedParameter("threshold", "0.5"));
                graph.Nodes.Add(thresholdNode);

                GenNodeData cellularNode = new GenNodeData("cellular-node", typeof(CellularAutomataNode).FullName, "Cellular", Vector2.zero);
                cellularNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
                cellularNode.Ports.Add(new GenPortData("SmoothedMask", PortDirection.Output, ChannelType.BoolMask));
                cellularNode.Parameters.Add(new SerializedParameter("birthRule", "3"));
                cellularNode.Parameters.Add(new SerializedParameter("survivalRule", "23456"));
                cellularNode.Parameters.Add(new SerializedParameter("iterations", "5"));
                cellularNode.Parameters.Add(new SerializedParameter("initialFillProbability", "0.45"));
                graph.Nodes.Add(cellularNode);

                graph.Connections.Add(new GenConnectionData("perlin-node", "Noise", "threshold-node", "Input"));
                graph.Connections.Add(new GenConnectionData("threshold-node", "Mask", "cellular-node", "Input"));

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.ErrorMessage, Is.Null);
                Assert.That(executionResult.Snapshot, Is.Not.Null);

                WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(executionResult.Snapshot, "SmoothedMask");
                Assert.That(output, Is.Not.Null);
                Assert.That(CountTrueTiles(output.Data), Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static int CountTrueTiles(byte[] values)
        {
            int trueTileCount = 0;

            int index;
            for (index = 0; index < values.Length; index++)
            {
                if (values[index] != 0)
                {
                    trueTileCount++;
                }
            }

            return trueTileCount;
        }

        private static async Task<WorldSnapshot> ExecuteNodesAsync(IReadOnlyList<IGenNode> nodes, int width, int height, long seed)
        {
            ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, seed);
            Executor executor = new Executor();
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Snapshot, Is.Not.Null);
            return result.Snapshot;
        }

        private static WorldSnapshot.BoolMaskChannelSnapshot GetBoolMaskChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.BoolMaskChannels.Length; index++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channel = snapshot.BoolMaskChannels[index];
                if (channel.Name == channelName)
                {
                    return channel;
                }
            }

            return null;
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

        private static void AssertFloatArraysEqual(float[] expectedValues, float[] actualValues)
        {
            Assert.That(actualValues.Length, Is.EqualTo(expectedValues.Length));

            int index;
            for (index = 0; index < expectedValues.Length; index++)
            {
                Assert.That(actualValues[index], Is.EqualTo(expectedValues[index]).Within(0.0001f));
            }
        }

        private static bool HasIsolatedSingleTileRegion(byte[] values, int width, int height)
        {
            bool[] visited = new bool[values.Length];

            int index;
            for (index = 0; index < values.Length; index++)
            {
                if (values[index] == 0 || visited[index])
                {
                    continue;
                }

                int regionSize = FloodFillRegion(values, width, height, index, visited);
                if (regionSize == 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FloodFillRegion(byte[] values, int width, int height, int startIndex, bool[] visited)
        {
            Queue<int> openSet = new Queue<int>();
            openSet.Enqueue(startIndex);
            visited[startIndex] = true;
            int regionSize = 0;

            while (openSet.Count > 0)
            {
                int currentIndex = openSet.Dequeue();
                int x = currentIndex % width;
                int y = currentIndex / width;
                regionSize++;

                int offsetY;
                for (offsetY = -1; offsetY <= 1; offsetY++)
                {
                    int offsetX;
                    for (offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int neighbourX = x + offsetX;
                        int neighbourY = y + offsetY;
                        if (neighbourX < 0 || neighbourX >= width || neighbourY < 0 || neighbourY >= height)
                        {
                            continue;
                        }

                        int neighbourIndex = neighbourY * width + neighbourX;
                        if (visited[neighbourIndex] || values[neighbourIndex] == 0)
                        {
                            continue;
                        }

                        visited[neighbourIndex] = true;
                        openSet.Enqueue(neighbourIndex);
                    }
                }
            }

            return regionSize;
        }

        private sealed class BoolMaskSourceNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly byte[] _values;

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
                get;
            }

            public string NodeName
            {
                get;
            }

            public BoolMaskSourceNode(string nodeId, string outputChannelName, byte[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<byte>();
                _ports = new[]
                {
                    new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.BoolMask)
                };
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.BoolMask, true)
                };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<byte> output = context.GetBoolMaskChannel(_channelDeclarations[0].ChannelName);

                int index;
                for (index = 0; index < output.Length; index++)
                {
                    output[index] = _values[index];
                }

                return context.InputDependency;
            }
        }

        private sealed class FloatSourceNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly float[] _values;

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
                get;
            }

            public string NodeName
            {
                get;
            }

            public FloatSourceNode(string nodeId, string outputChannelName, float[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<float>();
                _ports = new[]
                {
                    new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Float)
                };
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
                };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<float> output = context.GetFloatChannel(_channelDeclarations[0].ChannelName);

                int index;
                for (index = 0; index < output.Length; index++)
                {
                    output[index] = _values[index];
                }

                return context.InputDependency;
            }
        }
    }
}
