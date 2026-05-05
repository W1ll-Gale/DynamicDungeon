using DynamicDungeon.Editor.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class SavedAssetRestoreUtilityTests
    {
        private const string TestFolder = "Assets/__SharedRestoreUtilityTests";

        [Test]
        public void RestoreFromSavedAssetCopiesDiskStateBackIntoDirtyAsset()
        {
            EnsureTestFolder();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(TestFolder + "/RestoreTarget.asset");
            RestoreTargetAsset asset = ScriptableObject.CreateInstance<RestoreTargetAsset>();
            asset.Value = 7;

            try
            {
                AssetDatabase.CreateAsset(asset, assetPath);
                AssetDatabase.SaveAssets();
                EditorUtility.ClearDirty(asset);

                asset.Value = 42;
                EditorUtility.SetDirty(asset);

                bool restored = SavedAssetRestoreUtility.RestoreFromSavedAsset(
                    asset,
                    "__SharedRestoreUtilityTemp_",
                    "Could not find saved test asset file at",
                    "Could not load saved test asset copy from",
                    "Could not discard unsaved test asset changes for");

                Assert.That(restored, Is.True);
                Assert.That(asset.Value, Is.EqualTo(7));
                Assert.That(EditorUtility.IsDirty(asset), Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        private static void EnsureTestFolder()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder("Assets", "__SharedRestoreUtilityTests");
            }
        }

    }

    internal sealed class RestoreTargetAsset : ScriptableObject
    {
        public int Value;
    }
}
