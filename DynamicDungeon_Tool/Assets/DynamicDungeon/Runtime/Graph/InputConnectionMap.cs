using System;
using System.Collections;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class InputConnectionMap : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, InputConnectionSet> _connectionsByPort;
        private readonly Dictionary<string, string> _firstConnectionByPort;

        public IEnumerable<string> Keys => _firstConnectionByPort.Keys;
        public IEnumerable<string> Values => _firstConnectionByPort.Values;
        public int Count => _firstConnectionByPort.Count;

        public string this[string key] => _firstConnectionByPort[key];

        public InputConnectionMap()
        {
            _connectionsByPort = new Dictionary<string, InputConnectionSet>(StringComparer.Ordinal);
            _firstConnectionByPort = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public InputConnectionMap(IReadOnlyDictionary<string, IReadOnlyList<string>> connectionsByPort) : this()
        {
            if (connectionsByPort == null)
            {
                return;
            }

            foreach (KeyValuePair<string, IReadOnlyList<string>> entry in connectionsByPort)
            {
                SetConnections(entry.Key, entry.Value);
            }
        }

        public void SetConnections(string portName, IEnumerable<string> channelNames)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            InputConnectionSet connectionSet = new InputConnectionSet(channelNames);
            if (connectionSet.Count == 0)
            {
                _connectionsByPort.Remove(portName);
                _firstConnectionByPort.Remove(portName);
                return;
            }

            _connectionsByPort[portName] = connectionSet;
            _firstConnectionByPort[portName] = connectionSet.FirstOrDefault();
        }

        public bool TryGetConnectionSet(string portName, out InputConnectionSet connectionSet)
        {
            if (!string.IsNullOrWhiteSpace(portName) && _connectionsByPort.TryGetValue(portName, out connectionSet))
            {
                return true;
            }

            connectionSet = default;
            return false;
        }

        public IReadOnlyList<string> GetAll(string portName)
        {
            InputConnectionSet connectionSet;
            return TryGetConnectionSet(portName, out connectionSet)
                ? connectionSet
                : Array.Empty<string>();
        }

        public string FirstOrDefault(string portName)
        {
            string channelName;
            return TryGetValue(portName, out channelName) ? channelName : string.Empty;
        }

        public bool ContainsKey(string key)
        {
            return _firstConnectionByPort.ContainsKey(key);
        }

        public bool TryGetValue(string key, out string value)
        {
            return _firstConnectionByPort.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return _firstConnectionByPort.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
