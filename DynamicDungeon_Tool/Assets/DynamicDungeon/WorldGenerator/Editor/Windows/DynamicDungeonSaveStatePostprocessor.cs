using UnityEditor;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class DynamicDungeonSaveStatePostprocessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            EditorApplication.delayCall -= DynamicDungeonEditorWindow.RefreshSaveStateForAllOpenWindows;
            EditorApplication.delayCall += DynamicDungeonEditorWindow.RefreshSaveStateForAllOpenWindows;
            return paths;
        }
    }
}
