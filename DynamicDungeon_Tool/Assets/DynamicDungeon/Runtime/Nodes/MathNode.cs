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
    [NodeCategory("Math")]
    [NodeDisplayName("Math")]
    [Description("Applies a math operation to float input A with either float input B or a fallback scalar.")]
    public sealed class MathNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Math";
        private const string InputAPortName = "A";
        private const string InputBPortName = "B";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputAChannelName;
        private string _inputBChannelName;
        [Description("Fallback numeric value used when input B is not connected.")]
        private float _scalarB;
        [Description("Math operation applied to input A and input B or the fallback scalar.")]
        private MathOperation _operation;
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

        public MathNode(string nodeId, string nodeName, string inputAChannelName, string inputBChannelName, string outputChannelName, MathOperation operation = MathOperation.Add, float scalarB = 0.0f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputAChannelName = inputAChannelName ?? string.Empty;
            _inputBChannelName = inputBChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? FallbackOutputPortName : outputChannelName;
            _operation = operation;
            _scalarB = scalarB;
            _ports = new[]
            {
                new NodePortDefinition(InputAPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, true),
                new NodePortDefinition(InputBPortName, PortDirection.Input, ChannelType.Float, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.Float)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
        {
            _inputAChannelName = ResolveInputConnection(inputConnections, InputAPortName);
            _inputBChannelName = ResolveInputConnection(inputConnections, InputBPortName);
            RefreshChannelDeclarations();
        }

        public void ReceiveParameter(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (string.Equals(name, "operation", StringComparison.OrdinalIgnoreCase))
            {
                MathOperation parsedOperation;
                if (Enum.TryParse(value, true, out parsedOperation))
                {
                    _operation = parsedOperation;
                }

                return;
            }

            if (string.Equals(name, "scalarB", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "scalar", StringComparison.OrdinalIgnoreCase))
            {
                float parsedScalar;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedScalar))
                {
                    _scalarB = parsedScalar;
                }
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<float> inputA = context.GetFloatChannel(_inputAChannelName);
            NativeArray<float> output = context.GetFloatChannel(_outputChannelName);

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                NativeArray<float> inputB = context.GetFloatChannel(_inputBChannelName);
                BinaryMathJob binaryJob = new BinaryMathJob
                {
                    InputA = inputA,
                    InputB = inputB,
                    Output = output,
                    Operation = _operation
                };

                return binaryJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            ScalarMathJob scalarJob = new ScalarMathJob
            {
                InputA = inputA,
                Output = output,
                ScalarB = _scalarB,
                Operation = _operation
            };

            return scalarJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
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
            List<ChannelDeclaration> channelDeclarations = new List<ChannelDeclaration>(3);

            if (!string.IsNullOrWhiteSpace(_inputAChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputAChannelName, ChannelType.Float, false));
            }

            if (!string.IsNullOrWhiteSpace(_inputBChannelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(_inputBChannelName, ChannelType.Float, false));
            }

            channelDeclarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.Float, true));
            _channelDeclarations = channelDeclarations.ToArray();
        }

        [BurstCompile]
        private struct BinaryMathJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            [ReadOnly]
            public NativeArray<float> InputB;

            public NativeArray<float> Output;
            public MathOperation Operation;

            public void Execute(int index)
            {
                float aValue = InputA[index];
                float bValue = InputB[index];
                Output[index] = Evaluate(aValue, bValue, Operation);
            }
        }

        [BurstCompile]
        private struct ScalarMathJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<float> InputA;

            public NativeArray<float> Output;
            public float ScalarB;
            public MathOperation Operation;

            public void Execute(int index)
            {
                float aValue = InputA[index];
                Output[index] = Evaluate(aValue, ScalarB, Operation);
            }
        }

        private static float Evaluate(float aValue, float bValue, MathOperation operation)
        {
            switch (operation)
            {
                case MathOperation.Add:
                    return aValue + bValue;
                case MathOperation.Subtract:
                    return aValue - bValue;
                case MathOperation.Multiply:
                    return aValue * bValue;
                case MathOperation.Divide:
                    return bValue == 0.0f ? 0.0f : aValue / bValue;
                case MathOperation.Power:
                    return math.pow(aValue, bValue);
                case MathOperation.Abs:
                    return math.abs(aValue);
                case MathOperation.Min:
                    return math.min(aValue, bValue);
                case MathOperation.Max:
                    return math.max(aValue, bValue);
                default:
                    return aValue;
            }
        }
    }
}
