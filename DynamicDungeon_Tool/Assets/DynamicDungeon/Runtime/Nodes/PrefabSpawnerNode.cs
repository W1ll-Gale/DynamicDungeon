using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [Description("Places a single prefab at each specified point without modifying footprints or logical IDs.")]
    [DynamicDungeon.Runtime.Core.NodeCategory("Placement")]
    public sealed class PrefabSpawnerNode : IGenNode, IInputConnectionReceiver, IParameterReceiver, IPrefabPlacementNode
    {
        private const string DefaultNodeName = "Prefab Spawner";
        private const string PointsPortName = "Points";
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        private string _inputPointsChannelName;

        [InspectorName("Prefab Variants")]
        [DescriptionAttribute("Prefab variants and relative weights encoded as JSON.")]
        private string _prefabVariants;



        private PrefabStampTemplate[] _resolvedTemplates = Array.Empty<PrefabStampTemplate>();
        private int[] _resolvedTemplateIndices = Array.Empty<int>();
        private float[] _resolvedTemplateWeights = Array.Empty<float>();
        private bool _prefabResolutionFailed;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public PrefabSpawnerNode(
            string nodeId,
            string nodeName,
            string inputPointsChannelName = "",
            string prefabVariants = "")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputPointsChannelName = inputPointsChannelName ?? string.Empty;
            _prefabVariants = prefabVariants ?? string.Empty;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            if (inputConnections != null && inputConnections.TryGetValue(PointsPortName, out string inputChannelName))
            {
                _inputPointsChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputPointsChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "prefabVariants", StringComparison.OrdinalIgnoreCase))
            {
                _prefabVariants = value ?? string.Empty;
                ClearResolvedPrefabVariants();
                RefreshChannelDeclarations();
                return;
            }
        }

        public bool ResolvePrefabPalette(PrefabStampPalette palette, out string errorMessage)
        {
            errorMessage = null;
            ClearResolvedPrefabVariants();

            PrefabStampVariant[] variants = GetConfiguredVariants();
            if (variants.Length == 0)
            {
                return true;
            }

            if (palette == null)
            {
                errorMessage = "Prefab stamp palette is missing.";
                _prefabResolutionFailed = true;
                return false;
            }

            List<PrefabStampTemplate> resolvedTemplates = new List<PrefabStampTemplate>(variants.Length);
            List<int> resolvedTemplateIndices = new List<int>(variants.Length);
            List<float> resolvedTemplateWeights = new List<float>(variants.Length);

            for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                PrefabStampVariant variant = variants[variantIndex];
                if (!TryResolvePrefabVariant(palette, variant, variantIndex, resolvedTemplates, resolvedTemplateIndices, resolvedTemplateWeights, out errorMessage))
                {
                    _prefabResolutionFailed = true;
                    return false;
                }
            }

            _resolvedTemplates = resolvedTemplates.ToArray();
            _resolvedTemplateIndices = resolvedTemplateIndices.ToArray();
            _resolvedTemplateWeights = resolvedTemplateWeights.ToArray();
            return true;
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            if (!HasConfiguredPrefab() || string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                return context.InputDependency;
            }

            if (_resolvedTemplates == null || _resolvedTemplates.Length == 0)
            {
                string warningMessage = _prefabResolutionFailed
                    ? "Prefab Spawner could not resolve a prefab variant."
                    : "Prefab Spawner has no prefab variants with a positive weight.";
                ManagedBlackboardDiagnosticUtility.AppendWarning(context.ManagedBlackboard, warningMessage, _nodeId, PointsPortName);
                return context.InputDependency;
            }

            NativeList<int2> placements = context.GetPointListChannel(_inputPointsChannelName);
            NativeList<PrefabPlacementRecord> placementChannel = context.GetPrefabPlacementListChannel(PrefabPlacementChannelUtility.ChannelName);

            NativeArray<int> templateIndices = new NativeArray<int>(_resolvedTemplateIndices, Allocator.TempJob);

            PrefabSpawnJob job = new PrefabSpawnJob
            {
                Placements = placements,
                PlacementChannel = placementChannel,
                TemplateIndices = templateIndices,
                LocalSeed = context.LocalSeed
            };

            JobHandle jobHandle = job.Schedule(context.InputDependency);
            return templateIndices.Dispose(jobHandle);
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(2);

            if (!string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputPointsChannelName, ChannelType.PointList, false));
            }

            if (HasConfiguredPrefab() && !string.IsNullOrWhiteSpace(_inputPointsChannelName))
            {
                declarations.Add(new ChannelDeclaration(PrefabPlacementChannelUtility.ChannelName, ChannelType.PrefabPlacementList, true));
            }

            _channelDeclarations = declarations.ToArray();
        }

        private void RefreshPorts()
        {
            _ports = new[]
            {
                new NodePortDefinition(PointsPortName, PortDirection.Input, ChannelType.PointList, PortCapacity.Single, false),
                new NodePortDefinition(PrefabPlacementChannelUtility.ChannelName, PortDirection.Output, ChannelType.PrefabPlacementList)
            };
        }

        private bool HasConfiguredPrefab()
        {
            return GetConfiguredVariants().Length > 0;
        }

        private void ClearResolvedPrefabVariants()
        {
            _resolvedTemplates = Array.Empty<PrefabStampTemplate>();
            _resolvedTemplateIndices = Array.Empty<int>();
            _resolvedTemplateWeights = Array.Empty<float>();
            _prefabResolutionFailed = false;
        }

        private static float SanitizeWeight(float weight)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight)) return 0.0f;
            return math.max(0.0f, weight);
        }

        private static bool TryResolvePrefabVariant(
            PrefabStampPalette palette,
            PrefabStampVariant variant,
            int variantIndex,
            List<PrefabStampTemplate> templates,
            List<int> templateIndices,
            List<float> weights,
            out string errorMessage)
        {
            errorMessage = null;

            if (variant == null || string.IsNullOrWhiteSpace(variant.Prefab) || SanitizeWeight(variant.Weight) <= 0.0f)
            {
                return true;
            }

            if (!palette.TryResolve(variant.Prefab, out int templateIndex, out PrefabStampTemplate template, out errorMessage))
            {
                errorMessage = "Prefab variant " + (variantIndex + 1).ToString(CultureInfo.InvariantCulture) + " could not be resolved: " + errorMessage;
                return false;
            }

            templates.Add(template);
            templateIndices.Add(templateIndex);
            weights.Add(SanitizeWeight(variant.Weight));
            return true;
        }

        private PrefabStampVariant[] GetConfiguredVariants()
        {
            PrefabStampVariantSet variantSet = ParseVariantSet(_prefabVariants);
            return variantSet != null && variantSet.Variants != null
                ? variantSet.Variants
                : Array.Empty<PrefabStampVariant>();
        }

        private static PrefabStampVariantSet ParseVariantSet(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new PrefabStampVariantSet();
            try { return JsonUtility.FromJson<PrefabStampVariantSet>(value) ?? new PrefabStampVariantSet(); }
            catch { return new PrefabStampVariantSet(); }
        }

        [BurstCompile]
        private struct PrefabSpawnJob : IJob
        {
            [ReadOnly]
            public NativeList<int2> Placements;

            public NativeList<PrefabPlacementRecord> PlacementChannel;

            [ReadOnly]
            public NativeArray<int> TemplateIndices;

            public long LocalSeed;

            public void Execute()
            {
                if (TemplateIndices.Length == 0)
                {
                    return;
                }

                // For simplicity, just pick the first variant.
                // Could use LocalSeed to pick randomly based on weights if needed.
                int templateIndex = TemplateIndices[0];

                for (int i = 0; i < Placements.Length; i++)
                {
                    int2 point = Placements[i];
                    PlacementChannel.Add(new PrefabPlacementRecord(templateIndex, point.x, point.y, 0, false, false));
                }
            }
        }
    }
}
