using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class PointGenerationNodeTests
    {
        [Test]
        public async Task PoissonDiscSamplerNodeIsDeterministicAndRespectsMinimumDistance()
        {
            byte[] eligibleMask =
            {
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1
            };

            PoissonDiscSamplerNode firstNode = new PoissonDiscSamplerNode("poisson-node", "Poisson", "Mask", "Points", 2.0f, 30);
            PoissonDiscSamplerNode secondNode = new PoissonDiscSamplerNode("poisson-node", "Poisson", "Mask", "Points", 2.0f, 30);

            Vector2Int[] firstRun = SortPointsStable((await ExecutePoissonAsync(firstNode, eligibleMask, 6, 6)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecutePoissonAsync(secondNode, eligibleMask, 6, 6)).PointListChannels[0].Data);

            Assert.That(firstRun, Is.Not.Empty);
            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsUseEligibleTiles(firstRun, eligibleMask, 6);
            AssertAllPointPairsRespectMinimumDistance(firstRun, 2.0f);
        }

        [Test]
        public async Task PoissonDiscSamplerNodeSeedsAdditionalDisconnectedEligibleRegions()
        {
            byte[] eligibleMask =
            {
                1, 1, 0, 0, 0, 0,
                1, 1, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 1, 1,
                0, 0, 0, 0, 1, 1,
                0, 0, 0, 0, 0, 0
            };

            PoissonDiscSamplerNode node = new PoissonDiscSamplerNode("poisson-node", "Poisson", "Mask", "Points", 1.5f, 30);

            Vector2Int[] points = (await ExecutePoissonAsync(node, eligibleMask, 6, 6)).PointListChannels[0].Data;

            Assert.That(points, Is.Not.Empty);
            Assert.That(ContainsPointInRegion(points, 0, 0, 1, 1), Is.True);
            Assert.That(ContainsPointInRegion(points, 4, 3, 5, 4), Is.True);
            AssertAllPointsUseEligibleTiles(points, eligibleMask, 6);
            AssertAllPointPairsRespectMinimumDistance(points, 1.5f);
        }

        [Test]
        public async Task PoissonDiscSamplerNodeHonoursPointCountLimitDeterministically()
        {
            byte[] eligibleMask =
            {
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1
            };

            PoissonDiscSamplerNode firstNode = new PoissonDiscSamplerNode("poisson-node-limit", "Poisson", "Mask", "Points", 1.0f, 30, 4);
            PoissonDiscSamplerNode secondNode = new PoissonDiscSamplerNode("poisson-node-limit", "Poisson", "Mask", "Points", 1.0f, 30, 4);

            Vector2Int[] firstRun = SortPointsStable((await ExecutePoissonAsync(firstNode, eligibleMask, 6, 6)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecutePoissonAsync(secondNode, eligibleMask, 6, 6)).PointListChannels[0].Data);

            Assert.That(firstRun.Length, Is.EqualTo(4));
            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsUseEligibleTiles(firstRun, eligibleMask, 6);
            AssertAllPointPairsRespectMinimumDistance(firstRun, 1.0f);
        }

        [Test]
        public async Task StochasticScatterNodeThresholdSkipsWeightsBelowThresholdAndAlwaysKeepsFullWeights()
        {
            float[] weights =
            {
                1.0f, 0.4f, 0.0f,
                1.0f, 0.49f, 0.0f
            };

            FixedFloatMapNode weightNode = new FixedFloatMapNode("weights", "Weights", weights);
            StochasticScatterNode scatterNode = new StochasticScatterNode("scatter-node", "Scatter", "Weights", "Points", 0.5f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { weightNode, scatterNode }, 3, 2, 4302L);
            WorldSnapshot.PointListChannelSnapshot pointList = GetPointListChannel(snapshot, "Points");

            Assert.That(pointList, Is.Not.Null);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(0, 1)
                },
                pointList.Data);
        }

        [Test]
        public async Task StochasticScatterNodeHonoursPointCountLimitDeterministically()
        {
            float[] weights =
            {
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f
            };

            FixedFloatMapNode weightNodeA = new FixedFloatMapNode("weights-a", "Weights", weights);
            FixedFloatMapNode weightNodeB = new FixedFloatMapNode("weights-b", "Weights", weights);
            StochasticScatterNode firstNode = new StochasticScatterNode("scatter-node-limit", "Scatter", "Weights", "Points", 0.0f, 3);
            StochasticScatterNode secondNode = new StochasticScatterNode("scatter-node-limit", "Scatter", "Weights", "Points", 0.0f, 3);

            Vector2Int[] firstRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { weightNodeA, firstNode }, 4, 4, 4306L)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { weightNodeB, secondNode }, 4, 4, 4306L)).PointListChannels[0].Data);

            Assert.That(firstRun.Length, Is.EqualTo(3));
            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsStayWithinBounds(firstRun, 4, 4);
        }

        [Test]
        public async Task PointListOffsetNodeMovesPointsAndDropsOutOfBounds()
        {
            GraphCompilerPointListNode pointsNode = new GraphCompilerPointListNode("points", "Points", "InputPoints");
            pointsNode.ReceiveParameter("points", "0,0;2,1;3,3");

            PointListOffsetNode offsetNode = new PointListOffsetNode("offset", "Offset", outputChannelName: "OffsetPoints", offsetX: 0, offsetY: 1);
            InputConnectionMap inputConnections = new InputConnectionMap();
            inputConnections.SetConnections("Points", new[] { "InputPoints" });
            offsetNode.ReceiveInputConnections(inputConnections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { pointsNode, offsetNode }, 4, 4, 4309L);
            WorldSnapshot.PointListChannelSnapshot pointList = GetPointListChannel(snapshot, "OffsetPoints");

            Assert.That(SortPointsStable(pointList.Data), Is.EqualTo(new[]
            {
                new Vector2Int(0, 1),
                new Vector2Int(2, 2)
            }));
        }

        [Test]
        public async Task PointGridNodePlacesOnePointPerCellAtCentresWhenJitterIsDisconnected()
        {
            PointGridNode gridNode = new PointGridNode("grid-node", "Grid", string.Empty, "Points", 2, 0.0f);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { gridNode }, 5, 4, 4303L);
            WorldSnapshot.PointListChannelSnapshot pointList = GetPointListChannel(snapshot, "Points");

            Assert.That(pointList, Is.Not.Null);
            CollectionAssert.AreEqual(
                new[]
                {
                    new Vector2Int(1, 1),
                    new Vector2Int(3, 1),
                    new Vector2Int(4, 1),
                    new Vector2Int(1, 3),
                    new Vector2Int(3, 3),
                    new Vector2Int(4, 3)
                },
                pointList.Data);
        }

        [Test]
        public async Task PointGridNodeJitterIsDeterministicAndStaysWithinBounds()
        {
            float[] jitterValues =
            {
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 1.0f
            };

            FixedFloatMapNode jitterNodeA = new FixedFloatMapNode("jitter-source-a", "Jitter", jitterValues);
            FixedFloatMapNode jitterNodeB = new FixedFloatMapNode("jitter-source-b", "Jitter", jitterValues);
            PointGridNode firstGridNode = new PointGridNode("grid-node", "Grid", "Jitter", "Points", 2, 1.0f);
            PointGridNode secondGridNode = new PointGridNode("grid-node", "Grid", "Jitter", "Points", 2, 1.0f);

            Vector2Int[] firstRun = (await ExecuteNodesAsync(new IGenNode[] { jitterNodeA, firstGridNode }, 4, 4, 4304L)).PointListChannels[0].Data;
            Vector2Int[] secondRun = (await ExecuteNodesAsync(new IGenNode[] { jitterNodeB, secondGridNode }, 4, 4, 4304L)).PointListChannels[0].Data;

            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsStayWithinBounds(firstRun, 4, 4);
        }

        [Test]
        public async Task PointGridNodeHonoursPointCountLimitDeterministically()
        {
            PointGridNode firstNode = new PointGridNode("grid-node-limit", "Grid", string.Empty, "Points", 2, 0.0f, 2);
            PointGridNode secondNode = new PointGridNode("grid-node-limit", "Grid", string.Empty, "Points", 2, 0.0f, 2);

            Vector2Int[] firstRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { firstNode }, 5, 4, 4307L)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { secondNode }, 5, 4, 4307L)).PointListChannels[0].Data);

            Assert.That(firstRun.Length, Is.EqualTo(2));
            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsStayWithinBounds(firstRun, 5, 4);
        }

        [Test]
        public async Task EdgeFinderNodeRespectsSideSelectionAndMinimumRunLength()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0,
                1, 1, 2, 2,
                1, 1, 2, 2,
                0, 0, 0, 0
            };

            FixedLogicalMapNode mapNodeA = new FixedLogicalMapNode("logical-map-a", "LogicalIds", 4, 4, logicalIds);
            FixedLogicalMapNode mapNodeB = new FixedLogicalMapNode("logical-map-b", "LogicalIds", 4, 4, logicalIds);
            FixedLogicalMapNode mapNodeC = new FixedLogicalMapNode("logical-map-c", "LogicalIds", 4, 4, logicalIds);

            EdgeFinderNode sideANode = new EdgeFinderNode("edge-a", "Edge A", "LogicalIds", "Points", false, string.Empty, 1, false, string.Empty, 2, EdgeSide.SideA, 2);
            EdgeFinderNode bothNode = new EdgeFinderNode("edge-both", "Edge Both", "LogicalIds", "Points", false, string.Empty, 1, false, string.Empty, 2, EdgeSide.Both, 2);
            EdgeFinderNode filteredNode = new EdgeFinderNode("edge-filtered", "Edge Filtered", "LogicalIds", "Points", false, string.Empty, 1, false, string.Empty, 2, EdgeSide.Both, 3);

            WorldSnapshot sideASnapshot = await ExecuteNodesAsync(new IGenNode[] { mapNodeA, sideANode }, 4, 4, 4305L);
            WorldSnapshot bothSnapshot = await ExecuteNodesAsync(new IGenNode[] { mapNodeB, bothNode }, 4, 4, 4305L);
            WorldSnapshot filteredSnapshot = await ExecuteNodesAsync(new IGenNode[] { mapNodeC, filteredNode }, 4, 4, 4305L);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2Int(1, 1),
                    new Vector2Int(1, 2)
                },
                sideASnapshot.PointListChannels[0].Data);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2Int(1, 1),
                    new Vector2Int(2, 1),
                    new Vector2Int(1, 2),
                    new Vector2Int(2, 2)
                },
                bothSnapshot.PointListChannels[0].Data);

            Assert.That(filteredSnapshot.PointListChannels[0].Data, Is.Empty);
        }

        [Test]
        public async Task EdgeFinderNodeHonoursPointCountLimitDeterministically()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0,
                1, 1, 2, 2,
                1, 1, 2, 2,
                0, 0, 0, 0
            };

            FixedLogicalMapNode mapNodeA = new FixedLogicalMapNode("logical-map-limit-a", "LogicalIds", 4, 4, logicalIds);
            FixedLogicalMapNode mapNodeB = new FixedLogicalMapNode("logical-map-limit-b", "LogicalIds", 4, 4, logicalIds);
            EdgeFinderNode firstNode = new EdgeFinderNode("edge-limit", "Edge", "LogicalIds", "Points", false, string.Empty, 1, false, string.Empty, 2, EdgeSide.Both, 1, 2);
            EdgeFinderNode secondNode = new EdgeFinderNode("edge-limit", "Edge", "LogicalIds", "Points", false, string.Empty, 1, false, string.Empty, 2, EdgeSide.Both, 1, 2);

            Vector2Int[] firstRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { mapNodeA, firstNode }, 4, 4, 4308L)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecuteNodesAsync(new IGenNode[] { mapNodeB, secondNode }, 4, 4, 4308L)).PointListChannels[0].Data);

            Assert.That(firstRun.Length, Is.EqualTo(2));
            Assert.That(secondRun, Is.EqualTo(firstRun));
            AssertAllPointsStayWithinBounds(firstRun, 4, 4);
        }

        private static async Task<WorldSnapshot> ExecutePoissonAsync(PoissonDiscSamplerNode node, byte[] eligibleMask, int width, int height)
        {
            FixedBoolMaskNode maskNode = new FixedBoolMaskNode("mask-node", "Mask", eligibleMask);
            return await ExecuteNodesAsync(new IGenNode[] { maskNode, node }, width, height, 4301L);
        }

        private static async Task<WorldSnapshot> ExecuteNodesAsync(IReadOnlyList<IGenNode> nodes, int width, int height, long seed)
        {
            using (ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, seed))
            {
                Executor executor = new Executor();
                ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None, disposePlanOnCompletion: false);

                Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
                Assert.That(result.Snapshot, Is.Not.Null);
                return result.Snapshot;
            }
        }

        private static WorldSnapshot.PointListChannelSnapshot GetPointListChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.PointListChannels.Length; index++)
            {
                WorldSnapshot.PointListChannelSnapshot channel = snapshot.PointListChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static Vector2Int[] SortPointsStable(Vector2Int[] points)
        {
            Vector2Int[] sortedPoints = points != null ? (Vector2Int[])points.Clone() : Array.Empty<Vector2Int>();
            Array.Sort(sortedPoints, ComparePointsByRowMajorOrder);
            return sortedPoints;
        }

        private static int ComparePointsByRowMajorOrder(Vector2Int left, Vector2Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            if (yComparison != 0)
            {
                return yComparison;
            }

            return left.x.CompareTo(right.x);
        }

        private static void AssertAllPointsUseEligibleTiles(Vector2Int[] points, byte[] eligibleMask, int width)
        {
            int index;
            for (index = 0; index < points.Length; index++)
            {
                Vector2Int point = points[index];
                Assert.That(eligibleMask[(point.y * width) + point.x], Is.EqualTo((byte)1));
            }
        }

        private static void AssertAllPointPairsRespectMinimumDistance(Vector2Int[] points, float minimumDistance)
        {
            float minimumDistanceSquared = minimumDistance * minimumDistance;

            int firstIndex;
            for (firstIndex = 0; firstIndex < points.Length; firstIndex++)
            {
                int secondIndex;
                for (secondIndex = firstIndex + 1; secondIndex < points.Length; secondIndex++)
                {
                    Vector2 delta = points[firstIndex] - points[secondIndex];
                    Assert.That(delta.sqrMagnitude, Is.GreaterThanOrEqualTo(minimumDistanceSquared));
                }
            }
        }

        private static void AssertAllPointsStayWithinBounds(Vector2Int[] points, int width, int height)
        {
            int index;
            for (index = 0; index < points.Length; index++)
            {
                Assert.That(points[index].x, Is.InRange(0, width - 1));
                Assert.That(points[index].y, Is.InRange(0, height - 1));
            }
        }

        private static bool ContainsPointInRegion(Vector2Int[] points, int minX, int minY, int maxX, int maxY)
        {
            int index;
            for (index = 0; index < points.Length; index++)
            {
                Vector2Int point = points[index];
                if (point.x < minX || point.x > maxX || point.y < minY || point.y > maxY)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private sealed class FixedBoolMaskNode : IGenNode
        {
            private readonly byte[] _values;
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public FixedBoolMaskNode(string nodeId, string outputChannelName, byte[] values)
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

            public string NodeId { get; }

            public string NodeName { get; }

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

        private sealed class FixedFloatMapNode : IGenNode
        {
            private readonly float[] _values;
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public FixedFloatMapNode(string nodeId, string outputChannelName, float[] values)
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

            public string NodeId { get; }

            public string NodeName { get; }

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

        private sealed class FixedLogicalMapNode : IGenNode
        {
            private readonly int _width;
            private readonly int _height;
            private readonly int[] _values;
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public FixedLogicalMapNode(string nodeId, string outputChannelName, int width, int height, int[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _width = width;
                _height = height;
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

            public string NodeId { get; }

            public string NodeName { get; }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<int> output = context.GetIntChannel(_channelDeclarations[0].ChannelName);
                if (_values.Length != _width * _height)
                {
                    throw new InvalidOperationException("Fixed logical map data length does not match the declared dimensions.");
                }

                NativeArray<int> source = new NativeArray<int>(_values.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                int index;
                for (index = 0; index < _values.Length; index++)
                {
                    source[index] = _values[index];
                }

                CopyLogicalMapJob job = new CopyLogicalMapJob
                {
                    Source = source,
                    Output = output
                };

                JobHandle jobHandle = job.Schedule(output.Length, 64, context.InputDependency);
                return source.Dispose(jobHandle);
            }

            [BurstCompile]
            private struct CopyLogicalMapJob : IJobParallelFor
            {
                [ReadOnly]
                public NativeArray<int> Source;

                public NativeArray<int> Output;

                public void Execute(int index)
                {
                    Output[index] = Source[index];
                }
            }
        }
    }
}
