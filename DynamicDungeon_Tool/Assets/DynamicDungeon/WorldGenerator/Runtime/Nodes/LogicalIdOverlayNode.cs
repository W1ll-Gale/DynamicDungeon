using System;
using System.Collections.Generic;
using System.Globalization;
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
    [NodeCategory("Output")]
    [NodeDisplayName("Logical ID Overlay")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/logical-id/logicalidoverlay")]
    [Description("Writes one logical ID anywhere a bool mask is true, while preserving the base logical IDs elsewhere.")]
    public sealed class LogicalIdOverlayNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Logical ID Overlay";
        private const string BasePortName = "Base";
        private const string MaskPortName = "Mask";
        private const string DefaultOutputChannelName = GraphOutputUtility.OutputInputPortName;
        private const string PreferredOutputDisplayName = "Logical IDs";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputBaseChannelName;
        private string _inputMaskChannelName;

        [MinValue(0.0f)]
        [DescriptionAttribute("Logical ID written anywhere the mask is true.")]
        private int _overlayLogicalId;

        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public LogicalIdOverlayNode(string nodeId, string nodeName, string inputBaseChannelName, string inputMaskChannelName, string outputChannelName, int overlayLogicalId = 1)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputBaseChannelName = inputBaseChannelName ?? string.Empty;
            _inputMaskChannelName = inputMaskChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, PreferredOutputDisplayName);
            _overlayLogicalId = math.max(0, overlayLogicalId);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(BasePortName, PortDirection.Input, ChannelType.Int, PortCapacity.Single, true),
                new NodePortDefinition(MaskPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Int, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputBaseChannelName = ResolveInputConnection(inputConnections, BasePortName);
            _inputMaskChannelName = ResolveInputConnection(inputConnections, MaskPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "overlayLogicalId", StringComparison.OrdinalIgnoreCase))
            {
                int parsedValue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
                {
                    _overlayLogicalId = math.max(0, parsedValue);
                }

                return;
            }

            if (string.Equals(name, "outputChannelName", StringComparison.OrdinalIgnoreCase))
            {
                _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(_nodeId, value, DefaultOutputChannelName);
                RefreshPorts();
                RefreshChannelDeclarations();
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<int> baseIds = context.GetIntChannel(_inputBaseChannelName);
            NativeArray<byte> mask = context.GetBoolMaskChannel(_inputMaskChannelName);
            NativeArray<int> output = context.GetIntChannel(_outputChannelName);

            LogicalIdOverlayJob job = new LogicalIdOverlayJob
            {
                BaseIds = baseIds,
                Mask = mask,
                Output = output,
                OverlayLogicalId = _overlayLogicalId
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private static string ResolveInputConnection(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(3);

            if (!string.IsNullOrWhiteSpace(_inputBaseChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputBaseChannelName, ChannelType.Int, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputMaskChannelName))
            {
                declarations.Add(new ChannelDeclaration(_inputMaskChannelName, ChannelType.BoolMask, false));
            }

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Int, true));
            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct LogicalIdOverlayJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> BaseIds;

            [ReadOnly]
            public NativeArray<byte> Mask;

            public NativeArray<int> Output;
            public int OverlayLogicalId;

            public void Execute(int index)
            {
                Output[index] = Mask[index] != 0 ? OverlayLogicalId : BaseIds[index];
            }
        }
    }
}
