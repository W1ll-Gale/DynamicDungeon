using System;
using Unity.Collections;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class NumericBlackboard : IDisposable
    {
        private NativeHashMap<FixedString64Bytes, float> _values;
        private bool _isDisposed;

        public bool IsDisposed
        {
            get
            {
                return _isDisposed;
            }
        }

        public NumericBlackboard(int capacity, Allocator allocator)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Blackboard capacity cannot be negative.");
            }

            if (allocator == Allocator.None)
            {
                throw new ArgumentException("A valid allocator is required.", nameof(allocator));
            }

            int resolvedCapacity = capacity > 0 ? capacity : 1;

            _values = new NativeHashMap<FixedString64Bytes, float>(resolvedCapacity, allocator);
            _isDisposed = false;
        }

        public bool Read(FixedString64Bytes key, out float value)
        {
            ThrowIfDisposed();
            return _values.TryGetValue(key, out value);
        }

        public void Write(FixedString64Bytes key, float value)
        {
            ThrowIfDisposed();

            _values[key] = value;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_values.IsCreated)
            {
                _values.Dispose();
            }

            _values = default;
            _isDisposed = true;
        }

        internal NativeHashMap<FixedString64Bytes, float> NativeMap
        {
            get
            {
                ThrowIfDisposed();
                return _values;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(NumericBlackboard));
            }
        }
    }
}
