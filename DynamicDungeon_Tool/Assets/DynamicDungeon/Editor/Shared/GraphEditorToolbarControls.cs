using System;
using System.IO;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Shared
{
    public static class GraphEditorToolbarControls
    {
        public static void ApplyStandardToolbarStyle(Toolbar toolbar)
        {
            toolbar.style.height = 24.0f;
            toolbar.style.minHeight = 24.0f;
            toolbar.style.paddingLeft = 6.0f;
            toolbar.style.paddingRight = 4.0f;
            toolbar.style.paddingTop = 0.0f;
            toolbar.style.paddingBottom = 0.0f;
            toolbar.style.alignItems = Align.Center;
        }

        public static ToolbarToggle BuildAutoSaveToggle(bool initialValue, string tooltip, EventCallback<ChangeEvent<bool>> callback)
        {
            ToolbarToggle toggle = new ToolbarToggle();
            toggle.text = "Auto Save";
            toggle.SetValueWithoutNotify(initialValue);
            toggle.RegisterValueChangedCallback(callback);
            toggle.style.marginRight = 8.0f;
            toggle.tooltip = tooltip;
            return toggle;
        }

        public static Label BuildStatusLabel(string initialText)
        {
            Label label = new Label(initialText);
            label.style.fontSize = 11.0f;
            label.style.color = new Color(0.76f, 0.76f, 0.76f, 1.0f);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginRight = 8.0f;
            return label;
        }

        public static Label BuildSaveStateLabel()
        {
            Label label = new Label();
            label.style.fontSize = 11.0f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.minWidth = 58.0f;
            label.style.marginRight = 8.0f;
            return label;
        }

        public static ToolbarButton BuildDiscardButton(string tooltip, Action clicked)
        {
            ToolbarButton button = new ToolbarButton(clicked);
            button.text = "Discard";
            button.tooltip = tooltip;
            button.style.marginRight = 8.0f;
            return button;
        }

        public static VisualElement BuildToolbarSpacer()
        {
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1.0f;
            return spacer;
        }

        public static VisualElement BuildPanelToggleGroup()
        {
            VisualElement toggleGroup = new VisualElement();
            toggleGroup.style.flexDirection = FlexDirection.Row;
            toggleGroup.style.alignItems = Align.Center;
            return toggleGroup;
        }

        public static void SetButtonEnabledWithOpacity(VisualElement button, bool isEnabled)
        {
            if (button == null)
            {
                return;
            }

            button.SetEnabled(isEnabled);
            button.style.opacity = isEnabled ? 1.0f : 0.45f;
        }

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

    public static class EditorPreferenceUtility
    {
        public static T LoadJson<T>(string prefsKey, T defaultValue)
            where T : class
        {
            string json = EditorUserSettings.GetConfigValue(prefsKey);
            return string.IsNullOrWhiteSpace(json)
                ? defaultValue
                : (JsonUtility.FromJson<T>(json) ?? defaultValue);
        }

        public static void SaveJson<T>(string prefsKey, T value)
        {
            EditorUserSettings.SetConfigValue(prefsKey, JsonUtility.ToJson(value));
        }
    }

    public static class SavedAssetRestoreUtility
    {
        public static bool RestoreFromSavedAsset<T>(
            T asset,
            string tempAssetPrefix,
            string missingSavedAssetLogPrefix,
            string loadSavedCopyLogPrefix,
            string discardFailureLogPrefix)
            where T : UnityEngine.Object
        {
            if (asset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return false;
            }

            string sourceAbsolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(sourceAbsolutePath))
            {
                Debug.LogError(missingSavedAssetLogPrefix + " '" + sourceAbsolutePath + "'.");
                return false;
            }

            string tempDirectory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(tempDirectory))
            {
                tempDirectory = "Assets";
            }

            string safePrefix = string.IsNullOrWhiteSpace(tempAssetPrefix) ? "__DynamicDungeonDiscardTemp_" : tempAssetPrefix;
            string tempAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                (tempDirectory + "/" + safePrefix + Guid.NewGuid().ToString("N") + ".asset").Replace("\\", "/"));

            try
            {
                File.Copy(sourceAbsolutePath, Path.GetFullPath(Path.Combine(projectRoot, tempAssetPath)), false);
                AssetDatabase.ImportAsset(tempAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                T savedAsset = AssetDatabase.LoadAssetAtPath<T>(tempAssetPath);
                if (savedAsset == null)
                {
                    Debug.LogError(loadSavedCopyLogPrefix + " '" + tempAssetPath + "'.");
                    return false;
                }

                EditorUtility.CopySerialized(savedAsset, asset);
                asset.name = Path.GetFileNameWithoutExtension(assetPath);
                Undo.ClearUndo(asset);
                EditorUtility.ClearDirty(asset);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(discardFailureLogPrefix + " '" + assetPath + "': " + exception.Message);
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempAssetPath))
                {
                    AssetDatabase.DeleteAsset(tempAssetPath);
                }
            }
        }
    }

    public static class DiagnosticsPanelUtility
    {
        public static DiagnosticsPanel BuildDockedPanel(
            Func<string, string> resolveElementName,
            Func<string, bool> focusElement,
            float expandedHeight,
            bool isCollapsed,
            Color borderTopColor)
        {
            DiagnosticsPanel diagnosticsPanel = new DiagnosticsPanel();
            diagnosticsPanel.SetContext(resolveElementName, focusElement);
            diagnosticsPanel.style.flexShrink = 0.0f;
            diagnosticsPanel.style.borderTopWidth = 1.0f;
            diagnosticsPanel.style.borderTopColor = borderTopColor;
            diagnosticsPanel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            diagnosticsPanel.style.paddingLeft = 8.0f;
            diagnosticsPanel.style.paddingRight = 8.0f;
            diagnosticsPanel.style.paddingTop = 0.0f;
            diagnosticsPanel.style.paddingBottom = 6.0f;
            diagnosticsPanel.SetExpandedHeight(expandedHeight);
            diagnosticsPanel.SetCollapsed(isCollapsed);
            return diagnosticsPanel;
        }
    }

    public static class GraphViewShellUtility
    {
        public static void ConfigureDefaultGraphView(GraphView graphView)
        {
            graphView.SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            graphView.AddManipulator(new ContentDragger());
            graphView.AddManipulator(new SelectionDragger());
            graphView.AddManipulator(new RectangleSelector());
        }

        public static void GetViewportState(GraphView graphView, out Vector3 scrollOffset, out float zoomScale)
        {
            scrollOffset = graphView.contentViewContainer.resolvedStyle.translate;
            zoomScale = graphView.contentViewContainer.resolvedStyle.scale.value.x;
        }

        public static void RestoreViewportState(GraphView graphView, Vector3 scrollOffset, float zoomScale)
        {
            float safeZoom = Mathf.Clamp(
                zoomScale,
                ContentZoomer.DefaultMinScale,
                ContentZoomer.DefaultMaxScale);

            graphView.UpdateViewTransform(scrollOffset, new Vector3(safeZoom, safeZoom, 1.0f));
        }
    }

    public static class InspectorSharedControls
    {
        public static GUIStyle GetMutedMiniLabelStyle(GUIStyle cachedStyle)
        {
            if (cachedStyle != null)
            {
                return cachedStyle;
            }

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            return style;
        }

        public static void DrawSeedSettings(
            SerializedProperty seedModeProperty,
            SerializedProperty stableSeedProperty,
            long lastSeed,
            bool showLastSeedWhenRandom)
        {
            EditorGUILayout.PropertyField(seedModeProperty, new GUIContent("Seed Mode"));

            if (IsStableSeedMode(seedModeProperty))
            {
                EditorGUILayout.PropertyField(stableSeedProperty, new GUIContent("Stable Seed"));
                return;
            }

            if (showLastSeedWhenRandom)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LongField(new GUIContent("Last Seed", "Seed used in the most recent generation run."), lastSeed);
                EditorGUI.EndDisabledGroup();
            }
        }

        private static bool IsStableSeedMode(SerializedProperty seedModeProperty)
        {
            if (seedModeProperty == null || seedModeProperty.propertyType != SerializedPropertyType.Enum)
            {
                return false;
            }

            string[] enumNames = seedModeProperty.enumNames;
            int index = seedModeProperty.enumValueIndex;
            return index >= 0 &&
                index < enumNames.Length &&
                string.Equals(enumNames[index], "Stable", StringComparison.Ordinal);
        }
    }
}
