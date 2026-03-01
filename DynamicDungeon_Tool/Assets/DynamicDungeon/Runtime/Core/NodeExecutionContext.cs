using System;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    public struct NodeExecutionContext
    {
        private NodeChannelBindings _channelBindings;
        private NativeHashMap<FixedString64Bytes, float> _numericBlackboard;

        public readonly long LocalSeed;
        public readonly int Width;
        public readonly int Height;
        public readonly JobHandle InputDependency;

        public NativeHashMap<FixedString64Bytes, float> NumericBlackboard
        {
            get
            {
                return _numericBlackboard;
            }
        }

        public NodeExecutionContext(NodeChannelBindings channelBindings, NativeHashMap<FixedString64Bytes, float> numericBlackboard, long localSeed, int width, int height, JobHandle inputDependency)
        {
            if (!channelBindings.IsCreated)
            {
                throw new ArgumentException("Channel bindings must be created before building a node execution context.", nameof(channelBindings));
            }

            if (!numericBlackboard.IsCreated)
            {
                throw new ArgumentException("Numeric blackboard must be created before building a node execution context.", nameof(numericBlackboard));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "World width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "World height must be greater than zero.");
            }

            _channelBindings = channelBindings;
            _numericBlackboard = numericBlackboard;
            LocalSeed = localSeed;
            Width = width;
            Height = height;
            InputDependency = inputDependency;
        }

        public NativeArray<float> GetFloatChannel(string name)
        {
            return _channelBindings.GetFloatChannel(name);
        }

        public NativeArray<int> GetIntChannel(string name)
        {
            return _channelBindings.GetIntChannel(name);
        }

        public NativeArray<byte> GetBoolMaskChannel(string name)
        {
            return _channelBindings.GetBoolMaskChannel(name);
        }
    }
}
