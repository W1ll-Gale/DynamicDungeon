using System;
using System.Collections;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Graph
{
    public readonly struct InputConnectionSet : IReadOnlyList<string>
    {
        private readonly string[] _channels;

        public int Count => _channels != null ? _channels.Length : 0;

        public string this[int index] => _channels[index];

        public InputConnectionSet(IEnumerable<string> channels)
        {
            if (channels == null)
            {
                _channels = Array.Empty<string>();
                return;
            }

            List<string> resolvedChannels = new List<string>();
            foreach (string channel in channels)
            {
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    resolvedChannels.Add(channel);
                }
            }

            _channels = resolvedChannels.Count > 0 ? resolvedChannels.ToArray() : Array.Empty<string>();
        }

        public string FirstOrDefault()
        {
            return Count > 0 ? _channels[0] : string.Empty;
        }

        public string[] ToArray()
        {
            if (Count == 0)
            {
                return Array.Empty<string>();
            }

            string[] copy = new string[_channels.Length];
            Array.Copy(_channels, copy, _channels.Length);
            return copy;
        }

        public IEnumerator<string> GetEnumerator()
        {
            return ((IEnumerable<string>)(_channels ?? Array.Empty<string>())).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
