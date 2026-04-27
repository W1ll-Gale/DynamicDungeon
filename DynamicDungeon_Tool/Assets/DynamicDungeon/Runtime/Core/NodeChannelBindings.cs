using System;
using Unity.Collections;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Core
{
    public struct NodeChannelBindings : IDisposable
    {
        private NativeParallelHashMap<FixedString128Bytes, NativeArray<float>> _floatChannels;
        private NativeParallelHashMap<FixedString128Bytes, NativeArray<int>> _intChannels;
        private NativeParallelHashMap<FixedString128Bytes, NativeArray<byte>> _boolMaskChannels;
        private NativeParallelHashMap<FixedString128Bytes, NativeList<int2>> _pointListChannels;

        public NodeChannelBindings(int capacity, Allocator allocator)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Binding capacity cannot be negative.");
            }

            if (allocator == Allocator.None)
            {
                throw new ArgumentException("A valid allocator is required.", nameof(allocator));
            }

            int resolvedCapacity = capacity > 0 ? capacity : 1;

            _floatChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<float>>(resolvedCapacity, allocator);
            _intChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<int>>(resolvedCapacity, allocator);
            _boolMaskChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<byte>>(resolvedCapacity, allocator);
            _pointListChannels = new NativeParallelHashMap<FixedString128Bytes, NativeList<int2>>(resolvedCapacity, allocator);
        }

        public bool IsCreated
        {
            get
            {
                return _floatChannels.IsCreated && _intChannels.IsCreated && _boolMaskChannels.IsCreated && _pointListChannels.IsCreated;
            }
        }

        public void BindFloatChannel(string channelName, NativeArray<float> channelData)
        {
            ThrowIfNotCreated();

            if (!channelData.IsCreated)
            {
                throw new ArgumentException("Channel data must be created before binding.", nameof(channelData));
            }

            FixedString128Bytes key = CreateChannelKey(channelName);
            ThrowIfConflictingTypeBindingExists(key, ChannelType.Float);
            UpsertFloatChannel(key, channelData);
        }

        public void BindIntChannel(string channelName, NativeArray<int> channelData)
        {
            ThrowIfNotCreated();

            if (!channelData.IsCreated)
            {
                throw new ArgumentException("Channel data must be created before binding.", nameof(channelData));
            }

            FixedString128Bytes key = CreateChannelKey(channelName);
            ThrowIfConflictingTypeBindingExists(key, ChannelType.Int);
            UpsertIntChannel(key, channelData);
        }

        public void BindBoolMaskChannel(string channelName, NativeArray<byte> channelData)
        {
            ThrowIfNotCreated();

            if (!channelData.IsCreated)
            {
                throw new ArgumentException("Channel data must be created before binding.", nameof(channelData));
            }

            FixedString128Bytes key = CreateChannelKey(channelName);
            ThrowIfConflictingTypeBindingExists(key, ChannelType.BoolMask);
            UpsertBoolMaskChannel(key, channelData);
        }

        public void BindPointListChannel(string channelName, NativeList<int2> channelData)
        {
            ThrowIfNotCreated();

            if (!channelData.IsCreated)
            {
                throw new ArgumentException("Channel data must be created before binding.", nameof(channelData));
            }

            FixedString128Bytes key = CreateChannelKey(channelName);
            ThrowIfConflictingTypeBindingExists(key, ChannelType.PointList);
            UpsertPointListChannel(key, channelData);
        }

        public NativeArray<float> GetFloatChannel(string channelName)
        {
            ThrowIfNotCreated();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<float> channelData;
            if (_floatChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            throw new InvalidOperationException("No float channel binding exists for '" + channelName + "'.");
        }

        public NativeArray<int> GetIntChannel(string channelName)
        {
            ThrowIfNotCreated();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<int> channelData;
            if (_intChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            throw new InvalidOperationException("No int channel binding exists for '" + channelName + "'.");
        }

        public NativeArray<byte> GetBoolMaskChannel(string channelName)
        {
            ThrowIfNotCreated();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<byte> channelData;
            if (_boolMaskChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            throw new InvalidOperationException("No bool mask channel binding exists for '" + channelName + "'.");
        }

        public NativeList<int2> GetPointListChannel(string channelName)
        {
            ThrowIfNotCreated();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeList<int2> channelData;
            if (_pointListChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            throw new InvalidOperationException("No point list channel binding exists for '" + channelName + "'.");
        }

        public void Dispose()
        {
            if (_floatChannels.IsCreated)
            {
                _floatChannels.Dispose();
            }

            if (_intChannels.IsCreated)
            {
                _intChannels.Dispose();
            }

            if (_boolMaskChannels.IsCreated)
            {
                _boolMaskChannels.Dispose();
            }

            if (_pointListChannels.IsCreated)
            {
                _pointListChannels.Dispose();
            }

            _floatChannels = default;
            _intChannels = default;
            _boolMaskChannels = default;
            _pointListChannels = default;
        }

        private static FixedString128Bytes CreateChannelKey(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new ArgumentException("Channel name must be non-empty.", nameof(channelName));
            }

            return new FixedString128Bytes(channelName);
        }

        private void ThrowIfConflictingTypeBindingExists(FixedString128Bytes key, ChannelType expectedType)
        {
            bool hasFloatBinding = _floatChannels.ContainsKey(key);
            bool hasIntBinding = _intChannels.ContainsKey(key);
            bool hasBoolMaskBinding = _boolMaskChannels.ContainsKey(key);
            bool hasPointListBinding = _pointListChannels.ContainsKey(key);

            if (expectedType != ChannelType.Float && hasFloatBinding)
            {
                throw new InvalidOperationException("Channel binding '" + key + "' is already registered as a float channel.");
            }

            if (expectedType != ChannelType.Int && hasIntBinding)
            {
                throw new InvalidOperationException("Channel binding '" + key + "' is already registered as an int channel.");
            }

            if (expectedType != ChannelType.BoolMask && hasBoolMaskBinding)
            {
                throw new InvalidOperationException("Channel binding '" + key + "' is already registered as a bool mask channel.");
            }

            if (expectedType != ChannelType.PointList && hasPointListBinding)
            {
                throw new InvalidOperationException("Channel binding '" + key + "' is already registered as a point list channel.");
            }
        }

        private void ThrowIfNotCreated()
        {
            if (!IsCreated)
            {
                throw new InvalidOperationException("Node channel bindings have not been created.");
            }
        }

        private void UpsertFloatChannel(FixedString128Bytes key, NativeArray<float> channelData)
        {
            if (_floatChannels.ContainsKey(key))
            {
                _floatChannels.Remove(key);
            }

            _floatChannels.Add(key, channelData);
        }

        private void UpsertIntChannel(FixedString128Bytes key, NativeArray<int> channelData)
        {
            if (_intChannels.ContainsKey(key))
            {
                _intChannels.Remove(key);
            }

            _intChannels.Add(key, channelData);
        }

        private void UpsertBoolMaskChannel(FixedString128Bytes key, NativeArray<byte> channelData)
        {
            if (_boolMaskChannels.ContainsKey(key))
            {
                _boolMaskChannels.Remove(key);
            }

            _boolMaskChannels.Add(key, channelData);
        }

        private void UpsertPointListChannel(FixedString128Bytes key, NativeList<int2> channelData)
        {
            if (_pointListChannels.ContainsKey(key))
            {
                _pointListChannels.Remove(key);
            }

            _pointListChannels.Add(key, channelData);
        }
    }
}
