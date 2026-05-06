using DynamicDungeon.Runtime;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    public static class DynamicDungeonDocumentationMenu
    {
        private const string DocumentationUrl = "https://dynamicdungeon.pages.dev/docs/introduction";

        [MenuItem(DynamicDungeonMenuPaths.Documentation)]
        public static void OpenDocumentation()
        {
            Application.OpenURL(DocumentationUrl);
        }
    }
}
