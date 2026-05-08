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
    [NodeCategory("Filter")]
    [NodeDisplayName("Step")]
    [Description("Quantises float values to evenly spaced step boundaries between 0 and 1.")]
    [HelpURL("https://dynamicdungeon.mrbytesized.com/docs/nodes/filter-transform/step")]
    public sealed class StepNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const int MinimumSteps = 2;
        private const string DefaultNodeName = "Step";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputChannelName;
        [MinValue(MinimumSteps)]
        [Description("Number of discrete step boundaries used across the 0-1 range.")]
        private int _steps;
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

        public StepNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, int steps = MinimumSteps)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _steps = math.max(MinimumSteps, steps);

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(InputPortName, out inputChannelName))
            {
                _inputChannelName = inputChannelName ?? string.Empty;
            }
            else
            {
                _inputChannelName = string.Empty;
            }

            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "steps", StringComparison.OrdinalIgnoreCase))
            {
                int parsedSteps;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSteps))
                {
                    _steps = math.max(MinimumSteps, parsedSteps);
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
            NativeArray<float> input = context.GetFloatChannel(_inputChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            StepJob job = new StepJob
            {
                Input = input,
                Output = output,
                Steps = _steps
            };

            return job.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.Float, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.Float, true)
            };
        }

        [BurstCompile]
        private struct StepJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> Input;

            public NativeArray<float> Output;
            public int Steps;

            public void Execute(int index)
            {
                float denominator = (float)(Steps - 1);
                Output[index] = math.round(math.saturate(Input[index]) * denominator) / denominator;
            }
        }
    }
}
