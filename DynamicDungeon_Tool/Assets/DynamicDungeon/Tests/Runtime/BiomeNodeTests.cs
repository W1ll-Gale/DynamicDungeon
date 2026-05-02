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
        
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (AssetDatabase.IsValidFolder(TempAssetFolder))
            {
                AssetDatabase.DeleteAsset(TempAssetFolder);
                AssetDatabase.SaveAssets();
            }
        }

        [Test]
        public async Task BiomeLayerNodeYWritesExpectedBiomeIndexWithinRangeAndLeavesOtherTilesUnchanged()
        {
            string reservedBiomePath = null;
            string targetBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeLayerReserved");
                targetBiomePath = CreateBiomeAsset("BiomeLayerTarget");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string targetBiomeGuid = AssetDatabase.AssetPathToGUID(targetBiomePath);

                BiomeLayerNode layerNode = new BiomeLayerNode(
                    "biome-layer-y",
                    "Biome Layer Y",
                    axis: GradientDirection.Y,
                    rangeMin: 0.25f,
                    rangeMax: 0.75f,
                    biome: targetBiomeGuid);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(layerNode, palette);

                int expectedBiomeIndex = GetResolvedBiomeIndex(palette, targetBiomeGuid);
                Assert.That(expectedBiomeIndex, Is.EqualTo(1));

                WorldSnapshot snapshot = await ExecuteNodesAsync(
                    new IGenNode[] { layerNode },
                    3,
                    5,
                    4401L,
                    palette.Biomes,
                    CreateInitialBiomeSnapshot(3, 5, 0));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                AssertRowValues(biomeChannel.Data, 3, 0, 0);
                AssertRowValues(biomeChannel.Data, 3, 1, expectedBiomeIndex);
                AssertRowValues(biomeChannel.Data, 3, 2, expectedBiomeIndex);
                AssertRowValues(biomeChannel.Data, 3, 3, expectedBiomeIndex);
                AssertRowValues(biomeChannel.Data, 3, 4, 0);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(targetBiomePath);
            }
        }

        [Test]
        public void BiomeMaskNodeResolvesBiomeFromCanonicalParameter()
        {
            string reservedBiomePath = null;
            string targetBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeMaskReserved");
                targetBiomePath = CreateBiomeAsset("BiomeMaskTarget");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string targetBiomeGuid = AssetDatabase.AssetPathToGUID(targetBiomePath);

                BiomeMaskNode node = new BiomeMaskNode("biome-mask", "Biome Mask");
                node.ReceiveParameter("biome", targetBiomeGuid);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);

                string errorMessage;
                Assert.That(node.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(targetBiomePath);
            }
        }

        [Test]
        public async Task BiomeLayerNodeYStackedLayersWriteSeparateRangesWithoutOverwritingEachOther()
        {
            string reservedBiomePath = null;
            string lowerBiomePath = null;
            string upperBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeLayerStackReserved");
                lowerBiomePath = CreateBiomeAsset("BiomeLayerLower");
                upperBiomePath = CreateBiomeAsset("BiomeLayerUpper");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string lowerBiomeGuid = AssetDatabase.AssetPathToGUID(lowerBiomePath);
                string upperBiomeGuid = AssetDatabase.AssetPathToGUID(upperBiomePath);

                BiomeLayerNode lowerLayerNode = new BiomeLayerNode(
                    "biome-layer-lower",
                    "Biome Layer Lower",
                    axis: GradientDirection.Y,
                    rangeMin: 0.0f,
                    rangeMax: 0.25f,
                    biome: lowerBiomeGuid);

                BiomeLayerNode upperLayerNode = new BiomeLayerNode(
                    "biome-layer-upper",
                    "Biome Layer Upper",
                    axis: GradientDirection.Y,
                    rangeMin: 0.75f,
                    rangeMax: 1.0f,
                    biome: upperBiomeGuid);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(lowerLayerNode, palette);
                ResolveBiomePalette(upperLayerNode, palette);

                int lowerBiomeIndex = GetResolvedBiomeIndex(palette, lowerBiomeGuid);
                int upperBiomeIndex = GetResolvedBiomeIndex(palette, upperBiomeGuid);

                Assert.That(lowerBiomeIndex, Is.EqualTo(1));
                Assert.That(upperBiomeIndex, Is.EqualTo(2));

                WorldSnapshot snapshot = await ExecuteNodesAsync(
                    new IGenNode[] { lowerLayerNode, upperLayerNode },
                    2,
                    5,
                    4402L,
                    palette.Biomes,
                    CreateInitialBiomeSnapshot(2, 5, 0));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                AssertRowValues(biomeChannel.Data, 2, 0, lowerBiomeIndex);
                AssertRowValues(biomeChannel.Data, 2, 1, lowerBiomeIndex);
                AssertRowValues(biomeChannel.Data, 2, 2, 0);
                AssertRowValues(biomeChannel.Data, 2, 3, upperBiomeIndex);
                AssertRowValues(biomeChannel.Data, 2, 4, upperBiomeIndex);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(lowerBiomePath);
                DeleteAssetIfExists(upperBiomePath);
            }
        }

        [Test]
        public async Task BiomeOverrideNodeOverridesAllMaskedTilesWhenProbabilityIsOne()
        {
            string reservedBiomePath = null;
            string overrideBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeOverrideReserved");
                overrideBiomePath = CreateBiomeAsset("BiomeOverrideTarget");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string overrideBiomeGuid = AssetDatabase.AssetPathToGUID(overrideBiomePath);

                BiomeTestMaskNode maskNode = new BiomeTestMaskNode("mask-probability-one", "Mask Probability One", MaskChannelName);
                BiomeOverrideNode overrideNode = new BiomeOverrideNode(
                    "override-probability-one",
                    "Override Probability One",
                    inputMaskChannelName: MaskChannelName,
                    overrideBiome: overrideBiomeGuid,
                    blendEdgeWidth: 0.0f,
                    probability: 1.0f);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(overrideNode, palette);

                int overrideBiomeIndex = GetResolvedBiomeIndex(palette, overrideBiomeGuid);
                Assert.That(overrideBiomeIndex, Is.EqualTo(1));

                WorldSnapshot snapshot = await ExecuteNodesAsync(
                    new IGenNode[] { maskNode, overrideNode },
                    5,
                    5,
                    4403L,
                    palette.Biomes,
                    CreateInitialBiomeSnapshot(5, 5, 0));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                AssertMaskedOverrideValues(biomeChannel.Data, 5, 5, overrideBiomeIndex, 0);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(overrideBiomePath);
            }
        }

        [Test]
        public async Task BiomeOverrideNodeLeavesMaskedTilesUnchangedWhenProbabilityIsZero()
        {
            string reservedBiomePath = null;
            string overrideBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeOverrideZeroReserved");
                overrideBiomePath = CreateBiomeAsset("BiomeOverrideZeroTarget");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string overrideBiomeGuid = AssetDatabase.AssetPathToGUID(overrideBiomePath);

                BiomeTestMaskNode maskNode = new BiomeTestMaskNode("mask-probability-zero", "Mask Probability Zero", MaskChannelName);
                BiomeOverrideNode overrideNode = new BiomeOverrideNode(
                    "override-probability-zero",
                    "Override Probability Zero",
                    inputMaskChannelName: MaskChannelName,
                    overrideBiome: overrideBiomeGuid,
                    blendEdgeWidth: 0.0f,
                    probability: 0.0f);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(overrideNode, palette);

                WorldSnapshot snapshot = await ExecuteNodesAsync(
                    new IGenNode[] { maskNode, overrideNode },
                    5,
                    5,
                    4404L,
                    palette.Biomes,
                    CreateInitialBiomeSnapshot(5, 5, 0));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(new int[25], biomeChannel.Data);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(overrideBiomePath);
            }
        }

        [Test]
        public async Task BiomeOverrideNodeProducesDeterministicOutputAcrossRunsWithTheSameLocalSeed()
        {
            string reservedBiomePath = null;
            string overrideBiomePath = null;
            ExecutionPlan firstPlan = null;
            ExecutionPlan secondPlan = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeOverrideDeterministicReserved");
                overrideBiomePath = CreateBiomeAsset("BiomeOverrideDeterministicTarget");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string overrideBiomeGuid = AssetDatabase.AssetPathToGUID(overrideBiomePath);

                BiomeTestMaskNode firstMaskNode = new BiomeTestMaskNode("mask-deterministic", "Mask Deterministic", MaskChannelName);
                BiomeOverrideNode firstOverrideNode = new BiomeOverrideNode(
                    "override-deterministic",
                    "Override Deterministic",
                    inputMaskChannelName: MaskChannelName,
                    overrideBiome: overrideBiomeGuid,
                    blendEdgeWidth: 0.0f,
                    probability: 0.5f);

                BiomeTestMaskNode secondMaskNode = new BiomeTestMaskNode("mask-deterministic", "Mask Deterministic", MaskChannelName);
                BiomeOverrideNode secondOverrideNode = new BiomeOverrideNode(
                    "override-deterministic",
                    "Override Deterministic",
                    inputMaskChannelName: MaskChannelName,
                    overrideBiome: overrideBiomeGuid,
                    blendEdgeWidth: 0.0f,
                    probability: 0.5f);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(firstOverrideNode, palette);
                ResolveBiomePalette(secondOverrideNode, palette);

                firstPlan = ExecutionPlan.Build(new IGenNode[] { firstMaskNode, firstOverrideNode }, 5, 5, 4405L);
                secondPlan = ExecutionPlan.Build(new IGenNode[] { secondMaskNode, secondOverrideNode }, 5, 5, 4405L);

                firstPlan.SetBiomeChannelBiomes(palette.Biomes);
                secondPlan.SetBiomeChannelBiomes(palette.Biomes);
                firstPlan.RestoreWorldSnapshot(CreateInitialBiomeSnapshot(5, 5, 0));
                secondPlan.RestoreWorldSnapshot(CreateInitialBiomeSnapshot(5, 5, 0));

                long firstLocalSeed = firstPlan.GetLocalSeed("override-deterministic");
                long secondLocalSeed = secondPlan.GetLocalSeed("override-deterministic");

                Assert.That(firstLocalSeed, Is.EqualTo(secondLocalSeed));

                Executor executor = new Executor();
                ExecutionResult firstResult = await executor.ExecuteAsync(firstPlan, CancellationToken.None);
                ExecutionResult secondResult = await executor.ExecuteAsync(secondPlan, CancellationToken.None);

                Assert.That(firstResult.IsSuccess, Is.True);
                Assert.That(secondResult.IsSuccess, Is.True);

                WorldSnapshot.IntChannelSnapshot firstBiomeChannel = GetIntChannel(firstResult.Snapshot, BiomeChannelUtility.ChannelName);
                WorldSnapshot.IntChannelSnapshot secondBiomeChannel = GetIntChannel(secondResult.Snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(firstBiomeChannel, Is.Not.Null);
                Assert.That(secondBiomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(firstBiomeChannel.Data, secondBiomeChannel.Data);
            }
            finally
            {
                if (firstPlan != null)
                {
                    firstPlan.Dispose();
                }

                if (secondPlan != null)
                {
                    secondPlan.Dispose();
                }

                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(overrideBiomePath);
            }
        }

        [Test]
        public async Task BiomeSelectorNodeRangeModeWritesResolvedBiomeIndicesAndLeavesGapTilesUnchanged()
        {
            string reservedBiomePath = null;
            string lowerBiomePath = null;
            string upperBiomePath = null;

            try
            {
                reservedBiomePath = CreateBiomeAsset("BiomeSelectorRangeReserved");
                lowerBiomePath = CreateBiomeAsset("BiomeSelectorRangeLower");
                upperBiomePath = CreateBiomeAsset("BiomeSelectorRangeUpper");

                string reservedBiomeGuid = AssetDatabase.AssetPathToGUID(reservedBiomePath);
                string lowerBiomeGuid = AssetDatabase.AssetPathToGUID(lowerBiomePath);
                string upperBiomeGuid = AssetDatabase.AssetPathToGUID(upperBiomePath);

                BiomeFloatSourceNode inputNode = new BiomeFloatSourceNode(
                    "selector-range-input",
                    "RangeInput",
                    new[]
                    {
                        0.1f,
                        0.3f,
                        0.5f,
                        0.9f
                    });

                string rangeEntries =
                    "{\"Entries\":[" +
                    "{\"Biome\":\"" + lowerBiomeGuid + "\",\"RangeMin\":0.0,\"RangeMax\":0.2}," +
                    "{\"Biome\":\"" + upperBiomeGuid + "\",\"RangeMin\":0.4,\"RangeMax\":0.6}" +
                    "]}";

                BiomeSelectorNode selectorNode = new BiomeSelectorNode(
                    "selector-range",
                    "Selector Range",
                    inputChannelName: "RangeInput",
                    mode: BiomeSelectorMode.Range,
                    rangeEntries: rangeEntries);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(reservedBiomeGuid);
                ResolveBiomePalette(selectorNode, palette);

                int lowerBiomeIndex = GetResolvedBiomeIndex(palette, lowerBiomeGuid);
                int upperBiomeIndex = GetResolvedBiomeIndex(palette, upperBiomeGuid);

                Assert.That(lowerBiomeIndex, Is.EqualTo(1));
                Assert.That(upperBiomeIndex, Is.EqualTo(2));

                WorldSnapshot snapshot = await ExecuteNodesAsync(
                    new IGenNode[] { inputNode, selectorNode },
                    4,
                    1,
                    4406L,
                    palette.Biomes,
                    CreateInitialBiomeSnapshot(4, 1, 0));

                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(new[] { lowerBiomeIndex, 0, upperBiomeIndex, 0 }, biomeChannel.Data);
            }
            finally
            {
                DeleteAssetIfExists(reservedBiomePath);
                DeleteAssetIfExists(lowerBiomePath);
                DeleteAssetIfExists(upperBiomePath);
            }
        }


        [Test]
        public async Task BiomeLayoutNodeStripsAreDeterministicAndProtectCenterBiome()
        {
            string forestBiomePath = null;
            string jungleBiomePath = null;
            string desertBiomePath = null;

            try
            {
                forestBiomePath = CreateBiomeAsset("BiomeLayoutForest");
                jungleBiomePath = CreateBiomeAsset("BiomeLayoutJungle");
                desertBiomePath = CreateBiomeAsset("BiomeLayoutDesert");

                string forestBiomeGuid = AssetDatabase.AssetPathToGUID(forestBiomePath);
                string jungleBiomeGuid = AssetDatabase.AssetPathToGUID(jungleBiomePath);
                string desertBiomeGuid = AssetDatabase.AssetPathToGUID(desertBiomePath);
                string rules = CreateBiomeLayoutRulesJson(
                    new[]
                    {
                        CreateBiomeLayoutEntry(forestBiomeGuid, 1.0f),
                        CreateBiomeLayoutEntry(jungleBiomeGuid, 2.0f),
                        CreateBiomeLayoutEntry(desertBiomeGuid, 1.0f)
                    },
                    new[]
                    {
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.ProtectedCenter, forestBiomeGuid, 8)
                    });

                BiomeLayoutNode firstNode = new BiomeLayoutNode(
                    "layout-strips",
                    "Layout Strips",
                    axis: GradientDirection.X,
                    minRegionSize: 4,
                    maxRegionSize: 6,
                    blendWidth: 0,
                    rules: rules);
                BiomeLayoutNode secondNode = new BiomeLayoutNode(
                    "layout-strips",
                    "Layout Strips",
                    axis: GradientDirection.X,
                    minRegionSize: 4,
                    maxRegionSize: 6,
                    blendWidth: 0,
                    rules: rules);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(forestBiomeGuid);
                ResolveBiomePalette(firstNode, palette);
                ResolveBiomePalette(secondNode, palette);

                int forestBiomeIndex = GetResolvedBiomeIndex(palette, forestBiomeGuid);
                WorldSnapshot firstSnapshot = await ExecuteNodesAsync(new IGenNode[] { firstNode }, 32, 2, 7801L, palette.Biomes, null);
                WorldSnapshot secondSnapshot = await ExecuteNodesAsync(new IGenNode[] { secondNode }, 32, 2, 7801L, palette.Biomes, null);
                WorldSnapshot.IntChannelSnapshot firstBiomeChannel = GetIntChannel(firstSnapshot, BiomeChannelUtility.ChannelName);
                WorldSnapshot.IntChannelSnapshot secondBiomeChannel = GetIntChannel(secondSnapshot, BiomeChannelUtility.ChannelName);

                Assert.That(firstBiomeChannel, Is.Not.Null);
                Assert.That(secondBiomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(firstBiomeChannel.Data, secondBiomeChannel.Data);

                int y;
                for (y = 0; y < 2; y++)
                {
                    int x;
                    for (x = 12; x < 20; x++)
                    {
                        Assert.That(firstBiomeChannel.Data[x + (y * 32)], Is.EqualTo(forestBiomeIndex));
                    }
                }
            }
            finally
            {
                DeleteAssetIfExists(forestBiomePath);
                DeleteAssetIfExists(jungleBiomePath);
                DeleteAssetIfExists(desertBiomePath);
            }
        }

        [Test]
        public async Task BiomeLayoutNodeStripsRespectWeightsAndRegionBounds()
        {
            string forestBiomePath = null;
            string jungleBiomePath = null;
            string desertBiomePath = null;

            try
            {
                forestBiomePath = CreateBiomeAsset("BiomeLayoutWeightedForest");
                jungleBiomePath = CreateBiomeAsset("BiomeLayoutWeightedJungle");
                desertBiomePath = CreateBiomeAsset("BiomeLayoutWeightedDesert");

                string forestBiomeGuid = AssetDatabase.AssetPathToGUID(forestBiomePath);
                string jungleBiomeGuid = AssetDatabase.AssetPathToGUID(jungleBiomePath);
                string desertBiomeGuid = AssetDatabase.AssetPathToGUID(desertBiomePath);
                string rules = CreateBiomeLayoutRulesJson(
                    new[]
                    {
                        CreateBiomeLayoutEntry(forestBiomeGuid, 1.0f, 4, 4),
                        CreateBiomeLayoutEntry(jungleBiomeGuid, 0.0f, 4, 4),
                        CreateBiomeLayoutEntry(desertBiomeGuid, 1.0f, 4, 4)
                    });

                BiomeLayoutNode layoutNode = new BiomeLayoutNode(
                    "layout-weighted",
                    "Layout Weighted",
                    axis: GradientDirection.X,
                    minRegionSize: 4,
                    maxRegionSize: 4,
                    blendWidth: 0,
                    rules: rules);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(forestBiomeGuid);
                ResolveBiomePalette(layoutNode, palette);

                int jungleBiomeIndex = GetResolvedBiomeIndex(palette, jungleBiomeGuid);
                WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { layoutNode }, 24, 1, 7802L, palette.Biomes, null);
                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                Assert.That(Array.IndexOf(biomeChannel.Data, jungleBiomeIndex), Is.EqualTo(-1));
                AssertStripRunBounds(biomeChannel.Data, 24, 0, 4, 4);
            }
            finally
            {
                DeleteAssetIfExists(forestBiomePath);
                DeleteAssetIfExists(jungleBiomePath);
                DeleteAssetIfExists(desertBiomePath);
            }
        }

        [Test]
        public async Task BiomeLayoutNodeSupportsEdgeAndRequiredBiomesOutsideWeightedEntries()
        {
            string forestBiomePath = null;
            string desertBiomePath = null;
            string oceanBiomePath = null;
            string jungleBiomePath = null;

            try
            {
                forestBiomePath = CreateBiomeAsset("BiomeLayoutEdgeForest");
                desertBiomePath = CreateBiomeAsset("BiomeLayoutEdgeDesert");
                oceanBiomePath = CreateBiomeAsset("BiomeLayoutEdgeOcean");
                jungleBiomePath = CreateBiomeAsset("BiomeLayoutRequiredJungle");

                string forestBiomeGuid = AssetDatabase.AssetPathToGUID(forestBiomePath);
                string desertBiomeGuid = AssetDatabase.AssetPathToGUID(desertBiomePath);
                string oceanBiomeGuid = AssetDatabase.AssetPathToGUID(oceanBiomePath);
                string jungleBiomeGuid = AssetDatabase.AssetPathToGUID(jungleBiomePath);
                string rules = CreateBiomeLayoutRulesJson(
                    new[]
                    {
                        CreateBiomeLayoutEntry(forestBiomeGuid, 1.0f, 4, 8),
                        CreateBiomeLayoutEntry(desertBiomeGuid, 1.0f, 4, 8)
                    },
                    new[]
                    {
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.StartEdge, oceanBiomeGuid, 3),
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.EndEdge, oceanBiomeGuid, 4),
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.Required, jungleBiomeGuid, 5)
                    });

                BiomeLayoutNode layoutNode = new BiomeLayoutNode(
                    "layout-constraints",
                    "Layout Constraints",
                    axis: GradientDirection.X,
                    minRegionSize: 4,
                    maxRegionSize: 8,
                    blendWidth: 0,
                    rules: rules);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(forestBiomeGuid);
                ResolveBiomePalette(layoutNode, palette);

                int oceanBiomeIndex = GetResolvedBiomeIndex(palette, oceanBiomeGuid);
                int jungleBiomeIndex = GetResolvedBiomeIndex(palette, jungleBiomeGuid);
                WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { layoutNode }, 32, 1, 7803L, palette.Biomes, null);
                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(biomeChannel, Is.Not.Null);
                Assert.That(biomeChannel.Data[0], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[1], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[2], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[28], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[29], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[30], Is.EqualTo(oceanBiomeIndex));
                Assert.That(biomeChannel.Data[31], Is.EqualTo(oceanBiomeIndex));
                CollectionAssert.Contains(biomeChannel.Data, jungleBiomeIndex);
            }
            finally
            {
                DeleteAssetIfExists(forestBiomePath);
                DeleteAssetIfExists(desertBiomePath);
                DeleteAssetIfExists(oceanBiomePath);
                DeleteAssetIfExists(jungleBiomePath);
            }
        }

        [Test]
        public async Task BiomeLayoutNodeBoundaryBlendKeepsSeamColumnsMostlyNative()
        {
            const int width = 16;
            const int height = 512;
            string firstBiomePath = null;
            string secondBiomePath = null;

            try
            {
                firstBiomePath = CreateBiomeAsset("BiomeLayoutBlendFirst");
                secondBiomePath = CreateBiomeAsset("BiomeLayoutBlendSecond");

                string firstBiomeGuid = AssetDatabase.AssetPathToGUID(firstBiomePath);
                string secondBiomeGuid = AssetDatabase.AssetPathToGUID(secondBiomePath);
                string rules = CreateBiomeLayoutRulesJson(
                    new[]
                    {
                        CreateBiomeLayoutEntry(firstBiomeGuid, 1.0f, 8, 8),
                        CreateBiomeLayoutEntry(secondBiomeGuid, 1.0f, 8, 8)
                    });

                BiomeLayoutNode firstNode = new BiomeLayoutNode(
                    "layout-blend",
                    "Layout Blend",
                    axis: GradientDirection.X,
                    minRegionSize: 8,
                    maxRegionSize: 8,
                    blendWidth: 4,
                    rules: rules);
                BiomeLayoutNode secondNode = new BiomeLayoutNode(
                    "layout-blend",
                    "Layout Blend",
                    axis: GradientDirection.X,
                    minRegionSize: 8,
                    maxRegionSize: 8,
                    blendWidth: 4,
                    rules: rules);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(firstBiomeGuid);
                ResolveBiomePalette(firstNode, palette);
                ResolveBiomePalette(secondNode, palette);

                WorldSnapshot firstSnapshot = await ExecuteNodesAsync(new IGenNode[] { firstNode }, width, height, 7810L, palette.Biomes, null);
                WorldSnapshot secondSnapshot = await ExecuteNodesAsync(new IGenNode[] { secondNode }, width, height, 7810L, palette.Biomes, null);
                WorldSnapshot.IntChannelSnapshot firstBiomeChannel = GetIntChannel(firstSnapshot, BiomeChannelUtility.ChannelName);
                WorldSnapshot.IntChannelSnapshot secondBiomeChannel = GetIntChannel(secondSnapshot, BiomeChannelUtility.ChannelName);

                Assert.That(firstBiomeChannel, Is.Not.Null);
                Assert.That(secondBiomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(firstBiomeChannel.Data, secondBiomeChannel.Data);

                int leftNativeBiome = firstBiomeChannel.Data[0];
                int rightNativeBiome = firstBiomeChannel.Data[width - 1];
                Assert.That(leftNativeBiome, Is.Not.EqualTo(rightNativeBiome));

                int leftSeamNativeCount = CountColumnValue(firstBiomeChannel.Data, width, height, 7, leftNativeBiome);
                int rightSeamNativeCount = CountColumnValue(firstBiomeChannel.Data, width, height, 8, rightNativeBiome);

                Assert.That(leftSeamNativeCount, Is.GreaterThanOrEqualTo(height / 2), "Left seam column should not mostly flip into the neighbor biome.");
                Assert.That(rightSeamNativeCount, Is.GreaterThanOrEqualTo(height / 2), "Right seam column should not mostly flip into the neighbor biome.");
                Assert.That(CountColumnValue(firstBiomeChannel.Data, width, height, 7, rightNativeBiome), Is.GreaterThan(0));
                Assert.That(CountColumnValue(firstBiomeChannel.Data, width, height, 8, leftNativeBiome), Is.GreaterThan(0));
            }
            finally
            {
                DeleteAssetIfExists(firstBiomePath);
                DeleteAssetIfExists(secondBiomePath);
            }
        }

        [Test]
        public async Task BiomeLayoutNodeCellsAreDeterministicAndApplyConstraints()
        {
            string forestBiomePath = null;
            string desertBiomePath = null;
            string oceanBiomePath = null;
            string jungleBiomePath = null;

            try
            {
                forestBiomePath = CreateBiomeAsset("BiomeLayoutCellForest");
                desertBiomePath = CreateBiomeAsset("BiomeLayoutCellDesert");
                oceanBiomePath = CreateBiomeAsset("BiomeLayoutCellOcean");
                jungleBiomePath = CreateBiomeAsset("BiomeLayoutCellJungle");

                string forestBiomeGuid = AssetDatabase.AssetPathToGUID(forestBiomePath);
                string desertBiomeGuid = AssetDatabase.AssetPathToGUID(desertBiomePath);
                string oceanBiomeGuid = AssetDatabase.AssetPathToGUID(oceanBiomePath);
                string jungleBiomeGuid = AssetDatabase.AssetPathToGUID(jungleBiomePath);
                string rules = CreateBiomeLayoutRulesJson(
                    new[]
                    {
                        CreateBiomeLayoutEntry(forestBiomeGuid, 1.0f),
                        CreateBiomeLayoutEntry(desertBiomeGuid, 1.0f)
                    },
                    new[]
                    {
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.StartEdge, oceanBiomeGuid, 4),
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.ProtectedCenter, forestBiomeGuid, 4),
                        CreateBiomeLayoutConstraint(BiomeLayoutConstraintType.Required, jungleBiomeGuid, 4)
                    });

                BiomeLayoutNode firstNode = new BiomeLayoutNode(
                    "layout-cells",
                    "Layout Cells",
                    layoutMode: BiomeLayoutMode.Cells,
                    axis: GradientDirection.X,
                    cellSize: 4,
                    blendWidth: 0,
                    rules: rules);
                BiomeLayoutNode secondNode = new BiomeLayoutNode(
                    "layout-cells",
                    "Layout Cells",
                    layoutMode: BiomeLayoutMode.Cells,
                    axis: GradientDirection.X,
                    cellSize: 4,
                    blendWidth: 0,
                    rules: rules);

                BiomeChannelPalette palette = CreatePaletteWithReservedZeroIndex(forestBiomeGuid);
                ResolveBiomePalette(firstNode, palette);
                ResolveBiomePalette(secondNode, palette);

                int forestBiomeIndex = GetResolvedBiomeIndex(palette, forestBiomeGuid);
                int oceanBiomeIndex = GetResolvedBiomeIndex(palette, oceanBiomeGuid);
                int jungleBiomeIndex = GetResolvedBiomeIndex(palette, jungleBiomeGuid);
                WorldSnapshot firstSnapshot = await ExecuteNodesAsync(new IGenNode[] { firstNode }, 12, 8, 7804L, palette.Biomes, null);
                WorldSnapshot secondSnapshot = await ExecuteNodesAsync(new IGenNode[] { secondNode }, 12, 8, 7804L, palette.Biomes, null);
                WorldSnapshot.IntChannelSnapshot firstBiomeChannel = GetIntChannel(firstSnapshot, BiomeChannelUtility.ChannelName);
                WorldSnapshot.IntChannelSnapshot secondBiomeChannel = GetIntChannel(secondSnapshot, BiomeChannelUtility.ChannelName);

                Assert.That(firstBiomeChannel, Is.Not.Null);
                Assert.That(secondBiomeChannel, Is.Not.Null);
                CollectionAssert.AreEqual(firstBiomeChannel.Data, secondBiomeChannel.Data);

                int y;
                for (y = 0; y < 8; y++)
                {
                    int x;
                    for (x = 0; x < 4; x++)
                    {
                        Assert.That(firstBiomeChannel.Data[x + (y * 12)], Is.EqualTo(oceanBiomeIndex));
                    }

                    for (x = 4; x < 8; x++)
                    {
                        if (y >= 2 && y < 6)
                        {
                            Assert.That(firstBiomeChannel.Data[x + (y * 12)], Is.EqualTo(forestBiomeIndex));
                        }
                    }
                }

                CollectionAssert.Contains(firstBiomeChannel.Data, jungleBiomeIndex);
            }
            finally
            {
                DeleteAssetIfExists(forestBiomePath);
                DeleteAssetIfExists(desertBiomePath);
                DeleteAssetIfExists(oceanBiomePath);
                DeleteAssetIfExists(jungleBiomePath);
            }
        }

        [Test]
        public async Task LogicalIdRuleOverlayAppliesRulesInOrderAndUsesMaskSlots()
        {
            ConstantNode baseNode = new ConstantNode("logical-base", "Logical Base", "BaseIds", ChannelType.Int, intValue: 2);
            BiomeTestMaskNode maskNode = new BiomeTestMaskNode("logical-mask", "Logical Mask", MaskChannelName);
            LogicalIdRuleOverlayNode overlayNode = new LogicalIdRuleOverlayNode(
                "logical-rules",
                "Logical Rules",
                inputBaseChannelName: "BaseIds",
                inputMask1ChannelName: MaskChannelName,
                outputChannelName: LogicalChannelName,
                rules: CreateLogicalRuleSetJson(
                    new LogicalIdRule { MaskSlot = 0, SourceLogicalId = 2, TargetLogicalId = 10 },
                    new LogicalIdRule { MaskSlot = 1, SourceLogicalId = 10, TargetLogicalId = 20 }));

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { baseNode, maskNode, overlayNode }, 5, 5, 7805L, null, null);
            WorldSnapshot.IntChannelSnapshot logicalChannel = GetIntChannel(snapshot, LogicalChannelName);

            Assert.That(logicalChannel, Is.Not.Null);
            int y;
            for (y = 0; y < 5; y++)
            {
                int x;
                for (x = 0; x < 5; x++)
                {
                    int expectedValue = IsBiomeTestMaskTile(x, y, 5, 5) ? 20 : 10;
                    Assert.That(logicalChannel.Data[x + (y * 5)], Is.EqualTo(expectedValue));
                }
            }
        }

        [Test]
        public async Task ColumnSurfaceTransitionMaskIsDeterministicAndKeepsShallowTilesUnchanged()
        {
            const string transitionChannelName = "SubsurfaceTransitionMask";
            BiomeTestMaskNode firstSolidNode = new BiomeTestMaskNode("solid-mask-a", "Solid Mask", MaskChannelName);
            BiomeTestMaskNode secondSolidNode = new BiomeTestMaskNode("solid-mask-b", "Solid Mask", MaskChannelName);
            ColumnSurfaceTransitionMaskNode firstTransitionNode = new ColumnSurfaceTransitionMaskNode(
                "transition-mask",
                "Transition Mask",
                inputChannelName: MaskChannelName,
                outputChannelName: transitionChannelName,
                startDepth: 2,
                endDepth: 6,
                noiseFrequency: 0.12f);
            ColumnSurfaceTransitionMaskNode secondTransitionNode = new ColumnSurfaceTransitionMaskNode(
                "transition-mask",
                "Transition Mask",
                inputChannelName: MaskChannelName,
                outputChannelName: transitionChannelName,
                startDepth: 2,
                endDepth: 6,
                noiseFrequency: 0.12f);

            WorldSnapshot firstSnapshot = await ExecuteNodesAsync(new IGenNode[] { firstSolidNode, firstTransitionNode }, 10, 10, 7807L, null, null);
            WorldSnapshot secondSnapshot = await ExecuteNodesAsync(new IGenNode[] { secondSolidNode, secondTransitionNode }, 10, 10, 7807L, null, null);
            WorldSnapshot.BoolMaskChannelSnapshot firstMask = GetBoolMaskChannel(firstSnapshot, transitionChannelName);
            WorldSnapshot.BoolMaskChannelSnapshot secondMask = GetBoolMaskChannel(secondSnapshot, transitionChannelName);

            Assert.That(firstMask, Is.Not.Null);
            Assert.That(secondMask, Is.Not.Null);
            CollectionAssert.AreEqual(firstMask.Data, secondMask.Data);

            int x;
            for (x = 1; x < 9; x++)
            {
                Assert.That(firstMask.Data[x + (8 * 10)], Is.EqualTo(0), "Surface-adjacent depth should stay untransitioned.");
                Assert.That(firstMask.Data[x + (7 * 10)], Is.EqualTo(0), "Depth before the start depth should stay untransitioned.");
                Assert.That(firstMask.Data[x + (2 * 10)], Is.EqualTo(1), "Depth at the end depth should be fully transitioned.");
                Assert.That(firstMask.Data[x + (1 * 10)], Is.EqualTo(1), "Depth below the end depth should stay fully transitioned.");
            }
        }

        [Test]
        public async Task LogicalIdRuleOverlayLeavesUnmatchedTilesUnchanged()
        {
            ConstantNode baseNode = new ConstantNode("logical-fallback-base", "Logical Fallback Base", "BaseIds", ChannelType.Int, intValue: 7);
            LogicalIdRuleOverlayNode overlayNode = new LogicalIdRuleOverlayNode(
                "logical-fallback-rules",
                "Logical Fallback Rules",
                inputBaseChannelName: "BaseIds",
                outputChannelName: LogicalChannelName,
                rules: CreateLogicalRuleSetJson(new LogicalIdRule { MaskSlot = 0, SourceLogicalId = 99, TargetLogicalId = 20 }));

            WorldSnapshot snapshot = await ExecuteNodesAsync(new IGenNode[] { baseNode, overlayNode }, 3, 1, 7806L, null, null);
            WorldSnapshot.IntChannelSnapshot logicalChannel = GetIntChannel(snapshot, LogicalChannelName);

            Assert.That(logicalChannel, Is.Not.Null);
            CollectionAssert.AreEqual(new[] { 7, 7, 7 }, logicalChannel.Data);
        }

        [Test]
        public async Task TerrariaDemoGraphCompilesAndExecutesWithRefactoredBiomeLayout()
        {
            const string graphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph.asset";
            const string materialContextLogicalChannelName = "MaterialContextLogicalIds";
            GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(graphPath);
            GraphCompileResult compileResult = null;

            try
            {
                Assert.That(graph, Is.Not.Null);

                compileResult = GraphCompiler.Compile(graph);
                Assert.That(compileResult.IsSuccess, Is.True, BuildDiagnosticSummary(compileResult.Diagnostics));
                Assert.That(compileResult.HasConnectedOutput, Is.True);
                Assert.That(compileResult.OutputChannelName, Is.EqualTo(materialContextLogicalChannelName));
                Assert.That(compileResult.Plan, Is.Not.Null);

                Executor executor = new Executor();
                ExecutionResult executionResult = await executor.ExecuteAsync(compileResult.Plan, CancellationToken.None);

                Assert.That(executionResult.IsSuccess, Is.True, executionResult.ErrorMessage);
                Assert.That(executionResult.Snapshot, Is.Not.Null);

                WorldSnapshot.IntChannelSnapshot logicalChannel = GetIntChannel(executionResult.Snapshot, materialContextLogicalChannelName);
                WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(executionResult.Snapshot, BiomeChannelUtility.ChannelName);

                Assert.That(logicalChannel, Is.Not.Null);
                Assert.That(biomeChannel, Is.Not.Null);
                Assert.That(ContainsAny(logicalChannel.Data, 10, 11, 12, 13, 14, 15, 16, 17), Is.True);
                Assert.That(ContainsAny(logicalChannel.Data, 18), Is.True);
                Assert.That(ContainsBiomeTiles(executionResult.Snapshot, "MossCave"), Is.True);
                Assert.That(ContainsBiomeTiles(executionResult.Snapshot, "CrystalCave"), Is.True);
            }
            finally
            {
                if (compileResult != null && compileResult.Plan != null)
                {
                    compileResult.Plan.Dispose();
                }
            }
        }


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
                GraphOutputUtility.EnsureSingleOutputNode(graph);

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

        [Test]
        public void BackgroundFillCanUseSeparateBiomeChannel()
        {
            const string visualBiomeChannelName = "VisualBiomeChannel";
            const ushort backgroundLogicalId = 17;

            Grid grid = null;
            BiomeAsset fallbackBiome = null;
            BiomeAsset visualBiome = null;
            BiomeAsset oreBiome = null;
            TilemapLayerDefinition visualLayer = null;
            Tile visualTile = null;
            Tile oreTile = null;

            try
            {
                grid = new GameObject("BackgroundBiomeOutputGrid").AddComponent<Grid>();
                fallbackBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                visualBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                oreBiome = ScriptableObject.CreateInstance<BiomeAsset>();
                visualLayer = CreateLayerDefinition("Visual", false);
                visualTile = CreateTile(Color.gray);
                oreTile = CreateTile(Color.yellow);

                AddBiomeMapping(visualBiome, backgroundLogicalId, visualTile);
                AddBiomeMapping(oreBiome, backgroundLogicalId, oreTile);

                WorldSnapshot snapshot = new WorldSnapshot();
                snapshot.Width = 1;
                snapshot.Height = 2;
                snapshot.IntChannels = new[]
                {
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = LogicalChannelName,
                        Data = new[] { LogicalTileId.Void, (int)LogicalTileId.Floor }
                    },
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = BiomeChannelUtility.ChannelName,
                        Data = new[] { 1, 1 }
                    },
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = visualBiomeChannelName,
                        Data = new[] { 0, 0 }
                    }
                };
                snapshot.BiomeChannelBiomes = new[] { visualBiome, oreBiome };

                TilemapLayerWriter writer = new TilemapLayerWriter();
                TilemapOutputPass outputPass = new TilemapOutputPass();
                writer.EnsureTimelapsCreated(grid, new[] { visualLayer });

                outputPass.ExecuteBackgroundFill(snapshot, LogicalChannelName, fallbackBiome, writer, visualLayer, Vector3Int.zero, backgroundLogicalId, visualBiomeChannelName);

                Tilemap visualTilemap = GetLayerTilemap(grid, "Visual");
                TileBase resolvedTile = visualTilemap.GetTile(new Vector3Int(0, 0, 0));
                Assert.That(resolvedTile, Is.SameAs(visualTile));
                Assert.That(resolvedTile, Is.Not.SameAs(oreTile));
            }
            finally
            {
                DestroyImmediateIfNotNull(visualTile);
                DestroyImmediateIfNotNull(oreTile);
                DestroyImmediateIfNotNull(visualLayer);
                DestroyImmediateIfNotNull(visualBiome);
                DestroyImmediateIfNotNull(oreBiome);
                DestroyImmediateIfNotNull(fallbackBiome);
                DestroyImmediateIfNotNull(grid != null ? grid.gameObject : null);
            }
        }

        private static string CreateBiomeLayoutRulesJson(BiomeLayoutEntry[] entries, BiomeLayoutConstraint[] constraints = null)
        {
            BiomeLayoutRules rules = new BiomeLayoutRules
            {
                Entries = entries ?? Array.Empty<BiomeLayoutEntry>(),
                Constraints = constraints ?? Array.Empty<BiomeLayoutConstraint>()
            };

            return JsonUtility.ToJson(rules);
        }

        private static BiomeLayoutEntry CreateBiomeLayoutEntry(string biomeGuid, float weight, int minSize = 0, int maxSize = 0)
        {
            return new BiomeLayoutEntry
            {
                Biome = biomeGuid,
                Weight = weight,
                MinSize = minSize,
                MaxSize = maxSize,
                Enabled = true
            };
        }

        private static BiomeLayoutConstraint CreateBiomeLayoutConstraint(BiomeLayoutConstraintType type, string biomeGuid, int size = 0)
        {
            return new BiomeLayoutConstraint
            {
                Type = type,
                Biome = biomeGuid,
                Size = size,
                Enabled = true
            };
        }

        private static string CreateLogicalRuleSetJson(params LogicalIdRule[] rules)
        {
            return JsonUtility.ToJson(new LogicalIdRuleSet { Rules = rules ?? Array.Empty<LogicalIdRule>() });
        }

        private static void AssertStripRunBounds(int[] biomeData, int width, int row, int minRunLength, int maxRunLength)
        {
            int runStart = 0;
            int previousValue = biomeData[row * width];
            int x;
            for (x = 1; x <= width; x++)
            {
                int value = x < width ? biomeData[x + (row * width)] : int.MinValue;
                if (x < width && value == previousValue)
                {
                    continue;
                }

                int runLength = x - runStart;
                Assert.That(runLength, Is.GreaterThanOrEqualTo(minRunLength));
                Assert.That(runLength, Is.LessThanOrEqualTo(maxRunLength));
                runStart = x;
                previousValue = value;
            }
        }

        private static int CountColumnValue(int[] biomeData, int width, int height, int column, int value)
        {
            int count = 0;
            int y;
            for (y = 0; y < height; y++)
            {
                if (biomeData[column + (y * width)] == value)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool ContainsAny(int[] values, params int[] candidates)
        {
            if (values == null || candidates == null)
            {
                return false;
            }

            int valueIndex;
            for (valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                int candidateIndex;
                for (candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
                {
                    if (values[valueIndex] == candidates[candidateIndex])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildDiagnosticSummary(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return string.Empty;
            }

            List<string> lines = new List<string>();
            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = diagnostics[diagnosticIndex];
                lines.Add(diagnostic.Severity + ": " + diagnostic.Message + " (" + diagnostic.NodeId + ":" + diagnostic.PortName + ")");
            }

            return string.Join(Environment.NewLine, lines);
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

        private static BiomeChannelPalette CreatePaletteWithReservedZeroIndex(string reservedBiomeGuid)
        {
            BiomeChannelPalette palette = new BiomeChannelPalette();
            int reservedBiomeIndex;
            string errorMessage;

            Assert.That(palette.TryResolveIndex(reservedBiomeGuid, out reservedBiomeIndex, out errorMessage), Is.True, errorMessage);
            Assert.That(reservedBiomeIndex, Is.EqualTo(0));
            return palette;
        }

        private static void ResolveBiomePalette(IBiomeChannelNode node, BiomeChannelPalette palette)
        {
            string errorMessage;
            Assert.That(node.ResolveBiomePalette(palette, out errorMessage), Is.True, errorMessage);
        }

        private static int GetResolvedBiomeIndex(BiomeChannelPalette palette, string biomeGuid)
        {
            int biomeIndex;
            string errorMessage;

            Assert.That(palette.TryResolveIndex(biomeGuid, out biomeIndex, out errorMessage), Is.True, errorMessage);
            return biomeIndex;
        }

        private static WorldSnapshot CreateInitialBiomeSnapshot(int width, int height, int initialBiomeIndex)
        {
            int[] biomeData = new int[width * height];
            if (initialBiomeIndex != 0)
            {
                int index;
                for (index = 0; index < biomeData.Length; index++)
                {
                    biomeData[index] = initialBiomeIndex;
                }
            }

            return new WorldSnapshot
            {
                Width = width,
                Height = height,
                IntChannels = new[]
                {
                    new WorldSnapshot.IntChannelSnapshot
                    {
                        Name = BiomeChannelUtility.ChannelName,
                        Data = biomeData
                    }
                }
            };
        }

        private static async Task<WorldSnapshot> ExecuteNodesAsync(
            IReadOnlyList<IGenNode> nodes,
            int width,
            int height,
            long seed,
            IReadOnlyList<BiomeAsset> biomeChannelBiomes,
            WorldSnapshot initialSnapshot)
        {
            ExecutionPlan plan = ExecutionPlan.Build(nodes, width, height, seed);

            if (biomeChannelBiomes != null)
            {
                plan.SetBiomeChannelBiomes(biomeChannelBiomes);
            }

            if (initialSnapshot != null)
            {
                plan.RestoreWorldSnapshot(initialSnapshot);
            }

            Executor executor = new Executor();
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Snapshot, Is.Not.Null);
            return result.Snapshot;
        }

        private static void AssertRowValues(int[] biomeData, int width, int row, int expectedValue)
        {
            int x;
            for (x = 0; x < width; x++)
            {
                int index = x + (row * width);
                Assert.That(biomeData[index], Is.EqualTo(expectedValue));
            }
        }

        private static void AssertMaskedOverrideValues(int[] biomeData, int width, int height, int maskedValue, int unmaskedValue)
        {
            int y;
            for (y = 0; y < height; y++)
            {
                int x;
                for (x = 0; x < width; x++)
                {
                    int index = x + (y * width);
                    int expectedValue = IsBiomeTestMaskTile(x, y, width, height) ? maskedValue : unmaskedValue;
                    Assert.That(biomeData[index], Is.EqualTo(expectedValue));
                }
            }
        }

        private static bool IsBiomeTestMaskTile(int x, int y, int width, int height)
        {
            return x >= 1 && x <= width - 2 && y >= 1 && y <= height - 2;
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

        private static bool ContainsBiomeTiles(WorldSnapshot snapshot, string biomeName)
        {
            if (snapshot == null || snapshot.BiomeChannelBiomes == null || string.IsNullOrWhiteSpace(biomeName))
            {
                return false;
            }

            int biomeIndex = -1;
            int index;
            for (index = 0; index < snapshot.BiomeChannelBiomes.Length; index++)
            {
                BiomeAsset biome = snapshot.BiomeChannelBiomes[index];
                if (biome != null && string.Equals(biome.name, biomeName, StringComparison.Ordinal))
                {
                    biomeIndex = index;
                    break;
                }
            }

            if (biomeIndex < 0)
            {
                return false;
            }

            WorldSnapshot.IntChannelSnapshot biomeChannel = GetIntChannel(snapshot, BiomeChannelUtility.ChannelName);
            if (biomeChannel == null || biomeChannel.Data == null)
            {
                return false;
            }

            for (index = 0; index < biomeChannel.Data.Length; index++)
            {
                if (biomeChannel.Data[index] == biomeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static WorldSnapshot.BoolMaskChannelSnapshot GetBoolMaskChannel(WorldSnapshot snapshot, string channelName)
        {
            int channelIndex;
            for (channelIndex = 0; channelIndex < snapshot.BoolMaskChannels.Length; channelIndex++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channel = snapshot.BoolMaskChannels[channelIndex];
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

    internal sealed class BiomeFloatSourceNode : IGenNode
    {
        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
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
            get
            {
                return _nodeId;
            }
        }

        public string NodeName
        {
            get
            {
                return _nodeName;
            }
        }

        public BiomeFloatSourceNode(string nodeId, string outputChannelName, float[] values)
        {
            _nodeId = nodeId;
            _nodeName = nodeId;
            _outputChannelName = outputChannelName;
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
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            int index;
            for (index = 0; index < output.Length; index++)
            {
                output[index] = _values[index];
            }

            return context.InputDependency;
        }
    }
}
