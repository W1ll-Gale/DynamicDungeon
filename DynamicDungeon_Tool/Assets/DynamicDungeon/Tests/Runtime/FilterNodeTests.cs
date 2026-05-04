using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class FilterNodeTests
    {
        private const float FloatTolerance = 0.0001f;

        [Test]
        public async Task ClampNodeKeepsValuesWithinRangeAndPreservesValuesAlreadyInRange()
        {
            float[] inputValues =
            {
                -1.0f,
                0.25f,
                0.5f,
                0.75f,
                2.0f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            ClampNode clampNode = new ClampNode("clamp-node", "Clamp", "Input", "Output", 0.25f, 0.75f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, clampNode }, 5, 1, 1001L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 0.25f, 0.25f, 0.5f, 0.75f, 0.75f }, output.Data);
            AssertValuesInRange(output.Data, 0.25f, 0.75f);
        }

        [Test]
        public async Task RemapNodeMapsKnownValueAndFlatInputRangeOutputsZero()
        {
            float[] inputValues =
            {
                0.5f,
                0.75f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            RemapNode remapNode = new RemapNode("remap-node", "Remap", "Input", "Output", 0.0f, 1.0f, 10.0f, 20.0f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, remapNode }, 2, 1, 1002L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            Assert.That(output.Data[0], Is.EqualTo(15.0f).Within(FloatTolerance));

            RemapNode flatRangeRemapNode = new RemapNode("flat-remap-node", "Remap", "Input", "FlatOutput", 1.0f, 1.0f, 10.0f, 20.0f);
            WorldSnapshot flatSnapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, flatRangeRemapNode }, 2, 1, 1003L);
            WorldSnapshot.FloatChannelSnapshot flatOutput = GetFloatChannel(flatSnapshot, "FlatOutput");

            Assert.That(flatOutput, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 0.0f, 0.0f }, flatOutput.Data);
        }

        [Test]
        public async Task NormaliseNodeMapsInputMinimumToZeroAndMaximumToOne()
        {
            float[] inputValues =
            {
                -2.0f,
                0.0f,
                2.0f,
                6.0f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            NormaliseNode normaliseNode = new NormaliseNode("normalise-node", "Normalise", "Input", "Output");

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, normaliseNode }, 4, 1, 1004L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            Assert.That(GetMinValue(output.Data), Is.EqualTo(0.0f).Within(FloatTolerance));
            Assert.That(GetMaxValue(output.Data), Is.EqualTo(1.0f).Within(FloatTolerance));
        }

        [Test]
        public async Task NormaliseNodeOutputsZeroForFlatInputChannel()
        {
            float[] inputValues =
            {
                5.0f,
                5.0f,
                5.0f,
                5.0f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("flat-source", "Input", inputValues);
            NormaliseNode normaliseNode = new NormaliseNode("normalise-node", "Normalise", "Input", "Output");

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, normaliseNode }, 4, 1, 1005L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 0.0f, 0.0f, 0.0f, 0.0f }, output.Data);
        }

        [Test]
        public async Task StepNodeProducesExactlyRequestedDistinctValuesWithinZeroToOne()
        {
            float[] inputValues =
            {
                0.0f,
                0.2f,
                0.5f,
                0.8f,
                1.0f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            StepNode stepNode = new StepNode("step-node", "Step", "Input", "Output", 4);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, stepNode }, 5, 1, 1006L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            Assert.That(CountDistinctValues(output.Data), Is.EqualTo(4));
            AssertValuesInRange(output.Data, 0.0f, 1.0f);
        }

        [Test]
        public async Task SmoothstepNodeReachesEdgesAndHasSteeperMidpointSlope()
        {
            float[] inputValues =
            {
                0.0f,
                0.1f,
                0.45f,
                0.55f,
                0.9f,
                1.0f
            };

            FloatSourceNode sourceNode = new FloatSourceNode("float-source", "Input", inputValues);
            SmoothstepNode smoothstepNode = new SmoothstepNode("smoothstep-node", "Smoothstep", "Input", "Output", 0.0f, 1.0f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, smoothstepNode }, 6, 1, 1007L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            Assert.That(output.Data[0], Is.EqualTo(0.0f).Within(FloatTolerance));
            Assert.That(output.Data[5], Is.EqualTo(1.0f).Within(FloatTolerance));

            float lowerEdgeSlope = (output.Data[1] - output.Data[0]) / 0.1f;
            float midpointSlope = (output.Data[3] - output.Data[2]) / 0.1f;
            float upperEdgeSlope = (output.Data[5] - output.Data[4]) / 0.1f;

            Assert.That(midpointSlope, Is.GreaterThan(lowerEdgeSlope));
            Assert.That(midpointSlope, Is.GreaterThan(upperEdgeSlope));
        }

        [Test]
        public async Task EdgeDetectNodeMarksOnlyBorderTilesAroundUniformRegions()
        {
            int[] inputValues =
            {
                0, 0, 0, 0, 0,
                0, 1, 1, 1, 0,
                0, 1, 1, 1, 0,
                0, 1, 1, 1, 0,
                0, 0, 0, 0, 0
            };

            byte[] expectedValues =
            {
                0, 1, 1, 1, 0,
                1, 1, 1, 1, 1,
                1, 1, 0, 1, 1,
                1, 1, 1, 1, 1,
                0, 1, 1, 1, 0
            };

            IntSourceNode sourceNode = new IntSourceNode("int-source", "Input", inputValues);
            EdgeDetectNode edgeDetectNode = new EdgeDetectNode("edge-node", "Edge Detect", "Input", "Output", ChannelType.Int);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, edgeDetectNode }, 5, 5, 1008L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(expectedValues, output.Data);
            Assert.That(output.Data[12], Is.EqualTo(0));
        }

        [Test]
        public async Task DistanceFieldNodeNormalisesDistancesFromTrueTiles()
        {
            byte[] inputValues =
            {
                0, 0, 0,
                0, 1, 0,
                0, 0, 0
            };

            BoolMaskSourceNode sourceNode = new BoolMaskSourceNode("mask-source", "Input", inputValues);
            DistanceFieldNode distanceFieldNode = new DistanceFieldNode("distance-node", "Distance Field", "Input", "Output");

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, distanceFieldNode }, 3, 3, 1009L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            Assert.That(output.Data[4], Is.EqualTo(0.0f).Within(FloatTolerance));
            Assert.That(output.Data[1], Is.GreaterThan(0.0f));
            Assert.That(output.Data[1], Is.LessThan(1.0f));
            AssertValuesInRange(output.Data, 0.0f, 1.0f);
        }

        [Test]
        public async Task ColumnSurfaceBandNodeMarksOnlyTilesWithinDepthBelowHighestSolidPerColumn()
        {
            byte[] inputValues =
            {
                0, 0, 0,
                1, 0, 0,
                1, 0, 1,
                1, 1, 1,
                1, 1, 1
            };

            byte[] expectedValues =
            {
                0, 0, 0,
                0, 0, 0,
                1, 0, 1,
                1, 1, 1,
                0, 0, 0
            };

            BoolMaskSourceNode sourceNode = new BoolMaskSourceNode("surface-source", "Input", inputValues);
            ColumnSurfaceBandNode surfaceBandNode = new ColumnSurfaceBandNode("surface-band", "Surface Band", "Input", "Output", 1, 2);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNode, surfaceBandNode }, 3, 5, 1010L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(expectedValues, output.Data);
        }

        [Test]
        public async Task AxisBandNodeMarksOnlyCellsInsideXAxisRange()
        {
            byte[] expectedValues =
            {
                0, 1, 1, 0,
                0, 1, 1, 0
            };

            AxisBandNode axisBandNode = new AxisBandNode("axis-band", "Axis Band", "Output", GradientDirection.X, 0.25f, 0.74f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { axisBandNode }, 4, 2, 2024L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);

            byte[] actualValues = new byte[output.Data.Length];
            int index;
            for (index = 0; index < output.Data.Length; index++)
            {
                actualValues[index] = output.Data[index] > 0.5f ? (byte)1 : (byte)0;
            }

            CollectionAssert.AreEqual(expectedValues, actualValues);
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

        private static void AssertFloatArraysEqual(float[] expectedValues, float[] actualValues)
        {
            Assert.That(actualValues.Length, Is.EqualTo(expectedValues.Length));

            int index;
            for (index = 0; index < expectedValues.Length; index++)
            {
                Assert.That(actualValues[index], Is.EqualTo(expectedValues[index]).Within(FloatTolerance));
            }
        }

        private static void AssertValuesInRange(float[] values, float minValue, float maxValue)
        {
            int index;
            for (index = 0; index < values.Length; index++)
            {
                Assert.That(values[index], Is.InRange(minValue, maxValue));
            }
        }

        private static float GetMinValue(float[] values)
        {
            float minValue = float.MaxValue;

            int index;
            for (index = 0; index < values.Length; index++)
            {
                minValue = Math.Min(minValue, values[index]);
            }

            return minValue;
        }

        private static float GetMaxValue(float[] values)
        {
            float maxValue = float.MinValue;

            int index;
            for (index = 0; index < values.Length; index++)
            {
                maxValue = Math.Max(maxValue, values[index]);
            }

            return maxValue;
        }

        private static int CountDistinctValues(float[] values)
        {
            List<float> distinctValues = new List<float>();

            int index;
            for (index = 0; index < values.Length; index++)
            {
                if (!ContainsApproximateValue(distinctValues, values[index]))
                {
                    distinctValues.Add(values[index]);
                }
            }

            return distinctValues.Count;
        }

        private static bool ContainsApproximateValue(List<float> values, float targetValue)
        {
            int index;
            for (index = 0; index < values.Count; index++)
            {
                if (Math.Abs(values[index] - targetValue) <= FloatTolerance)
                {
                    return true;
                }
            }

            return false;
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

        private sealed class IntSourceNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly int[] _values;

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

            public IntSourceNode(string nodeId, string outputChannelName, int[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<int>();
                _ports = new[]
                {
                    new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Int)
                };
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.Int, true)
                };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<int> output = context.GetIntChannel(_channelDeclarations[0].ChannelName);

                int index;
                for (index = 0; index < output.Length; index++)
                {
                    output[index] = _values[index];
                }

                return context.InputDependency;
            }
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
    }
}
