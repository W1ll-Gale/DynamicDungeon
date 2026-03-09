using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.Runtime.Semantic
{
    [CreateAssetMenu(fileName = "TileSemanticRegistry", menuName = "DynamicDungeon/Tile Semantic Registry")]
    public sealed class TileSemanticRegistry : ScriptableObject
    {
        private const string RegistryAssetPath = "Assets/DynamicDungeon/TileSemanticRegistry.asset";

        private static TileSemanticRegistry _cachedRegistry;
        private static bool _hasAttemptedLoad;

        public List<TileEntry> Entries = new List<TileEntry>();
        public List<string> AllTags = new List<string>();

        public bool TryGetEntry(ushort logicalId, out TileEntry entry)
        {
            int index;
            for (index = 0; index < Entries.Count; index++)
            {
                TileEntry candidate = Entries[index];
                if (candidate != null && candidate.LogicalId == logicalId)
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public int[] GetTagIds(ushort logicalId)
        {
            TileEntry entry;
            if (!TryGetEntry(logicalId, out entry) || entry.Tags == null || entry.Tags.Count == 0)
            {
                return Array.Empty<int>();
            }

            List<int> tagIds = new List<int>(entry.Tags.Count);

            int index;
            for (index = 0; index < entry.Tags.Count; index++)
            {
                string tag = entry.Tags[index];
                int tagIndex = AllTags.IndexOf(tag);
                if (tagIndex >= 0)
                {
                    tagIds.Add(tagIndex);
                }
            }

            return tagIds.ToArray();
        }

        public static TileSemanticRegistry GetOrLoad()
        {
            if (_cachedRegistry != null)
            {
                return _cachedRegistry;
            }

            if (_hasAttemptedLoad)
            {
                return null;
            }

            _hasAttemptedLoad = true;

#if UNITY_EDITOR
            _cachedRegistry = AssetDatabase.LoadAssetAtPath<TileSemanticRegistry>(RegistryAssetPath);
            if (_cachedRegistry == null)
            {
                Debug.LogWarning("TileSemanticRegistry could not be loaded from '" + RegistryAssetPath + "'.");
            }

            return _cachedRegistry;
#else
            Debug.LogWarning("TileSemanticRegistry could not be loaded from '" + RegistryAssetPath + "' outside the Unity Editor.");
            return null;
#endif
        }
    }
}
