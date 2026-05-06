using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Semantic
{
    public sealed class TilemapLayerDefinition : ScriptableObject
    {
        public string LayerName = "Default";
        public List<string> ExcludedRegistryGuids = new List<string>();
        public List<string> RoutingTags = new List<string>();
        public List<string> ComponentsToAdd = new List<string>();
        public int SortOrder;
        public bool IsCatchAll;

        public bool MatchesTags(int[] tileTagIds, List<string> allTags)
        {
            if (IsCatchAll)
            {
                return true;
            }

            if (tileTagIds == null || tileTagIds.Length == 0 || allTags == null || RoutingTags == null || RoutingTags.Count == 0)
            {
                return false;
            }

            int routingTagIndex;
            for (routingTagIndex = 0; routingTagIndex < RoutingTags.Count; routingTagIndex++)
            {
                string routingTag = RoutingTags[routingTagIndex];
                int tagId = allTags.IndexOf(routingTag);
                if (tagId < 0)
                {
                    continue;
                }

                int tileTagIndex;
                for (tileTagIndex = 0; tileTagIndex < tileTagIds.Length; tileTagIndex++)
                {
                    if (tileTagIds[tileTagIndex] == tagId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
