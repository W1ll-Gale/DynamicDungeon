using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.Runtime.Biome
{
    public sealed class BiomeChannelPalette
    {
        private readonly List<BiomeAsset> _biomes = new List<BiomeAsset>();
        private readonly Dictionary<string, int> _indicesByGuid = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<BiomeAsset> Biomes
        {
            get
            {
                return _biomes;
            }
        }

        public bool TryResolveIndex(string biomeGuid, out int biomeIndex, out string errorMessage)
        {
            biomeIndex = BiomeChannelUtility.UnassignedBiomeIndex;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(biomeGuid))
            {
                errorMessage = "Biome asset GUID is empty.";
                return false;
            }

            if (_indicesByGuid.TryGetValue(biomeGuid, out biomeIndex))
            {
                return true;
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GUIDToAssetPath(biomeGuid);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                errorMessage = "Biome asset GUID '" + biomeGuid + "' could not be resolved to an asset path.";
                return false;
            }

            BiomeAsset biomeAsset = AssetDatabase.LoadAssetAtPath<BiomeAsset>(assetPath);
            if (biomeAsset == null)
            {
                errorMessage = "Biome asset GUID '" + biomeGuid + "' does not resolve to a BiomeAsset.";
                return false;
            }

            biomeIndex = _biomes.Count;
            _biomes.Add(biomeAsset);
            _indicesByGuid.Add(biomeGuid, biomeIndex);
            return true;
#else
            errorMessage = "Biome asset GUID resolution requires the Unity Editor asset database.";
            return false;
#endif
        }

        public BiomeAsset[] ToArray()
        {
            return _biomes.ToArray();
        }
    }
}
