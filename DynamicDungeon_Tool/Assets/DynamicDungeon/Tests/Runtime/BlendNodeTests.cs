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
    public sealed class BlendNodeTests
    {
        private const float FloatTolerance = 0.0001f;

        [Test]
        public async Task MaskBlendNodeSelectsChannelAForTrueMaskAndChannelBForFalseMask()
        {
            float[] inputAValues =
            {
                1.0f,
                2.0f,
                3.0f,
                4.0f
            };

            float[] inputBValues =
            {
                10.0f,
                20.0f,
                30.0f,
                40.0f
            };

            byte[] maskValues =
            {
                1,
                0,
                1,
                0
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "A", inputAValues);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "B", inputBValues);
            BoolMaskSourceNode maskSourceNode = new BoolMaskSourceNode("mask-source", "Mask", maskValues);
            MaskBlendNode maskBlendNode = new MaskBlendNode("mask-blend-node", "Mask Blend", "A", "B", "Mask", "Output");

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, maskSourceNode, maskBlendNode }, 2, 2, 4101L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 1.0f, 20.0f, 3.0f, 40.0f }, output.Data);
        }

        [Test]
        public async Task WeightedBlendNodeProducesAAtZeroBAtOneAndMidpointAtHalfWeight()
        {
            float[] inputAValues =
            {
                2.0f,
                2.0f,
                2.0f
            };

            float[] inputBValues =
            {
                10.0f,
                10.0f,
                10.0f
            };

            float[] weightValues =
            {
                0.0f,
                1.0f,
                0.5f
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "A", inputAValues);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "B", inputBValues);
            FloatSourceNode weightSourceNode = new FloatSourceNode("weight-source", "Weight", weightValues);
            WeightedBlendNode weightedBlendNode = new WeightedBlendNode("weighted-blend-node", "Weighted Blend", "A", "B", "Weight", "Output");

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, weightSourceNode, weightedBlendNode }, 3, 1, 4102L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 2.0f, 10.0f, 6.0f }, output.Data);
        }

        [Test]
        public async Task LayerBlendNodeMultiplyOutputsPerTileProductWithinZeroToOne()
        {
            float[] inputAValues =
            {
                0.2f,
                0.8f,
                1.0f,
                0.5f
            };

            float[] inputBValues =
            {
                0.5f,
                0.25f,
                0.75f,
                0.4f
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "A", inputAValues);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "B", inputBValues);
            LayerBlendNode layerBlendNode = new LayerBlendNode("layer-blend-node", "Layer Blend", "A", "B", "Output", LayerBlendMode.Multiply);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, layerBlendNode }, 2, 2, 4103L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 0.1f, 0.2f, 0.75f, 0.2f }, output.Data);
            AssertValuesInRange(output.Data, 0.0f, 1.0f);
        }

        [Test]
        public async Task LayerBlendNodeDifferenceOutputsAbsoluteDifferencePerTile()
        {
            float[] inputAValues =
            {
                0.1f,
                0.9f,
                0.3f,
                0.8f
            };

            float[] inputBValues =
            {
                0.4f,
                0.2f,
                0.3f,
                0.1f
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "A", inputAValues);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "B", inputBValues);
            LayerBlendNode layerBlendNode = new LayerBlendNode("layer-blend-node", "Layer Blend", "A", "B", "Output", LayerBlendMode.Difference);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, layerBlendNode }, 2, 2, 4104L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 0.3f, 0.7f, 0.0f, 0.7f }, output.Data);
        }

        [Test]
        public async Task SelectNodeWithTwoChannelsAndOneThresholdSwitchesBetweenAAndB()
        {
            float[] inputAValues =
            {
                1.0f,
                2.0f,
                3.0f,
                4.0f
            };

            float[] inputBValues =
            {
                10.0f,
                20.0f,
                30.0f,
                40.0f
            };

            float[] controlValues =
            {
                0.2f,
                0.49f,
                0.5f,
                0.9f
            };

            FloatSourceNode sourceNodeA = new FloatSourceNode("float-source-a", "A", inputAValues);
            FloatSourceNode sourceNodeB = new FloatSourceNode("float-source-b", "B", inputBValues);
            FloatSourceNode controlSourceNode = new FloatSourceNode("control-source", "Control", controlValues);
            SelectNode selectNode = new SelectNode("select-node", "Select", "A", "B", string.Empty, string.Empty, "Control", "Output", 0.5f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, controlSourceNode, selectNode }, 2, 2, 4105L);
            WorldSnapshot.FloatChannelSnapshot output = GetFloatChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            AssertFloatArraysEqual(new[] { 1.0f, 2.0f, 30.0f, 40.0f }, output.Data);
        }

        [Test]
        public async Task CombineMasksNodeAndOutputsTrueOnlyWhereBothInputsAreTrue()
        {
            byte[] inputAValues =
            {
                0,
                1,
                1,
                0,
                1
            };

            byte[] inputBValues =
            {
                0,
                0,
                1,
                1,
                1
            };

            BoolMaskSourceNode sourceNodeA = new BoolMaskSourceNode("mask-source-a", "A", inputAValues);
            BoolMaskSourceNode sourceNodeB = new BoolMaskSourceNode("mask-source-b", "B", inputBValues);
            CombineMasksNode combineMasksNode = new CombineMasksNode("combine-masks-node", "Combine Masks", "A", "B", "Output", MaskOperation.AND);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, combineMasksNode }, 5, 1, 4106L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 0, 1 }, output.Data);
        }

        [Test]
        public async Task CombineMasksNodeNotInvertsAAndIgnoresB()
        {
            byte[] inputAValues =
            {
                0,
                1,
                1,
                0,
                1
            };

            byte[] inputBValues =
            {
                1,
                1,
                0,
                0,
                1
            };

            BoolMaskSourceNode sourceNodeA = new BoolMaskSourceNode("mask-source-a", "A", inputAValues);
            BoolMaskSourceNode sourceNodeB = new BoolMaskSourceNode("mask-source-b", "B", inputBValues);
            CombineMasksNode combineMasksNode = new CombineMasksNode("combine-masks-node", "Combine Masks", "A", "B", "Output", MaskOperation.NOT);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceNodeA, sourceNodeB, combineMasksNode }, 5, 1, 4107L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Output");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 1, 0 }, output.Data);
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
