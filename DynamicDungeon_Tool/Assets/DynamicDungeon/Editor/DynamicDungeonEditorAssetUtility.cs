using System;
using System.IO;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    internal static class DynamicDungeonEditorAssetUtility
    {
        public static string GetSelectedFolderPath()
        {
            string selectedFolderPath = "Assets";
            UnityEngine.Object[] selectedAssets = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);
            if (selectedAssets == null || selectedAssets.Length == 0)
            {
                return selectedFolderPath;
            }

            string selectedAssetPath = AssetDatabase.GetAssetPath(selectedAssets[0]);
            if (string.IsNullOrWhiteSpace(selectedAssetPath))
            {
                return selectedFolderPath;
            }

            if (AssetDatabase.IsValidFolder(selectedAssetPath))
            {
                return selectedAssetPath;
            }

            string directoryPath = Path.GetDirectoryName(selectedAssetPath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return selectedFolderPath;
            }

            return directoryPath.Replace("\\", "/");
        }

        public static void EnsureFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Folder path must be non-empty.", nameof(folderPath));
            }

            string normalisedFolderPath = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(normalisedFolderPath))
            {
                return;
            }

            string[] pathSegments = normalisedFolderPath.Split('/');
            if (pathSegments.Length == 0 || !string.Equals(pathSegments[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Folder path '" + folderPath + "' must be inside the Assets folder.");
            }

            string currentPath = pathSegments[0];

            int segmentIndex;
            for (segmentIndex = 1; segmentIndex < pathSegments.Length; segmentIndex++)
            {
                string childFolderName = pathSegments[segmentIndex];
                string nextPath = currentPath + "/" + childFolderName;
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, childFolderName);
                }

                currentPath = nextPath;
            }
        }

        public static T CreateAssetInSelectedFolder<T>(string defaultFileName) where T : ScriptableObject
        {
            string folderPath = GetSelectedFolderPath();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath((folderPath + "/" + defaultFileName).Replace("\\", "/"));
            T asset = ScriptableObject.CreateInstance<T>();
            InitialiseAsset(asset);
            ProjectWindowUtil.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void InitialiseAsset<T>(T asset) where T : ScriptableObject
        {
            GenGraph graph = asset as GenGraph;
            if (graph == null)
            {
                return;
            }

            graph.SchemaVersion = GraphSchemaVersion.Current;
            GraphOutputUtility.EnsureSingleOutputNode(graph, false);
        }
    }
}
