using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    internal static class GrowthSeedUtility
    {
        public static string ResolveInputConnection(IReadOnlyDictionary<string, string> inputConnections, string portName)
        {
            string inputChannelName;
            if (inputConnections != null && inputConnections.TryGetValue(portName, out inputChannelName))
            {
                return inputChannelName ?? string.Empty;
            }

            return string.Empty;
        }

        public static void AppendReadDeclarationIfConnected(List<ChannelDeclaration> channelDeclarations, string channelName, ChannelType type)
        {
            if (!string.IsNullOrWhiteSpace(channelName))
            {
                channelDeclarations.Add(new ChannelDeclaration(channelName, type, false));
            }
        }

        public static NativeList<int> BuildCandidateTileIndices(NativeArray<byte> mask)
        {
            NativeList<int> candidateTiles = new NativeList<int>(math.max(1, mask.Length), Allocator.TempJob);

            int index;
            for (index = 0; index < mask.Length; index++)
            {
                if (mask[index] != 0)
                {
                    candidateTiles.Add(index);
                }
            }

            return candidateTiles;
        }

        public static NativeList<int> BuildAllTileIndices(int width, int height)
        {
            int tileCount = math.max(1, width * height);
            NativeList<int> candidateTiles = new NativeList<int>(tileCount, Allocator.TempJob);

            int index;
            for (index = 0; index < width * height; index++)
            {
                candidateTiles.Add(index);
            }

            return candidateTiles;
        }
    }
}
