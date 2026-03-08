using System;
using Unity.Collections;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class WorldData : IDisposable
    {
        private const int DefaultMapCapacity = 4;

        private NativeParallelHashMap<FixedString128Bytes, NativeArray<float>> _floatChannels;
        private NativeParallelHashMap<FixedString128Bytes, NativeArray<int>> _intChannels;
        private NativeParallelHashMap<FixedString128Bytes, NativeArray<byte>> _boolMaskChannels;
        private Allocator _channelAllocator;
        private bool _isDisposed;

        public int Width { get; }

        public int Height { get; }

        public int Seed { get; }

        public int TileCount
        {
            get
            {
                return Width * Height;
            }
        }

        public WorldData(int width, int height, int seed) : this(width, height, seed, Allocator.Persistent)
        {
        }

        public WorldData(int width, int height, int seed, Allocator channelAllocator)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "World width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "World height must be greater than zero.");
            }

            if (channelAllocator == Allocator.None)
            {
                throw new ArgumentException("A valid allocator is required.", nameof(channelAllocator));
            }

            Width = width;
            Height = height;
            Seed = seed;

            _channelAllocator = channelAllocator;
            _floatChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<float>>(DefaultMapCapacity, channelAllocator);
            _intChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<int>>(DefaultMapCapacity, channelAllocator);
            _boolMaskChannels = new NativeParallelHashMap<FixedString128Bytes, NativeArray<byte>>(DefaultMapCapacity, channelAllocator);
            _isDisposed = false;
        }

        public bool HasChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _floatChannels.ContainsKey(key) || _intChannels.ContainsKey(key) || _boolMaskChannels.ContainsKey(key);
        }

        public bool HasFloatChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _floatChannels.ContainsKey(key);
        }

        public bool HasIntChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _intChannels.ContainsKey(key);
        }

        public bool HasBoolMaskChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _boolMaskChannels.ContainsKey(key);
        }

        public bool TryAddFloatChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            if (HasAnyChannel(key))
            {
                return false;
            }

            NativeArray<float> channelData = new NativeArray<float>(TileCount, _channelAllocator, NativeArrayOptions.ClearMemory);
            return _floatChannels.TryAdd(key, channelData);
        }

        public bool TryAddIntChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            if (HasAnyChannel(key))
            {
                return false;
            }

            NativeArray<int> channelData = new NativeArray<int>(TileCount, _channelAllocator, NativeArrayOptions.ClearMemory);
            return _intChannels.TryAdd(key, channelData);
        }

        public bool TryAddBoolMaskChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            if (HasAnyChannel(key))
            {
                return false;
            }

            NativeArray<byte> channelData = new NativeArray<byte>(TileCount, _channelAllocator, NativeArrayOptions.ClearMemory);
            return _boolMaskChannels.TryAdd(key, channelData);
        }

        public NativeArray<float> GetFloatChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<float> channelData;
            if (_floatChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            return default;
        }

        public NativeArray<int> GetIntChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<int> channelData;
            if (_intChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            return default;
        }

        public NativeArray<byte> GetBoolMaskChannel(string channelName)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            NativeArray<byte> channelData;
            if (_boolMaskChannels.TryGetValue(key, out channelData))
            {
                return channelData;
            }

            return default;
        }

        public bool TryGetFloatChannel(string channelName, out NativeArray<float> channelData)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _floatChannels.TryGetValue(key, out channelData);
        }

        public bool TryGetIntChannel(string channelName, out NativeArray<int> channelData)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _intChannels.TryGetValue(key, out channelData);
        }

        public bool TryGetBoolMaskChannel(string channelName, out NativeArray<byte> channelData)
        {
            ThrowIfDisposed();

            FixedString128Bytes key = CreateChannelKey(channelName);
            return _boolMaskChannels.TryGetValue(key, out channelData);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            DisposeChannelMap(_floatChannels);
            DisposeChannelMap(_intChannels);
            DisposeChannelMap(_boolMaskChannels);

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

            _floatChannels = default;
            _intChannels = default;
            _boolMaskChannels = default;
            _channelAllocator = Allocator.None;
            _isDisposed = true;
        }

        internal NativeKeyValueArrays<FixedString128Bytes, NativeArray<float>> GetFloatChannelPairs(Allocator allocator)
        {
            ThrowIfDisposed();
            return _floatChannels.GetKeyValueArrays(allocator);
        }

        internal NativeKeyValueArrays<FixedString128Bytes, NativeArray<int>> GetIntChannelPairs(Allocator allocator)
        {
            ThrowIfDisposed();
            return _intChannels.GetKeyValueArrays(allocator);
        }

        internal NativeKeyValueArrays<FixedString128Bytes, NativeArray<byte>> GetBoolMaskChannelPairs(Allocator allocator)
        {
            ThrowIfDisposed();
            return _boolMaskChannels.GetKeyValueArrays(allocator);
        }

        private static FixedString128Bytes CreateChannelKey(string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
            {
                throw new ArgumentException("Channel names must be non-empty.", nameof(channelName));
            }

            FixedString128Bytes key = channelName;
            return key;
        }

        private static void DisposeChannelMap<T>(NativeParallelHashMap<FixedString128Bytes, NativeArray<T>> channelMap)
            where T : unmanaged
        {
            if (!channelMap.IsCreated)
            {
                return;
            }

            NativeKeyValueArrays<FixedString128Bytes, NativeArray<T>> channelPairs = channelMap.GetKeyValueArrays(Allocator.TempJob);
            try
            {
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeArray<T> channelData = channelPairs.Values[index];
                    if (channelData.IsCreated)
                    {
                        channelData.Dispose();
                    }
                }
            }
            finally
            {
                channelPairs.Dispose();
            }
        }

        private bool HasAnyChannel(FixedString128Bytes key)
        {
            return _floatChannels.ContainsKey(key) || _intChannels.ContainsKey(key) || _boolMaskChannels.ContainsKey(key);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WorldData));
            }
        }
    }
}
