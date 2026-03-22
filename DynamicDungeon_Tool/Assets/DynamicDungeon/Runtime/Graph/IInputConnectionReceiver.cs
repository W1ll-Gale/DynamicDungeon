using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Graph
{
    public interface IInputConnectionReceiver
    {
        void ReceiveInputConnections(IReadOnlyDictionary<string, string> inputConnections);
    }
}
