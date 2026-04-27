using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class BiomeNodeTests
    {
        private const string LogicalChannelName = "LogicalIds";
        private const string MaskChannelName = "OverrideMask";
        private const string TempAssetFolder = "Assets/DynamicDungeon/Tests/TempGenerated";

        [Test]
        public async Task GraphCompileIncludesDisconnectedBiomeLayersAndProducesSharedBiomeChannel()
        {
            string lowBiomePath = null;
            string highBiomePath = null;
            GenGraph graph = null;

            try
            {
                lowBiomePath = CreateBiomeAsset("BiomeLayerLow");
                highBiomePath = CreateBiomeAsset("BiomeLayerHigh");
                string lowBiomeGuid = AssetDatabase.AssetPathToGUID(lowBiomePath);
                string highBiomeGuid = AssetDatabase.AssetPathToGUID(highBiomePath);

                graph = ScriptableObject.CreateInstance<GenGraph>();
                graph.SchemaVersion = GraphSchemaVersion.Current;
                graph.WorldWidth = 3;
                graph.WorldHeight = 3;
                graph.DefaultSeed = 77L;
                GraphOutputUtility.EnsureSingleOutputNode(graph, false);

                AddIntFillNode(graph, "logical-fill", "Logical Fill", LogicalChannelName, (int)LogicalTileId.Floor);
                ConnectToOutput(graph, "logical-fill", LogicalChannelName);

                AddBiomeLayerNode(graph, "biome-low", "Biome Low", GradientDirection.Y, 0.0f, 0.1f, lowBiomeGuid);
                AddBiomeLayerNode(graph, "biome-high", "Biome High", GradientDirection.Y, 0.9f, 1.0f, highBiomeGuid);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                Assert.That(compileResult.Plan.BiomeChannelBiomes.Count, Is.EqualTo(2));

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                Assert.That(executionResult.Snapshot, Is.Not.Null);
                Assert.That(executionResult.Snapshot.BiomeChannelBiomes.Length, Is.EqualTo(2));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(executionResult.Snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                Assert.That(biomeChannel.Data[0], Is.EqualTo(0));
                Assert.That(biomeChannel.Data[3], Is.EqualTo(BiomeChannelUtility.UnassignedBiomeIndex));
                Assert.That(biomeChannel.Data[6], Is.EqualTo(1));
            }
            finally
            {
                if (graph != null)
                {
                    UnityEngine.Object.DestroyImmediate(graph);
                }

                DeleteAssetIfExists(lowBiomePath);
                DeleteAssetIfExists(highBiomePath);
            }
        }

        [Test]
        public async Task BiomeOverrideNodeUsesDistanceBlendToLimitBoundaryOverrides()
        {
            string baseBiomePath = null;
            string overrideBiomePath = null;

            try
            {
                baseBiomePath = CreateBiomeAsset("BiomeOverrideBase");
                overrideBiomePath = CreateBiomeAsset("BiomeOverrideInner");

                BiomeLayerNode baseLayerNode = new BiomeLayerNode(
                    "base-layer",
                    "Base Layer",
                    axis: GradientDirection.Y,
                    rangeMin: 0.0f,
                    rangeMax: 1.0f,
                    biome: AssetDatabase.AssetPathToGUID(baseBiomePath));

                BiomeTestMaskNode maskNode = new BiomeTestMaskNode("mask", "Mask", MaskChannelName);

                BiomeOverrideNode overrideNode = new BiomeOverrideNode(
                    "override",
                    "Override",
                    inputBiomeChannelName: BiomeChannelUtility.ChannelName,
                    inputMaskChannelName: MaskChannelName,
                    overrideBiome: AssetDatabase.AssetPathToGUID(overrideBiomePath),
                    blendEdgeWidth: 1.0f,
                    probability: 1.0f);

                BiomeChannelPalette palette = new BiomeChannelPalette();
                string errorMessage;
                Assert.That(baseLayerNode.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);
                Assert.That(overrideNode.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);

                List<IGenNode> orderedNodes = new List<IGenNode>
                {
                    baseLayerNode,
                    maskNode,
                    overrideNode
                };

                ExecutionPlan plan = ExecutionPlan.Build(orderedNodes, 5, 5, 99L);
                plan.SetBiomeChannelBiomes(palette.Biomes);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(executionResult.Snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel.Data[2 + (2 * 5)], Is.EqualTo(1));
                Assert.That(biomeChannel.Data[1 + (2 * 5)], Is.EqualTo(0));
                Assert.That(biomeChannel.Data[2 + (1 * 5)], Is.EqualTo(0));
            }
            finally
            {
                DeleteAssetIfExists(baseBiomePath);
                DeleteAssetIfExists(overrideBiomePath);
            }
        }

        [Test]
        public async Task BiomeSelectorMatrixModeWritesResolvedPaletteIndices()
        {
            string bottomLeftBiomePath = null;
            string bottomRightBiomePath = null;
            string topLeftBiomePath = null;
            string topRightBiomePath = null;

            try
            {
                bottomLeftBiomePath = CreateBiomeAsset("SelectorBottomLeft");
                bottomRightBiomePath = CreateBiomeAsset("SelectorBottomRight");
                topLeftBiomePath = CreateBiomeAsset("SelectorTopLeft");
                topRightBiomePath = CreateBiomeAsset("SelectorTopRight");

                GradientNoiseNode inputXNode = new GradientNoiseNode("gradient-x", "Gradient X", "InputX", GradientDirection.X, new Vector2(0.5f, 0.5f), 45.0f);
                GradientNoiseNode inputYNode = new GradientNoiseNode("gradient-y", "Gradient Y", "InputY", GradientDirection.Y, new Vector2(0.5f, 0.5f), 45.0f);

                string matrixEntries = "{\"Entries\":[\"" +
                    AssetDatabase.AssetPathToGUID(bottomLeftBiomePath) + "\",\"" +
                    AssetDatabase.AssetPathToGUID(bottomRightBiomePath) + "\",\"" +
                    AssetDatabase.AssetPathToGUID(topLeftBiomePath) + "\",\"" +
                    AssetDatabase.AssetPathToGUID(topRightBiomePath) + "\"]}";

                BiomeSelectorNode selectorNode = new BiomeSelectorNode(
                    "selector",
                    "Selector",
                    inputAChannelName: "InputX",
                    inputBChannelName: "InputY",
                    mode: BiomeSelectorMode.Matrix,
                    matrixColumnCount: 2,
                    matrixRowCount: 2,
                    matrixEntries: matrixEntries);

                BiomeChannelPalette palette = new BiomeChannelPalette();
                string errorMessage;
                Assert.That(selectorNode.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);

                List<IGenNode> orderedNodes = new List<IGenNode>
                {
                    inputXNode,
                    inputYNode,
                    selectorNode
                };

                ExecutionPlan plan = ExecutionPlan.Build(orderedNodes, 2, 2, 5L);
                plan.SetBiomeChannelBiomes(palette.Biomes);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True);
                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(executionResult.Snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel.Data[0], Is.EqualTo(0));
                Assert.That(biomeChannel.Data[1], Is.EqualTo(1));
                Assert.That(biomeChannel.Data[2], Is.EqualTo(2));
                Assert.That(biomeChannel.Data[3], Is.EqualTo(3));
            }
            finally
            {
                DeleteAssetIfExists(bottomLeftBiomePath);
                DeleteAssetIfExists(bottomRightBiomePath);
                DeleteAssetIfExists(topLeftBiomePath);
                DeleteAssetIfExists(topRightBiomePath);
            }
        }

        [Test]
        public void OutputPassUsesBiomeChannelPalettePerTile()
        {
            Grid grid = null;
            TileSemanticRegistry registry = null;
            BiomeAsset fallbackBiome = null;
            BiomeAsset firstBiome = null;
            BiomeAsset secondBiome = null;
            TilemapLayerDefinition floorLayer = null;
            TilemapLayerDefinition defaultLayer = null;
            Tile firstTile = null;
            Tile secondTile = null;

            try
            {
                grid = new GameObject("BiomeOutputGrid").AddComponent<Grid>();
                registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();
                fallbackBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                firstBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                secondBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                floorLayer = CreateLayerDefinition("Floor", false, "Walkable");
                defaultLayer = CreateLayerDefinition("Default", true);
                firstTile = CreateTile(Color.red);
                secondTile = CreateTile(Color.blue);

                AddRegistryEntry(registry, LogicalTileId.Floor, "Floor", "Walkable");
                AddBiomeMapping(firstBiome, LogicalTileId.Floor, firstTile);
                AddBiomeMapping(secondBiome, LogicalTileId.Floor, secondTile);

                WorldSnapshot snapshot = new WorldSnapshot();
                snapshot.Width = 2;
                snapshot.Height = 1;
                snapshot.IntChannels = new[]
                {
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = LogicalChannelName,
                        Data = new[] { (int)LogicalTileId.Floor, (int)LogicalTileId.Floor }
                    },
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = BiomeChannelUtility.ChannelName,
                        Data = new[] { 0, 1 }
                    }
                };
                snapshot.BiomeChannelBiomes = new[] { firstBiome, secondBiome };

                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                TilemapLayerDefinition[] layers = new[] { floorLayer, defaultLayer };
                writer.EnsureTimelapsCreated(grid, layers);

                outputPass.Execute(snapshot, LogicalChannelName, fallbackBiome, registry, writer, layers, Vector3Int.zero);

                Tilemap floorTilemap = GetLayerTilemap(grid, "Floor");
                Assert.That(floorTilemap.GetTile(new Vector3Int(0, 0, 0)), Is.SameAs(firstTile));
                Assert.That(floorTilemap.GetTile(new Vector3Int(1, 0, 0)), Is.SameAs(secondTile));
            }
            finally
            {
                DestroyImmediateIfNotNull(firstTile);
                DestroyImmediateIfNotNull(secondTile);
                DestroyImmediateIfNotNull(floorLayer);
                DestroyImmediateIfNotNull(defaultLayer);
                DestroyImmediateIfNotNull(firstBiome);
                DestroyImmediateIfNotNull(secondBiome);
                DestroyImmediateIfNotNull(fallbackBiome);
                DestroyImmediateIfNotNull(registry);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        private static void AddBiomeLayerNode(GenGraph graph, string nodeId, string nodeName, GradientDirection axis, float rangeMin, float rangeMax, string biomeGuid)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(BiomeLayerNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData("Data", PortDirection.Input, ChannelType.Float));
            node.Ports.Add(new GenPortData(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int));
            node.Parameters.Add(new SerializedParameter("axis", axis.ToString()));
            node.Parameters.Add(new SerializedParameter("rangeMin", rangeMin.ToString(CultureInfo.InvariantCulture)));
            node.Parameters.Add(new SerializedParameter("rangeMax", rangeMax.ToString(CultureInfo.InvariantCulture)));
            node.Parameters.Add(new SerializedParameter("biome", biomeGuid));
            graph.Nodes.Add(node);
        }

        private static void AddIntFillNode(GenGraph graph, string nodeId, string nodeName, string outputChannelName, int fillValue)
        {
            GenNodeData node = new GenNodeData(nodeId, typeof(GraphCompilerIntFillNode).FullName, nodeName, Vector2.zero);
            node.Ports.Add(new GenPortData(outputChannelName, PortDirection.Output, ChannelType.Int));
            node.Parameters.Add(new SerializedParameter("fillValue", fillValue.ToString(CultureInfo.InvariantCulture)));
            graph.Nodes.Add(node);
        }

        private static void ConnectToOutput(GenGraph graph, string fromNodeId, string fromPortName)
        {
            GenNodeData outputNode = GraphOutputUtility.FindOutputNode(graph);
            graph.Connections.Add(new GenConnectionData(fromNodeId, fromPortName, outputNode.NodeId, GraphOutputUtility.OutputInputPortName));
        }

        private static string CreateBiomeAsset(string assetName)
        {
            EnsureTempAssetFolder();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/" + assetName + ".asset");
            BiomeAsset biomeAsset = ScriptableObject.CreateInstance<BiomeAsset>();
            AssetDatabase.CreateAsset(biomeAsset, assetPath);
            AssetDatabase.SaveAssets();
            return assetPath;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (!string.IsNullOrWhiteSpace(assetPath) && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.SaveAssets();
            }
        }

        private static void EnsureTempAssetFolder()
        {
            if (AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon/Tests"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon"))
                {
                    AssetDatabase.CreateFolder("Assets", "DynamicDungeon");
                }

                AssetDatabase.CreateFolder("Assets/DynamicDungeon", "Tests");
            }

            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon/Tests/TempGenerated"))
            {
                AssetDatabase.CreateFolder("Assets/DynamicDungeon/Tests", "TempGenerated");
            }
        }

        private static WorldSnapshot.IntChannelSnapshot GetIntChannel(WorldSnapshot snapshot, string channelName)
        {
            int channelIndex;
            for (channelIndex = 0; channelIndex < snapshot.IntChannels.Length; channelIndex++)
            {
                WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels[channelIndex];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static void AddBiomeMapping(BiomeAsset biome, ushort logicalId, TileBase tile)
        {
            BiomeTileMapping mapping = new BiomeTileMapping();
            mapping.LogicalId = logicalId;
            mapping.Tile = tile;
            biome.TileMappings.Add(mapping);
        }

        private static void AddRegistryEntry(TileSemanticRegistry registry, ushort logicalId, string displayName, params string[] tags)
        {
            TileEntry entry = new TileEntry();
            entry.LogicalId = logicalId;
            entry.DisplayName = displayName;

            int tagIndex;
            for (tagIndex = 0; tagIndex < tags.Length; tagIndex++)
            {
                string tag = tags[tagIndex];
                entry.Tags.Add(tag);
                if (!registry.AllTags.Contains(tag))
                {
                    registry.AllTags.Add(tag);
                }
            }

            registry.Entries.Add(entry);
        }

        private static TilemapLayerDefinition CreateLayerDefinition(string layerName, bool isCatchAll, params string[] routingTags)
        {
            TilemapLayerDefinition layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();
            layerDefinition.LayerName = layerName;
            layerDefinition.IsCatchAll = isCatchAll;

            int tagIndex;
            for (tagIndex = 0; tagIndex < routingTags.Length; tagIndex++)
            {
                layerDefinition.RoutingTags.Add(routingTags[tagIndex]);
            }

            return layerDefinition;
        }

        private static Tile CreateTile(Color colour)
        {
            Tile tile = ScriptableObject.CreateInstance<Tile>();
            tile.color = colour;
            return tile;
        }

        private static Tilemap GetLayerTilemap(Grid grid, string layerName)
        {
            Transform child = grid.transform.Find("Tilemap_" + layerName);
            Assert.That(child, Is.Not.Null);
            Tilemap tilemap = child.GetComponent<Tilemap>();
            Assert.That(tilemap, Is.Not.Null);
            return tilemap;
        }

        private static void DestroyImmediateIfNotNull(UnityEngine.Object unityObject)
        {
            if (unityObject != null)
            {
                UnityEngine.Object.DestroyImmediate(unityObject);
            }
        }
    }

    internal sealed class BiomeTestMaskNode : IGenNode
    {
        private const int DefaultBatchSize = 64;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public BiomeTestMaskNode(string nodeId, string nodeName, string outputChannelName)
        {
            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = outputChannelName;
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
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            BiomeTestMaskJob job = new BiomeTestMaskJob
            {
                Width = context.Width,
                Height = context.Height,
                Output = output
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private struct BiomeTestMaskJob : IJobParallelFor
        {
            public int Width;
            public int Height;
            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                bool inside = x >= 1 && x <= Width - 2 && y >= 1 && y <= Height - 2;
                Output[index] = inside ? (byte)1 : (byte)0;
            }
        }
    }
}
