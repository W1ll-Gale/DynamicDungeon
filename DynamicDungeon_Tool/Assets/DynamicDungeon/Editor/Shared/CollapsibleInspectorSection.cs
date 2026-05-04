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

            Rect clickRect = new Rect(headerRect.x, headerRect.y, headerRect.width, headerRect.height);
            if (GUI.Button(clickRect, GUIContent.none, GUIStyle.none))
            {
                expanded = !expanded;
            }

            Rect arrowRect = new Rect(headerRect.x + 5.0f, headerRect.y, 16.0f, headerRect.height);
            GUI.Label(arrowRect, expanded ? "\u25BE" : "\u25B8", EditorStyles.label);

            Rect labelRect = new Rect(headerRect.x + 20.0f, headerRect.y, headerRect.width - 20.0f, headerRect.height);
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
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
