using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Select Mask")]
    [Description("Selects between boolean mask inputs A, B, C, and D based on a control float channel and defined thresholds.")]
    public sealed class SelectMaskNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Select Mask";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string InputCPortName = "C";
        private const string InputDPortName = "D";
        private const string ControlPortName = "Control";
        private const string FallbackOutputPortName = "Output";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        private string _inputCChannelName;
        private string _inputDChannelName;
        private string _controlChannelName;

        private float _thresholdAB;
        private float _thresholdBC;
        private float _thresholdCD;
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

        public SelectMaskNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string inputCChannelName = "", string inputDChannelName = "", string controlChannelName = "", string outputChannelName = "", float thresholdAB = 0.25f, float thresholdBC = 0.5f, float thresholdCD = 0.75f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _inputCChannelName = inputCChannelName ?? string.Empty;
            _inputDChannelName = inputDChannelName ?? string.Empty;
            _controlChannelName = controlChannelName ?? string.Empty;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);

            _thresholdAB = thresholdAB;
            _thresholdBC = thresholdBC;
            _thresholdCD = thresholdCD;

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(InputCPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(InputDPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(ControlPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            _inputAChannelName = ResolveInputConnection(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputConnection(inputConnections, InputBPortName);
            _inputCChannelName = ResolveInputConnection(inputConnections, InputCPortName);
            _inputDChannelName = ResolveInputConnection(inputConnections, InputDPortName);
            _controlChannelName = ResolveInputConnection(inputConnections, ControlPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            float parsedThreshold;
            if (string.Equals(name, "thresholdAB", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedThreshold))
                {
                    _thresholdAB = parsedThreshold;
                }

                return;
            }

            if (string.Equals(name, "thresholdBC", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedThreshold))
                {
                    _thresholdBC = parsedThreshold;
                }

                return;
            }

            if (string.Equals(name, "thresholdCD", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedThreshold))
                {
                    _thresholdCD = parsedThreshold;
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
            NativeArray<byte> inputA = context.GetBoolMaskChannel(_inputAChannelName);
            NativeArray<byte> inputB = !string.IsNullOrWhiteSpace(_inputBChannelName) ? context.GetBoolMaskChannel(_inputBChannelName) : inputA;
            NativeArray<byte> inputC = !string.IsNullOrWhiteSpace(_inputCChannelName) ? context.GetBoolMaskChannel(_inputCChannelName) : inputB;
            NativeArray<byte> inputD = !string.IsNullOrWhiteSpace(_inputDChannelName) ? context.GetBoolMaskChannel(_inputDChannelName) : inputC;
            NativeArray<float> control = context.GetFloatChannel(_controlChannelName);
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);

            SelectMaskJob job = new SelectMaskJob
            {
                InputA = inputA,
                InputB = inputB,
                InputC = inputC,
                InputD = inputD,
                Control = control,
                Output = output,
                ThresholdAB = _thresholdAB,
                ThresholdBC = _thresholdBC,
                ThresholdCD = _thresholdCD
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
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>(6);
            if (!string.IsNullOrWhiteSpace(_inputAChannelName)) declarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.BoolMask, false));
            if (!string.IsNullOrWhiteSpace(_inputBChannelName)) declarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.BoolMask, false));
            if (!string.IsNullOrWhiteSpace(_inputCChannelName)) declarations.Add(new ChannelDeclaration(_inputCChannelName, ChannelType.BoolMask, false));
            if (!string.IsNullOrWhiteSpace(_inputDChannelName)) declarations.Add(new ChannelDeclaration(_inputDChannelName, ChannelType.BoolMask, false));
            if (!string.IsNullOrWhiteSpace(_controlChannelName)) declarations.Add(new ChannelDeclaration(_controlChannelName, ChannelType.Float, false));

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct SelectMaskJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<byte> InputA;
            [ReadOnly] public NativeArray<byte> InputB;
            [ReadOnly] public NativeArray<byte> InputC;
            [ReadOnly] public NativeArray<byte> InputD;
            [ReadOnly] public NativeArray<float> Control;
            public NativeArray<byte> Output;

            public float ThresholdAB;
            public float ThresholdBC;
            public float ThresholdCD;

            public void Execute(int index)
            {
                float controlValue = Control[index];

                if (controlValue < ThresholdAB)
                {
                    Output[index] = InputA[index];
                }
                else if (controlValue < ThresholdBC)
                {
                    Output[index] = InputB[index];
                }
                else if (controlValue < ThresholdCD)
                {
                    Output[index] = InputC[index];
                }
                else
                {
                    Output[index] = InputD[index];
                }
            }
        }
    }
}
