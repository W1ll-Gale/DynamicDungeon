using System;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenConnectionData
    {
        public string FromNodeId = string.Empty;
        public string FromPortName = string.Empty;
        public string ToNodeId = string.Empty;
        public string ToPortName = string.Empty;

        public GenConnectionData()
        {
        }

        public GenConnectionData(string fromNodeId, string fromPortName, string toNodeId, string toPortName)
        {
            FromNodeId = fromNodeId ?? string.Empty;
            FromPortName = fromPortName ?? string.Empty;
            ToNodeId = toNodeId ?? string.Empty;
            ToPortName = toPortName ?? string.Empty;
        }
    }
}
