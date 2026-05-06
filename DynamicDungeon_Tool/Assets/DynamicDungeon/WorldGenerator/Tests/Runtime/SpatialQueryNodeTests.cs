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
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class SpatialQueryNodeTests
    {
        [Test]
        public async Task ContextualQueryNodeReturnsExactlyExpectedPositionsForFloorWithEmptyTileAbove()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 1, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            };

            Vector2Int[] expectedPositions =
            {
                new Vector2Int(1, 1),
                new Vector2Int(3, 2),
                new Vector2Int(6, 3),
                new Vector2Int(4, 5),
                new Vector2Int(8, 6)
            };

            ContextualQueryNode queryNode = CreateContextualQueryNode(
                CreateFloorWithEmptyAboveConditionsJson(),
                "Matches");

            WorldSnapshot snapshot = await ExecuteGraphAsync(logicalIds, 10, 10, queryNode);
            WorldSnapshot.PointListChannelSnapshot pointListChannel = GetPointListChannel(snapshot, "Matches");

            Assert.That(pointListChannel, Is.Not.Null);
            Assert.That(pointListChannel.Data, Has.Length.EqualTo(expectedPositions.Length));
            CollectionAssert.AreEquivalent(expectedPositions, pointListChannel.Data);
        }

        [Test]
        public async Task ContextualQueryNodeReturnsEmptyPointListWhenPatternHasNoMatches()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 1, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            };

            string noMatchConditionsJson =
                "{\"Entries\":[" +
                "{\"Offset\":{\"x\":0,\"y\":0},\"MatchById\":true,\"LogicalId\":1,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":0,\"y\":-1},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}" +
                "]}";

            ContextualQueryNode queryNode = CreateContextualQueryNode(noMatchConditionsJson, "Matches");

            WorldSnapshot snapshot = await ExecuteGraphAsync(logicalIds, 10, 10, queryNode);
            WorldSnapshot.PointListChannelSnapshot pointListChannel = GetPointListChannel(snapshot, "Matches");

            Assert.That(pointListChannel, Is.Not.Null);
            Assert.That(pointListChannel.Data, Is.Empty);
        }

        [Test]
        public async Task ContextualQueryNodeProducesDeterministicResultsAcrossRepeatedRuns()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 1, 0, 1, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 1, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            };

            ContextualQueryNode firstQueryNode = CreateContextualQueryNode(
                CreateFloorWithEmptyAboveConditionsJson(),
                "Matches");
            ContextualQueryNode secondQueryNode = CreateContextualQueryNode(
                CreateFloorWithEmptyAboveConditionsJson(),
                "Matches");
            ContextualQueryNode thirdQueryNode = CreateContextualQueryNode(
                CreateFloorWithEmptyAboveConditionsJson(),
                "Matches");

            Vector2Int[] firstRun = SortPointsStable((await ExecuteGraphAsync(logicalIds, 10, 10, firstQueryNode)).PointListChannels[0].Data);
            Vector2Int[] secondRun = SortPointsStable((await ExecuteGraphAsync(logicalIds, 10, 10, secondQueryNode)).PointListChannels[0].Data);
            Vector2Int[] thirdRun = SortPointsStable((await ExecuteGraphAsync(logicalIds, 10, 10, thirdQueryNode)).PointListChannels[0].Data);

            Assert.That(secondRun, Is.EqualTo(firstRun));
            Assert.That(thirdRun, Is.EqualTo(firstRun));
        }

        [Test]
        public async Task NeighbourhoodCheckNodeChebyshevMarksAdjacentTileWithinRadiusOneAsTrue()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 7, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0
            };

            WorldSnapshot snapshot = await ExecuteNeighbourhoodCheckAsync(logicalIds, 5, 5, 7, 1, DistanceMode.Chebyshev);
            WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(boolMaskChannel, Is.Not.Null);
            AssertMaskValue(boolMaskChannel.Data, 5, 2, 1, true);
        }

        [Test]
        public async Task NeighbourhoodCheckNodeChebyshevMarksTileWithoutNearbyMatchAsFalse()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 7, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0
            };

            WorldSnapshot snapshot = await ExecuteNeighbourhoodCheckAsync(logicalIds, 5, 5, 7, 1, DistanceMode.Chebyshev);
            WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(boolMaskChannel, Is.Not.Null);
            AssertMaskValue(boolMaskChannel.Data, 5, 0, 0, false);
        }

        [Test]
        public async Task NeighbourhoodCheckNodeChebyshevMarksBoundaryTileAsTrue()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 7, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0
            };

            WorldSnapshot snapshot = await ExecuteNeighbourhoodCheckAsync(logicalIds, 5, 5, 7, 2, DistanceMode.Chebyshev);
            WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(boolMaskChannel, Is.Not.Null);
            AssertMaskValue(boolMaskChannel.Data, 5, 0, 2, true);
        }

        [Test]
        public async Task NeighbourhoodCheckNodeEuclideanExcludesDiagonalTilesBeyondRadius()
        {
            int[] logicalIds =
            {
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 7, 0, 0,
                0, 0, 0, 0, 0,
                0, 0, 0, 0, 0
            };

            WorldSnapshot snapshot = await ExecuteNeighbourhoodCheckAsync(logicalIds, 5, 5, 7, 1, DistanceMode.Euclidean);
            WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(snapshot, "Mask");

            Assert.That(boolMaskChannel, Is.Not.Null);
            AssertMaskValue(boolMaskChannel.Data, 5, 1, 1, false);
            AssertMaskValue(boolMaskChannel.Data, 5, 3, 1, false);
            AssertMaskValue(boolMaskChannel.Data, 5, 1, 3, false);
            AssertMaskValue(boolMaskChannel.Data, 5, 3, 3, false);
        }

        private static ContextualQueryNode CreateContextualQueryNode(string conditionsJson, string outputChannelName)
        {
            return new ContextualQueryNode(
                "contextual-query",
                "Contextual Query",
                "LogicalIds",
                outputChannelName,
                conditionsJson);
        }

        private static string CreateFloorWithEmptyAboveConditionsJson()
        {
            return
                "{\"Entries\":[" +
                "{\"Offset\":{\"x\":0,\"y\":0},\"MatchById\":true,\"LogicalId\":1,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":0,\"y\":-1},\"MatchById\":true,\"LogicalId\":0,\"TagName\":\"\"}" +
                "]}";
        }

        private static async Task<WorldSnapshot> ExecuteNeighbourhoodCheckAsync(
            int[] logicalIds,
            int width,
            int height,
            int logicalId,
            int radius,
            DistanceMode distanceMode)
        {
            NeighbourhoodCheckNode node = new NeighbourhoodCheckNode(
                "neighbourhood-check",
                "Neighbourhood Check",
                "LogicalIds",
                "Mask",
                true,
                logicalId,
                string.Empty,
                radius,
                distanceMode);

            return await ExecuteGraphAsync(logicalIds, width, height, node);
        }

        private static async Task<WorldSnapshot> ExecuteGraphAsync(int[] logicalIds, int width, int height, IGenNode queryNode)
        {
            FixedLogicalMapNode logicalMapNode = new FixedLogicalMapNode(
                "logical-map",
                "Logical Map",
                "LogicalIds",
                width,
                height,
                logicalIds);

            ExecutionResult result = await ExecuteGraphWithResultAsync(new IGenNode[] { logicalMapNode, queryNode }, width, height);

            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            Assert.That(result.Snapshot, Is.Not.Null);
            return result.Snapshot;
        }

        private static async Task<ExecutionResult> ExecuteGraphWithResultAsync(IReadOnlyList<IGenNode> nodes, int width, int height)
        {
            using (ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, 1001L))
            {
                Executor executor = new Executor();
                return await executor.ExecuteAsync(plan, CancellationToken.None, disposePlanOnCompletion: false);
            }
        }

        private static WorldSnapshot.PointListChannelSnapshot GetPointListChannel(WorldSnapshot snapshot, string name)
        {
            int index;
            for (index = 0; index < snapshot.PointListChannels.Length; index++)
            {
                WorldSnapshot.PointListChannelSnapshot channel = snapshot.PointListChannels[index];
                if (channel != null && string.Equals(channel.Name, name, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static WorldSnapshot.BoolMaskChannelSnapshot GetBoolMaskChannel(WorldSnapshot snapshot, string name)
        {
            int index;
            for (index = 0; index < snapshot.BoolMaskChannels.Length; index++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channel = snapshot.BoolMaskChannels[index];
                if (channel != null && string.Equals(channel.Name, name, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static void AssertMaskValue(byte[] data, int width, int x, int y, bool expected)
        {
            int index = (y * width) + x;
            Assert.That(data[index], Is.EqualTo(expected ? (byte)1 : (byte)0));
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

        private sealed class FixedLogicalMapNode : IGenNode
        {
            private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly int _width;
            private readonly int _height;
            private readonly int[] _values;
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public FixedLogicalMapNode(string nodeId, string nodeName, string outputChannelName, int width, int height, int[] values)
            {
                NodeId = nodeId;
                NodeName = nodeName;
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
                    return _blackboardDeclarations;
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
