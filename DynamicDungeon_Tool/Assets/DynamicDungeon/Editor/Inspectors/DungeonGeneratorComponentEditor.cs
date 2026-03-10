using DynamicDungeon.Runtime.Component;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Inspectors
{
    [CustomEditor(typeof(DungeonGeneratorComponent))]
    public sealed class DungeonGeneratorComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _generateOnStartProperty;
        private SerializedProperty _seedModeProperty;
        private SerializedProperty _stableSeedProperty;
        private SerializedProperty _worldWidthProperty;
        private SerializedProperty _worldHeightProperty;
        private SerializedProperty _gridProperty;
        private SerializedProperty _layerDefinitionsProperty;
        private SerializedProperty _biomeProperty;
        private SerializedProperty _intChannelNameProperty;
        private SerializedProperty _tilemapOffsetProperty;

        private void OnEnable()
        {
            _generateOnStartProperty = serializedObject.FindProperty("_generateOnStart");
            _seedModeProperty = serializedObject.FindProperty("_seedMode");
            _stableSeedProperty = serializedObject.FindProperty("_stableSeed");
            _worldWidthProperty = serializedObject.FindProperty("_worldWidth");
            _worldHeightProperty = serializedObject.FindProperty("_worldHeight");
            _gridProperty = serializedObject.FindProperty("_grid");
            _layerDefinitionsProperty = serializedObject.FindProperty("_layerDefinitions");
            _biomeProperty = serializedObject.FindProperty("_biome");
            _intChannelNameProperty = serializedObject.FindProperty("_intChannelName");
            _tilemapOffsetProperty = serializedObject.FindProperty("_tilemapOffset");

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        public override void OnInspectorGUI()
        {
            DungeonGeneratorComponent component = (DungeonGeneratorComponent)target;

            serializedObject.Update();

            EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_generateOnStartProperty);
            EditorGUILayout.PropertyField(_seedModeProperty);

            SeedMode seedMode = (SeedMode)_seedModeProperty.enumValueIndex;
            if (seedMode == SeedMode.Stable)
            {
                EditorGUILayout.PropertyField(_stableSeedProperty);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_worldWidthProperty);
            EditorGUILayout.PropertyField(_worldHeightProperty);
            EditorGUILayout.PropertyField(_intChannelNameProperty);
            EditorGUILayout.PropertyField(_tilemapOffsetProperty);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_gridProperty);
            EditorGUILayout.PropertyField(_biomeProperty);
            EditorGUILayout.PropertyField(_layerDefinitionsProperty, true);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", component.StatusLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Generate"))
            {
                component.Generate();
            }

            if (GUILayout.Button("Cancel"))
            {
                component.CancelGeneration();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void OnEditorUpdate()
        {
            DungeonGeneratorComponent component = target as DungeonGeneratorComponent;
            if (component != null && component.IsGenerating)
            {
                Repaint();
            }
        }
    }
}
