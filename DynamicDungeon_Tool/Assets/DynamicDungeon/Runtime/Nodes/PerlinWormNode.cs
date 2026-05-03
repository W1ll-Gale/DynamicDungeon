using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Growth")]
    [NodeDisplayName("Perlin Worm")]
    [Description("Carves smooth Terraria-style worm tunnels by walking noise-steered paths through an optional mask.")]
    public sealed class PerlinWormNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        public enum WormStartMode
        {
            RandomInMask = 0,
            SurfaceColumn = 1
        }

        private const string DefaultNodeName = "Perlin Worm";
        private const string MaskPortName = "Mask";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = "Worms";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _maskChannelName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        [MinValue(0.0f)]
        [DescriptionAttribute("Number of worm paths to carve.")]
        private int _wormCount;

        [MinValue(0.0f)]
        [DescriptionAttribute("Minimum worm length in walk steps.")]
        private int _lengthMin;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum worm length in walk steps.")]
        private int _lengthMax;

        [MinValue(0.0f)]
        [DescriptionAttribute("Tunnel radius in tiles.")]
        private int _radius;

        [MinValue(0.0f)]
        [DescriptionAttribute("Tunnel radius near the end of the path. Use the same value as Radius for a constant width.")]
        private int _endRadius;

        [MinValue(0.0f)]
        [DescriptionAttribute("Noise frequency used to steer each worm.")]
        private float _turnFrequency;

        [MinValue(0.0f)]
        [DescriptionAttribute("Maximum heading change per step, in radians.")]
        private float _turnStrength;

        [MinValue(0.0f)]
        [DescriptionAttribute("Distance each worm advances per step.")]
        private float _stepSize;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("How strongly paths prefer long horizontal Terraria-style tunnels.")]
        private float _horizontalBias;

        [RangeAttribute(0.0f, 1.0f)]
        [DescriptionAttribute("How strongly paths prefer downward motion from their start.")]
        private float _downwardBias;

        [DescriptionAttribute("Where worm paths choose their starting point.")]
        private WormStartMode _startMode;

        [MinValue(0.0f)]
        [DescriptionAttribute("Extra radius used for the surface opening when Start Mode is Surface Column.")]
        private int _mouthRadius;

        [MinValue(0.0f)]
        [DescriptionAttribute("Vertical opening length carved below the surface when Start Mode is Surface Column.")]
        private int _mouthLength;

        [MinValue(0.0f)]
        [DescriptionAttribute("Steps used to fade from a vertical entrance into the regular horizontal worm bias.")]
        private int _biasFadeSteps;

        [DescriptionAttribute("Deterministic seed offset for alternate worm layouts.")]
        private int _seedOffset;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public PerlinWormNode(
            string nodeId,
            string nodeName,
            string maskChannelName = "",
            string outputChannelName = "",
            int wormCount = 6,
            int lengthMin = 32,
            int lengthMax = 96,
            int radius = 2,
            int endRadius = 2,
            float turnFrequency = 0.06f,
            float turnStrength = 0.65f,
            float stepSize = 1.0f,
            float horizontalBias = 0.65f,
            float downwardBias = 0.0f,
            WormStartMode startMode = WormStartMode.RandomInMask,
            int mouthRadius = 0,
            int mouthLength = 0,
            int biasFadeSteps = 0,
            int seedOffset = 0)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _maskChannelName = maskChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _wormCount = math.max(0, wormCount);
            _lengthMin = math.max(0, lengthMin);
            _lengthMax = math.max(_lengthMin, lengthMax);
            _radius = math.max(0, radius);
            _endRadius = math.max(0, endRadius);
            _turnFrequency = math.max(0.0f, turnFrequency);
            _turnStrength = math.max(0.0f, turnStrength);
            _stepSize = math.max(0.01f, stepSize);
            _horizontalBias = math.saturate(horizontalBias);
            _downwardBias = math.saturate(downwardBias);
            _startMode = startMode;
            _mouthRadius = math.max(0, mouthRadius);
            _mouthLength = math.max(0, mouthLength);
            _biasFadeSteps = math.max(0, biasFadeSteps);
            _seedOffset = seedOffset;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            string maskChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(MaskPortName, out maskChannelName))
            {
                _maskChannelName = maskChannelName ?? string.Empty;
            }
            else
            {
                _maskChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (TryReceiveInt(name, value, "wormCount", ref _wormCount, 0))
            {
                return;
            }

            if (TryReceiveInt(name, value, "lengthMin", ref _lengthMin, 0))
            {
                _lengthMax = math.max(_lengthMin, _lengthMax);
                return;
            }

            if (TryReceiveInt(name, value, "lengthMax", ref _lengthMax, _lengthMin))
            {
                return;
            }

            if (TryReceiveInt(name, value, "radius", ref _radius, 0))
            {
                return;
            }

            if (TryReceiveInt(name, value, "endRadius", ref _endRadius, 0))
            {
                return;
            }

            if (TryReceiveFloat(name, value, "turnFrequency", ref _turnFrequency, 0.0f))
            {
                return;
            }

            if (TryReceiveFloat(name, value, "turnStrength", ref _turnStrength, 0.0f))
            {
                return;
            }

            if (TryReceiveFloat(name, value, "stepSize", ref _stepSize, 0.01f))
            {
                return;
            }

            if (TryReceiveFloat(name, value, "horizontalBias", ref _horizontalBias, 0.0f))
            {
                _horizontalBias = math.saturate(_horizontalBias);
                return;
            }

            if (TryReceiveFloat(name, value, "downwardBias", ref _downwardBias, 0.0f))
            {
                _downwardBias = math.saturate(_downwardBias);
                return;
            }

            if (string.Equals(name, "startMode", StringComparison.OrdinalIgnoreCase))
            {
                WormStartMode parsedStartMode;
                if (Enum.TryParse(value ?? string.Empty, true, out parsedStartMode))
                {
                    _startMode = parsedStartMode;
                }

                return;
            }

            if (TryReceiveInt(name, value, "mouthRadius", ref _mouthRadius, 0))
            {
                return;
            }

            if (TryReceiveInt(name, value, "mouthLength", ref _mouthLength, 0))
            {
                return;
            }

            if (TryReceiveInt(name, value, "biasFadeSteps", ref _biasFadeSteps, 0))
            {
                return;
            }

            if (string.Equals(name, "seedOffset", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _seedOffset = parsedValue;
                }

                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, FallbackOutputPortName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            NativeArray<byte> mask = !string.IsNullOrWhiteSpace(_maskChannelName)
                ? context.GetBoolMaskChannel(_maskChannelName)
                : default;

            PerlinWormJob job = new PerlinWormJob
            {
                Mask = mask,
                Output = output,
                Width = context.Width,
                Height = context.Height,
                WormCount = _wormCount,
                LengthMin = _lengthMin,
                LengthMax = _lengthMax,
                Radius = _radius,
                EndRadius = _endRadius,
                TurnFrequency = _turnFrequency,
                TurnStrength = _turnStrength,
                StepSize = _stepSize,
                HorizontalBias = _horizontalBias,
                DownwardBias = _downwardBias,
                StartMode = _startMode,
                MouthRadius = _mouthRadius,
                MouthLength = _mouthLength,
                BiasFadeSteps = _biasFadeSteps,
                SeedOffset = _seedOffset,
                LocalSeed = context.LocalSeed
            };

            return job.Schedule(context.InputDependency);
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(2);
            if (!string.IsNullOrWhiteSpace(_maskChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_maskChannelName, ChannelType.BoolMask, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        private static bool TryReceiveInt(string name, string value, string expectedName, ref int target, int minimum)
        {
            if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int parsedValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                target = math.max(minimum, parsedValue);
            }

            return true;
        }

        private static bool TryReceiveFloat(string name, string value, string expectedName, ref float target, float minimum)
        {
            if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            float parsedValue;
            if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedValue))
            {
                target = math.max(minimum, parsedValue);
            }

            return true;
        }

        [BurstCompile]
        private struct PerlinWormJob : IJob
        {
            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int WormCount;
            public int LengthMin;
            public int LengthMax;
            public int Radius;
            public int EndRadius;
            public float TurnFrequency;
            public float TurnStrength;
            public float StepSize;
            public float HorizontalBias;
            public float DownwardBias;
            public WormStartMode StartMode;
            public int MouthRadius;
            public int MouthLength;
            public int BiasFadeSteps;
            public int SeedOffset;
            public long LocalSeed;

            public void Execute()
            {
                ClearOutput();
                if (WormCount <= 0 || LengthMax <= 0 || Width <= 0 || Height <= 0)
                {
                    return;
                }

                int wormIndex;
                for (wormIndex = 0; wormIndex < WormCount; wormIndex++)
                {
                    float2 startPosition;
                    if (!TryResolveStartPosition(wormIndex, out startPosition))
                    {
                        continue;
                    }

                    WalkWorm(wormIndex, startPosition);
                }
            }

            private void ClearOutput()
            {
                int index;
                for (index = 0; index < Output.Length; index++)
                {
                    Output[index] = 0;
                }
            }

            private bool TryResolveStartPosition(int wormIndex, out float2 startPosition)
            {
                if (StartMode == WormStartMode.SurfaceColumn)
                {
                    return TryResolveSurfaceStartPosition(wormIndex, out startPosition);
                }

                const int MaxAttempts = 96;
                bool hasMask = Mask.IsCreated;
                int attempt;
                for (attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    int x = (int)math.floor(HashToUnitFloat(wormIndex, attempt, 0x41A7u) * Width);
                    int y = (int)math.floor(HashToUnitFloat(wormIndex, attempt, 0x72C5u) * Height);
                    x = math.clamp(x, 0, Width - 1);
                    y = math.clamp(y, 0, Height - 1);

                    if (!hasMask || IsMaskOpen(x, y))
                    {
                        startPosition = new float2(x + 0.5f, y + 0.5f);
                        return true;
                    }
                }

                startPosition = float2.zero;
                return false;
            }

            private bool TryResolveSurfaceStartPosition(int wormIndex, out float2 startPosition)
            {
                if (!Mask.IsCreated)
                {
                    int x = (int)math.floor(HashToUnitFloat(wormIndex, 0, 0x62B9u) * Width);
                    x = math.clamp(x, 0, Width - 1);
                    startPosition = new float2(x + 0.5f, Height - 0.5f);
                    return true;
                }

                const int MaxAttempts = 96;
                int attempt;
                for (attempt = 0; attempt < MaxAttempts; attempt++)
                {
                    int x = (int)math.floor(HashToUnitFloat(wormIndex, attempt, 0x62B9u) * Width);
                    x = math.clamp(x, 0, Width - 1);

                    int y;
                    for (y = Height - 1; y >= 0; y--)
                    {
                        if (IsMaskOpen(x, y))
                        {
                            startPosition = new float2(x + 0.5f, y + 0.5f);
                            return true;
                        }
                    }
                }

                startPosition = float2.zero;
                return false;
            }

            private void WalkWorm(int wormIndex, float2 position)
            {
                int length = ResolveLength(wormIndex);
                float angle = ResolveInitialAngle(wormIndex, length);
                float2 seedSlice = new float2(
                    HashToUnitFloat(wormIndex, SeedOffset, 0xD1B5u) * 1024.0f,
                    HashToUnitFloat(wormIndex, SeedOffset, 0x94EFu) * 1024.0f);

                if (StartMode == WormStartMode.SurfaceColumn)
                {
                    StampSurfaceMouth((int)math.floor(position.x), (int)math.floor(position.y));
                }

                int stepIndex;
                for (stepIndex = 0; stepIndex < length; stepIndex++)
                {
                    int centerX = (int)math.floor(position.x);
                    int centerY = (int)math.floor(position.y);
                    if (!IsWithinBounds(centerX, centerY))
                    {
                        return;
                    }

                    StampResolvedRadius(centerX, centerY, stepIndex, length);

                    float2 noisePosition = (position + seedSlice + new float2(wormIndex * 19.37f, stepIndex * 0.11f)) * TurnFrequency;
                    float turnNoise = noise.cnoise(noisePosition);
                    float headingNoise = noise.cnoise(noisePosition + new float2(37.17f, 91.43f));
                    float effectiveHorizontalBias = ResolveEffectiveHorizontalBias(stepIndex);
                    float effectiveDownwardBias = ResolveEffectiveDownwardBias(stepIndex);
                    float targetAngle = ResolveBiasedAngle(wormIndex, headingNoise, effectiveDownwardBias);
                    float currentBlend = math.saturate(effectiveHorizontalBias) * 0.08f;
                    angle = math.lerp(angle + (turnNoise * TurnStrength), targetAngle, currentBlend);

                    float2 direction = new float2(math.cos(angle), math.sin(angle));
                    direction.y *= math.lerp(1.0f, 0.35f, math.saturate(effectiveHorizontalBias));
                    direction = math.normalizesafe(direction, new float2(1.0f, 0.0f));
                    position += direction * StepSize;
                }
            }

            private float ResolveInitialAngle(int wormIndex, int length)
            {
                float horizontalSign = HashToUnitFloat(wormIndex, length, 0x813Fu) < 0.5f ? -1.0f : 1.0f;
                float wobble = (HashToUnitFloat(wormIndex, length, 0xB33Fu) - 0.5f) * math.PI;
                float horizontalAngle = horizontalSign < 0.0f ? math.PI : 0.0f;
                float freeAngle = HashToUnitFloat(wormIndex, length, 0xC48Du) * math.PI * 2.0f;
                float biasedAngle = math.lerp(freeAngle, horizontalAngle + wobble, math.saturate(ResolveEffectiveHorizontalBias(0)));
                return math.lerp(biasedAngle, -math.PI * 0.5f, math.saturate(DownwardBias));
            }

            private float ResolveBiasedAngle(int wormIndex, float noiseValue, float effectiveDownwardBias)
            {
                float horizontalSign = HashToUnitFloat(wormIndex, SeedOffset, 0x44E7u) < 0.5f ? -1.0f : 1.0f;
                float horizontalAngle = horizontalSign < 0.0f ? math.PI : 0.0f;
                float horizontalTarget = horizontalAngle + (noiseValue * TurnStrength);
                return math.lerp(horizontalTarget, -math.PI * 0.5f, math.saturate(effectiveDownwardBias));
            }

            private float ResolveEffectiveHorizontalBias(int stepIndex)
            {
                if (StartMode != WormStartMode.SurfaceColumn || BiasFadeSteps <= 0)
                {
                    return HorizontalBias;
                }

                float t = math.saturate((float)stepIndex / BiasFadeSteps);
                return math.lerp(0.0f, HorizontalBias, t);
            }

            private float ResolveEffectiveDownwardBias(int stepIndex)
            {
                if (StartMode != WormStartMode.SurfaceColumn || BiasFadeSteps <= 0)
                {
                    return DownwardBias;
                }

                float t = math.saturate((float)stepIndex / BiasFadeSteps);
                return math.lerp(DownwardBias, 0.0f, t);
            }

            private int ResolveLength(int wormIndex)
            {
                if (LengthMax <= LengthMin)
                {
                    return LengthMin;
                }

                int range = LengthMax - LengthMin + 1;
                return LengthMin + (int)(Hash(wormIndex, LengthMin, LengthMax, 0x857Bu) % (uint)range);
            }

            private void StampSurfaceMouth(int centerX, int surfaceY)
            {
                int radius = math.max(Radius, MouthRadius);
                int length = math.max(0, MouthLength);
                int depth;
                for (depth = 0; depth <= length; depth++)
                {
                    StampRadius(centerX, surfaceY - depth, radius);
                }
            }

            private void StampResolvedRadius(int centerX, int centerY, int stepIndex, int length)
            {
                int radius = ResolveRadius(stepIndex, length);
                StampRadius(centerX, centerY, radius);
            }

            private void StampRadius(int centerX, int centerY, int radius)
            {
                if (!IsWithinBounds(centerX, centerY))
                {
                    return;
                }

                int radiusSquared = radius * radius;
                int minX = math.max(0, centerX - radius);
                int maxX = math.min(Width - 1, centerX + radius);
                int minY = math.max(0, centerY - radius);
                int maxY = math.min(Height - 1, centerY + radius);

                int y;
                for (y = minY; y <= maxY; y++)
                {
                    int x;
                    for (x = minX; x <= maxX; x++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        if ((dx * dx) + (dy * dy) > radiusSquared)
                        {
                            continue;
                        }

                        if (Mask.IsCreated && !IsMaskOpen(x, y))
                        {
                            continue;
                        }

                        Output[ToIndex(x, y)] = 1;
                    }
                }
            }

            private int ResolveRadius(int stepIndex, int length)
            {
                if (length <= 1 || Radius == EndRadius)
                {
                    return Radius;
                }

                float t = math.saturate((float)stepIndex / (length - 1));
                return math.max(0, (int)math.round(math.lerp(Radius, EndRadius, t)));
            }

            private bool IsMaskOpen(int x, int y)
            {
                return Mask[ToIndex(x, y)] != 0;
            }

            private bool IsWithinBounds(int x, int y)
            {
                return x >= 0 && x < Width && y >= 0 && y < Height;
            }

            private int ToIndex(int x, int y)
            {
                return (y * Width) + x;
            }

            private float HashToUnitFloat(int valueA, int valueB, uint salt)
            {
                return (float)(Hash(valueA, valueB, SeedOffset, salt) / 4294967296.0d);
            }

            private uint Hash(int valueA, int valueB, int valueC, uint salt)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                return math.hash(new uint4(
                    unchecked((uint)valueA) ^ seedLow ^ salt,
                    unchecked((uint)valueB) ^ (salt * 0x9E3779B9u),
                    unchecked((uint)valueC) ^ seedHigh,
                    unchecked((uint)SeedOffset) ^ 0xB5297A4Du));
            }
        }
    }
}
