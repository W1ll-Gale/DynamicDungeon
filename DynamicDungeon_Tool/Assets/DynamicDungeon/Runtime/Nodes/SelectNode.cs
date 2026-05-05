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

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Select")]
    [Description("Selects one of up to four float channels per tile according to threshold bands on a control input.")]
    public sealed class SelectNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Select";
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
        [Description("Control values below this threshold select channel A; values at or above move to the next band.")]
        private float _thresholdAB;
        [Description("Control values below this threshold select channel B; values at or above move to the next band.")]
        private float _thresholdBC;
        [Description("Control values below this threshold select channel C; values at or above move to channel D.")]
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

        public SelectNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string inputCChannelName, string inputDChannelName, string controlChannelName, string outputChannelName, float thresholdAB = 0.25f, float thresholdBC = 0.5f, float thresholdCD = 0.75f)
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
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(InputCPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(InputDPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(ControlPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float, displayName: outputPortDisplayName)
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
            NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
            bool hasInputB = !string.IsNullOrWhiteSpace(_inputBChannelName);
            bool hasInputC = !string.IsNullOrWhiteSpace(_inputCChannelName);
            bool hasInputD = !string.IsNullOrWhiteSpace(_inputDChannelName);
            NativeArray<float> inputB = hasInputB ? context.GetFloatChannel(_inputBChannelName) : inputA;
            NativeArray<float> inputC = hasInputC ? context.GetFloatChannel(_inputCChannelName) : inputA;
            NativeArray<float> inputD = hasInputD ? context.GetFloatChannel(_inputDChannelName) : inputA;
            NativeArray<float> control = context.GetFloatChannel(_controlChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);
            float thresholdAB = _thresholdAB;
            float thresholdBC = math.max(_thresholdAB, _thresholdBC);
            float thresholdCD = math.max(thresholdBC, _thresholdCD);
            SelectJob job = new SelectJob
            {
                InputA = inputA,
                InputB = inputB,
                InputC = inputC,
                InputD = inputD,
                Control = control,
                Output = output,
                HasInputB = hasInputB,
                HasInputC = hasInputC,
                HasInputD = hasInputD,
                ThresholdAB = thresholdAB,
                ThresholdBC = thresholdBC,
                ThresholdCD = thresholdCD
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
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(6);

            if (!string.IsNullOrWhiteSpace(_inputAChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputCChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputCChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputDChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputDChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_controlChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_controlChannelName, ChannelType.Float, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct SelectJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            [ReadOnly]
            public NativeArray<float> InputC;

            [ReadOnly]
            public NativeArray<float> InputD;

            [ReadOnly]
            public NativeArray<float> Control;

            public NativeArray<float> Output;
            public bool HasInputB;
            public bool HasInputC;
            public bool HasInputD;
            public float ThresholdAB;
            public float ThresholdBC;
            public float ThresholdCD;

            public void Execute(int index)
            {
                float controlValue = Control[index];
                float selectedValue = InputA[index];

                if (controlValue >= ThresholdAB)
                {
                    selectedValue = HasInputB ? InputB[index] : 0.0f;
                }

                if (controlValue >= ThresholdBC && HasInputC)
                {
                    selectedValue = InputC[index];
                }

                if (controlValue >= ThresholdCD && HasInputD)
                {
                    selectedValue = InputD[index];
                }

                Output[index] = selectedValue;
            }
        }
    }
}
