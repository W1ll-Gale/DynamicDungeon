using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class ExtendedNoiseNodeTests
    {
        [Test]
        public async Task VoronoiCellIdsStayStableAcrossDifferentGlobalSeeds()
        {
            Executor executor = new Executor();
            VoronoiNoiseNode firstNode = new VoronoiNoiseNode("voronoi-node", "Voronoi", 0.18f, new Vector2(1.5f, -0.75f));
            VoronoiNoiseNode secondNode = new VoronoiNoiseNode("voronoi-node", "Voronoi", 0.18f, new Vector2(1.5f, -0.75f));

            ExecutionPlan firstPlan = ExecutionPlan.Build(new IGenNode[] { firstNode }, 9, 7, 111L);
            ExecutionPlan secondPlan = ExecutionPlan.Build(new IGenNode[] { secondNode }, 9, 7, 222L);

            ExecutionResult firstResult = await executor.ExecuteAsync(firstPlan, CancellationToken.None);
            ExecutionResult secondResult = await executor.ExecuteAsync(secondPlan, CancellationToken.None);

            Assert.That(firstResult.IsSuccess, Is.True);
            Assert.That(secondResult.IsSuccess, Is.True);

            WorldSnapshot.IntChannelSnapshot firstCellIds = GetIntChannel(firstResult.Snapshot, firstNode.CellIdChannelName);
            WorldSnapshot.IntChannelSnapshot secondCellIds = GetIntChannel(secondResult.Snapshot, secondNode.CellIdChannelName);
            WorldSnapshot.FloatChannelSnapshot firstDistances = GetFloatChannel(firstResult.Snapshot, firstNode.DistanceChannelName);
            WorldSnapshot.FloatChannelSnapshot secondDistances = GetFloatChannel(secondResult.Snapshot, secondNode.DistanceChannelName);

            Assert.That(firstCellIds, Is.Not.Null);
            Assert.That(secondCellIds, Is.Not.Null);
            Assert.That(firstDistances, Is.Not.Null);
            Assert.That(secondDistances, Is.Not.Null);

            CollectionAssert.AreEqual(firstCellIds.Data, secondCellIds.Data);
            Assert.That(HasAnyFloatDifference(firstDistances.Data, secondDistances.Data), Is.True);
        }

        [Test]
        public void VoronoiGraphCompilationRetainsBothOwnedOutputChannels()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;
                graph.WorldWidth = 8;
                graph.WorldHeight = 6;
                graph.DefaultSeed = 1234L;
                GraphOutputUtility.EnsureSingleOutputNode(graph, false);

                string nodeId = "voronoi-node";
                string distancePortName = "Distance__" + nodeId;
                string cellIdPortName = "CellId__" + nodeId;

                GenNodeData voronoiNode = new GenNodeData(nodeId, typeof(VoronoiNoiseNode).FullName, "Voronoi", Vector2.zero);
                voronoiNode.Ports.Add(new GenPortData(distancePortName, PortDirection.Output, ChannelType.Float, "Distance"));
                voronoiNode.Ports.Add(new GenPortData(cellIdPortName, PortDirection.Output, ChannelType.Int, "CellId"));
                voronoiNode.Parameters.Add(new SerializedParameter("frequency", "0.15"));
                voronoiNode.Parameters.Add(new SerializedParameter("offset", "0,0"));
                graph.Nodes.Add(voronoiNode);

                GenNodeData outputNode = GraphOutputUtility.FindOutputNode(graph);
                graph.Connections.Add(new GenConnectionData(nodeId, distancePortName, outputNode.NodeId, GraphOutputUtility.OutputInputPortName));

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.Plan.AllocatedWorld.HasFloatChannel(distancePortName), Is.True);
                Assert.That(compileResult.Plan.AllocatedWorld.HasIntChannel(cellIdPortName), Is.True);

                compileResult.Plan.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task GradientRadialModeIncreasesOutwardFromTheCentre()
        {
            Executor executor = new Executor();
            GradientNoiseNode node = new GradientNoiseNode("gradient-node", "Gradient", "GradientOut", GradientDirection.Radial, new Vector2(0.5f, 0.5f), 45.0f, 1.0f, 1.0f, 1.0f);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 3, 3, 0L);

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);

            float[] values = result.Snapshot.FloatChannels[0].Data;
            float centre = values[(1 * 3) + 1];
            float topLeft = values[0];
            float bottomRight = values[8];

            Assert.That(centre, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(topLeft, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(bottomRight, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(topLeft, Is.GreaterThan(centre));
        }

        [Test]
        public async Task GradientScaleAndAmplitudeAffectOutputStrength()
        {
            Executor executor = new Executor();
            GradientNoiseNode baseNode = new GradientNoiseNode("gradient-base", "Gradient", "GradientOut", GradientDirection.X, new Vector2(0.5f, 0.5f), 45.0f, 1.0f, 1.0f, 1.0f);
            GradientNoiseNode scaledNode = new GradientNoiseNode("gradient-scaled", "Gradient", "GradientOut", GradientDirection.X, new Vector2(0.5f, 0.5f), 45.0f, 2.0f, 1.0f, 1.0f);
            GradientNoiseNode strongerNode = new GradientNoiseNode("gradient-stronger", "Gradient", "GradientOut", GradientDirection.X, new Vector2(0.5f, 0.5f), 45.0f, 1.0f, 1.0f, 0.5f);

            ExecutionResult baseResult = await executor.ExecuteAsync(ExecutionPlan.Build(new IGenNode[] { baseNode }, 5, 1, 0L), CancellationToken.None);
            ExecutionResult scaledResult = await executor.ExecuteAsync(ExecutionPlan.Build(new IGenNode[] { scaledNode }, 5, 1, 0L), CancellationToken.None);
            ExecutionResult strongerResult = await executor.ExecuteAsync(ExecutionPlan.Build(new IGenNode[] { strongerNode }, 5, 1, 0L), CancellationToken.None);

            Assert.That(baseResult.IsSuccess, Is.True);
            Assert.That(scaledResult.IsSuccess, Is.True);
            Assert.That(strongerResult.IsSuccess, Is.True);

            float[] baseValues = baseResult.Snapshot.FloatChannels[0].Data;
            float[] scaledValues = scaledResult.Snapshot.FloatChannels[0].Data;
            float[] strongerValues = strongerResult.Snapshot.FloatChannels[0].Data;

            Assert.That(scaledValues[4], Is.LessThan(baseValues[4]));
            Assert.That(strongerValues[4], Is.LessThan(baseValues[4]));
        }

        [Test]
        public async Task ConstantNodeSwitchesOutputTypeViaParameters()
        {
            ConstantNode node = new ConstantNode("constant-node", "Constant", "ConstantOut");
            node.ReceiveParameter("outputType", "Int");
            node.ReceiveParameter("intValue", "7");

            Assert.That(node.OutputType, Is.EqualTo(ChannelType.Int));
            Assert.That(node.Ports[0].Type, Is.EqualTo(ChannelType.Int));
            Assert.That(node.ChannelDeclarations[0].Type, Is.EqualTo(ChannelType.Int));

            Executor executor = new Executor();
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 4, 2, 0L);
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Snapshot.IntChannels.Length, Is.EqualTo(1));

            int[] data = result.Snapshot.IntChannels[0].Data;
            int index;
            for (index = 0; index < data.Length; index++)
            {
                Assert.That(data[index], Is.EqualTo(7));
            }
        }

        private static WorldSnapshot.FloatChannelSnapshot GetFloatChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.FloatChannels.Length; index++)
            {
                if (snapshot.FloatChannels[index].Name == channelName)
                {
                    return snapshot.FloatChannels[index];
                }
            }

            return null;
        }

        private static WorldSnapshot.IntChannelSnapshot GetIntChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                if (snapshot.IntChannels[index].Name == channelName)
                {
                    return snapshot.IntChannels[index];
                }
            }

            return null;
        }

        private static bool HasAnyFloatDifference(float[] first, float[] second)
        {
            if (first.Length != second.Length)
            {
                return true;
            }

            int index;
            for (index = 0; index < first.Length; index++)
            {
                if (!Mathf.Approximately(first[index], second[index]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
