using System;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Core
{
    public struct NodeJobDescriptor
    {
        public readonly IGenNode Node;
        public readonly IReadOnlyList<ChannelDeclaration> Channels;
        public bool IsDirty;

        public NodeJobDescriptor(IGenNode node, IReadOnlyList<ChannelDeclaration> channels, bool isDirty)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            Node = node;
            Channels = channels ?? Array.Empty<ChannelDeclaration>();
            IsDirty = isDirty;
        }
    }
}
