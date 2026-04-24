using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Noise")]
    [NodeDisplayName("Unified Noise")]
    [Description("Single noise node covering all algorithms. Select the algorithm from the dropdown; relevant parameters are shown automatically.")]
    public sealed class UnifiedNoiseNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IParameterVisibilityProvider
    {
        private const int DefaultBatchSize = 64;
        private const string OutputDisplayName = "Output";
        private const string CellIdDisplayName = "CellId";
        private const string InputPortName = "Input";
        private const int MaxFractalOctaves = 8;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private static string MakeOutputChannelName(string nodeId) => OutputDisplayName + "__" + nodeId;
        private static string MakeCellIdChannelName(string nodeId) => CellIdDisplayName + "__" + nodeId;

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly string _cellIdChannelName;

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        [Description("Selects which noise algorithm this node evaluates.")]
        private NoiseAlgorithm _algorithm;

        private string _inputChannelName;

        // Perlin / Simplex / Voronoi
        [MinValue(0.0f)]
        [Description("Controls how quickly the noise pattern changes across the grid.")]
        private float _frequency;

        // Perlin / Simplex
        [MinValue(0.0f)]
        [Description("Scales the strength of the generated noise values.")]
        private float _amplitude;

        // Perlin / Simplex / Voronoi
        [Description("Offsets the sampled position in X and Y.")]
        private Vector2 _offset;

        // Perlin / Fractal
        [Range(1, MaxFractalOctaves)]
        [Description("Number of noise layers stacked together.")]
        private int _octaves;

        // Perlin only
        [Description("Deterministically varies this node's result relative to the graph seed without changing the graph-wide seed.")]
        private int _seedOffset;

        // Fractal
        [MinValue(1.0f)]
        [Description("Frequency multiplier applied to each successive octave.")]
        private float _lacunarity;

        [Range(0.0f, 1.0f)]
        [Description("Amplitude multiplier applied to each successive octave.")]
        private float _persistence;

        // Gradient
        [Description("Determines the direction of the gradient.")]
        private GradientDirection _direction;

        [Description("Centre point for Radial mode, in normalised 0-1 map coordinates.")]
        private Vector2 _centre;

        [Description("Angle in degrees for Diagonal mode. 0 = left-to-right, 90 = bottom-to-top.")]
        private float _angle;

        // Constant
        [Description("The constant float value written to every tile.")]
        private float _floatValue;

        [Description("The constant integer value written to every tile (cast to float on output).")]
        private int _intValue;

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

        public string OutputChannelName
        {
            get
            {
                return _outputChannelName;
            }
        }

        public string CellIdChannelName
        {
            get
            {
                return _cellIdChannelName;
            }
        }

        public NoiseAlgorithm Algorithm
        {
            get
            {
                return _algorithm;
            }
        }

        public UnifiedNoiseNode(string nodeId, string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = MakeOutputChannelName(nodeId);
            _cellIdChannelName = MakeCellIdChannelName(nodeId);
            _inputChannelName = string.Empty;

            _algorithm = NoiseAlgorithm.Perlin;
            _frequency = 0.05f;
            _amplitude = 1.0f;
            _offset = Vector2.zero;
            _octaves = 4;
            _seedOffset = 0;
            _lacunarity = 2.0f;
            _persistence = 0.5f;
            _direction = GradientDirection.X;
            _centre = new Vector2(0.5f, 0.5f);
            _angle = 45.0f;
            _floatValue = 0.0f;
            _intValue = 0;

            RefreshPortsAndChannels();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "algorithm", StringComparison.OrdinalIgnoreCase))
            {
                NoiseAlgorithm parsed;
                try
                {
                    parsed = (NoiseAlgorithm)Enum.Parse(typeof(NoiseAlgorithm), value ?? string.Empty, true);
                }
                catch
                {
                    return;
                }

                if (parsed != _algorithm)
                {
                    _algorithm = parsed;
                    RefreshPortsAndChannels();
                }

                return;
            }

            if (string.Equals(name, "frequency", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _frequency = math.max(0.0f, parsed);
                }

                return;
            }

            if (string.Equals(name, "amplitude", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _amplitude = math.max(0.0f, parsed);
                }

                return;
            }

            if (string.Equals(name, "offset", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 parsed;
                if (TryParseVector2(value, out parsed))
                {
                    _offset = parsed;
                }

                return;
            }

            if (string.Equals(name, "octaves", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    _octaves = math.clamp(parsed, 1, MaxFractalOctaves);
                }

                return;
            }

            if (string.Equals(name, "seedOffset", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    _seedOffset = parsed;
                }

                return;
            }

            if (string.Equals(name, "lacunarity", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _lacunarity = math.max(1.0f, parsed);
                }

                return;
            }

            if (string.Equals(name, "persistence", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _persistence = math.clamp(parsed, 0.0f, 1.0f);
                }

                return;
            }

            if (string.Equals(name, "direction", StringComparison.OrdinalIgnoreCase))
            {
                GradientDirection parsed;
                try
                {
                    parsed = (GradientDirection)Enum.Parse(typeof(GradientDirection), value ?? string.Empty, true);
                    _direction = parsed;
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "centre", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 parsed;
                if (TryParseVector2(value, out parsed))
                {
                    _centre = parsed;
                }

                return;
            }

            if (string.Equals(name, "angle", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _angle = parsed;
                }

                return;
            }

            if (string.Equals(name, "floatValue", StringComparison.OrdinalIgnoreCase))
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                {
                    _floatValue = parsed;
                }

                return;
            }

            if (string.Equals(name, "intValue", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    _intValue = parsed;
                }
            }
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string channelName;
            if (_algorithm == NoiseAlgorithm.Fractal &&
                inputConnections != null &&
                inputConnections.TryGetValue(InputPortName, out channelName))
            {
                _inputChannelName = channelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshPortsAndChannels();
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            if (string.Equals(parameterName, "algorithm", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            switch (_algorithm)
            {
                case NoiseAlgorithm.Perlin:
                    return string.Equals(parameterName, "frequency", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "amplitude", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "offset", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "octaves", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "seedOffset", StringComparison.OrdinalIgnoreCase);

                case NoiseAlgorithm.Simplex:
                    return string.Equals(parameterName, "frequency", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "amplitude", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "offset", StringComparison.OrdinalIgnoreCase);

                case NoiseAlgorithm.Voronoi:
                    return string.Equals(parameterName, "frequency", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "offset", StringComparison.OrdinalIgnoreCase);

                case NoiseAlgorithm.Fractal:
                    return string.Equals(parameterName, "octaves", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "lacunarity", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "persistence", StringComparison.OrdinalIgnoreCase);

                case NoiseAlgorithm.Gradient:
                    return string.Equals(parameterName, "direction", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "centre", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "angle", StringComparison.OrdinalIgnoreCase);

                case NoiseAlgorithm.Constant:
                    return string.Equals(parameterName, "floatValue", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(parameterName, "intValue", StringComparison.OrdinalIgnoreCase);

                default:
                    return false;
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            switch (_algorithm)
            {
                case NoiseAlgorithm.Perlin:
                    return SchedulePerlin(context);
                case NoiseAlgorithm.Simplex:
                    return ScheduleSimplex(context);
                case NoiseAlgorithm.Voronoi:
                    return ScheduleVoronoi(context);
                case NoiseAlgorithm.Fractal:
                    return ScheduleFractal(context);
                case NoiseAlgorithm.Gradient:
                    return ScheduleGradient(context);
                case NoiseAlgorithm.Constant:
                    return ScheduleConstant(context);
                default:
                    return ScheduleConstant(context);
            }
        }

        private JobHandle SchedulePerlin(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            PerlinNoiseNode.PerlinNoiseJob job = new PerlinNoiseNode.PerlinNoiseJob
            {
                Output = output,
                Width = context.Width,
                Frequency = _frequency,
                Amplitude = _amplitude,
                Offset = new float2(_offset.x, _offset.y),
                SeedOffset = CreatePerlinSeedOffset(CombinePerlinSeed(context.GlobalSeed, _seedOffset)),
                Octaves = _octaves
            };
            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private JobHandle ScheduleSimplex(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            SimplexNoiseNode.SimplexNoiseJob job = new SimplexNoiseNode.SimplexNoiseJob
            {
                Output = output,
                Width = context.Width,
                Frequency = _frequency,
                Amplitude = _amplitude,
                Offset = new float2(_offset.x, _offset.y),
                SeedOffset = CreateSimplexSeedOffset(context.LocalSeed)
            };
            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private JobHandle ScheduleVoronoi(NodeExecutionContext context)
        {
            NativeArray<float> distanceOutput = context.GetFloatChannel(_outputChannelName);
            NativeArray<int> cellIdOutput = context.GetIntChannel(_cellIdChannelName);
            VoronoiNoiseNode.VoronoiJob job = new VoronoiNoiseNode.VoronoiJob
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

        private JobHandle ScheduleFractal(NodeExecutionContext context)
        {
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            FractalNoiseNode.FractalNoiseJob job = new FractalNoiseNode.FractalNoiseJob
            {
                Input = input,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                Octaves = _octaves,
                Lacunarity = _lacunarity,
                Persistence = _persistence
            };
            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private JobHandle ScheduleGradient(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            float widthMinusOne = math.max(1.0f, (float)(context.Width - 1));
            float heightMinusOne = math.max(1.0f, (float)(context.Height - 1));
            float2 centre = new float2(_centre.x, _centre.y);

            float2 c00 = new float2(0.0f, 0.0f);
            float2 c10 = new float2(1.0f, 0.0f);
            float2 c01 = new float2(0.0f, 1.0f);
            float2 c11 = new float2(1.0f, 1.0f);
            float radialMaxDist = math.max(
                math.max(math.distance(centre, c00), math.distance(centre, c10)),
                math.max(math.distance(centre, c01), math.distance(centre, c11)));
            radialMaxDist = math.max(radialMaxDist, 1e-6f);

            float angleRad = _angle * (math.PI / 180.0f);
            float2 diagonalDir = new float2(math.cos(angleRad), math.sin(angleRad));
            float d00 = math.dot(new float2(0.0f, 0.0f), diagonalDir);
            float d10 = math.dot(new float2(1.0f, 0.0f), diagonalDir);
            float d01 = math.dot(new float2(0.0f, 1.0f), diagonalDir);
            float d11 = math.dot(new float2(1.0f, 1.0f), diagonalDir);
            float diagonalMin = math.min(math.min(d00, d10), math.min(d01, d11));
            float diagonalRange = math.max(math.max(d00, d10), math.max(d01, d11)) - diagonalMin;
            diagonalRange = math.max(diagonalRange, 1e-6f);

            GradientNoiseNode.GradientJob job = new GradientNoiseNode.GradientJob
            {
                Output = output,
                Width = context.Width,
                WidthMinusOne = widthMinusOne,
                HeightMinusOne = heightMinusOne,
                Direction = _direction,
                Centre = centre,
                RadialMaxDist = radialMaxDist,
                DiagonalDir = diagonalDir,
                DiagonalMin = diagonalMin,
                DiagonalRange = diagonalRange
            };
            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private JobHandle ScheduleConstant(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            ConstantNode.FloatFillJob job = new ConstantNode.FloatFillJob
            {
                Output = output,
                Value = _floatValue
            };
            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshPortsAndChannels()
        {
            List<NodePortDefinition> portList = new List<NodePortDefinition>(3);
            List<ChannelDeclaration> channelList = new List<ChannelDeclaration>(3);

            if (_algorithm == NoiseAlgorithm.Fractal)
            {
                portList.Add(new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false));
            }

            portList.Add(new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: OutputDisplayName));

            if (_algorithm == NoiseAlgorithm.Voronoi)
            {
                portList.Add(new NodePortDefinition(_cellIdChannelName, PortDirection.Output, ChannelType.Int, displayName: CellIdDisplayName));
            }

            if (_algorithm == NoiseAlgorithm.Fractal && !string.IsNullOrWhiteSpace(_inputChannelName))
            {
                channelList.Add(new ChannelDeclaration(_inputChannelName, ChannelType.Float, false));
            }

            channelList.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));

            if (_algorithm == NoiseAlgorithm.Voronoi)
            {
                channelList.Add(new ChannelDeclaration(_cellIdChannelName, ChannelType.Int, true));
            }

            _ports = portList.ToArray();
            _channelDeclarations = channelList.ToArray();
        }

        private static float2 CreatePerlinSeedOffset(long seed)
        {
            unchecked
            {
                uint seedLow = (uint)seed;
                uint seedHigh = (uint)(seed >> 32);
                float seedX = ((seedLow & 65535u) / 65535.0f) * 10000.0f;
                float seedY = ((seedHigh & 65535u) / 65535.0f) * 10000.0f;
                return new float2(seedX, seedY);
            }
        }

        private static long CombinePerlinSeed(long globalSeed, int seedOffset)
        {
            unchecked
            {
                const long Prime = 1099511628211L;
                long combined = (globalSeed * Prime) ^ seedOffset;
                combined = (combined * Prime) ^ (seedOffset >> 16);
                return combined;
            }
        }

        private static float2 CreateSimplexSeedOffset(long localSeed)
        {
            unchecked
            {
                uint seedLow = (uint)localSeed;
                uint seedHigh = (uint)(localSeed >> 32);
                float seedX = ((seedLow & 65535u) / 65535.0f) * 10000.0f;
                float seedY = ((seedHigh & 65535u) / 65535.0f) * 10000.0f;
                return new float2(seedX, seedY);
            }
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
                if (float.TryParse(parts[0].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out xValue) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out yValue))
                {
                    result = new Vector2(xValue, yValue);
                    return true;
                }
            }

            float scalarValue;
            if (float.TryParse(trimmedValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out scalarValue))
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
    }
}
