using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Shared
{
    public static class ComponentHeaderControls
    {
        public static void DrawScriptlessHeader(Object target, string title)
        {
            Behaviour behaviour = target as Behaviour;
            Rect rect = EditorGUILayout.GetControlRect(false, 22.0f);
            rect.x += 2.0f;
            rect.width -= 4.0f;

            Rect toggleRect = new Rect(rect.x, rect.y + 3.0f, 16.0f, 16.0f);
            Rect labelRect = new Rect(rect.x + 22.0f, rect.y + 2.0f, rect.width - 22.0f, rect.height - 2.0f);

            if (behaviour != null)
            {
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUI.Toggle(toggleRect, behaviour.enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(behaviour, "Toggle Component");
                    behaviour.enabled = enabled;
                    EditorUtility.SetDirty(behaviour);
                }
            }

            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
        }
    }
}
