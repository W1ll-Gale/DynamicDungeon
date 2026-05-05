using System.Collections.Generic;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    public interface IGenNode
    {
        IReadOnlyList<NodePortDefinition> Ports { get; }

        IReadOnlyList<ChannelDeclaration> ChannelDeclarations { get; }

        IReadOnlyList<BlackboardKey> BlackboardDeclarations { get; }

        string NodeId { get; }

        string NodeName { get; }

        JobHandle Schedule(NodeExecutionContext context);
    }

    public interface IMainThreadExecutionNode
    {
    }
}
