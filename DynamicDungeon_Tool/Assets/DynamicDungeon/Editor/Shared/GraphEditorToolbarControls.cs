using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Shared
{
    public static class GraphEditorToolbarControls
    {
        public static ToolbarToggle BuildPanelToggle(Texture icon, string tooltip, bool initialValue, EventCallback<ChangeEvent<bool>> callback)
        {
            ToolbarToggle toggle = new ToolbarToggle();
            toggle.text = string.Empty;
            toggle.tooltip = tooltip;
            toggle.style.width = 24.0f;
            toggle.style.minWidth = 24.0f;
            toggle.style.height = 20.0f;
            toggle.style.justifyContent = Justify.Center;
            toggle.style.alignItems = Align.Center;
            toggle.style.paddingLeft = 0.0f;
            toggle.style.paddingRight = 0.0f;
            toggle.style.paddingTop = 0.0f;
            toggle.style.paddingBottom = 0.0f;
            toggle.style.marginLeft = 3.0f;
            toggle.style.marginRight = 0.0f;
            toggle.style.borderLeftWidth = 0.0f;
            toggle.style.borderTopWidth = 0.0f;
            toggle.style.borderRightWidth = 0.0f;
            toggle.style.borderBottomWidth = 0.0f;
            toggle.style.backgroundColor = Color.clear;
            toggle.style.unityBackgroundImageTintColor = Color.clear;
            toggle.style.borderTopLeftRadius = 0.0f;
            toggle.style.borderTopRightRadius = 0.0f;
            toggle.style.borderBottomLeftRadius = 0.0f;
            toggle.style.borderBottomRightRadius = 0.0f;

            Image image = new Image();
            image.image = icon;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.width = 16.0f;
            image.style.height = 16.0f;
            image.pickingMode = PickingMode.Ignore;
            toggle.Add(image);

            toggle.SetValueWithoutNotify(initialValue);
            ApplyPanelToggleVisualState(toggle, image, initialValue);
            toggle.RegisterValueChangedCallback(evt => ApplyPanelToggleVisualState(toggle, image, evt.newValue));
            toggle.RegisterValueChangedCallback(callback);
            return toggle;
        }

        public static ToolbarButton BuildIconButton(Texture icon, string tooltip, Action clicked)
        {
            ToolbarButton button = new ToolbarButton(clicked);
            button.text = string.Empty;
            button.tooltip = tooltip;
            button.style.width = 24.0f;
            button.style.minWidth = 24.0f;
            button.style.height = 20.0f;
            button.style.justifyContent = Justify.Center;
            button.style.alignItems = Align.Center;
            button.style.paddingLeft = 0.0f;
            button.style.paddingRight = 0.0f;
            button.style.paddingTop = 0.0f;
            button.style.paddingBottom = 0.0f;
            button.style.marginLeft = 3.0f;
            button.style.marginRight = 0.0f;
            button.style.borderLeftWidth = 0.0f;
            button.style.borderTopWidth = 0.0f;
            button.style.borderRightWidth = 0.0f;
            button.style.borderBottomWidth = 0.0f;
            button.style.backgroundColor = Color.clear;
            button.style.unityBackgroundImageTintColor = Color.clear;

            Image image = new Image();
            image.image = icon;
            image.scaleMode = ScaleMode.ScaleToFit;
            image.style.width = 16.0f;
            image.style.height = 16.0f;
            image.pickingMode = PickingMode.Ignore;
            image.tintColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            button.Add(image);
            return button;
        }

        public static Texture2D LoadInspectorIcon()
        {
            return EditorGUIUtility.IconContent("UnityEditor.InspectorWindow").image as Texture2D;
        }

        public static Texture2D LoadMiniMapIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("d_ViewToolZoom").image as Texture2D;
            return icon != null ? icon : EditorGUIUtility.IconContent("ViewToolZoom").image as Texture2D;
        }

        public static Texture2D LoadDiagnosticsIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image as Texture2D;
            return icon != null ? icon : EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow").image as Texture2D;
        }

        public static Texture2D LoadDocsIcon()
        {
            Texture2D icon = EditorGUIUtility.IconContent("_Help").image as Texture2D;
            return icon != null ? icon : EditorGUIUtility.IconContent("console.infoicon").image as Texture2D;
        }

        private static void ApplyPanelToggleVisualState(ToolbarToggle toggle, Image image, bool isActive)
        {
            toggle.style.opacity = isActive ? 1.0f : 0.65f;
            image.tintColor = isActive
                ? new Color(0.92f, 0.92f, 0.92f, 1.0f)
                : new Color(0.72f, 0.72f, 0.72f, 1.0f);
        }
    }
}
