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
    [NodeCategory("Noise")]
    [NodeDisplayName("Gradient")]
    [Description("Deterministic spatial gradient with no randomness. Direction selectable: X, Y, Radial, Diagonal. Output is always 0–1.")]
    public sealed class GradientNoiseNode : IGenNode, IParameterReceiver, IParameterVisibilityProvider
    {
        private const string DefaultNodeName = "Gradient";
        private const int DefaultBatchSize = 64;
        private const string PreferredOutputDisplayName = GraphPortNameUtility.LegacyGenericOutputDisplayName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;
        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;

        [Description("Determines the direction of the gradient.")]
        private GradientDirection _direction;

        [Description("Centre point for Radial mode, in normalised 0–1 map coordinates.")]
        private Vector2 _centre;

        [Description("Angle in degrees for Diagonal mode. 0 = left-to-right, 90 = bottom-to-top.")]
        private float _angle;

        [MinValue(0.0001f)]
        [Description("Controls how quickly the linear gradient reaches its maximum value. Higher values spread it further across the map.")]
        private float _scale;

        [MinValue(0.0001f)]
        [Description("Controls how quickly the radial gradient reaches its maximum value from the centre. Higher values produce a larger radius.")]
        private float _radius;

        [MinValue(0.0f)]
        [Description("Scales the strength of the gradient before the final 0–1 clamp.")]
        private float _amplitude;

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

        public GradientDirection Direction
        {
            get
            {
                return _direction;
            }
        }

        public Vector2 Centre
        {
            get
            {
                return _centre;
            }
        }

        public float Angle
        {
            get
            {
                return _angle;
            }
        }

        public float Scale
        {
            get
            {
                return _scale;
            }
        }

        public float Radius
        {
            get
            {
                return _radius;
            }
        }

        public float Amplitude
        {
            get
            {
                return _amplitude;
            }
        }

        public GradientNoiseNode(string nodeId, string nodeName, string outputChannelName) : this(nodeId, nodeName, outputChannelName, GradientDirection.X, new Vector2(0.5f, 0.5f), 45.0f, 1.0f, 1.0f, 1.0f)
        {
        }

        public GradientNoiseNode(string nodeId, string nodeName, string outputChannelName, GradientDirection direction, Vector2 centre, float angle, float scale = 1.0f, float radius = 1.0f, float amplitude = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            if (string.IsNullOrWhiteSpace(nodeName))
            {
                throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
            }

            if (string.IsNullOrWhiteSpace(outputChannelName))
            {
                throw new ArgumentException("Output channel name must be non-empty.", nameof(outputChannelName));
            }

            _nodeId = nodeId;
            _nodeName = nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _direction = direction;
            _centre = centre;
            _angle = angle;
            _scale = math.max(0.0001f, scale);
            _radius = math.max(0.0001f, radius);
            _amplitude = math.max(0.0f, amplitude);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "direction", StringComparison.OrdinalIgnoreCase))
            {
                GradientDirection parsedDirection;
                try
                {
                    parsedDirection = (GradientDirection)Enum.Parse(typeof(GradientDirection), value, true);
                    _direction = parsedDirection;
                }
                catch
                {
                }

                return;
            }

            if (string.Equals(name, "centre", StringComparison.OrdinalIgnoreCase))
            {
                Vector2 parsedCentre;
                if (TryParseVector2(value, out parsedCentre))
                {
                    _centre = parsedCentre;
                }

                return;
            }

            if (string.Equals(name, "angle", StringComparison.OrdinalIgnoreCase))
            {
                float parsedAngle;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedAngle))
                {
                    _angle = parsedAngle;
                }

                return;
            }

            if (string.Equals(name, "scale", StringComparison.OrdinalIgnoreCase))
            {
                float parsedScale;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedScale))
                {
                    _scale = math.max(0.0001f, parsedScale);
                }

                return;
            }

            if (string.Equals(name, "radius", StringComparison.OrdinalIgnoreCase))
            {
                float parsedRadius;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedRadius))
                {
                    _radius = math.max(0.0001f, parsedRadius);
                }

                return;
            }

            if (string.Equals(name, "amplitude", StringComparison.OrdinalIgnoreCase))
            {
                float parsedAmplitude;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedAmplitude))
                {
                    _amplitude = math.max(0.0f, parsedAmplitude);
                }

                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, PreferredOutputDisplayName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public bool IsParameterVisible(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return true;
            }

            if (string.Equals(parameterName, "centre", StringComparison.OrdinalIgnoreCase))
            {
                return _direction == GradientDirection.Radial;
            }

            if (string.Equals(parameterName, "scale", StringComparison.OrdinalIgnoreCase))
            {
                return _direction != GradientDirection.Radial;
            }

            if (string.Equals(parameterName, "radius", StringComparison.OrdinalIgnoreCase))
            {
                return _direction == GradientDirection.Radial;
            }

            if (string.Equals(parameterName, "amplitude", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(parameterName, "angle", StringComparison.OrdinalIgnoreCase))
            {
                return _direction == GradientDirection.Diagonal;
            }

            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            float widthMinusOne = math.max(1.0f, (float)(context.Width - 1));
            float heightMinusOne = math.max(1.0f, (float)(context.Height - 1));
            float2 centre = new float2(_centre.x, _centre.y);

            // Precompute radial max distance (furthest map corner from the centre).
            float2 c00 = new float2(0.0f, 0.0f);
            float2 c10 = new float2(1.0f, 0.0f);
            float2 c01 = new float2(0.0f, 1.0f);
            float2 c11 = new float2(1.0f, 1.0f);
            float radialMaxDist = math.max(
                math.max(math.distance(centre, c00), math.distance(centre, c10)),
                math.max(math.distance(centre, c01), math.distance(centre, c11)));
            radialMaxDist = math.max(radialMaxDist, 1e-6f);

            // Precompute diagonal direction and normalisation range.
            float angleRad = _angle * (math.PI / 180.0f);
            float2 diagonalDir = new float2(math.cos(angleRad), math.sin(angleRad));
            float d00 = math.dot(new float2(0.0f, 0.0f), diagonalDir);
            float d10 = math.dot(new float2(1.0f, 0.0f), diagonalDir);
            float d01 = math.dot(new float2(0.0f, 1.0f), diagonalDir);
            float d11 = math.dot(new float2(1.0f, 1.0f), diagonalDir);
            float diagonalMin = math.min(math.min(d00, d10), math.min(d01, d11));
            float diagonalRange = math.max(math.max(d00, d10), math.max(d01, d11)) - diagonalMin;
            diagonalRange = math.max(diagonalRange, 1e-6f);

            GradientJob job = new GradientJob
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
                DiagonalRange = diagonalRange,
                Scale = _scale,
                Radius = _radius,
                Amplitude = _amplitude
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private static bool TryParseVector2(string rawValue, out Vector2 result)
        {
            string safeValue = rawValue ?? string.Empty;
            string trimmedValue = safeValue.Trim();

            if (trimmedValue.Length == 0)
            {
                result = new Vector2(0.5f, 0.5f);
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

            result = new Vector2(0.5f, 0.5f);
            return false;
        }

        [BurstCompile]
        internal struct GradientJob : IJobParallelFor
        {
            public NativeArray<float> Output;
            public int Width;
            public float WidthMinusOne;
            public float HeightMinusOne;
            public GradientDirection Direction;
            public float2 Centre;
            public float RadialMaxDist;
            public float2 DiagonalDir;
            public float DiagonalMin;
            public float DiagonalRange;
            public float Scale;
            public float Radius;
            public float Amplitude;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;

                float u = (float)x / WidthMinusOne;
                float v = (float)y / HeightMinusOne;

                float value;
                if (Direction == GradientDirection.X)
                {
                    value = u;
                }
                else if (Direction == GradientDirection.Y)
                {
                    value = v;
                }
                else if (Direction == GradientDirection.Radial)
                {
                    float dist = math.distance(new float2(u, v), Centre);
                    value = dist / RadialMaxDist;
                }
                else
                {
                    float proj = math.dot(new float2(u, v), DiagonalDir);
                    value = (proj - DiagonalMin) / DiagonalRange;
                }

                float extent = Direction == GradientDirection.Radial ? Radius : Scale;
                float scaledValue = value / math.max(extent, 0.0001f);
                Output[index] = math.saturate(scaledValue * Amplitude);
            }
        }
    }
}
