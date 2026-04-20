using System;
using System.Collections.Generic;
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
    public sealed class CastRegistryTests
    {
        [Test]
        public void FloatToIntFloorCorrectlyFloors()
        {
            NativeArray<float> input = new NativeArray<float>(new[] { -1.9f, -0.5f, 0.0f, 0.4f, 0.9f, 2.7f }, Allocator.Temp);
            NativeArray<int> output = default;

            try
            {
                output = CastRegistry.CastFloatToInt(input, CastMode.FloatToIntFloor, Allocator.Temp);

                CollectionAssert.AreEqual(new[] { -2, -1, 0, 0, 0, 2 }, output.ToArray());
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
        public void FloatToIntRoundCorrectlyRounds()
        {
            // FloatToIntRound uses floor(x + 0.5f), which rounds half toward positive infinity.
            // 0.5 rounds to 1, -0.5 rounds to 0.
            NativeArray<float> input = new NativeArray<float>(new[] { -1.6f, -0.5f, 0.0f, 0.4f, 0.5f, 1.5f, 2.6f }, Allocator.Temp);
            NativeArray<int> output = default;

            try
            {
                output = CastRegistry.CastFloatToInt(input, CastMode.FloatToIntRound, Allocator.Temp);

                // -0.5 rounds to 0 (half rounds toward positive infinity, not away from zero)
                CollectionAssert.AreEqual(new[] { -2, 0, 0, 0, 1, 2, 3 }, output.ToArray());
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
        public async Task CompiledGraphWithFloatToIntRoundConnectionProducesRoundedValues()
        {
            // Fill every cell with 0.5. After FloatToIntRound, each cell should equal 1
            // because floor(0.5 + 0.5) = floor(1.0) = 1.
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.WorldWidth = 4;
            graph.WorldHeight = 4;
            graph.DefaultSeed = 1L;

            try
            {
                GenNodeData fillNode = new GenNodeData("fill-node", typeof(EmptyGridNode).FullName, "Fill", Vector2.zero);
                fillNode.Ports.Add(new GenPortData("Output", PortDirection.Output, ChannelType.Float));
                fillNode.Parameters.Add(new SerializedParameter("fillValue", "0.5"));
                graph.Nodes.Add(fillNode);

                GenNodeData outputNode = new GenNodeData("output-node", typeof(TilemapOutputNode).FullName, "Output", Vector2.zero);
                outputNode.Ports.Add(new GenPortData("LogicalIds", PortDirection.Input, ChannelType.Int));
                graph.Nodes.Add(outputNode);

                GenConnectionData connection = new GenConnectionData("fill-node", "Output", "output-node", "LogicalIds");
                connection.CastMode = CastMode.FloatToIntRound;
                graph.Connections.Add(connection);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True, "Graph compilation failed.");
                Assert.That(compileResult.Plan, Is.Not.Null);
                // fill node + implicit cast node + output node
                Assert.That(compileResult.Plan.Jobs.Count, Is.EqualTo(3));

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.Snapshot, Is.Not.Null);

                // The implicit cast writes to channel "__cast_0" (the first cast in this graph).
                WorldSnapshot.IntChannelSnapshot castChannel = FindIntChannel(executionResult.Snapshot, "__cast_0");

                Assert.That(castChannel, Is.Not.Null, "Expected implicit cast channel '__cast_0' was not found in the snapshot.");

                int tileIndex;
                for (tileIndex = 0; tileIndex < castChannel.Data.Length; tileIndex++)
                {
                    Assert.That(castChannel.Data[tileIndex], Is.EqualTo(1),
                        "Expected all cells to equal 1 after FloatToIntRound of 0.5.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void SameTypeConnectionDoesNotInsertImplicitCastNode()
        {
            // A same-type (Int → Int) connection has CastMode.None and must not add an
            // ImplicitCastNode to the compiled plan.
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.WorldWidth = 4;
            graph.WorldHeight = 4;
            graph.DefaultSeed = 1L;

            try
            {
                GenNodeData fillNode = new GenNodeData("fill-node", typeof(GraphCompilerIntFillNode).FullName, "Fill", Vector2.zero);
                fillNode.Ports.Add(new GenPortData("LogicalIds", PortDirection.Output, ChannelType.Int));
                fillNode.Parameters.Add(new SerializedParameter("fillValue", "3"));
                graph.Nodes.Add(fillNode);

                GenNodeData outputNode = new GenNodeData("output-node", typeof(TilemapOutputNode).FullName, "Output", Vector2.zero);
                outputNode.Ports.Add(new GenPortData("LogicalIds", PortDirection.Input, ChannelType.Int));
                graph.Nodes.Add(outputNode);

                graph.Connections.Add(new GenConnectionData("fill-node", "LogicalIds", "output-node", "LogicalIds"));

                GraphCompileResult result = GraphCompiler.Compile(graph);

                Assert.That(result.IsSuccess, Is.True, "Graph compilation failed.");
                Assert.That(result.Plan, Is.Not.Null);
                // No ImplicitCastNode — only fill node + output node.
                Assert.That(result.Plan.Jobs.Count, Is.EqualTo(2));

                result.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static WorldSnapshot.IntChannelSnapshot FindIntChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels[index];
                if (string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }
    }
}
