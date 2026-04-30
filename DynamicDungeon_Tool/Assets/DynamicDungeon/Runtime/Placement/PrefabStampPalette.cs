using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Placement
{
    public sealed class PrefabStampPalette
    {
        private readonly List<GameObject> _prefabs = new List<GameObject>();
        private readonly List<PrefabStampTemplate> _templates = new List<PrefabStampTemplate>();
        private readonly Dictionary<string, int> _indicesByGuid = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<GameObject> Prefabs
        {
            get
            {
                return _prefabs;
            }
        }

        public IReadOnlyList<PrefabStampTemplate> Templates
        {
            get
            {
                return _templates;
            }
        }

        public bool TryResolve(string prefabGuid, out int templateIndex, out PrefabStampTemplate template, out string errorMessage)
        {
            templateIndex = -1;
            template = default;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(prefabGuid))
            {
                errorMessage = "Prefab GUID is empty.";
                return false;
            }

            if (_indicesByGuid.TryGetValue(prefabGuid, out templateIndex))
            {
                template = _templates[templateIndex];
                return true;
            }

            PrefabStampCatalog.Entry entry;
#if UNITY_EDITOR
            if (!PrefabStampCatalog.TryEnsureEntry(prefabGuid, out entry, out errorMessage))
            {
                return false;
            }
#else
            PrefabStampCatalog catalog = PrefabStampCatalog.LoadCatalog();
            if (catalog == null)
            {
                errorMessage = "Prefab stamp catalog could not be loaded from Resources.";
                return false;
            }

            if (!catalog.TryGetEntry(prefabGuid, out entry))
            {
                errorMessage = "Prefab stamp catalog does not contain prefab GUID '" + prefabGuid + "'.";
                return false;
            }
#endif

            if (entry == null || entry.Prefab == null || !entry.Template.IsValid)
            {
                errorMessage = "Prefab stamp catalog entry for GUID '" + prefabGuid + "' is invalid.";
                return false;
            }

            templateIndex = _templates.Count;
            _indicesByGuid.Add(prefabGuid, templateIndex);
            _prefabs.Add(entry.Prefab);
            _templates.Add(entry.Template);
            template = entry.Template;
            return true;
        }
    }
}
