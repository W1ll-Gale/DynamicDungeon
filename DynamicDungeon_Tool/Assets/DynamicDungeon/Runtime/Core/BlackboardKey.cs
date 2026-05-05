using System;
using Unity.Collections;

namespace DynamicDungeon.Runtime.Core
{
    public readonly struct BlackboardKey
    {
        public readonly FixedString64Bytes Key;
        public readonly bool IsWrite;

        public BlackboardKey(FixedString64Bytes key, bool isWrite)
        {
            if (key.Length == 0)
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            Key = key;
            IsWrite = isWrite;
        }

        public BlackboardKey(string key, bool isWrite)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            Key = new FixedString64Bytes(key);
            IsWrite = isWrite;
        }
    }
}
