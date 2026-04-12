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
    [Description("Smooths or grows cave-like masks by repeatedly applying cellular automata rules.")]
    public sealed class CellularAutomataNode : IGenNode, IInputConnectionReceiver, IParameterReceiver
    {
        private const int DefaultBatchSize = 64;
        private const string DefaultNodeName = "Cellular Automata";
        private const string InputPortName = "Input";
        private const string FallbackOutputPortName = "Output";

        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;
        private readonly string _outputChannelName;
        private readonly NodePortDefinition[] _ports;

        private string _inputChannelName;
        [InspectorName("Birth On")]
        [NeighbourCountRule]
        [Description("Empty cells become filled when they have one of these neighbouring filled-cell counts. Classic cave default: 5, 6, 7, 8.")]
        private string _birthRule;
        [InspectorName("Survive On")]
        [NeighbourCountRule]
        [Description("Filled cells stay filled when they have one of these neighbouring filled-cell counts. Classic cave default: 4, 5, 6, 7, 8.")]
        private string _survivalRule;
        [Description("Number of cellular automata passes to run.")]
        private int _iterations;
        [Description("Chance of a cell starting filled when there is no input mask.")]
        private float _initialFillProbability;
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

        public CellularAutomataNode(string nodeId, string nodeName, string inputChannelName, string outputChannelName, string birthRule = "5678", string survivalRule = "45678", int iterations = 1, float initialFillProbability = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? DefaultNodeName : nodeName;
            _inputChannelName = inputChannelName ?? string.Empty;
            _outputChannelName = string.IsNullOrWhiteSpace(outputChannelName) ? FallbackOutputPortName : outputChannelName;
            _birthRule = birthRule ?? string.Empty;
            _survivalRule = survivalRule ?? string.Empty;
            _iterations = math.max(0, iterations);
            _initialFillProbability = math.clamp(initialFillProbability, 0.0f, 1.0f);
            _ports = new[]
            {
                new NodePortDefinition(InputPortName, PortDirection.Input, ChannelType.BoolMask, PortCapacity.Single, false),
                new NodePortDefinition(_outputChannelName, PortDirection.Output, ChannelType.BoolMask)
            };

            RefreshChannelDeclarations();
        }

        public void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections)
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

            if (string.Equals(name, "birthRule", StringComparison.OrdinalIgnoreCase))
            {
                _birthRule = value ?? string.Empty;
                return;
            }

            if (string.Equals(name, "survivalRule", StringComparison.OrdinalIgnoreCase))
            {
                _survivalRule = value ?? string.Empty;
                return;
            }

            if (string.Equals(name, "iterations", StringComparison.OrdinalIgnoreCase))
            {
                int parsedIterations;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedIterations))
                {
                    _iterations = math.max(0, parsedIterations);
                }

                return;
            }

            if (string.Equals(name, "initialFillProbability", StringComparison.OrdinalIgnoreCase))
            {
                float parsedProbability;
                if (float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedProbability))
                {
                    _initialFillProbability = math.clamp(parsedProbability, 0.0f, 1.0f);
                }
            }
        }

        public JobHandle Schedule(NodeExecutionContext context)
        {
            NativeArray<byte> output = context.GetBoolMaskChannel(_outputChannelName);
            NativeArray<byte> currentState = new NativeArray<byte>(output.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte> nextState = new NativeArray<byte>(output.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int birthMask = ParseRuleMask(_birthRule);
            int survivalMask = ParseRuleMask(_survivalRule);

            JobHandle currentHandle;
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                NativeArray<byte> input = context.GetBoolMaskChannel(_inputChannelName);
                CopyMaskJob copyInputJob = new CopyMaskJob
                {
                    Input = input,
                    Output = currentState
                };

                currentHandle = copyInputJob.Schedule(currentState.Length, DefaultBatchSize, context.InputDependency);
            }
            else
            {
                InitialiseRandomMaskJob initialiseJob = new InitialiseRandomMaskJob
                {
                    Output = currentState,
                    FillProbability = _initialFillProbability,
                    LocalSeed = context.LocalSeed
                };

                currentHandle = initialiseJob.Schedule(currentState.Length, DefaultBatchSize, context.InputDependency);
            }

            int iterationIndex;
            for (iterationIndex = 0; iterationIndex < _iterations; iterationIndex++)
            {
                CellularAutomataIterationJob iterationJob = new CellularAutomataIterationJob
                {
                    Input = currentState,
                    Output = nextState,
                    Width = context.Width,
                    Height = context.Height,
                    BirthMask = birthMask,
                    SurvivalMask = survivalMask
                };

                currentHandle = iterationJob.Schedule(currentState.Length, DefaultBatchSize, currentHandle);

                NativeArray<byte> temp = currentState;
                currentState = nextState;
                nextState = temp;
            }

            CopyMaskJob copyOutputJob = new CopyMaskJob
            {
                Input = currentState,
                Output = output
            };

            JobHandle outputHandle = copyOutputJob.Schedule(output.Length, DefaultBatchSize, currentHandle);
            JobHandle disposeCurrentHandle = currentState.Dispose(outputHandle);
            JobHandle disposeNextHandle = nextState.Dispose(outputHandle);
            return JobHandle.CombineDependencies(disposeCurrentHandle, disposeNextHandle);
        }

        private static int ParseRuleMask(string rule)
        {
            if (string.IsNullOrEmpty(rule))
            {
                return 0;
            }

            int mask = 0;

            int characterIndex;
            for (characterIndex = 0; characterIndex < rule.Length; characterIndex++)
            {
                char ruleCharacter = rule[characterIndex];
                if (ruleCharacter < '0' || ruleCharacter > '8')
                {
                    continue;
                }

                mask |= 1 << (ruleCharacter - '0');
            }

            return mask;
        }

        private void RefreshChannelDeclarations()
        {
            if (!string.IsNullOrWhiteSpace(_inputChannelName))
            {
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(_inputChannelName, ChannelType.BoolMask, false),
                    new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
                };
                return;
            }

            _channelDeclarations = new[]
            {
                new ChannelDeclaration(_outputChannelName, ChannelType.BoolMask, true)
            };
        }

        [BurstCompile]
        private struct CopyMaskJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;

            public void Execute(int index)
            {
                Output[index] = Input[index];
            }
        }

        [BurstCompile]
        private struct InitialiseRandomMaskJob : IJobParallelFor
        {
            public NativeArray<byte> Output;
            public float FillProbability;
            public long LocalSeed;

            public void Execute(int index)
            {
                uint hashedValue = Hash(index, LocalSeed);
                float randomValue = (hashedValue & 16777215u) / 16777215.0f;
                Output[index] = randomValue < FillProbability ? (byte)1 : (byte)0;
            }

            private static uint Hash(int index, long localSeed)
            {
                uint seed = (uint)localSeed ^ (uint)(localSeed >> 32);
                uint value = seed + (uint)index * 747796405u + 2891336453u;
                value = (value ^ (value >> 16)) * 2246822519u;
                value = (value ^ (value >> 13)) * 3266489917u;
                return value ^ (value >> 16);
            }
        }

        [BurstCompile]
        private struct CellularAutomataIterationJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> Input;

            public NativeArray<byte> Output;
            public int Width;
            public int Height;
            public int BirthMask;
            public int SurvivalMask;

            public void Execute(int index)
            {
                int x = index % Width;
                int y = index / Width;
                int liveNeighbourCount = CountLiveNeighbours(x, y);
                bool isAlive = Input[index] != 0;

                if (isAlive)
                {
                    Output[index] = ((SurvivalMask >> liveNeighbourCount) & 1) != 0 ? (byte)1 : (byte)0;
                }
                else
                {
                    Output[index] = ((BirthMask >> liveNeighbourCount) & 1) != 0 ? (byte)1 : (byte)0;
                }
            }

            private int CountLiveNeighbours(int x, int y)
            {
                int liveNeighbourCount = 0;
                int offsetY;
                for (offsetY = -1; offsetY <= 1; offsetY++)
                {
                    int offsetX;
                    for (offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        int neighbourX = x + offsetX;
                        int neighbourY = y + offsetY;
                        if (neighbourX < 0 || neighbourX >= Width || neighbourY < 0 || neighbourY >= Height)
                        {
                            continue;
                        }

                        int neighbourIndex = neighbourY * Width + neighbourX;
                        if (Input[neighbourIndex] != 0)
                        {
                            liveNeighbourCount++;
                        }
                    }
                }

                return liveNeighbourCount;
            }
        }
    }
}
