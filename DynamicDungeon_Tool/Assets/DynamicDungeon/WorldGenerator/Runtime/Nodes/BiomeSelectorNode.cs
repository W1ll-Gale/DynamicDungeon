using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Biome")]
    [NodeDisplayName("Biome Selector")]
    [Description("Assigns biomes from float or int input channels using range, matrix, or cell lookup modes.")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/biome/biome-selector")]
    public sealed class BiomeSelectorNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider, IBiomeChannelNode
    {
        [Serializable]
        private sealed class RangeEntryRecord
        {
            public string Biome = string.Empty;
            public float RangeMin;
            public float RangeMax;
        }

        [Serializable]
        private sealed class RangeEntryCollection
        {
            public RangeEntryRecord[] Entries = Array.Empty<RangeEntryRecord>();
        }

        [Serializable]
        private sealed class CellEntryRecord
        {
            public int CellId;
            public string Biome = string.Empty;
        }

        [Serializable]
        private sealed class CellEntryCollection
        {
            public CellEntryRecord[] Entries = Array.Empty<CellEntryRecord>();
        }

        [Serializable]
        private sealed class BiomeGuidCollection
        {
            public string[] Entries = Array.Empty<string>();
        }

        private struct ResolvedRangeEntry
        {
            public float RangeMin;
            public float RangeMax;
            public int BiomeIndex;
        }

        private struct ResolvedCellEntry
        {
            public int CellId;
            public int BiomeIndex;
        }

        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Biome Selector";
        private const string InputPortName = "Input";
        private const string InputAPortName = "InputA";
        private const string InputBPortName = "InputB";
        private const string CellPortName = "Cells";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;

        private string _inputChannelName;
        private string _inputAChannelName;
        private string _inputBChannelName;
        private string _inputCellChannelName;

        [DescriptionAttribute("Controls which biome assignment strategy this node uses.")]
        private BiomeSelectorMode _mode;

        [DescriptionAttribute("Range mode entries encoded as JSON: {\"Entries\":[{\"Biome\":\"<guid>\",\"RangeMin\":0.0,\"RangeMax\":0.5}]}")]
        private string _rangeEntries;

        [MinValue(1.0f)]
        [DescriptionAttribute("Matrix mode column count.")]
        private int _matrixColumnCount;

        [MinValue(1.0f)]
        [DescriptionAttribute("Matrix mode row count.")]
        private int _matrixRowCount;

        [DescriptionAttribute("Matrix mode biome GUIDs encoded as JSON: {\"Entries\":[\"<guid>\",\"<guid>\"]}")]
        private string _matrixEntries;

        [DescriptionAttribute("Cell mode entries encoded as JSON: {\"Entries\":[{\"CellId\":0,\"Biome\":\"<guid>\"}]}")]
        private string _cellEntries;

        private RangeEntryRecord[] _parsedRangeEntries;
        private string[] _parsedMatrixBiomeGuids;
        private CellEntryRecord[] _parsedCellEntries;
        private ResolvedRangeEntry[] _resolvedRangeEntries;
        private int[] _resolvedMatrixBiomeIndices;
        private ResolvedCellEntry[] _resolvedCellEntries;
        private ChannelDeclaration[] _channelDeclarations;

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

        public BiomeSelectorNode(
            string nodeId,
            string nodeName,
            string inputChannelName = "",
            string inputAChannelName = "",
            string inputBChannelName = "",
            string inputCellChannelName = "",
            BiomeSelectorMode mode = BiomeSelectorMode.Range,
            string rangeEntries = "",
            int matrixColumnCount = 1,
            int matrixRowCount = 1,
            string matrixEntries = "",
            string cellEntries = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _inputCellChannelName = inputCellChannelName ?? string.Empty;
            _mode = mode;
            _rangeEntries = rangeEntries ?? string.Empty;
            _matrixColumnCount = math.max(1, matrixColumnCount);
            _matrixRowCount = math.max(1, matrixRowCount);
            _matrixEntries = matrixEntries ?? string.Empty;
            _cellEntries = cellEntries ?? string.Empty;
            _parsedRangeEntries = ParseRangeEntries(_rangeEntries);
            _parsedMatrixBiomeGuids = ParseBiomeGuidEntries(_matrixEntries);
            _parsedCellEntries = ParseCellEntries(_cellEntries);
            _resolvedRangeEntries = Array.Empty<ResolvedRangeEntry>();
            _resolvedMatrixBiomeIndices = Array.Empty<int>();
            _resolvedCellEntries = Array.Empty<ResolvedCellEntry>();
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(CellPortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, false),
                new NodePortDefinition(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int, displayName: "Biomes")
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputChannelName = ResolveInputChannel(inputConnections, InputPortName);
            _inputAChannelName = ResolveInputChannel(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputChannel(inputConnections, InputBPortName);
            _inputCellChannelName = ResolveInputChannel(inputConnections, CellPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "mode", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _mode = (BiomeSelectorMode)Enum.Parse(typeof(BiomeSelectorMode), value ?? string.Empty, true);
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "rangeEntries", StringComparison.OrdinalIgnoreCase))
            {
                _rangeEntries = value ?? string.Empty;
                _parsedRangeEntries = ParseRangeEntries(_rangeEntries);
                return;
            }

            if (string.Equals(name, "matrixColumnCount", StringComparison.OrdinalIgnoreCase))
            {
                _matrixColumnCount = math.max(1, ParseInt(value, _matrixColumnCount));
                return;
            }

            if (string.Equals(name, "matrixRowCount", StringComparison.OrdinalIgnoreCase))
            {
                _matrixRowCount = math.max(1, ParseInt(value, _matrixRowCount));
                return;
            }

            if (string.Equals(name, "matrixEntries", StringComparison.OrdinalIgnoreCase))
            {
                _matrixEntries = value ?? string.Empty;
                _parsedMatrixBiomeGuids = ParseBiomeGuidEntries(_matrixEntries);
                return;
            }

            if (string.Equals(name, "cellEntries", StringComparison.OrdinalIgnoreCase))
            {
                _cellEntries = value ?? string.Empty;
                _parsedCellEntries = ParseCellEntries(_cellEntries);
            }
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return true;
            }

            if (string.Equals(parameterName, "rangeEntries", StringComparison.OrdinalIgnoreCase))
            {
                return _mode == BiomeSelectorMode.Range;
            }

            if (string.Equals(parameterName, "matrixColumnCount", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameterName, "matrixRowCount", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameterName, "matrixEntries", StringComparison.OrdinalIgnoreCase))
            {
                return _mode == BiomeSelectorMode.Matrix;
            }

            if (string.Equals(parameterName, "cellEntries", StringComparison.OrdinalIgnoreCase))
            {
                return _mode == BiomeSelectorMode.Cell;
            }

            return true;
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (_mode == BiomeSelectorMode.Range)
            {
                return ResolveRangeBiomePalette(palette, out errorMessage);
            }

            if (_mode == BiomeSelectorMode.Matrix)
            {
                return ResolveMatrixBiomePalette(palette, out errorMessage);
            }

            return ResolveCellBiomePalette(palette, out errorMessage);
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> biomeChannel = context.GetIntChannel(BiomeChannelUtility.ChannelName);

            if (_mode == BiomeSelectorMode.Range)
            {
                if (string.IsNullOrWhiteSpace(_inputChannelName))
                {
                    throw new InvalidOperationException("Biome Selector node in Range mode requires a connected float input.");
                }

                NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
                NativeArray<float2> ranges = new NativeArray<float2>(_resolvedRangeEntries.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                NativeArray<int> lookup = new NativeArray<int>(_resolvedRangeEntries.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                PopulateRangeBuffers(ranges, lookup);

                BiomeSelectorRangeJob rangeJob = new BiomeSelectorRangeJob
                {
                    Input = input,
                    BiomeChannel = biomeChannel,
                    Ranges = ranges,
                    Lookup = lookup
                };

                JobHandle jobHandle = rangeJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
                JobHandle disposeRangesHandle = ranges.Dispose(jobHandle);
                return lookup.Dispose(disposeRangesHandle);
            }

            if (_mode == BiomeSelectorMode.Matrix)
            {
                if (string.IsNullOrWhiteSpace(_inputAChannelName) || string.IsNullOrWhiteSpace(_inputBChannelName))
                {
                    throw new InvalidOperationException("Biome Selector node in Matrix mode requires both float inputs.");
                }

                NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
                NativeArray<float> inputB = context.GetFloatChannel(_inputBChannelName);
                NativeArray<int> lookup = new NativeArray<int>(_resolvedMatrixBiomeIndices.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                PopulateMatrixLookup(lookup);

                BiomeSelectorMatrixJob matrixJob = new BiomeSelectorMatrixJob
                {
                    InputA = inputA,
                    InputB = inputB,
                    BiomeChannel = biomeChannel,
                    Lookup = lookup,
                    Columns = _matrixColumnCount,
                    Rows = _matrixRowCount
                };

                JobHandle jobHandle = matrixJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
                return lookup.Dispose(jobHandle);
            }

            if (string.IsNullOrWhiteSpace(_inputCellChannelName))
            {
                throw new InvalidOperationException("Biome Selector node in Cell mode requires a connected int input.");
            }

            NativeArray<int> cellIds = context.GetIntChannel(_inputCellChannelName);
            NativeArray<int2> lookupPairs = new NativeArray<int2>(_resolvedCellEntries.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            PopulateCellLookup(lookupPairs);

            BiomeSelectorCellJob cellJob = new BiomeSelectorCellJob
            {
                CellIds = cellIds,
                BiomeChannel = biomeChannel,
                LookupPairs = lookupPairs
            };

            JobHandle cellHandle = cellJob.Schedule(biomeChannel.Length, DefaultBatchSize, context.InputDependency);
            return lookupPairs.Dispose(cellHandle);
        }

        private bool ResolveRangeBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            _resolvedRangeEntries = new ResolvedRangeEntry[_parsedRangeEntries.Length];

            int entryIndex;
            for (entryIndex = 0; entryIndex < _parsedRangeEntries.Length; entryIndex++)
            {
                RangeEntryRecord entry = _parsedRangeEntries[entryIndex];
                int biomeIndex;
                string paletteError;
                if (!palette.TryResolveIndex(entry != null ? entry.Biome : string.Empty, out biomeIndex, out paletteError))
                {
                    errorMessage = "Biome Selector node '" + _nodeName + "' has an invalid Range entry at index " + entryIndex.ToString(CultureInfo.InvariantCulture) + ": " + paletteError;
                    return false;
                }

                float rangeMin = entry != null ? entry.RangeMin : 0.0f;
                float rangeMax = entry != null ? entry.RangeMax : 0.0f;
                _resolvedRangeEntries[entryIndex] = new ResolvedRangeEntry
                {
                    RangeMin = math.min(rangeMin, rangeMax),
                    RangeMax = math.max(rangeMin, rangeMax),
                    BiomeIndex = biomeIndex
                };
            }

            errorMessage = null;
            return true;
        }

        private bool ResolveMatrixBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            int requiredCount = math.max(1, _matrixColumnCount) * math.max(1, _matrixRowCount);
            _resolvedMatrixBiomeIndices = new int[requiredCount];

            int entryIndex;
            for (entryIndex = 0; entryIndex < requiredCount; entryIndex++)
            {
                _resolvedMatrixBiomeIndices[entryIndex] = BiomeChannelUtility.UnassignedBiomeIndex;
            }

            int resolvedCount = math.min(requiredCount, _parsedMatrixBiomeGuids.Length);
            for (entryIndex = 0; entryIndex < resolvedCount; entryIndex++)
            {
                int biomeIndex;
                string paletteError;
                if (!palette.TryResolveIndex(_parsedMatrixBiomeGuids[entryIndex], out biomeIndex, out paletteError))
                {
                    errorMessage = "Biome Selector node '" + _nodeName + "' has an invalid Matrix entry at index " + entryIndex.ToString(CultureInfo.InvariantCulture) + ": " + paletteError;
                    return false;
                }

                _resolvedMatrixBiomeIndices[entryIndex] = biomeIndex;
            }

            errorMessage = null;
            return true;
        }

        private bool ResolveCellBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            _resolvedCellEntries = new ResolvedCellEntry[_parsedCellEntries.Length];

            int entryIndex;
            for (entryIndex = 0; entryIndex < _parsedCellEntries.Length; entryIndex++)
            {
                CellEntryRecord entry = _parsedCellEntries[entryIndex];
                int biomeIndex;
                string paletteError;
                if (!palette.TryResolveIndex(entry != null ? entry.Biome : string.Empty, out biomeIndex, out paletteError))
                {
                    errorMessage = "Biome Selector node '" + _nodeName + "' has an invalid Cell entry at index " + entryIndex.ToString(CultureInfo.InvariantCulture) + ": " + paletteError;
                    return false;
                }

                _resolvedCellEntries[entryIndex] = new ResolvedCellEntry
                {
                    CellId = entry != null ? entry.CellId : 0,
                    BiomeIndex = biomeIndex
                };
            }

            errorMessage = null;
            return true;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(5);

            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputAChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputCellChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputCellChannelName, ChannelType.Int, false));
            }

            declarations.Add(new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private void PopulateRangeBuffers(NativeArray<float2> ranges, NativeArray<int> lookup)
        {
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedRangeEntries.Length; entryIndex++)
            {
                ResolvedRangeEntry entry = _resolvedRangeEntries[entryIndex];
                ranges[entryIndex] = new float2(entry.RangeMin, entry.RangeMax);
                lookup[entryIndex] = entry.BiomeIndex;
            }
        }

        private void PopulateMatrixLookup(NativeArray<int> lookup)
        {
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedMatrixBiomeIndices.Length; entryIndex++)
            {
                lookup[entryIndex] = _resolvedMatrixBiomeIndices[entryIndex];
            }
        }

        private void PopulateCellLookup(NativeArray<int2> lookupPairs)
        {
            int entryIndex;
            for (entryIndex = 0; entryIndex < _resolvedCellEntries.Length; entryIndex++)
            {
                ResolvedCellEntry entry = _resolvedCellEntries[entryIndex];
                lookupPairs[entryIndex] = new int2(entry.CellId, entry.BiomeIndex);
            }
        }

        private static RangeEntryRecord[] ParseRangeEntries(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Array.Empty<RangeEntryRecord>();
            }

            try
            {
                RangeEntryCollection collection = UnityEngine.JsonUtility.FromJson<RangeEntryCollection>(rawJson);
                if (collection == null || collection.Entries == null)
                {
                    return Array.Empty<RangeEntryRecord>();
                }

                return collection.Entries;
            }
            catch
            {
                return Array.Empty<RangeEntryRecord>();
            }
        }

        private static CellEntryRecord[] ParseCellEntries(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Array.Empty<CellEntryRecord>();
            }

            try
            {
                CellEntryCollection collection = UnityEngine.JsonUtility.FromJson<CellEntryCollection>(rawJson);
                if (collection == null || collection.Entries == null)
                {
                    return Array.Empty<CellEntryRecord>();
                }

                return collection.Entries;
            }
            catch
            {
                return Array.Empty<CellEntryRecord>();
            }
        }

        private static string[] ParseBiomeGuidEntries(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                BiomeGuidCollection collection = UnityEngine.JsonUtility.FromJson<BiomeGuidCollection>(rawJson);
                if (collection == null || collection.Entries == null)
                {
                    return Array.Empty<string>();
                }

                return collection.Entries;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string ResolveInputChannel(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        private static int ParseInt(string value, int fallbackValue)
        {
            int parsedValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                return parsedValue;
            }

            return fallbackValue;
        }

        [BurstCompile]
        private struct BiomeSelectorRangeJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<float> Input;

            [Unity.Collections.ReadOnly]
            public NativeArray<float2> Ranges;

            [Unity.Collections.ReadOnly]
            public NativeArray<int> Lookup;

            public NativeArray<int> BiomeChannel;

            public void Execute(int index)
            {
                float value = Input[index];

                int entryIndex;
                for (entryIndex = 0; entryIndex < Ranges.Length; entryIndex++)
                {
                    float2 range = Ranges[entryIndex];
                    if (value >= range.x && value <= range.y)
                    {
                        int biomeIndex = Lookup[entryIndex];
                        if (biomeIndex >= 0)
                        {
                            BiomeChannel[index] = biomeIndex;
                        }

                        return;
                    }
                }
            }
        }

        [BurstCompile]
        private struct BiomeSelectorMatrixJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<float> InputA;

            [Unity.Collections.ReadOnly]
            public NativeArray<float> InputB;

            [Unity.Collections.ReadOnly]
            public NativeArray<int> Lookup;

            public NativeArray<int> BiomeChannel;
            public int Columns;
            public int Rows;

            public void Execute(int index)
            {
                float valueA = math.saturate(InputA[index]);
                float valueB = math.saturate(InputB[index]);
                int column = math.clamp((int)math.floor(valueA * Columns), 0, Columns - 1);
                int row = math.clamp((int)math.floor(valueB * Rows), 0, Rows - 1);
                int lookupIndex = row * Columns + column;
                int biomeIndex = Lookup[lookupIndex];
                if (biomeIndex >= 0)
                {
                    BiomeChannel[index] = biomeIndex;
                }
            }
        }

        [BurstCompile]
        private struct BiomeSelectorCellJob : IJobParallelFor
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<int> CellIds;

            [Unity.Collections.ReadOnly]
            public NativeArray<int2> LookupPairs;

            public NativeArray<int> BiomeChannel;

            public void Execute(int index)
            {
                int cellId = CellIds[index];

                int entryIndex;
                for (entryIndex = 0; entryIndex < LookupPairs.Length; entryIndex++)
                {
                    int2 pair = LookupPairs[entryIndex];
                    if (pair.x == cellId)
                    {
                        if (pair.y >= 0)
                        {
                            BiomeChannel[index] = pair.y;
                        }

                        return;
                    }
                }
            }
        }
    }
}
