using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Placement;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class StackNodeTests
    {
        private const string TempAssetFolder = "Assets/DynamicDungeon/Tests/TempStackNodes";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
            EnsureTempFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public async Task MaskStackNodeCombinesAllMaskInputsInOrder()
        {
            BoolMaskSourceNode sourceA = new BoolMaskSourceNode("a", "A", new byte[] { 1, 0, 1, 0 });
            BoolMaskSourceNode sourceB = new BoolMaskSourceNode("b", "B", new byte[] { 0, 1, 1, 0 });
            BoolMaskSourceNode sourceC = new BoolMaskSourceNode("c", "C", new byte[] { 1, 1, 0, 0 });
            MaskStackNode stack = new MaskStackNode("stack", "Stack", "Out", MaskOperation.XOR);
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "A", "B", "C" });
            stack.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceA, sourceB, sourceC, stack }, 4, 1, 99L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, output.Data);
        }

        [Test]
        public async Task MaskStackNodeCanInvertCombinedOutput()
        {
            BoolMaskSourceNode sourceA = new BoolMaskSourceNode("a", "A", new byte[] { 1, 0, 0, 0 });
            BoolMaskSourceNode sourceB = new BoolMaskSourceNode("b", "B", new byte[] { 0, 1, 0, 0 });
            MaskStackNode stack = new MaskStackNode("stack", "Stack", "Out", MaskOperation.OR, true);
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "A", "B" });
            stack.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceA, sourceB, stack }, 4, 1, 99L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 1, 1 }, output.Data);
        }

        [Test]
        public async Task MaskExpressionNodeAppliesMixedOperationsInOrder()
        {
            BoolMaskSourceNode sourceA = new BoolMaskSourceNode("a", "A", new byte[] { 1, 1, 1, 0 });
            BoolMaskSourceNode sourceB = new BoolMaskSourceNode("b", "B", new byte[] { 1, 0, 1, 1 });
            BoolMaskSourceNode sourceC = new BoolMaskSourceNode("c", "C", new byte[] { 0, 1, 0, 1 });
            MaskExpressionNode expression = new MaskExpressionNode("expr", "Expression", "Out");
            expression.ReceiveParameter("rules", JsonUtility.ToJson(new MaskExpressionRuleSet
            {
                Rules = new[]
                {
                    new MaskExpressionRule { Enabled = true, MaskSlot = 1, Operation = MaskExpressionOperation.Replace },
                    new MaskExpressionRule { Enabled = true, MaskSlot = 2, Operation = MaskExpressionOperation.AND },
                    new MaskExpressionRule { Enabled = true, MaskSlot = 3, Operation = MaskExpressionOperation.OR }
                }
            }));
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "A", "B", "C" });
            expression.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceA, sourceB, sourceC, expression }, 4, 1, 99L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 1, 1, 1, 1 }, output.Data);
        }

        [Test]
        public async Task MaskExpressionNodeSupportsSubtractAndInvertedInputs()
        {
            BoolMaskSourceNode sourceA = new BoolMaskSourceNode("a", "A", new byte[] { 1, 1, 1, 1 });
            BoolMaskSourceNode sourceB = new BoolMaskSourceNode("b", "B", new byte[] { 1, 0, 1, 0 });
            BoolMaskSourceNode sourceC = new BoolMaskSourceNode("c", "C", new byte[] { 1, 1, 0, 0 });
            MaskExpressionNode expression = new MaskExpressionNode("expr", "Expression", "Out");
            expression.ReceiveParameter("rules", JsonUtility.ToJson(new MaskExpressionRuleSet
            {
                Rules = new[]
                {
                    new MaskExpressionRule { Enabled = true, MaskSlot = 1, Operation = MaskExpressionOperation.Replace },
                    new MaskExpressionRule { Enabled = true, MaskSlot = 2, Operation = MaskExpressionOperation.Subtract },
                    new MaskExpressionRule { Enabled = true, MaskSlot = 3, Operation = MaskExpressionOperation.AND, Invert = true }
                }
            }));
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "A", "B", "C" });
            expression.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceA, sourceB, sourceC, expression }, 4, 1, 99L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1 }, output.Data);
        }

        [Test]
        public async Task MaskExpressionNodeClearsOutputForEmptyOrMissingRows()
        {
            BoolMaskSourceNode sourceA = new BoolMaskSourceNode("a", "A", new byte[] { 1, 1, 1, 1 });
            MaskExpressionNode expression = new MaskExpressionNode("expr", "Expression", "Out");
            expression.ReceiveParameter("rules", JsonUtility.ToJson(new MaskExpressionRuleSet
            {
                Rules = new[]
                {
                    new MaskExpressionRule { Enabled = true, MaskSlot = 2, Operation = MaskExpressionOperation.Replace }
                }
            }));
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "A" });
            expression.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { sourceA, expression }, 4, 1, 99L);
            WorldSnapshot.BoolMaskChannelSnapshot output = GetBoolMaskChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, output.Data);
        }

        [Test]
        public async Task LogicalIdRuleStackNodeAppliesOrderedMaskRows()
        {
            IntSourceNode baseIds = new IntSourceNode("base", "Base", new[] { 1, 1, 2, 2 });
            BoolMaskSourceNode maskA = new BoolMaskSourceNode("mask-a", "A", new byte[] { 1, 0, 1, 0 });
            BoolMaskSourceNode maskB = new BoolMaskSourceNode("mask-b", "B", new byte[] { 0, 1, 1, 0 });
            LogicalIdRuleStackNode stack = new LogicalIdRuleStackNode("rules", "Rules", outputChannelName: "Out");
            stack.ReceiveParameter("rules", JsonUtility.ToJson(new LogicalIdRuleSet
            {
                Rules = new[]
                {
                    new LogicalIdRule { Enabled = true, MaskSlot = 1, SourceLogicalId = -1, TargetLogicalId = 8 },
                    new LogicalIdRule { Enabled = true, MaskSlot = 2, SourceLogicalId = 8, TargetLogicalId = 9 }
                }
            }));

            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Base", new[] { "Base" });
            connections.SetConnections("Masks", new[] { "A", "B" });
            stack.ReceiveInputConnections(connections);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { baseIds, maskA, maskB, stack }, 4, 1, 99L);
            WorldSnapshot.IntChannelSnapshot output = GetIntChannel(snapshot, "Out");

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new[] { 8, 1, 9, 2 }, output.Data);
        }

        [Test]
        public async Task BiomeOverrideStackNodeAppliesResolvedOverrideRows()
        {
            string baseBiomeGuid = CreateBiomeAsset("BaseBiome");
            string overrideBiomeGuid = CreateBiomeAsset("OverrideBiome");
            BiomeChannelPalette palette = new BiomeChannelPalette();
            ResolveBiome(palette, baseBiomeGuid);
            int overrideIndex = ResolveBiome(palette, overrideBiomeGuid);

            BiomeFillNode baseBiome = new BiomeFillNode("base-biome", "Base Biome", 0);
            BoolMaskSourceNode mask = new BoolMaskSourceNode("mask", "Mask", new byte[] { 1, 0, 1, 0 });
            BiomeOverrideStackNode stack = new BiomeOverrideStackNode("override", "Override");
            stack.ReceiveParameter("rules", JsonUtility.ToJson(new BiomeOverrideStackRuleSet
            {
                Rules = new[]
                {
                    new BiomeOverrideStackRule
                    {
                        Enabled = true,
                        MaskSlot = 1,
                        OverrideBiome = overrideBiomeGuid,
                        Probability = 1.0f
                    }
                }
            }));
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Masks", new[] { "Mask" });
            stack.ReceiveInputConnections(connections);
            string errorMessage;
            Assert.That(stack.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { baseBiome, mask, stack }, 4, 1, 99L);
            WorldSnapshot.IntChannelSnapshot output = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

            Assert.That(output, Is.Not.Null);
            CollectionAssert.AreEqual(new[] { overrideIndex, 0, overrideIndex, 0 }, output.Data);
        }

        [Test]
        public async Task PlacementSetNodeEmitsRowsFromWeightedInputs()
        {
            string prefabGuid = CreateStampPrefab("StackPlacementPrefab");
            PlacementSetNode placementSet = new PlacementSetNode("placement", "Placement");
            placementSet.ReceiveParameter("rules", JsonUtility.ToJson(new PlacementSetRuleSet
            {
                Rules = new[]
                {
                    new PlacementSetRule
                    {
                        Enabled = true,
                        WeightSlot = 1,
                        Prefab = prefabGuid,
                        Threshold = 0.5f,
                        Density = 1.0f,
                        PointCount = 2,
                        OffsetX = 1
                    }
                }
            }));
            InputConnectionMap connections = new InputConnectionMap();
            connections.SetConnections("Weights", new[] { "Weights" });
            placementSet.ReceiveInputConnections(connections);
            string errorMessage;
            Assert.That(placementSet.ResolvePrefabPalette(new PrefabStampPalette(), out errorMessage), Is.True, errorMessage);

            FloatSourceNode weights = new FloatSourceNode("weights", "Weights", new[] { 1.0f, 1.0f, 0.0f, 1.0f });
            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { weights, placementSet }, 4, 1, 99L);
            PrefabPlacementRecord[] placements = GetPlacements(snapshot);

            Assert.That(placements.Length, Is.EqualTo(2));
            Assert.That(placements[0].TemplateIndex, Is.EqualTo(0));
            Assert.That(placements[0].OriginX, Is.EqualTo(1));
        }

        private static async Task<WorldSnapshot> ExecuteNodesAsync(IReadOnlyList<IGenNode> nodes, int width, int height, long seed)
        {
            ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, seed);
            Executor executor = new Executor();
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            Assert.That(result.Snapshot, Is.Not.Null);
            plan.Dispose();
            return result.Snapshot;
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

        private static PrefabPlacementRecord[] GetPlacements(WorldSnapshot snapshot)
        {
            int index;
            for (index = 0; index < snapshot.PrefabPlacementChannels.Length; index++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot channel = snapshot.PrefabPlacementChannels[index];
                if (channel.Name == PrefabPlacementChannelUtility.ChannelName)
                {
                    return channel.Data;
                }
            }

            return Array.Empty<PrefabPlacementRecord>();
        }

        private static string CreateBiomeAsset(string name)
        {
            BiomeAsset asset = ScriptableObject.CreateInstance<BiomeAsset>();
            string path = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/" + name + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static int ResolveBiome(BiomeChannelPalette palette, string guid)
        {
            int index;
            string errorMessage;
            Assert.That(palette.TryResolveIndex(guid, out index, out errorMessage), Is.True, errorMessage);
            return index;
        }

        private static string CreateStampPrefab(string name)
        {
            GameObject root = new GameObject(name);
            root.AddComponent<PrefabStampAuthoring>();
            GameObject occupiedCell = new GameObject("Cell");
            occupiedCell.transform.SetParent(root.transform, false);
            occupiedCell.transform.localPosition = Vector3.zero;

            string path = AssetDatabase.GenerateUniqueAssetPath(TempAssetFolder + "/" + name + ".prefab");
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.AssetPathToGUID(path);
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon"))
            {
                AssetDatabase.CreateFolder("Assets", "DynamicDungeon");
            }

            if (!AssetDatabase.IsValidFolder("Assets/DynamicDungeon/Tests"))
            {
                AssetDatabase.CreateFolder("Assets/DynamicDungeon", "Tests");
            }

            if (!AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                AssetDatabase.CreateFolder("Assets/DynamicDungeon/Tests", "TempStackNodes");
            }
        }

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                AssetDatabase.DeleteAsset(TempAssetFolder);
                AssetDatabase.SaveAssets();
            }
        }

        private sealed class BoolMaskSourceNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly byte[] _values;

            public IReadOnlyList<NodePortDefinition> Ports => _ports;
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId { get; }
            public string NodeName { get; }

            public BoolMaskSourceNode(string nodeId, string outputChannelName, byte[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<byte>();
                _ports = new[] { new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.BoolMask) };
                _channelDeclarations = new[] { new ChannelDeclaration(outputChannelName, ChannelType.BoolMask, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<byte> output = context.GetBoolMaskChannel(_channelDeclarations[0].ChannelName);
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] = _values[index];
                }

                return context.InputDependency;
            }
        }

        private sealed class FloatSourceNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly float[] _values;

            public IReadOnlyList<NodePortDefinition> Ports => _ports;
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId { get; }
            public string NodeName { get; }

            public FloatSourceNode(string nodeId, string outputChannelName, float[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<float>();
                _ports = new[] { new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Float) };
                _channelDeclarations = new[] { new ChannelDeclaration(outputChannelName, ChannelType.Float, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<float> output = context.GetFloatChannel(_channelDeclarations[0].ChannelName);
                for (int index = 0; index < output.Length; index++)
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

            public IReadOnlyList<NodePortDefinition> Ports => _ports;
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId { get; }
            public string NodeName { get; }

            public IntSourceNode(string nodeId, string outputChannelName, int[] values)
            {
                NodeId = nodeId;
                NodeName = nodeId;
                _values = values ?? Array.Empty<int>();
                _ports = new[] { new NodePortDefinition(outputChannelName, PortDirection.Output, ChannelType.Int) };
                _channelDeclarations = new[] { new ChannelDeclaration(outputChannelName, ChannelType.Int, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<int> output = context.GetIntChannel(_channelDeclarations[0].ChannelName);
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] = _values[index];
                }

                return context.InputDependency;
            }
        }

        private sealed class BiomeFillNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly int _value;

            public IReadOnlyList<NodePortDefinition> Ports => _ports;
            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
            public IReadOnlyList<BlackboardKey> BlackboardDeclarations => Array.Empty<BlackboardKey>();
            public string NodeId { get; }
            public string NodeName { get; }

            public BiomeFillNode(string nodeId, string nodeName, int value)
            {
                NodeId = nodeId;
                NodeName = nodeName;
                _value = value;
                _channelDeclarations = new[] { new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true) };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                NativeArray<int> output = context.GetIntChannel(BiomeChannelUtility.ChannelName);
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] = _value;
                }

                return context.InputDependency;
            }
        }
    }
}
