using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GrowthNodeTests
    {
        [Test]
        public async Task ClusterNodeSampledSeedsAreDeterministicAndRespectMinimumSeparation()
        {
            byte[] eligibleMask =
            {
                1, 1, 1, 1, 1,
                1, 1, 1, 1, 1,
                1, 1, 1, 1, 1,
                1, 1, 1, 1, 1,
                1, 1, 1, 1, 1
            };

            FixedBoolMaskNode maskNodeA = new FixedBoolMaskNode("cluster-mask-a", "Mask", eligibleMask);
            FixedBoolMaskNode maskNodeB = new FixedBoolMaskNode("cluster-mask-b", "Mask", eligibleMask);
            ClusterNode firstNode = new ClusterNode("cluster-sampled", "Cluster", string.Empty, "Mask", "Output", 4, 0, 1.0f, 1.0f, 2);
            ClusterNode secondNode = new ClusterNode("cluster-sampled", "Cluster", string.Empty, "Mask", "Output", 4, 0, 1.0f, 1.0f, 2);

            byte[] firstRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { maskNodeA, firstNode }, 5, 5, 5101L)), "Output").Data;
            byte[] secondRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { maskNodeB, secondNode }, 5, 5, 5101L)), "Output").Data;
            Vector2Int[] seedPoints = ExtractTruePoints(firstRun, 5, 5);

            Assert.That(secondRun, Is.EqualTo(firstRun));
            Assert.That(seedPoints.Length, Is.EqualTo(4));
            AssertAllPointPairsRespectMinimumDistance(seedPoints, 2.0f);
        }

        [Test]
        public async Task ClusterNodePointListInputTakesPriorityAndFullySpreadsWhenProbabilityIsOne()
        {
            Vector2Int[] seedPoints =
            {
                new Vector2Int(2, 2)
            };

            byte[] ignoredMask =
            {
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0
            };

            FixedPointListNode pointNode = new FixedPointListNode("cluster-points", "Seeds", seedPoints);
            FixedBoolMaskNode maskNode = new FixedBoolMaskNode("cluster-mask", "Mask", ignoredMask);
            ClusterNode clusterNode = new ClusterNode("cluster-full", "Cluster", "Seeds", "Mask", "Output", 3, 2, 1.0f, 1.0f, 0);

            byte[] output = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { pointNode, maskNode, clusterNode }, 5, 5, 5102L)), "Output").Data;

            byte[] expected =
            {
                0, 0, 1, 0, 0,
                0, 1, 1, 1, 0,
                1, 1, 1, 1, 1,
                0, 1, 1, 1, 0,
                0, 0, 1, 0, 0
            };

            Assert.That(output, Is.EqualTo(expected));
        }

        [Test]
        public async Task VeinNodePointListInputProducesDeterministicStraightWalkWithoutForks()
        {
            Vector2Int[] seedPoints =
            {
                new Vector2Int(4, 4)
            };

            FixedPointListNode pointNodeA = new FixedPointListNode("vein-points-a", "Seeds", seedPoints);
            FixedPointListNode pointNodeB = new FixedPointListNode("vein-points-b", "Seeds", seedPoints);
            VeinNode firstNode = new VeinNode("vein-line", "Vein", "Seeds", string.Empty, "Output", 0, 4, 4, 0.0f, 0.0f, 45.0f);
            VeinNode secondNode = new VeinNode("vein-line", "Vein", "Seeds", string.Empty, "Output", 0, 4, 4, 0.0f, 0.0f, 45.0f);

            byte[] firstRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { pointNodeA, firstNode }, 9, 9, 5103L)), "Output").Data;
            byte[] secondRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { pointNodeB, secondNode }, 9, 9, 5103L)), "Output").Data;
            Vector2Int[] veinPoints = ExtractTruePoints(firstRun, 9, 9);

            Assert.That(secondRun, Is.EqualTo(firstRun));
            Assert.That(veinPoints.Length, Is.EqualTo(5));
            AssertStraightEightWayLine(veinPoints);
        }

        [Test]
        public async Task VeinNodeSamplesMaskSeedsDeterministicallyWhenPointListIsDisconnected()
        {
            byte[] eligibleMask =
            {
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1,
                1, 1, 1, 1, 1, 1, 1, 1, 1
            };

            FixedBoolMaskNode maskNodeA = new FixedBoolMaskNode("vein-mask-a", "Mask", eligibleMask);
            FixedBoolMaskNode maskNodeB = new FixedBoolMaskNode("vein-mask-b", "Mask", eligibleMask);
            VeinNode firstNode = new VeinNode("vein-sampled", "Vein", string.Empty, "Mask", "Output", 1, 1, 1, 0.0f, 0.0f, 45.0f);
            VeinNode secondNode = new VeinNode("vein-sampled", "Vein", string.Empty, "Mask", "Output", 1, 1, 1, 0.0f, 0.0f, 45.0f);

            byte[] firstRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { maskNodeA, firstNode }, 9, 9, 5104L)), "Output").Data;
            byte[] secondRun = GetBoolMaskChannel((await ExecuteNodesAsync(new IGenNode[] { maskNodeB, secondNode }, 9, 9, 5104L)), "Output").Data;
            Vector2Int[] veinPoints = ExtractTruePoints(firstRun, 9, 9);

            Assert.That(secondRun, Is.EqualTo(firstRun));
            Assert.That(veinPoints.Length, Is.EqualTo(2));
            AssertAllPointsStayWithinBounds(veinPoints, 9, 9);
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

        private static WorldSnapshot.BoolMaskChannelSnapshot GetBoolMaskChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.BoolMaskChannels.Length; index++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channel = snapshot.BoolMaskChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static Vector2Int[] ExtractTruePoints(byte[] mask, int width, int height)
        {
            List<Vector2Int> points = new List<Vector2Int>();

            int y;
            for (y = 0; y < height; y++)
            {
                int x;
                for (x = 0; x < width; x++)
                {
                    if (mask[(y * width) + x] != 0)
                    {
                        points.Add(new Vector2Int(x, y));
                    }
                }
            }

            return points.ToArray();
        }

        private static void AssertStraightEightWayLine(Vector2Int[] points)
        {
            Assert.That(points, Is.Not.Empty);

            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;

            int index;
            for (index = 0; index < points.Length; index++)
            {
                minX = math.min(minX, points[index].x);
                maxX = math.max(maxX, points[index].x);
                minY = math.min(minY, points[index].y);
                maxY = math.max(maxY, points[index].y);
            }

            if (minX == maxX)
            {
                Assert.That(points.Length, Is.EqualTo((maxY - minY) + 1));
                return;
            }

            if (minY == maxY)
            {
                Assert.That(points.Length, Is.EqualTo((maxX - minX) + 1));
                return;
            }

            Assert.That(maxX - minX, Is.EqualTo(maxY - minY));
            Assert.That(points.Length, Is.EqualTo((maxX - minX) + 1));

            bool hasConstantDifference = true;
            int expectedDifference = points[0].x - points[0].y;
            for (index = 1; index < points.Length; index++)
            {
                if ((points[index].x - points[index].y) != expectedDifference)
                {
                    hasConstantDifference = false;
                    break;
                }
            }

            if (hasConstantDifference)
            {
                return;
            }

            int expectedSum = points[0].x + points[0].y;
            for (index = 1; index < points.Length; index++)
            {
                Assert.That(points[index].x + points[index].y, Is.EqualTo(expectedSum));
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

        private sealed class FixedPointListNode : IGenNode
        {
            private readonly Vector2Int[] _points;
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public FixedPointListNode(string nodeId, string outputChannelName, Vector2Int[] points)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _points = points ?? Array.Empty<Vector2Int>();
                _ports = new[]
                {
                    new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.PointList)
                };
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.PointList, true)
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
                NativeList<int2> output = context.GetPointListChannel(_channelDeclarations[0].ChannelName);
                output.Clear();

                if (output.Capacity < _points.Length)
                {
                    output.Capacity = _points.Length;
                }

                int index;
                for (index = 0; index < _points.Length; index++)
                {
                    output.Add(new int2(_points[index].x, _points[index].y));
                }

                return context.InputDependency;
            }
        }
    }
}
