using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Noise")]
    [NodeDisplayName("Voronoi Noise")]
    [Description("Voronoi/Worley noise. Outputs a normalised distance float channel and a stable per-cell integer ID channel.")]
    public sealed class VoronoiNoiseNode : IGenNode, IParameterReceiver
    {
        private const string DefaultNodeName = "Voronoi Noise";
        private const int DefaultBatchSize = 64;
        private const string DistanceDisplayName = "Distance";
        private const string CellIdDisplayName = "CellId";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private static string MakeDistanceChannelName(string nodeId) => DistanceDisplayName + "__" + nodeId;
        private static string MakeCellIdChannelName(string nodeId) => CellIdDisplayName + "__" + nodeId;

        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _distanceChannelName;
        private readonly string _cellIdChannelName;

        [MinValue(0.0f)]
        [Description("Controls the scale of the Voronoi cells.")]
        private float _frequency;

        [Description("Offsets the sampled position in X and Y.")]
        private Vector2 _offset;

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

        public string DistanceChannelName
        {
            get
            {
                return _distanceChannelName;
            }
        }

        public string CellIdChannelName
        {
            get
            {
                return _cellIdChannelName;
            }
        }

        public float Frequency
        {
            get
            {
                return _frequency;
            }
        }

        public Vector2 Offset
        {
            get
            {
                return _offset;
            }
        }

        public VoronoiNoiseNode(string nodeId, string nodeName) : this(nodeId, nodeName, 0.1f, Vector2.zero)
        {
        }

        public VoronoiNoiseNode(string nodeId, string nodeName, float frequency, Vector2 offset)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (frequency < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency cannot be negative.");
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _frequency = frequency;
            _offset = offset;

            _distanceChannelName = MakeDistanceChannelName(nodeId);
            _cellIdChannelName = MakeCellIdChannelName(nodeId);

            _ports = new[]
            {
                new NodePortDefinition(_distanceChannelName, PortDirection.Output, ChannelType.Float, displayName: DistanceDisplayName),
                new NodePortDefinition(_cellIdChannelName, PortDirection.Output, ChannelType.Int, displayName: CellIdDisplayName)
            };

            // Both channels are declared as owned (IsWrite = true) by this node.
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_distanceChannelName, ChannelType.Float, true),
                new ChannelDeclaration(_cellIdChannelName, ChannelType.Int, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "frequency", StringComparison.OrdinalIgnoreCase))
            {
                float parsedFrequency;
                if (float.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out parsedFrequency))
                {
                    _frequency = math.max(0.0f, parsedFrequency);
                }

                return;
            }

            if (string.Equals(name, "offset", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 parsedOffset;
                if (TryParseVector2(value, out parsedOffset))
                {
                    _offset = parsedOffset;
                }
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> distanceOutput = context.GetFloatChannel(_distanceChannelName);
            NativeArray<int> cellIdOutput = context.GetIntChannel(_cellIdChannelName);

            VoronoiJob job = new VoronoiJob
            {
                DistanceOutput = distanceOutput,
                CellIdOutput = cellIdOutput,
                Width = context.Width,
                Frequency = _frequency,
                Offset = new float2(_offset.x, _offset.y),
                LocalSeed = context.LocalSeed
            };

            return job.Schedule(distanceOutput.Length, DefaultBatchSize, context.InputDependency);
        }

        private static bool TryParseVector2(string rawValue, out Vector2 result)
        {
            string safeValue = rawValue ?? string.Empty;
            string trimmedValue = safeValue.Trim();

            if (trimmedValue.Length == 0)
            {
                result = Vector2.zero;
                return true;
            }

            string normalisedValue = trimmedValue.Replace("(", string.Empty).Replace(")", string.Empty);
            string[] parts = normalisedValue.Split(',');
            if (parts.Length == 2)
            {
                float xValue;
                float yValue;
                if (float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out xValue) &&
                    float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out yValue))
                {
                    result = new Vector2(xValue, yValue);
                    return true;
                }
            }

            float scalarValue;
            if (float.TryParse(trimmedValue, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out scalarValue))
            {
                result = new Vector2(scalarValue, scalarValue);
                return true;
            }

            try
            {
                Vector2 jsonVector = UnityEngine.JsonUtility.FromJson<Vector2>(trimmedValue);
                if (!float.IsNaN(jsonVector.x) && !float.IsNaN(jsonVector.y))
                {
                    result = jsonVector;
                    return true;
                }
            }
            catch
            {
            }

            result = Vector2.zero;
            return false;
        }

        [BurstCompile]
        private struct VoronoiJob : IJobParallelFor
        {
            public NativeArray<float> DistanceOutput;
            public NativeArray<int> CellIdOutput;
            public int Width;
            public float Frequency;
            public float2 Offset;
            public long LocalSeed;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                float2 samplePos = new float2(
                    ((float)x + Offset.x) * Frequency,
                    ((float)y + Offset.y) * Frequency);

                float2 cellFloor = math.floor(samplePos);
                int2 cellOrigin = new int2((int)cellFloor.x, (int)cellFloor.y);

                float minDist = float.MaxValue;
                int2 nearestCell = int2.zero;

                int dy;
                for (dy = -1; dy <= 1; dy++)
                {
                    int dx;
                    for (dx = -1; dx <= 1; dx++)
                    {
                        int2 neighbour = new int2(cellOrigin.x + dx, cellOrigin.y + dy);
                        float2 cellPoint = new float2(neighbour) + GetJitteredPoint(neighbour, LocalSeed);
                        float dist = math.distance(samplePos, cellPoint);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearestCell = neighbour;
                        }
                    }
                }

                // Voronoi distance is normalised; sqrt(2) is the upper bound when jitter is in [0,1)^2.
                DistanceOutput[index] = math.saturate(minDist / math.sqrt(2.0f));

                // CellId is derived only from cell coordinates — no seed involvement — giving
                // stable IDs that are consistent across different global seeds.
                CellIdOutput[index] = StableCellHash(nearestCell);
            }

            // Jitter uses LocalSeed so the point positions vary with the graph seed.
            private static float2 GetJitteredPoint(int2 cellCoord, long localSeed)
            {
                unchecked
                {
                    int h = (cellCoord.x * 1664525) ^ (cellCoord.y * 1013904223) ^ (int)localSeed;
                    h = (h ^ (h >> 16)) * unchecked((int)0x45d9f3b);
                    h = h ^ (h >> 16);
                    int h2 = h * 1013904223 ^ (int)(localSeed >> 32);
                    h2 = (h2 ^ (h2 >> 16)) * unchecked((int)0x45d9f3b);
                    h2 = h2 ^ (h2 >> 16);
                    return new float2(
                        (h & 0xFFFF) / 65535.0f,
                        (h2 & 0xFFFF) / 65535.0f);
                }
            }

            // CellId hash uses only cell coordinates so IDs remain stable across different seeds.
            private static int StableCellHash(int2 cellCoord)
            {
                unchecked
                {
                    int h = (cellCoord.x * 1664525) ^ (cellCoord.y * 1013904223);
                    h = (h ^ (h >> 16)) * unchecked((int)0x45d9f3b);
                    return h ^ (h >> 16);
                }
            }
        }
    }
}
