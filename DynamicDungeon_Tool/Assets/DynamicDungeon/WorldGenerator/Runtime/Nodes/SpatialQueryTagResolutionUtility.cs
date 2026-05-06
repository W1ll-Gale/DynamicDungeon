using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Semantic;

namespace DynamicDungeon.Runtime.Nodes
{
    internal static class SpatialQueryTagResolutionUtility
    {
        public static bool TryResolveMatchingLogicalIds(string tagName, out int resolvedTagId, out int[] matchingLogicalIds, out bool registryMissing)
        {
            resolvedTagId = -1;
            matchingLogicalIds = Array.Empty<int>();
            registryMissing = false;

            if (string.IsNullOrWhiteSpace(tagName))
            {
                return false;
            }

            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
            if (registry == null)
            {
                registryMissing = true;
                return false;
            }

            resolvedTagId = registry.AllTags.IndexOf(tagName);
            if (resolvedTagId < 0)
            {
                return false;
            }

            List<int> matchedLogicalIds = new List<int>();

            int entryIndex;
            for (entryIndex = 0; entryIndex < registry.Entries.Count; entryIndex++)
            {
                TileEntry entry = registry.Entries[entryIndex];
                if (entry == null || entry.Tags == null || entry.Tags.Count == 0)
                {
                    continue;
                }

                if (entry.Tags.Contains(tagName))
                {
                    matchedLogicalIds.Add(entry.LogicalId);
                }
            }

            matchingLogicalIds = matchedLogicalIds.ToArray();
            return true;
        }
    }
}
