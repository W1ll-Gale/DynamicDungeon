using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Placement;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    internal sealed class PrefabStampPreviewRefreshPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool shouldRefreshPreviews = false;

            shouldRefreshPreviews |= ProcessChangedAssets(importedAssets, invalidateCatalogEntry: true);
            shouldRefreshPreviews |= ProcessChangedAssets(movedAssets, invalidateCatalogEntry: true);
            shouldRefreshPreviews |= ProcessChangedAssets(deletedAssets, invalidateCatalogEntry: true);

            if (shouldRefreshPreviews)
            {
                EditorApplication.delayCall += DynamicDungeonEditorWindow.RequestPreviewRefreshForAllOpenWindows;
            }
        }

        private static bool ProcessChangedAssets(string[] assetPaths, bool invalidateCatalogEntry)
        {
            if (assetPaths == null || assetPaths.Length == 0)
            {
                return false;
            }

            bool foundRelevantAsset = false;

            int index;
            for (index = 0; index < assetPaths.Length; index++)
            {
                string assetPath = assetPaths[index];
                if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (invalidateCatalogEntry)
                {
                    foundRelevantAsset |= PrefabStampCatalog.TryInvalidatePrefabAtPath(assetPath);
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null && prefab.GetComponent<PrefabStampAuthoring>() != null)
                {
                    foundRelevantAsset = true;
                }
            }

            return foundRelevantAsset;
        }
    }
}
