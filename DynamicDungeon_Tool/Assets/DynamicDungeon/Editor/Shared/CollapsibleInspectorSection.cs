using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Shared
{
    public static class CollapsibleInspectorSection
    {
        private const float HeaderHeight = 20.0f;
        private const float HeaderTopPadding = 1.0f;
        private const float HeaderBottomPadding = 1.0f;

        private static readonly Color HeaderBackground = new Color(0.20f, 0.20f, 0.20f, 1.0f);
        private static readonly Color HeaderBorder = new Color(0.13f, 0.13f, 0.13f, 1.0f);

        public static bool Begin(string key, string title, bool defaultExpanded = true)
        {
            bool expanded = SessionState.GetBool(key, defaultExpanded);
            Rect headerRect = EditorGUILayout.GetControlRect(false, HeaderHeight + HeaderTopPadding + HeaderBottomPadding);
            headerRect.y += HeaderTopPadding;
            headerRect.height = HeaderHeight;

            EditorGUI.DrawRect(headerRect, HeaderBackground);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1.0f, headerRect.width, 1.0f), HeaderBorder);

            Rect foldoutRect = new Rect(headerRect.x + 4.0f, headerRect.y, headerRect.width - 4.0f, headerRect.height);
            expanded = EditorGUI.Foldout(foldoutRect, expanded, title, true, EditorStyles.foldout);
            SessionState.SetBool(key, expanded);

            if (!expanded)
            {
                return false;
            }

            EditorGUI.indentLevel++;
            GUILayout.Space(2.0f);
            return true;
        }

        public static void End()
        {
            if (EditorGUI.indentLevel > 0)
            {
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(4.0f);
        }
    }
}
