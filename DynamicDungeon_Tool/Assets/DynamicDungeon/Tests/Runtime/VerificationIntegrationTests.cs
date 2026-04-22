using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class VerificationIntegrationTests
    {
        private const string DefaultIntChannelName = "LogicalIds";
        private const string DimensionMismatchMessage = "Baked world snapshot dimension mismatch: snapshot is 64x64, current world is 128x128.";

        [Test]
        public async Task PerlinThresholdCellularGraphCompilesExecutesAndProducesExpectedChannels()
        {
            GenGraph graph = CreatePerlinThresholdCellularGraph();

            try
            {
                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.HasConnectedOutput, Is.True);
                Assert.That(compileResult.OutputChannelName, Is.EqualTo(DefaultIntChannelName));
                Assert.That(CountDiagnostics(compileResult.Diagnostics, DiagnosticSeverity.Error), Is.EqualTo(0));

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.WasCancelled, Is.False);
                Assert.That(executionResult.ErrorMessage, Is.Null);
                Assert.That(executionResult.Snapshot, Is.Not.Null);
                Assert.That(executionResult.Snapshot.Width, Is.EqualTo(graph.WorldWidth));
                Assert.That(executionResult.Snapshot.Height, Is.EqualTo(graph.WorldHeight));
                Assert.That(GetFloatChannel(executionResult.Snapshot, "Noise"), Is.Not.Null);
                Assert.That(GetBoolMaskChannel(executionResult.Snapshot, "Mask"), Is.Not.Null);
                Assert.That(GetBoolMaskChannel(executionResult.Snapshot, "SmoothedMask"), Is.Not.Null);
                Assert.That(GetIntChannel(executionResult.Snapshot, DefaultIntChannelName), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public async Task TwoPerlinNodesWithSameVisibleOutputNameCompileAndExecuteThroughOutputPath()
        {
            GenGraph graph = CreateTwoPerlinMathOutputGraph();
            try
            {
                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.HasConnectedOutput, Is.True);
                Assert.That(ContainsError(compileResult.Diagnostics, "is owned"), Is.False);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.ErrorMessage, Is.Null);
                Assert.That(GetFloatChannel(executionResult.Snapshot, GraphPortNameUtility.CreateGeneratedOutputPortName("perlin-a", GraphPortNameUtility.LegacyGenericOutputDisplayName)), Is.Not.Null);
                Assert.That(GetFloatChannel(executionResult.Snapshot, GraphPortNameUtility.CreateGeneratedOutputPortName("perlin-b", GraphPortNameUtility.LegacyGenericOutputDisplayName)), Is.Not.Null);
                Assert.That(GetFloatChannel(executionResult.Snapshot, GraphPortNameUtility.CreateGeneratedOutputPortName("math-node", GraphPortNameUtility.LegacyGenericOutputDisplayName)), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void GraphWithMissingRequiredConnectionFailsWithErrorDiagnostics()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 16;
            graph.WorldHeight = 16;
            graph.DefaultSeed = 2468L;

            try
            {
                GraphOutputUtility.EnsureSingleOutputNode(graph, false);

                GenNodeData logicalIdNode = new GenNodeData("logical-id-node", typeof(BoolMaskToLogicalIdNode).FullName, "Mask To Logical IDs", Vector2.zero);
                logicalIdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
                logicalIdNode.Ports.Add(new GenPortData(DefaultIntChannelName, PortDirection.Output, ChannelType.Int));
                logicalIdNode.Parameters.Add(new SerializedParameter("trueLogicalId", ((ushort)LogicalTileId.Wall).ToString(CultureInfo.InvariantCulture)));
                logicalIdNode.Parameters.Add(new SerializedParameter("falseLogicalId", ((ushort)LogicalTileId.Floor).ToString(CultureInfo.InvariantCulture)));
                graph.Nodes.Add(logicalIdNode);
                ConnectToOutput(graph, logicalIdNode.NodeId, DefaultIntChannelName);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.False);
                Assert.That(compileResult.Plan, Is.Null);
                Assert.That(CountDiagnostics(compileResult.Diagnostics, DiagnosticSeverity.Error), Is.GreaterThan(0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void VersionZeroGraphMigratesToCurrentSchemaBeforeCompilation()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = 0;
            graph.WorldWidth = 8;
            graph.WorldHeight = 8;
            graph.DefaultSeed = 97531L;

            try
            {
                MigrationResult migrationResult = GraphMigrationRunner.RunMigrations(graph, DefaultGraphMigrations.All);
                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(migrationResult.Success, Is.True);
                Assert.That(migrationResult.FromVersion, Is.EqualTo(0));
                Assert.That(migrationResult.ToVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(graph.SchemaVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(GraphOutputUtility.FindOutputNode(graph), Is.Not.Null);
                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.HasConnectedOutput, Is.False);
                Assert.That(compileResult.OutputChannelName, Is.Empty);

                compileResult.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void BakedWorldSnapshotDimensionMismatchIsRejectedAndLeavesTilemapsEmpty()
        {
            GameObject generatorObject = null;
            Grid grid = null;
            BiomeAsset biome = null;
            TilemapLayerDefinition defaultLayer = null;
            BakedWorldSnapshot bakedWorldSnapshot = null;

            try
            {
                generatorObject = new GameObject("VerificationGenerator");
                DungeonGeneratorComponent component = generatorObject.AddComponent<DungeonGeneratorComponent>();
                grid = generatorObject.AddComponent<Grid>();
                biome = ScriptableObject.CreateInstance<BiomeAsset>();
                defaultLayer = CreateLayerDefinition("Default", true);
                bakedWorldSnapshot = CreateBakedSnapshot(64, 64);

                SetPrivateField(component, "_grid", grid);
                SetPrivateField(component, "_biome", biome);
                SetPrivateField(component, "_worldWidth", 128);
                SetPrivateField(component, "_worldHeight", 128);
                SetPrivateField(component, "_layerDefinitions", new List<TilemapLayerDefinition> { defaultLayer });
                SetPrivateField(component, "_bakedWorldSnapshot", bakedWorldSnapshot);

                MethodInfo tryGetValidBakedSnapshotMethod = typeof(DungeonGeneratorComponent).GetMethod("TryGetValidBakedSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(tryGetValidBakedSnapshotMethod, Is.Not.Null);

                object[] arguments =
                {
                    null,
                    null
                };

                bool isValid = (bool)tryGetValidBakedSnapshotMethod.Invoke(component, arguments);
                BakedWorldSnapshot resolvedSnapshot = (BakedWorldSnapshot)arguments[0];
                string errorMessage = (string)arguments[1];

                Assert.That(isValid, Is.False);
                Assert.That(resolvedSnapshot, Is.SameAs(bakedWorldSnapshot));
                Assert.That(errorMessage, Is.EqualTo(DimensionMismatchMessage));

                TilemapLayerWriter writer = new TilemapLayerWriter();
                writer.EnsureTimelapsCreated(grid, new[] { defaultLayer });

                Tilemap defaultTilemap = GetLayerTilemap(grid, "Default");
                Assert.That(defaultTilemap, Is.Not.Null);
                Assert.That(defaultTilemap.GetUsedTilesCount(), Is.EqualTo(0));
            }
            finally
            {
                DestroyImmediateIfNotNull(bakedWorldSnapshot);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(biome);
                DestroyImmediateIfNotNull(generatorObject);
            }
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

        private static BakedWorldSnapshot CreateBakedSnapshot(int width, int height)
        {
            BakedWorldSnapshot bakedSnapshot = ScriptableObject.CreateInstance<BakedWorldSnapshot>();
            bakedSnapshot.Width = width;
            bakedSnapshot.Height = height;
            bakedSnapshot.Seed = 555L;
            bakedSnapshot.Timestamp = "2026-04-27T00:00:00.0000000Z";

            WorldSnapshot snapshot = new WorldSnapshot();
            snapshot.Width = width;
            snapshot.Height = height;
            snapshot.Seed = 555;
            snapshot.IntChannels = new[]
            {
                new WorldSnapshot.IntChannelSnapshot
                {
                    Name = DefaultIntChannelName,
                    Data = new int[width * height]
                }
            };

            bakedSnapshot.Snapshot = snapshot;
            bakedSnapshot.OutputChannelName = DefaultIntChannelName;
            return bakedSnapshot;
        }

        private static TilemapLayerDefinition CreateLayerDefinition(string layerName, bool isCatchAll)
        {
            TilemapLayerDefinition layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();
            layerDefinition.LayerName = layerName;
            layerDefinition.IsCatchAll = isCatchAll;
            return layerDefinition;
        }

        private static GenGraph CreatePerlinThresholdCellularGraph()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 32;
            graph.WorldHeight = 32;
            graph.DefaultSeed = 123456L;
            GraphOutputUtility.EnsureSingleOutputNode(graph, false);

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

            GenNodeData logicalIdNode = new GenNodeData("logical-id-node", typeof(BoolMaskToLogicalIdNode).FullName, "Mask To Logical IDs", Vector2.zero);
            logicalIdNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
            logicalIdNode.Ports.Add(new GenPortData(DefaultIntChannelName, PortDirection.Output, ChannelType.Int));
            logicalIdNode.Parameters.Add(new SerializedParameter("trueLogicalId", ((ushort)LogicalTileId.Wall).ToString(CultureInfo.InvariantCulture)));
            logicalIdNode.Parameters.Add(new SerializedParameter("falseLogicalId", ((ushort)LogicalTileId.Floor).ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(logicalIdNode);

            graph.Connections.Add(new GenConnectionData("perlin-node", "Noise", "threshold-node", "Input"));
            graph.Connections.Add(new GenConnectionData("threshold-node", "Mask", "cellular-node", "Input"));
            graph.Connections.Add(new GenConnectionData("cellular-node", "SmoothedMask", "logical-id-node", "Input"));
            ConnectToOutput(graph, logicalIdNode.NodeId, DefaultIntChannelName);
            return graph;
        }

        private static GenGraph CreateTwoPerlinMathOutputGraph()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.SchemaVersion = GraphSchemaVersion.Current;
            graph.WorldWidth = 16;
            graph.WorldHeight = 16;
            graph.DefaultSeed = 98765L;
            GraphOutputUtility.EnsureSingleOutputNode(graph, false);

            string firstPerlinOutput = GraphPortNameUtility.CreateGeneratedOutputPortName("perlin-a", GraphPortNameUtility.LegacyGenericOutputDisplayName);
            string secondPerlinOutput = GraphPortNameUtility.CreateGeneratedOutputPortName("perlin-b", GraphPortNameUtility.LegacyGenericOutputDisplayName);
            string mathOutput = GraphPortNameUtility.CreateGeneratedOutputPortName("math-node", GraphPortNameUtility.LegacyGenericOutputDisplayName);

            GenNodeData firstPerlinNode = new GenNodeData("perlin-a", typeof(PerlinNoiseNode).FullName, "Perlin A", Vector2.zero);
            firstPerlinNode.Ports.Add(new GenPortData(firstPerlinOutput, PortDirection.Output, ChannelType.Float, GraphPortNameUtility.LegacyGenericOutputDisplayName));
            firstPerlinNode.Parameters.Add(new SerializedParameter("frequency", "0.05"));
            firstPerlinNode.Parameters.Add(new SerializedParameter("amplitude", "1.0"));
            firstPerlinNode.Parameters.Add(new SerializedParameter("offset", "0,0"));
            firstPerlinNode.Parameters.Add(new SerializedParameter("octaves", "1"));
            graph.Nodes.Add(firstPerlinNode);

            GenNodeData secondPerlinNode = new GenNodeData("perlin-b", typeof(PerlinNoiseNode).FullName, "Perlin B", new Vector2(220.0f, 0.0f));
            secondPerlinNode.Ports.Add(new GenPortData(secondPerlinOutput, PortDirection.Output, ChannelType.Float, GraphPortNameUtility.LegacyGenericOutputDisplayName));
            secondPerlinNode.Parameters.Add(new SerializedParameter("frequency", "0.08"));
            secondPerlinNode.Parameters.Add(new SerializedParameter("amplitude", "0.75"));
            secondPerlinNode.Parameters.Add(new SerializedParameter("offset", "2,3"));
            secondPerlinNode.Parameters.Add(new SerializedParameter("octaves", "2"));
            graph.Nodes.Add(secondPerlinNode);

            GenNodeData mathNode = new GenNodeData("math-node", typeof(MathNode).FullName, "Math", new Vector2(440.0f, 0.0f));
            mathNode.Ports.Add(new GenPortData("A", PortDirection.Input, ChannelType.Float));
            mathNode.Ports.Add(new GenPortData("B", PortDirection.Input, ChannelType.Float));
            mathNode.Ports.Add(new GenPortData(mathOutput, PortDirection.Output, ChannelType.Float, GraphPortNameUtility.LegacyGenericOutputDisplayName));
            mathNode.Parameters.Add(new SerializedParameter("operation", MathOperation.Add.ToString()));
            mathNode.Parameters.Add(new SerializedParameter("scalarB", "0"));
            graph.Nodes.Add(mathNode);

            graph.Connections.Add(new GenConnectionData("perlin-a", firstPerlinOutput, "math-node", "A"));
            graph.Connections.Add(new GenConnectionData("perlin-b", secondPerlinOutput, "math-node", "B"));
            ConnectToOutput(graph, "math-node", mathOutput);
            return graph;
        }

        private static void DestroyImmediateIfNotNull(UnityEngine.Object unityObject)
        {
            if (unityObject != null)
            {
                UnityEngine.Object.DestroyImmediate(unityObject);
            }
        }

        private static bool ContainsError(IReadOnlyList<GraphDiagnostic> diagnostics, string messageFragment)
        {
            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = diagnostics[diagnosticIndex];
                if (diagnostic.Severity == DiagnosticSeverity.Error &&
                    diagnostic.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
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

        private static void ConnectToOutput(GenGraph graph, string fromNodeId, string fromPortName)
        {
            GenNodeData outputNode = GraphOutputUtility.FindOutputNode(graph);
            Assert.That(outputNode, Is.Not.Null);
            graph.Connections.Add(new GenConnectionData(fromNodeId, fromPortName, outputNode.NodeId, GraphOutputUtility.OutputInputPortName));
        }

        private static Tilemap GetLayerTilemap(Grid grid, string layerName)
        {
            Transform child = grid.transform.Find("Tilemap_" + layerName);
            Assert.That(child, Is.Not.Null);

            Tilemap tilemap = child.GetComponent<Tilemap>();
            Assert.That(tilemap, Is.Not.Null);
            return tilemap;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
