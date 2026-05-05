using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{
    [NodeCategory("Blend")]
    [NodeDisplayName("Mask Stack")]
    [Description("Combines any number of bool mask inputs into one mask using a single ordered stack.")]
    public sealed class MaskStackNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Mask Stack";
        private const string MasksPortName = "Masks";
        private const string FallbackOutputPortName = "Mask";
        private const string PreferredOutputDisplayName = FallbackOutputPortName;

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private string _outputChannelName;
        private string[] _inputChannelNames;
        private MaskOperation _operation;
        private bool _invertOutput;
        private NodePortDefinition[] _ports;
        private ChannelDeclaration[] _channelDeclarations;

        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations => _channelDeclarations;
        public IReadOnlyList<BlackboardKey> BlackboardDeclarations => _blackboardDeclarations;
        public string NodeId => _nodeId;
        public string NodeName => _nodeName;

        public MaskStackNode(
            string nodeId,
            string nodeName,
            string outputChannelName = "",
            MaskOperation operation = MaskOperation.OR,
            bool invertOutput = false)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _outputChannelName = GraphPortNameUtility.ResolveOwnedOutputChannelName(nodeId, outputChannelName, FallbackOutputPortName);
            _operation = NormalizeOperation(operation);
            _invertOutput = invertOutput;
            _inputChannelNames = Array.Empty<string>();

            RefreshPorts();
            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(InputConnectionMap inputConnections)
        {
            IReadOnlyList<string> connections = inputConnections != null
                ? inputConnections.GetAll(MasksPortName)
                : Array.Empty<string>();

            _inputChannelNames = new string[connections.Count];
            for (int index = 0; index < connections.Count; index++)
            {
                _inputChannelNames[index] = connections[index] ?? string.Empty;
            }

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
                MaskOperation parsedOperation;
                if (Enum.TryParse(value, true, out parsedOperation))
                {
                    _operation = NormalizeOperation(parsedOperation);
                }

                return;
            }

            if (string.Equals(name, "invertOutput", StringComparison.OrdinalIgnoreCase))
            {
                bool parsedValue;
                if (bool.TryParse(value, out parsedValue))
                {
                    _invertOutput = parsedValue;
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
            if (_inputChannelNames == null || _inputChannelNames.Length == 0)
            {
                ClearMaskJob clearJob = new ClearMaskJob { Output = output };
                return clearJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            }

            NativeArray<byte> firstInput = context.GetBoolMaskChannel(_inputChannelNames[0]);
            CopyMaskJob copyJob = new CopyMaskJob
            {
                Input = firstInput,
                Output = output,
                Invert = _inputChannelNames.Length == 1 && _invertOutput
            };

            JobHandle dependency = copyJob.Schedule(output.Length, DefaultBatchSize, context.InputDependency);
            for (int inputIndex = 1; inputIndex < _inputChannelNames.Length; inputIndex++)
            {
                NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelNames[inputIndex]);
                CombineIntoMaskJob combineJob = new CombineIntoMaskJob
                {
                    Input = input,
                    Output = output,
                    Operation = (int)_operation,
                    InvertAfterCombine = inputIndex == _inputChannelNames.Length - 1 && _invertOutput
                };

                dependency = combineJob.Schedule(output.Length, DefaultBatchSize, dependency);
            }

            return dependency;
        }

        private void RefreshPorts()
        {
            string outputPortDisplayName = GraphPortNameUtility.ResolveOutputDisplayName(_nodeId, _outputChannelName, PreferredOutputDisplayName);
            _ports = new[]
            {
                new NodePortDefinition(MasksPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Multi, true, "All masks are combined in connection order."),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask, displayName: outputPortDisplayName)
            };
        }

        private void RefreshChannelDeclarations()
        {
            List<ChannelDeclaration> declarations = new List<ChannelDeclaration>();
            if (_inputChannelNames != null)
            {
                for (int index = 0; index < _inputChannelNames.Length; index++)
                {
                    if (!string.IsNullOrWhiteSpace(_inputChannelNames[index]))
                    {
                        declarations.Add(new ChannelDeclaration(_inputChannelNames[index], ChannelType.BoolMask, false));
                    }
                }
            }

            declarations.Add(new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true));
            _channelDeclarations = declarations.ToArray();
        }

        [BurstCompile]
        private struct ClearMaskJob : IJobParallelFor
        {
            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                Output[index] = 0;
            }
        }

        [BurstCompile]
        private struct CopyMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public bool Invert;

            public void Execute(int index)
            {
                bool value = Input[index] != 0;
                Output[index] = (value ^ Invert) ? (byte)1 : (byte)0;
            }
        }

        [BurstCompile]
        private struct CombineIntoMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public int Operation;
            public bool InvertAfterCombine;

            public void Execute(int index)
            {
                bool left = Output[index] != 0;
                bool right = Input[index] != 0;
                bool value;

                if (Operation == (int)MaskOperation.AND)
                {
                    value = left && right;
                }
                else if (Operation == (int)MaskOperation.XOR)
                {
                    value = left ^ right;
                }
                else
                {
                    value = left || right;
                }

                Output[index] = (value ^ InvertAfterCombine) ? (byte)1 : (byte)0;
            }
        }

        private static MaskOperation NormalizeOperation(MaskOperation operation)
        {
            if (operation == MaskOperation.AND || operation == MaskOperation.XOR)
            {
                return operation;
            }

            return MaskOperation.OR;
        }
    }
}
