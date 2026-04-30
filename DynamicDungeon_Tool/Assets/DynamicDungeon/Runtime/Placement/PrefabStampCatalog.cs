using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.Runtime.Placement
{
    public sealed class PrefabStampCatalog : ScriptableObject
    {
        public const string ResourceName = "DynamicDungeon/PrefabStampCatalog";

#if UNITY_EDITOR
        private const string AssetPath = "Assets/Resources/DynamicDungeon/PrefabStampCatalog.asset";
#endif

        [Serializable]
        public sealed class Entry
        {
            public string PrefabGuid = string.Empty;
            public GameObject Prefab;
            public PrefabStampTemplate Template;
        }

        public List<Entry> Entries = new List<Entry>();

        public bool TryGetEntry(string prefabGuid, out Entry entry)
        {
            entry = null;

            if (Entries == null || string.IsNullOrWhiteSpace(prefabGuid))
            {
                return false;
            }

            int index;
            for (index = 0; index < Entries.Count; index++)
            {
                Entry candidate = Entries[index];
                if (candidate != null &&
                    string.Equals(candidate.PrefabGuid, prefabGuid, StringComparison.Ordinal) &&
                    candidate.Prefab != null &&
                    candidate.Template.IsValid)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        public static PrefabStampCatalog LoadCatalog()
        {
            return Resources.Load<PrefabStampCatalog>(ResourceName);
        }

#if UNITY_EDITOR
        public static bool TryEnsureEntry(string prefabGuid, out Entry entry, out string errorMessage)
        {
            entry = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(prefabGuid))
            {
                errorMessage = "Prefab GUID is empty.";
                return false;
            }

            PrefabStampCatalog catalog = LoadOrCreateCatalogAsset();

            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                errorMessage = "Prefab GUID '" + prefabGuid + "' could not be resolved.";
                return false;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                errorMessage = "Prefab GUID '" + prefabGuid + "' does not resolve to a GameObject prefab.";
                return false;
            }

            PrefabStampAuthoring authoring = prefab.GetComponent<PrefabStampAuthoring>();
            if (authoring == null)
            {
                errorMessage = "Prefab '" + prefab.name + "' requires a PrefabStampAuthoring component.";
                return false;
            }

            PrefabStampTemplate template;
            if (!authoring.TryBuildTemplate(prefabGuid, out template, out errorMessage))
            {
                return false;
            }

            bool changed;
            entry = UpsertEntry(catalog, prefabGuid, prefab, template, out changed);
            if (changed)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            return true;
        }

        public static bool TryInvalidatePrefabAtPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            PrefabStampCatalog catalog = LoadCatalog();
            if (catalog == null || catalog.Entries == null || catalog.Entries.Count == 0)
            {
                return false;
            }

            string prefabGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(prefabGuid))
            {
                return false;
            }

            bool removed = false;

            for (int index = catalog.Entries.Count - 1; index >= 0; index--)
            {
                Entry entry = catalog.Entries[index];
                if (entry != null && string.Equals(entry.PrefabGuid, prefabGuid, StringComparison.Ordinal))
                {
                    catalog.Entries.RemoveAt(index);
                    removed = true;
                }
            }

            if (removed)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            return removed;
        }

        private static PrefabStampCatalog LoadOrCreateCatalogAsset()
        {
            PrefabStampCatalog catalog = LoadCatalog();
            if (catalog != null)
            {
                return catalog;
            }

            EnsureFolderPath("Assets/Resources");
            EnsureFolderPath("Assets/Resources/DynamicDungeon");

            catalog = AssetDatabase.LoadAssetAtPath<PrefabStampCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = CreateInstance<PrefabStampCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
                AssetDatabase.SaveAssets();
            }

            return catalog;
        }

        private static Entry UpsertEntry(PrefabStampCatalog catalog, string prefabGuid, GameObject prefab, PrefabStampTemplate template, out bool changed)
        {
            changed = false;

            Entry entry;
            if (catalog.TryGetEntry(prefabGuid, out entry))
            {
                changed = entry.Prefab != prefab || !TemplatesEqual(entry.Template, template);
                entry.Prefab = prefab;
                entry.Template = template;
                return entry;
            }

            entry = new Entry
            {
                PrefabGuid = prefabGuid,
                Prefab = prefab,
                Template = template
            };

            if (catalog.Entries == null)
            {
                catalog.Entries = new List<Entry>();
            }

            catalog.Entries.Add(entry);
            changed = true;
            return entry;
        }

        private static bool TemplatesEqual(PrefabStampTemplate left, PrefabStampTemplate right)
        {
            if (!string.Equals(left.PrefabGuid, right.PrefabGuid, StringComparison.Ordinal) ||
                left.AnchorOffset != right.AnchorOffset ||
                left.SupportsRandomRotation != right.SupportsRandomRotation ||
                left.UsesTilemapFootprint != right.UsesTilemapFootprint)
            {
                return false;
            }

            Vector2Int[] leftCells = left.OccupiedCells ?? Array.Empty<Vector2Int>();
            Vector2Int[] rightCells = right.OccupiedCells ?? Array.Empty<Vector2Int>();
            if (leftCells.Length != rightCells.Length)
            {
                return false;
            }

            int index;
            for (index = 0; index < leftCells.Length; index++)
            {
                if (leftCells[index] != rightCells[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            string currentPath = parts[0];

            int index;
            for (index = 1; index < parts.Length; index++)
            {
                string nextPath = currentPath + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[index]);
                }

                currentPath = nextPath;
            }
        }
#endif
    }
}
