using System;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    public struct NodeExecutionContext
    {
        private NodeChannelBindings _channelBindings;
        private NumericBlackboard _numericBlackboard;
        private ManagedBlackboard _managedBlackboard;

        public readonly long LocalSeed;
        public readonly int Width;
        public readonly int Height;
        public readonly JobHandle InputDependency;

        public NumericBlackboard NumericBlackboard
        {
            get
            {
                return _numericBlackboard;
            }
        }

        public ManagedBlackboard ManagedBlackboard
        {
            get
            {
                return _managedBlackboard;
            }
        }

        public NodeExecutionContext(NodeChannelBindings channelBindings, NumericBlackboard numericBlackboard, ManagedBlackboard managedBlackboard, long localSeed, int width, int height, JobHandle inputDependency)
        {
            if (!channelBindings.IsCreated)
            {
                throw new ArgumentException("Channel bindings must be created before building a node execution context.", nameof(channelBindings));
            }

            if (numericBlackboard == null)
            {
                throw new ArgumentException("Numeric blackboard must be created before building a node execution context.", nameof(numericBlackboard));
            }

            if (numericBlackboard.IsDisposed)
            {
                throw new ArgumentException("Numeric blackboard must not be disposed before building a node execution context.", nameof(numericBlackboard));
            }

            if (managedBlackboard == null)
            {
                throw new ArgumentException("Managed blackboard must be created before building a node execution context.", nameof(managedBlackboard));
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
            _managedBlackboard = managedBlackboard;
            LocalSeed = localSeed;
            Width = width;
            Height = height;
            InputDependency = inputDependency;
        }

        public Unity.Collections.NativeArray<float> GetFloatChannel(string name)
        {
            return _channelBindings.GetFloatChannel(name);
        }

        public Unity.Collections.NativeArray<int> GetIntChannel(string name)
        {
            return _channelBindings.GetIntChannel(name);
        }

        public Unity.Collections.NativeArray<byte> GetBoolMaskChannel(string name)
        {
            return _channelBindings.GetBoolMaskChannel(name);
        }
    }
}
