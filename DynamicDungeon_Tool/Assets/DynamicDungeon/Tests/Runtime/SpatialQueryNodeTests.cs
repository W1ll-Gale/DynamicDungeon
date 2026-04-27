using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class SpatialQueryNodeTests
    {
        [TearDown]
        public void TearDown()
        {
            ResetRegistryCache();
        }

        [Test]
        public async Task ContextualQueryNodeReturnsMatchingPointList()
        {
            FixedLogicalMapNode logicalMapNode = new FixedLogicalMapNode(
                "logical-map",
                "Logical Map",
                "LogicalIds",
                3,
                3,
                new[]
                {
                    2, 2, 2,
                    2, 1, 2,
                    2, 2, 2
                });

            string conditionsJson =
                "{\"Entries\":[" +
                "{\"Offset\":{\"x\":0,\"y\":0},\"MatchById\":true,\"LogicalId\":1,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":0,\"y\":-1},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":0,\"y\":1},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":-1,\"y\":0},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":1,\"y\":0},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}" +
                "]}";

            ContextualQueryNode queryNode = new ContextualQueryNode(
                "contextual-query",
                "Contextual Query",
                "LogicalIds",
                "Matches",
                conditionsJson);

            WorldSnapshot snapshot = await ExecuteGraphAsync(new IGenNode[] { logicalMapNode, queryNode }, 3, 3);
            WorldSnapshot.PointListChannelSnapshot pointListChannel = GetPointListChannel(snapshot, "Matches");

            Assert.That(pointListChannel, Is.Not.Null);
            Assert.That(pointListChannel.Data, Has.Length.EqualTo(1));
            Assert.That(pointListChannel.Data[0], Is.EqualTo(new Vector2Int(1, 1)));
        }

        [Test]
        public async Task NeighbourhoodCheckNodeTagModeUsesResolvedRegistryTags()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                registry.AllTags.Add("Water");

                TileEntry waterEntry = new TileEntry();
                waterEntry.LogicalId = 7;
                waterEntry.DisplayName = "Water";
                waterEntry.Tags.Add("Water");
                registry.Entries.Add(waterEntry);

                SetRegistryCache(registry);

                FixedLogicalMapNode logicalMapNode = new FixedLogicalMapNode(
                    "logical-map",
                    "Logical Map",
                    "LogicalIds",
                    5,
                    5,
                    new[]
                    {
                        0, 0, 0, 0, 0,
                        0, 0, 0, 0, 0,
                        0, 0, 7, 0, 0,
                        0, 0, 0, 0, 0,
                        0, 0, 0, 0, 0
                    });

                NeighbourhoodCheckNode checkNode = new NeighbourhoodCheckNode(
                    "neighbourhood-check",
                    "Neighbourhood Check",
                    "LogicalIds",
                    "NearbyWater",
                    false,
                    0,
                    "Water",
                    1,
                    DistanceMode.Euclidean);

                WorldSnapshot snapshot = await ExecuteGraphAsync(new IGenNode[] { logicalMapNode, checkNode }, 5, 5);
                WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(snapshot, "NearbyWater");

                Assert.That(boolMaskChannel, Is.Not.Null);
                AssertMaskValue(boolMaskChannel.Data, 5, 2, 2, true);
                AssertMaskValue(boolMaskChannel.Data, 5, 2, 1, true);
                AssertMaskValue(boolMaskChannel.Data, 5, 2, 3, true);
                AssertMaskValue(boolMaskChannel.Data, 5, 1, 2, true);
                AssertMaskValue(boolMaskChannel.Data, 5, 3, 2, true);
                AssertMaskValue(boolMaskChannel.Data, 5, 1, 1, false);
                AssertMaskValue(boolMaskChannel.Data, 5, 3, 1, false);
                AssertMaskValue(boolMaskChannel.Data, 5, 1, 3, false);
                AssertMaskValue(boolMaskChannel.Data, 5, 3, 3, false);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public async Task TagBasedSpatialQueryWritesWarningWhenRegistryIsUnavailable()
        {
            SetRegistryCache(null, true);

            FixedLogicalMapNode logicalMapNode = new FixedLogicalMapNode(
                "logical-map",
                "Logical Map",
                "LogicalIds",
                3,
                3,
                new[]
                {
                    7, 7, 7,
                    7, 7, 7,
                    7, 7, 7
                });

            NeighbourhoodCheckNode checkNode = new NeighbourhoodCheckNode(
                "neighbourhood-check",
                "Neighbourhood Check",
                "LogicalIds",
                "NearbyWater",
                false,
                0,
                "Water",
                1,
                DistanceMode.Chebyshev);

            ExecutionResult result = await ExecuteGraphWithResultAsync(new IGenNode[] { logicalMapNode, checkNode }, 3, 3);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Diagnostics, Is.Not.Null);
            Assert.That(ContainsWarning(result.Diagnostics, "TileSemanticRegistry"), Is.True);

            WorldSnapshot.BoolMaskChannelSnapshot boolMaskChannel = GetBoolMaskChannel(result.Snapshot, "NearbyWater");
            Assert.That(boolMaskChannel, Is.Not.Null);

            int index;
            for (index = 0; index < boolMaskChannel.Data.Length; index++)
            {
                Assert.That(boolMaskChannel.Data[index], Is.EqualTo((byte)0));
            }
        }

        private static async Task<WorldSnapshot> ExecuteGraphAsync(IReadOnlyList<IGenNode> nodes, int width, int height)
        {
            ExecutionResult result = await ExecuteGraphWithResultAsync(nodes, width, height);
            Assert.That(result.IsSuccess, Is.True);
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

        private static bool ContainsWarning(IReadOnlyList<GraphDiagnostic> diagnostics, string messageFragment)
        {
            int index;
            for (index = 0; index < diagnostics.Count; index++)
            {
                GraphDiagnostic diagnostic = diagnostics[index];
                if (diagnostic.Severity == DiagnosticSeverity.Warning &&
                    diagnostic.Message != null &&
                    diagnostic.Message.IndexOf(messageFragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetRegistryCache(TileSemanticRegistry registry, bool hasAttemptedLoad = true)
        {
            FieldInfo cachedRegistryField = typeof(TileSemanticRegistry).GetField("_cachedRegistry", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hasAttemptedLoadField = typeof(TileSemanticRegistry).GetField("_hasAttemptedLoad", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(cachedRegistryField, Is.Not.Null);
            Assert.That(hasAttemptedLoadField, Is.Not.Null);

            cachedRegistryField.SetValue(null, registry);
            hasAttemptedLoadField.SetValue(null, hasAttemptedLoad);
        }

        private static void ResetRegistryCache()
        {
            SetRegistryCache(null, false);
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
