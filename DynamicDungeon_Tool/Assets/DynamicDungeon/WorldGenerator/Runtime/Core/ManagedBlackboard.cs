using System;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class ManagedBlackboard
    {
        private readonly Dictionary<string, object> _values;

        public ManagedBlackboard()
        {
            _values = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public bool Read<T>(string key, out T value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            object boxedValue;
            if (_values.TryGetValue(key, out boxedValue) && boxedValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        public void Write(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            _values[key] = value;
        }

        public void Clear()
        {
            _values.Clear();
        }
    }
}
