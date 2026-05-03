using System;
using System.Collections.Generic;
using System.Globalization;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Authoring")]
    [NodeDisplayName("Terrain Profile")]
    [Description("Builds the high-level terrain silhouette, depth bands, placement weights, and base logical IDs for a side-on world.")]
    public sealed class TerrainProfileNode : IGenNode, IParameterReceiver
    {
        public const string TerrainSolidMaskChannel = "TerrainSolidMask";
        public const string SurfaceCrustMaskChannel = "SurfaceCrustMask";
        public const string UndergroundBandMaskChannel = "UndergroundBandMask";
        public const string DeepBandMaskChannel = "DeepBandMask";
        public const string SurfacePlacementWeightsChannel = "SurfacePlacementWeights";
        public const string CloudPlacementWeightsChannel = "CloudPlacementWeights";
        public const string BaseLogicalIdsChannel = "BaseLogicalIds";

        private const int DefaultBatchSize = 64;
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private readonly ChannelDeclaration[] _channelDeclarations;

        private float _baseSurfaceHeight;
        private float _hillAmplitude;
        private float _hillFrequency;
        private int _crustDepth;
        private float _deepStart;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public TerrainProfileNode(
            string nodeId,
            string nodeName,
            float baseSurfaceHeight = 0.68f,
            float hillAmplitude = 0.095f,
            float hillFrequency = 0.017f,
            int crustDepth = 14,
            float deepStart = 0.34f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "Terrain Profile" : nodeName;
            _baseSurfaceHeight = math.saturate(baseSurfaceHeight);
            _hillAmplitude = math.max(0.0f, hillAmplitude);
            _hillFrequency = math.max(0.001f, hillFrequency);
            _crustDepth = math.max(1, crustDepth);
            _deepStart = math.saturate(deepStart);

            _ports = new[]
            {
                new NodePortDefinition(TerrainSolidMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(SurfaceCrustMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(UndergroundBandMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(DeepBandMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(SurfacePlacementWeightsChannel, PortDirection.Output, ChannelType.Float),
                new NodePortDefinition(CloudPlacementWeightsChannel, PortDirection.Output, ChannelType.Float),
                new NodePortDefinition(BaseLogicalIdsChannel, PortDirection.Output, ChannelType.Int)
            };

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(TerrainSolidMaskChannel, ChannelType.BoolMask, true),
                new ChannelDeclaration(SurfaceCrustMaskChannel, ChannelType.BoolMask, true),
                new ChannelDeclaration(UndergroundBandMaskChannel, ChannelType.BoolMask, true),
                new ChannelDeclaration(DeepBandMaskChannel, ChannelType.BoolMask, true),
                new ChannelDeclaration(SurfacePlacementWeightsChannel, ChannelType.Float, true),
                new ChannelDeclaration(CloudPlacementWeightsChannel, ChannelType.Float, true),
                new ChannelDeclaration(BaseLogicalIdsChannel, ChannelType.Int, true)
            };
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "baseSurfaceHeight", StringComparison.OrdinalIgnoreCase))
            {
                _baseSurfaceHeight = math.saturate(ParseFloat(value, _baseSurfaceHeight));
            }
            else if (string.Equals(name, "hillAmplitude", StringComparison.OrdinalIgnoreCase))
            {
                _hillAmplitude = math.max(0.0f, ParseFloat(value, _hillAmplitude));
            }
            else if (string.Equals(name, "hillFrequency", StringComparison.OrdinalIgnoreCase))
            {
                _hillFrequency = math.max(0.001f, ParseFloat(value, _hillFrequency));
            }
            else if (string.Equals(name, "crustDepth", StringComparison.OrdinalIgnoreCase))
            {
                _crustDepth = math.max(1, ParseInt(value, _crustDepth));
            }
            else if (string.Equals(name, "deepStart", StringComparison.OrdinalIgnoreCase))
            {
                _deepStart = math.saturate(ParseFloat(value, _deepStart));
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            TerrainProfileJob job = new TerrainProfileJob
            {
                TerrainSolidMask = context.GetBoolMaskChannel(TerrainSolidMaskChannel),
                SurfaceCrustMask = context.GetBoolMaskChannel(SurfaceCrustMaskChannel),
                UndergroundBandMask = context.GetBoolMaskChannel(UndergroundBandMaskChannel),
                DeepBandMask = context.GetBoolMaskChannel(DeepBandMaskChannel),
                SurfacePlacementWeights = context.GetFloatChannel(SurfacePlacementWeightsChannel),
                CloudPlacementWeights = context.GetFloatChannel(CloudPlacementWeightsChannel),
                BaseLogicalIds = context.GetIntChannel(BaseLogicalIdsChannel),
                Width = context.Width,
                Height = context.Height,
                Seed = context.LocalSeed,
                BaseSurfaceHeight = _baseSurfaceHeight,
                HillAmplitude = _hillAmplitude,
                HillFrequency = _hillFrequency,
                CrustDepth = _crustDepth,
                DeepStart = _deepStart
            };

            return job.Schedule(context.Width * context.Height, DefaultBatchSize, context.InputDependency);
        }

        private struct TerrainProfileJob : IJobParallelFor
        {
            public NativeArray<byte> TerrainSolidMask;
            public NativeArray<byte> SurfaceCrustMask;
            public NativeArray<byte> UndergroundBandMask;
            public NativeArray<byte> DeepBandMask;
            public NativeArray<float> SurfacePlacementWeights;
            public NativeArray<float> CloudPlacementWeights;
            public NativeArray<int> BaseLogicalIds;
            public int Width;
            public int Height;
            public long Seed;
            public float BaseSurfaceHeight;
            public float HillAmplitude;
            public float HillFrequency;
            public int CrustDepth;
            public float DeepStart;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                int surfaceY = ResolveSurfaceY(x, Width, Height, Seed, BaseSurfaceHeight, HillAmplitude, HillFrequency);
                bool solid = y <= surfaceY;
                int depthBelowSurface = surfaceY - y;
                float yNorm = Height <= 1 ? 0.0f : y / (float)(Height - 1);
                float depthFromTop = 1.0f - yNorm;

                TerrainSolidMask[index] = solid ? (byte)1 : (byte)0;
                SurfaceCrustMask[index] = solid && depthBelowSurface <= CrustDepth ? (byte)1 : (byte)0;
                UndergroundBandMask[index] = solid && yNorm > DeepStart && depthBelowSurface > CrustDepth ? (byte)1 : (byte)0;
                DeepBandMask[index] = solid && yNorm <= DeepStart ? (byte)1 : (byte)0;
                SurfacePlacementWeights[index] = (!solid && y == surfaceY + 1 && y < Height - 2)
                    ? math.saturate(0.65f + 0.35f * AuthoringNoise.ValueNoise1D(x * 0.08f, Seed + 17L))
                    : 0.0f;
                CloudPlacementWeights[index] = (!solid && y > Height * 0.78f && y < Height * 0.91f)
                    ? math.saturate(AuthoringNoise.ValueNoise2D(x * 0.035f, y * 0.09f, Seed + 701L) - 0.72f) * 3.6f
                    : 0.0f;

                if (!solid)
                {
                    BaseLogicalIds[index] = 0;
                    return;
                }

                float materialNoise = AuthoringNoise.ValueNoise2D(x * 0.07f, y * 0.055f, Seed + 101L);
                if (depthBelowSurface <= 1)
                {
                    BaseLogicalIds[index] = 10;
                }
                else if (depthBelowSurface <= CrustDepth)
                {
                    BaseLogicalIds[index] = materialNoise > 0.68f ? 13 : 11;
                }
                else if (depthFromTop > 0.70f)
                {
                    BaseLogicalIds[index] = materialNoise > 0.62f ? 17 : 16;
                }
                else if (depthFromTop > 0.46f)
                {
                    BaseLogicalIds[index] = materialNoise > 0.56f ? 15 : 14;
                }
                else
                {
                    BaseLogicalIds[index] = materialNoise > 0.60f ? 18 : 13;
                }
            }
        }

        internal static int ResolveSurfaceY(int x, int width, int height, long seed, float baseSurfaceHeight, float hillAmplitude, float hillFrequency)
        {
            float broad = AuthoringNoise.ValueNoise1D(x * hillFrequency, seed + 19L) * 2.0f - 1.0f;
            float detail = AuthoringNoise.ValueNoise1D(x * hillFrequency * 3.2f, seed + 43L) * 2.0f - 1.0f;
            float ridge = math.sin((x + (seed & 127L)) * 0.045f) * 0.35f;
            float surface = baseSurfaceHeight + hillAmplitude * (broad * 0.70f + detail * 0.22f + ridge * 0.08f);
            return math.clamp((int)math.round(surface * (height - 1)), 8, height - 18);
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }
    }

    [NodeCategory("Authoring")]
    [NodeDisplayName("Cave System")]
    [Description("Generates connected cave, shallow/deep cave, and surface-entrance masks from a solid terrain mask.")]
    public sealed class CaveSystemNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        public const string TerrainSolidInputPort = TerrainProfileNode.TerrainSolidMaskChannel;
        public const string CaveMaskChannel = "CaveMask";
        public const string ShallowCaveMaskChannel = "ShallowCaveMask";
        public const string DeepCaveMaskChannel = "DeepCaveMask";
        public const string SurfaceEntranceMaskChannel = "SurfaceEntranceMask";

        private const int DefaultBatchSize = 64;
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private string _terrainSolidChannelName;
        private float _density;
        private float _worminess;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public CaveSystemNode(string nodeId, string nodeName, float density = 0.56f, float worminess = 0.72f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "Cave System" : nodeName;
            _terrainSolidChannelName = string.Empty;
            _density = math.saturate(density);
            _worminess = math.saturate(worminess);
            _ports = new[]
            {
                new NodePortDefinition(TerrainSolidInputPort, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(CaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(ShallowCaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(DeepCaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(SurfaceEntranceMaskChannel, PortDirection.Output, ChannelType.BoolMask)
            };
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _terrainSolidChannelName = inputConnections != null ? inputConnections.FirstOrDefault(TerrainSolidInputPort) : string.Empty;
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "density", StringComparison.OrdinalIgnoreCase))
            {
                _density = math.saturate(ParseFloat(value, _density));
            }
            else if (string.Equals(name, "worminess", StringComparison.OrdinalIgnoreCase))
            {
                _worminess = math.saturate(ParseFloat(value, _worminess));
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            CaveSystemJob job = new CaveSystemJob
            {
                TerrainSolidMask = context.GetBoolMaskChannel(_terrainSolidChannelName),
                CaveMask = context.GetBoolMaskChannel(CaveMaskChannel),
                ShallowCaveMask = context.GetBoolMaskChannel(ShallowCaveMaskChannel),
                DeepCaveMask = context.GetBoolMaskChannel(DeepCaveMaskChannel),
                SurfaceEntranceMask = context.GetBoolMaskChannel(SurfaceEntranceMaskChannel),
                Width = context.Width,
                Height = context.Height,
                Seed = context.LocalSeed,
                Density = _density,
                Worminess = _worminess
            };

            return job.Schedule(context.Width * context.Height, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_terrainSolidChannelName))
            {
                declarations.Add(new ChannelDeclaration(_terrainSolidChannelName, ChannelType.BoolMask, false));
            }

            declarations.Add(new ChannelDeclaration(CaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(ShallowCaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(DeepCaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(SurfaceEntranceMaskChannel, ChannelType.BoolMask, true));
            _channelDeclarations = declarations.ToArray();
        }

        private struct CaveSystemJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> TerrainSolidMask;
            public NativeArray<byte> CaveMask;
            public NativeArray<byte> ShallowCaveMask;
            public NativeArray<byte> DeepCaveMask;
            public NativeArray<byte> SurfaceEntranceMask;
            public int Width;
            public int Height;
            public long Seed;
            public float Density;
            public float Worminess;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                bool solid = TerrainSolidMask[index] != 0;
                float yNorm = Height <= 1 ? 0.0f : y / (float)(Height - 1);
                float depthFromTop = 1.0f - yNorm;

                float cellular = AuthoringNoise.ValueNoise2D(x * 0.055f, y * 0.070f, Seed + 211L);
                float pocket = AuthoringNoise.ValueNoise2D(x * 0.020f, y * 0.032f, Seed + 263L);
                float wormA = math.abs(math.sin((x + (Seed & 255L)) * 0.052f + y * 0.105f + pocket * 5.4f));
                float wormB = math.abs(math.cos(x * 0.083f - y * 0.061f + cellular * 4.3f));
                bool worm = math.min(wormA, wormB) < math.lerp(0.075f, 0.18f, Worminess);
                bool chamber = cellular > math.lerp(0.88f, 0.62f, math.saturate(Density + depthFromTop * 0.22f));
                bool upperPocket = depthFromTop > 0.18f && depthFromTop < 0.44f && pocket > 0.78f;
                int entrancePeriod = 84;
                int entranceCenter = (int)((AuthoringNoise.Hash((uint)(x / entrancePeriod), (uint)(Seed + 811L)) % (uint)entrancePeriod));
                int localX = x % entrancePeriod;
                bool entrance = depthFromTop > 0.18f && depthFromTop < 0.42f && math.abs(localX - entranceCenter) <= 2;
                bool open = solid && depthFromTop > 0.12f && (worm || chamber || upperPocket || entrance);

                CaveMask[index] = open ? (byte)1 : (byte)0;
                ShallowCaveMask[index] = open && depthFromTop < 0.52f ? (byte)1 : (byte)0;
                DeepCaveMask[index] = open && depthFromTop >= 0.52f ? (byte)1 : (byte)0;
                SurfaceEntranceMask[index] = entrance && solid ? (byte)1 : (byte)0;
            }
        }

        private static float ParseFloat(string value, float fallback)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }
    }

    [Serializable]
    public sealed class FeatureMaskSetRule
    {
        public bool Enabled = true;
        public int Slot = 1;
        public float MinDepth;
        public float MaxDepth = 1.0f;
        public float Density = 0.12f;
        public float Scale = 0.05f;
        public bool RequiresSolid = true;
        public bool RequiresCave;
    }

    [Serializable]
    public sealed class FeatureMaskSetRuleSet
    {
        public FeatureMaskSetRule[] Rules = Array.Empty<FeatureMaskSetRule>();
    }

    [NodeCategory("Authoring")]
    [NodeDisplayName("Feature Mask Set")]
    [Description("Builds a table-driven set of ore, cave-variant, and structure-candidate masks from terrain and cave channels.")]
    public sealed class FeatureMaskSetNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        public const string SolidInputPort = "Solid";
        public const string CaveInputPort = "Caves";
        public const string CoalOreMaskChannel = "CoalOreMask";
        public const string IronOreMaskChannel = "IronOreMask";
        public const string GoldOreMaskChannel = "GoldOreMask";
        public const string DiamondOreMaskChannel = "DiamondOreMask";
        public const string MossCaveMaskChannel = "MossCaveMask";
        public const string IceCaveMaskChannel = "IceCaveMask";
        public const string CrystalCaveMaskChannel = "CrystalCaveMask";
        public const string CaveHouseMaskChannel = "CaveHouseMask";

        private const int DefaultBatchSize = 64;
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private string _solidChannelName;
        private string _caveChannelName;
        private string _rules;
        private FeatureMaskSetRuleSet _parsedRules;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public FeatureMaskSetNode(string nodeId, string nodeName, string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "Feature Mask Set" : nodeName;
            _rules = rules ?? string.Empty;
            _parsedRules = ParseRules(_rules);
            _solidChannelName = string.Empty;
            _caveChannelName = string.Empty;
            _ports = new[]
            {
                new NodePortDefinition(SolidInputPort, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(CaveInputPort, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(CoalOreMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(IronOreMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(GoldOreMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(DiamondOreMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(MossCaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(IceCaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(CrystalCaveMaskChannel, PortDirection.Output, ChannelType.BoolMask),
                new NodePortDefinition(CaveHouseMaskChannel, PortDirection.Output, ChannelType.BoolMask)
            };
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _solidChannelName = inputConnections != null ? inputConnections.FirstOrDefault(SolidInputPort) : string.Empty;
            _caveChannelName = inputConnections != null ? inputConnections.FirstOrDefault(CaveInputPort) : string.Empty;
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase))
            {
                _rules = value ?? string.Empty;
                _parsedRules = ParseRules(_rules);
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            FeatureRuleData[] ruleData = BuildRules();
            NativeArray<FeatureRuleData> nativeRules = new NativeArray<FeatureRuleData>(ruleData.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int index = 0; index < ruleData.Length; index++)
            {
                nativeRules[index] = ruleData[index];
            }

            FeatureMaskSetJob job = new FeatureMaskSetJob
            {
                SolidMask = context.GetBoolMaskChannel(_solidChannelName),
                CaveMask = context.GetBoolMaskChannel(_caveChannelName),
                CoalOreMask = context.GetBoolMaskChannel(CoalOreMaskChannel),
                IronOreMask = context.GetBoolMaskChannel(IronOreMaskChannel),
                GoldOreMask = context.GetBoolMaskChannel(GoldOreMaskChannel),
                DiamondOreMask = context.GetBoolMaskChannel(DiamondOreMaskChannel),
                MossCaveMask = context.GetBoolMaskChannel(MossCaveMaskChannel),
                IceCaveMask = context.GetBoolMaskChannel(IceCaveMaskChannel),
                CrystalCaveMask = context.GetBoolMaskChannel(CrystalCaveMaskChannel),
                CaveHouseMask = context.GetBoolMaskChannel(CaveHouseMaskChannel),
                Rules = nativeRules,
                Width = context.Width,
                Height = context.Height,
                Seed = context.LocalSeed
            };

            JobHandle handle = job.Schedule(context.Width * context.Height, DefaultBatchSize, context.InputDependency);
            return nativeRules.Dispose(handle);
        }

        private FeatureRuleData[] BuildRules()
        {
            FeatureMaskSetRule[] rawRules = _parsedRules != null && _parsedRules.Rules != null ? _parsedRules.Rules : Array.Empty<FeatureMaskSetRule>();
            if (rawRules.Length == 0)
            {
                rawRules = new[]
                {
                    new FeatureMaskSetRule { Slot = 1, MinDepth = 0.28f, MaxDepth = 0.86f, Density = 0.11f, Scale = 0.055f, RequiresSolid = true, RequiresCave = false },
                    new FeatureMaskSetRule { Slot = 2, MinDepth = 0.36f, MaxDepth = 0.92f, Density = 0.085f, Scale = 0.050f, RequiresSolid = true, RequiresCave = false },
                    new FeatureMaskSetRule { Slot = 3, MinDepth = 0.50f, MaxDepth = 0.98f, Density = 0.060f, Scale = 0.045f, RequiresSolid = true, RequiresCave = false },
                    new FeatureMaskSetRule { Slot = 4, MinDepth = 0.66f, MaxDepth = 1.00f, Density = 0.038f, Scale = 0.040f, RequiresSolid = true, RequiresCave = false },
                    new FeatureMaskSetRule { Slot = 5, MinDepth = 0.34f, MaxDepth = 0.76f, Density = 0.32f, Scale = 0.040f, RequiresSolid = true, RequiresCave = true },
                    new FeatureMaskSetRule { Slot = 6, MinDepth = 0.30f, MaxDepth = 0.72f, Density = 0.22f, Scale = 0.038f, RequiresSolid = true, RequiresCave = true },
                    new FeatureMaskSetRule { Slot = 7, MinDepth = 0.58f, MaxDepth = 0.96f, Density = 0.28f, Scale = 0.035f, RequiresSolid = true, RequiresCave = true },
                    new FeatureMaskSetRule { Slot = 8, MinDepth = 0.40f, MaxDepth = 0.82f, Density = 0.020f, Scale = 0.060f, RequiresSolid = true, RequiresCave = true }
                };
            }

            List<FeatureRuleData> rules = new List<FeatureRuleData>(rawRules.Length);
            for (int index = 0; index < rawRules.Length; index++)
            {
                FeatureMaskSetRule rule = rawRules[index];
                if (rule == null || !rule.Enabled || rule.Slot < 1 || rule.Slot > 8)
                {
                    continue;
                }

                rules.Add(new FeatureRuleData
                {
                    Slot = rule.Slot,
                    MinDepth = math.saturate(rule.MinDepth),
                    MaxDepth = math.saturate(math.max(rule.MinDepth, rule.MaxDepth)),
                    Density = math.saturate(rule.Density),
                    Scale = math.max(0.005f, rule.Scale),
                    RequiresSolid = rule.RequiresSolid,
                    RequiresCave = rule.RequiresCave
                });
            }

            return rules.ToArray();
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_solidChannelName))
            {
                declarations.Add(new ChannelDeclaration(_solidChannelName, ChannelType.BoolMask, false));
            }

            if (!string.IsNullOrWhiteSpace(_caveChannelName))
            {
                declarations.Add(new ChannelDeclaration(_caveChannelName, ChannelType.BoolMask, false));
            }

            declarations.Add(new ChannelDeclaration(CoalOreMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(IronOreMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(GoldOreMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(DiamondOreMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(MossCaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(IceCaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(CrystalCaveMaskChannel, ChannelType.BoolMask, true));
            declarations.Add(new ChannelDeclaration(CaveHouseMaskChannel, ChannelType.BoolMask, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static FeatureMaskSetRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new FeatureMaskSetRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<FeatureMaskSetRuleSet>(rawJson) ?? new FeatureMaskSetRuleSet();
            }
            catch
            {
                return new FeatureMaskSetRuleSet();
            }
        }

        private struct FeatureRuleData
        {
            public int Slot;
            public float MinDepth;
            public float MaxDepth;
            public float Density;
            public float Scale;
            public bool RequiresSolid;
            public bool RequiresCave;
        }

        private struct FeatureMaskSetJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> SolidMask;
            [ReadOnly] public NativeArray<byte> CaveMask;
            [ReadOnly] public NativeArray<FeatureRuleData> Rules;
            public NativeArray<byte> CoalOreMask;
            public NativeArray<byte> IronOreMask;
            public NativeArray<byte> GoldOreMask;
            public NativeArray<byte> DiamondOreMask;
            public NativeArray<byte> MossCaveMask;
            public NativeArray<byte> IceCaveMask;
            public NativeArray<byte> CrystalCaveMask;
            public NativeArray<byte> CaveHouseMask;
            public int Width;
            public int Height;
            public long Seed;

            public void Execute(int index)
            {
                SetSlotValue(1, index, 0);
                SetSlotValue(2, index, 0);
                SetSlotValue(3, index, 0);
                SetSlotValue(4, index, 0);
                SetSlotValue(5, index, 0);
                SetSlotValue(6, index, 0);
                SetSlotValue(7, index, 0);
                SetSlotValue(8, index, 0);

                bool solid = SolidMask[index] != 0;
                bool cave = CaveMask[index] != 0;
                int x = index % Width;
                int y = index / Width;
                float yNorm = Height <= 1 ? 0.0f : y / (float)(Height - 1);
                float depthFromTop = 1.0f - yNorm;

                for (int ruleIndex = 0; ruleIndex < Rules.Length; ruleIndex++)
                {
                    FeatureRuleData rule = Rules[ruleIndex];
                    if (depthFromTop < rule.MinDepth || depthFromTop > rule.MaxDepth)
                    {
                        continue;
                    }

                    if (rule.RequiresSolid && !solid)
                    {
                        continue;
                    }

                    if (rule.RequiresCave != cave)
                    {
                        continue;
                    }

                    float blob = AuthoringNoise.ValueNoise2D(x * rule.Scale, y * rule.Scale, Seed + rule.Slot * 4099L);
                    float detail = AuthoringNoise.ValueNoise2D(x * rule.Scale * 2.7f, y * rule.Scale * 2.1f, Seed + rule.Slot * 8191L);
                    float score = blob * 0.78f + detail * 0.22f;
                    float threshold = 1.0f - rule.Density;
                    if (score >= threshold)
                    {
                        SetSlotValue(rule.Slot, index, 1);
                    }
                }
            }

            private void SetSlotValue(int slot, int index, byte value)
            {
                switch (slot)
                {
                    case 1:
                        CoalOreMask[index] = value;
                        break;
                    case 2:
                        IronOreMask[index] = value;
                        break;
                    case 3:
                        GoldOreMask[index] = value;
                        break;
                    case 4:
                        DiamondOreMask[index] = value;
                        break;
                    case 5:
                        MossCaveMask[index] = value;
                        break;
                    case 6:
                        IceCaveMask[index] = value;
                        break;
                    case 7:
                        CrystalCaveMask[index] = value;
                        break;
                    case 8:
                        CaveHouseMask[index] = value;
                        break;
                }
            }
        }
    }

    [NodeCategory("Authoring")]
    [NodeDisplayName("Biome Region Stack")]
    [Description("Builds a broad region biome channel, then applies ordered cave and ore visual overrides from feature masks.")]
    public sealed class BiomeRegionStackNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IBiomeChannelNode
    {
        private const int DefaultBatchSize = 64;
        private const string ForestBiomeParameter = "forestBiome";
        private const string JungleBiomeParameter = "jungleBiome";
        private const string DesertBiomeParameter = "desertBiome";
        private const string IceBiomeParameter = "iceBiome";
        private const string MossCaveBiomeParameter = "mossCaveBiome";
        private const string IceCaveBiomeParameter = "iceCaveBiome";
        private const string CrystalCaveBiomeParameter = "crystalCaveBiome";
        private const string CoalOreBiomeParameter = "coalOreBiome";
        private const string IronOreBiomeParameter = "ironOreBiome";
        private const string GoldOreBiomeParameter = "goldOreBiome";
        private const string DiamondOreBiomeParameter = "diamondOreBiome";
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly NodePortDefinition[] _ports;
        private string _mossMaskChannel;
        private string _iceMaskChannel;
        private string _crystalMaskChannel;
        private string _coalMaskChannel;
        private string _ironMaskChannel;
        private string _goldMaskChannel;
        private string _diamondMaskChannel;
        private string _forestBiome;
        private string _jungleBiome;
        private string _desertBiome;
        private string _iceBiome;
        private string _mossCaveBiome;
        private string _iceCaveBiome;
        private string _crystalCaveBiome;
        private string _coalOreBiome;
        private string _ironOreBiome;
        private string _goldOreBiome;
        private string _diamondOreBiome;
        private int _forestIndex;
        private int _jungleIndex;
        private int _desertIndex;
        private int _iceIndex;
        private int _mossIndex;
        private int _iceCaveIndex;
        private int _crystalIndex;
        private int _coalIndex;
        private int _ironIndex;
        private int _goldIndex;
        private int _diamondIndex;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public BiomeRegionStackNode(string nodeId, string nodeName)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "Biome Region Stack" : nodeName;
            _ports = new[]
            {
                new NodePortDefinition(FeatureMaskSetNode.MossCaveMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.IceCaveMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.CrystalCaveMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.CoalOreMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.IronOreMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.GoldOreMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(FeatureMaskSetNode.DiamondOreMaskChannel, PortDirection.Input, ChannelType.BoolMask),
                new NodePortDefinition(BiomeChannelUtility.ChannelName, PortDirection.Output, ChannelType.Int)
            };
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _mossMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.MossCaveMaskChannel) : string.Empty;
            _iceMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.IceCaveMaskChannel) : string.Empty;
            _crystalMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.CrystalCaveMaskChannel) : string.Empty;
            _coalMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.CoalOreMaskChannel) : string.Empty;
            _ironMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.IronOreMaskChannel) : string.Empty;
            _goldMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.GoldOreMaskChannel) : string.Empty;
            _diamondMaskChannel = inputConnections != null ? inputConnections.FirstOrDefault(FeatureMaskSetNode.DiamondOreMaskChannel) : string.Empty;
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, ForestBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _forestBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, JungleBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _jungleBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, DesertBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _desertBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, IceBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _iceBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, MossCaveBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _mossCaveBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, IceCaveBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _iceCaveBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, CrystalCaveBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _crystalCaveBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, CoalOreBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _coalOreBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, IronOreBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _ironOreBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, GoldOreBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _goldOreBiome = value ?? string.Empty;
            }
            else if (string.Equals(name, DiamondOreBiomeParameter, StringComparison.OrdinalIgnoreCase))
            {
                _diamondOreBiome = value ?? string.Empty;
            }
        }

        public bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage)
        {
            if (!ResolveRequiredBiome(palette, _forestBiome, "forest", out _forestIndex, out errorMessage) ||
                !ResolveRequiredBiome(palette, _jungleBiome, "jungle", out _jungleIndex, out errorMessage) ||
                !ResolveRequiredBiome(palette, _desertBiome, "desert", out _desertIndex, out errorMessage) ||
                !ResolveRequiredBiome(palette, _iceBiome, "ice", out _iceIndex, out errorMessage))
            {
                return false;
            }

            _mossIndex = ResolveOptionalBiome(palette, _mossCaveBiome, _forestIndex, out errorMessage);
            if (errorMessage != null) return false;
            _iceCaveIndex = ResolveOptionalBiome(palette, _iceCaveBiome, _iceIndex, out errorMessage);
            if (errorMessage != null) return false;
            _crystalIndex = ResolveOptionalBiome(palette, _crystalCaveBiome, _forestIndex, out errorMessage);
            if (errorMessage != null) return false;
            _coalIndex = ResolveOptionalBiome(palette, _coalOreBiome, _forestIndex, out errorMessage);
            if (errorMessage != null) return false;
            _ironIndex = ResolveOptionalBiome(palette, _ironOreBiome, _forestIndex, out errorMessage);
            if (errorMessage != null) return false;
            _goldIndex = ResolveOptionalBiome(palette, _goldOreBiome, _desertIndex, out errorMessage);
            if (errorMessage != null) return false;
            _diamondIndex = ResolveOptionalBiome(palette, _diamondOreBiome, _crystalIndex, out errorMessage);
            return errorMessage == null;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            BiomeRegionStackJob job = new BiomeRegionStackJob
            {
                Output = context.GetIntChannel(BiomeChannelUtility.ChannelName),
                MossMask = GetOptionalMask(context, _mossMaskChannel),
                IceMask = GetOptionalMask(context, _iceMaskChannel),
                CrystalMask = GetOptionalMask(context, _crystalMaskChannel),
                CoalMask = GetOptionalMask(context, _coalMaskChannel),
                IronMask = GetOptionalMask(context, _ironMaskChannel),
                GoldMask = GetOptionalMask(context, _goldMaskChannel),
                DiamondMask = GetOptionalMask(context, _diamondMaskChannel),
                Width = context.Width,
                Height = context.Height,
                Seed = context.LocalSeed,
                ForestIndex = _forestIndex,
                JungleIndex = _jungleIndex,
                DesertIndex = _desertIndex,
                IceIndex = _iceIndex,
                MossIndex = _mossIndex,
                IceCaveIndex = _iceCaveIndex,
                CrystalIndex = _crystalIndex,
                CoalIndex = _coalIndex,
                IronIndex = _ironIndex,
                GoldIndex = _goldIndex,
                DiamondIndex = _diamondIndex
            };

            return job.Schedule(context.Width * context.Height, DefaultBatchSize, context.InputDependency);
        }

        private static NativeArray<byte> GetOptionalMask(NodeExecutionContext context, string channelName)
        {
            return string.IsNullOrWhiteSpace(channelName) ? default : context.GetBoolMaskChannel(channelName);
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            AddReadDeclaration(declarations, _mossMaskChannel);
            AddReadDeclaration(declarations, _iceMaskChannel);
            AddReadDeclaration(declarations, _crystalMaskChannel);
            AddReadDeclaration(declarations, _coalMaskChannel);
            AddReadDeclaration(declarations, _ironMaskChannel);
            AddReadDeclaration(declarations, _goldMaskChannel);
            AddReadDeclaration(declarations, _diamondMaskChannel);
            declarations.Add(new ChannelDeclaration(BiomeChannelUtility.ChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static void AddReadDeclaration(List<ChannelDeclaration> declarations, string channelName)
        {
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                declarations.Add(new ChannelDeclaration(channelName, ChannelType.BoolMask, false));
            }
        }

        private bool ResolveRequiredBiome(BiomeChannelPalette palette, string guid, string label, out int index, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                index = 0;
                errorMessage = "Biome Region Stack node '" + _nodeName + "' requires a " + label + " biome GUID.";
                return false;
            }

            if (!palette.TryResolveIndex(guid, out index, out errorMessage))
            {
                errorMessage = "Biome Region Stack node '" + _nodeName + "' could not resolve " + label + " biome: " + errorMessage;
                return false;
            }

            return true;
        }

        private static int ResolveOptionalBiome(BiomeChannelPalette palette, string guid, int fallbackIndex, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(guid))
            {
                return fallbackIndex;
            }

            int index;
            if (!palette.TryResolveIndex(guid, out index, out errorMessage))
            {
                return fallbackIndex;
            }

            return index;
        }

        private struct BiomeRegionStackJob : IJobParallelFor
        {
            public NativeArray<int> Output;
            [ReadOnly] public NativeArray<byte> MossMask;
            [ReadOnly] public NativeArray<byte> IceMask;
            [ReadOnly] public NativeArray<byte> CrystalMask;
            [ReadOnly] public NativeArray<byte> CoalMask;
            [ReadOnly] public NativeArray<byte> IronMask;
            [ReadOnly] public NativeArray<byte> GoldMask;
            [ReadOnly] public NativeArray<byte> DiamondMask;
            public int Width;
            public int Height;
            public long Seed;
            public int ForestIndex;
            public int JungleIndex;
            public int DesertIndex;
            public int IceIndex;
            public int MossIndex;
            public int IceCaveIndex;
            public int CrystalIndex;
            public int CoalIndex;
            public int IronIndex;
            public int GoldIndex;
            public int DiamondIndex;

            public void Execute(int index)
            {
                int x = index % Width;
                float xNorm = Width <= 1 ? 0.0f : x / (float)(Width - 1);
                float wobble = (AuthoringNoise.ValueNoise1D(x * 0.018f, Seed + 331L) - 0.5f) * 0.06f;
                float region = math.saturate(xNorm + wobble);
                int biome = ForestIndex;
                if (region > 0.73f)
                {
                    biome = IceIndex;
                }
                else if (region > 0.50f)
                {
                    biome = DesertIndex;
                }
                else if (region > 0.28f)
                {
                    biome = JungleIndex;
                }

                if (IsMaskSet(MossMask, index))
                {
                    biome = MossIndex;
                }

                if (IsMaskSet(IceMask, index))
                {
                    biome = IceCaveIndex;
                }

                if (IsMaskSet(CrystalMask, index))
                {
                    biome = CrystalIndex;
                }

                if (IsMaskSet(CoalMask, index))
                {
                    biome = CoalIndex;
                }
                else if (IsMaskSet(IronMask, index))
                {
                    biome = IronIndex;
                }
                else if (IsMaskSet(GoldMask, index))
                {
                    biome = GoldIndex;
                }
                else if (IsMaskSet(DiamondMask, index))
                {
                    biome = DiamondIndex;
                }

                Output[index] = biome;
            }

            private static bool IsMaskSet(NativeArray<byte> mask, int index)
            {
                return mask.IsCreated && mask[index] != 0;
            }
        }
    }

    [NodeCategory("Authoring")]
    [NodeDisplayName("Material Rule Stack")]
    [Description("Applies ordered logical ID material rules from a multi-input mask stack.")]
    public sealed class MaterialRuleStackNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string BasePortName = "Base";
        private const string MasksPortName = "Masks";
        private const string OutputChannelName = "FinalLogicalIds";
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _inputBaseChannelName;
        private string[] _inputMaskChannelNames;
        private string _rules;
        private LogicalIdRule[] _resolvedRules;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public MaterialRuleStackNode(string nodeId, string nodeName, string rules = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? "Material Rule Stack" : nodeName;
            _inputBaseChannelName = string.Empty;
            _inputMaskChannelNames = Array.Empty<string>();
            _rules = rules ?? string.Empty;
            _resolvedRules = ResolveRules(ParseRules(_rules));
            _ports = new[]
            {
                new NodePortDefinition(BasePortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, false, "Masks are referenced by one-based Mask Slot."),
                new NodePortDefinition(OutputChannelName, PortDirection.Output, ChannelType.Int)
            };
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBaseChannelName = inputConnections != null ? inputConnections.FirstOrDefault(BasePortName) : string.Empty;
            IReadOnlyList<string> masks = inputConnections != null ? inputConnections.GetAll(MasksPortName) : Array.Empty<string>();
            _inputMaskChannelNames = new string[masks.Count];
            for (int index = 0; index < masks.Count; index++)
            {
                _inputMaskChannelNames[index] = masks[index] ?? string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase))
            {
                _rules = value ?? string.Empty;
                _resolvedRules = ResolveRules(ParseRules(_rules));
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> baseIds = context.GetIntChannel(_inputBaseChannelName);
            NativeArray<int> output = context.GetIntChannel(OutputChannelName);
            CopyIdsJob copy = new CopyIdsJob { Input = baseIds, Output = output };
            JobHandle dependency = copy.Schedule(output.Length, DefaultBatchSize, context.InputDependency);

            for (int index = 0; index < _resolvedRules.Length; index++)
            {
                LogicalIdRule rule = _resolvedRules[index];
                bool hasMask = rule.MaskSlot > 0 &&
                    _inputMaskChannelNames != null &&
                    rule.MaskSlot <= _inputMaskChannelNames.Length &&
                    !string.IsNullOrWhiteSpace(_inputMaskChannelNames[rule.MaskSlot - 1]);

                NativeArray<byte> mask = hasMask ? context.GetBoolMaskChannel(_inputMaskChannelNames[rule.MaskSlot - 1]) : default;
                ApplyMaterialRuleJob job = new ApplyMaterialRuleJob
                {
                    Output = output,
                    Mask = mask,
                    HasMask = hasMask,
                    SourceLogicalId = rule.SourceLogicalId,
                    TargetLogicalId = rule.TargetLogicalId
                };
                dependency = job.Schedule(output.Length, DefaultBatchSize, dependency);
            }

            return dependency;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (!string.IsNullOrWhiteSpace(_inputBaseChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBaseChannelName, ChannelType.Int, false));
            }

            if (_inputMaskChannelNames != null)
            {
                for (int index = 0; index < _inputMaskChannelNames.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(_inputMaskChannelNames[index]))
                    {
                        declarations.Add(new ChannelDeclaration(_inputMaskChannelNames[index], ChannelType.BoolMask, false));
                    }
                }
            }

            declarations.Add(new ChannelDeclaration(OutputChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        private static LogicalIdRuleSet ParseRules(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new LogicalIdRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<LogicalIdRuleSet>(rawJson) ?? new LogicalIdRuleSet();
            }
            catch
            {
                return new LogicalIdRuleSet();
            }
        }

        private static LogicalIdRule[] ResolveRules(LogicalIdRuleSet ruleSet)
        {
            LogicalIdRule[] rawRules = ruleSet != null && ruleSet.Rules != null ? ruleSet.Rules : Array.Empty<LogicalIdRule>();
            List<LogicalIdRule> rules = new List<LogicalIdRule>(rawRules.Length);
            for (int index = 0; index < rawRules.Length; index++)
            {
                LogicalIdRule rule = rawRules[index];
                if (rule == null || !rule.Enabled || rule.TargetLogicalId < 0)
                {
                    continue;
                }

                rules.Add(rule);
            }

            return rules.ToArray();
        }

        private struct CopyIdsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> Input;
            public NativeArray<int> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }

        private struct ApplyMaterialRuleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> Mask;
            public NativeArray<int> Output;
            public bool HasMask;
            public int SourceLogicalId;
            public int TargetLogicalId;

            public void Execute(int index)
            {
                if (HasMask && Mask[index] == 0)
                {
                    return;
                }

                int current = Output[index];
                if (SourceLogicalId >= 0 && current != SourceLogicalId)
                {
                    return;
                }

                Output[index] = TargetLogicalId;
            }
        }
    }

    internal static class AuthoringNoise
    {
        public static uint Hash(uint x, uint seed)
        {
            uint h = x ^ (seed * 0x9E3779B9u);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }

        public static uint Hash(uint x, uint y, uint seed)
        {
            return Hash(x ^ (y * 0x85EBCA6Bu), seed + 0xC2B2AE35u);
        }

        public static float Hash01(uint x, uint seed)
        {
            return Hash(x, seed) / 4294967295.0f;
        }

        public static float Hash01(uint x, uint y, uint seed)
        {
            return Hash(x, y, seed) / 4294967295.0f;
        }

        public static float ValueNoise1D(float x, long seed)
        {
            int x0 = (int)math.floor(x);
            int x1 = x0 + 1;
            float t = Smooth(x - x0);
            float a = Hash01((uint)x0, unchecked((uint)seed));
            float b = Hash01((uint)x1, unchecked((uint)seed));
            return math.lerp(a, b, t);
        }

        public static float ValueNoise2D(float x, float y, long seed)
        {
            int x0 = (int)math.floor(x);
            int y0 = (int)math.floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float tx = Smooth(x - x0);
            float ty = Smooth(y - y0);
            uint safeSeed = unchecked((uint)seed);
            float a = Hash01((uint)x0, (uint)y0, safeSeed);
            float b = Hash01((uint)x1, (uint)y0, safeSeed);
            float c = Hash01((uint)x0, (uint)y1, safeSeed);
            float d = Hash01((uint)x1, (uint)y1, safeSeed);
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);
        }

        private static float Smooth(float t)
        {
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
