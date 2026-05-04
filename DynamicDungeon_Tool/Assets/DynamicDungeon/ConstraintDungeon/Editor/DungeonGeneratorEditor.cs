using UnityEngine;
using UnityEditor;
using System.IO;
using DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner;
using DynamicDungeon.Editor.Shared;

namespace DynamicDungeon.ConstraintDungeon
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : UnityEditor.Editor
    {
        private const float CompactButtonWidth = 118.0f;
        private const string HeaderTitle = "Constraint Dungeon Generator";
        private GUIStyle _mutedMiniLabelStyle;

        protected override void OnHeaderGUI()
        {
            ComponentHeaderControls.DrawScriptlessHeader(target, HeaderTitle);
        }

        public override void OnInspectorGUI()
        {
            DungeonGenerator gen = (DungeonGenerator)target;

            EnsureStyles();
            serializedObject.Update();

            DrawFlowAssetSection(gen);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generationMode"));
            DrawGenerationSettingsSection();

            if (gen.generationMode == DungeonGenerationMode.FlowGraph)
            {
                DrawFlowGraphSettingsSection(gen);

                if (gen.dungeonFlow != null)
                {
                    DrawInlineDefaults(gen);
                }
            }
            else
            {
                DrawOrganicSettings();
            }

            GenerationInspectorAction generationAction = DrawGenerationControls(gen);
            if (generationAction == GenerationInspectorAction.Generate)
            {
                gen.Generate();
            }
            else if (generationAction == GenerationInspectorAction.Clear)
            {
                gen.Clear();
            }
            else if (generationAction == GenerationInspectorAction.Cancel)
            {
                gen.CancelGeneration();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureStyles()
        {
            if (_mutedMiniLabelStyle == null)
            {
                _mutedMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel);
                _mutedMiniLabelStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            }
        }

        private void DrawFlowAssetSection(DungeonGenerator gen)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("dungeonFlow"), new GUIContent("Dungeon Flow"));

            using (new EditorGUI.DisabledScope(gen.dungeonFlow == null))
            {
                if (GUILayout.Button("Open Designer", GUILayout.Width(CompactButtonWidth)))
                {
                    DungeonDesignerWindow.Open(gen.dungeonFlow);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (gen.dungeonFlow == null)
            {
                if (GUILayout.Button("Create New Dungeon Flow", GUILayout.Height(30)))
                {
                    CreateDungeonFlow(gen);
                }
            }
        }

        private void DrawGenerationSettingsSection()
        {
            if (!BeginSection("Generation Settings"))
            {
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("layoutAttempts"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSearchSteps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generateOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDiagnostics"));

            EndSection();
        }

        private void DrawFlowGraphSettingsSection(DungeonGenerator gen)
        {
            if (!BeginSection("Flow Graph Settings"))
            {
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("useRandomFlowSeed"));
            using (new EditorGUI.DisabledScope(serializedObject.FindProperty("useRandomFlowSeed").boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flowSeed"));
            }

            if (gen.dungeonFlow == null)
            {
                EditorGUILayout.HelpBox("Assign a Dungeon Flow asset to enable flow graph generation.", MessageType.Error);
            }

            EndSection();
        }

        public override bool RequiresConstantRepaint()
        {
            DungeonGenerator gen = (DungeonGenerator)target;
            return gen != null && gen.IsGenerating;
        }

        private GenerationInspectorAction DrawGenerationControls(DungeonGenerator gen)
        {
            if (!BeginSection("Generation"))
            {
                return GenerationInspectorAction.None;
            }

            GenerationInspectorAction action = GenerationInspectorControls.Draw(
                new GenerationInspectorOptions
                {
                    GenerateLabel = "GENERATE DUNGEON",
                    GeneratingLabel = "GENERATING...",
                    ClearLabel = "CLEAR DUNGEON",
                    Status = gen.GenerationStatus,
                    Progress = gen.GenerationProgress,
                    CanGenerate = !gen.IsGenerating,
                    CanClear = !gen.IsGenerating,
                    IsGenerating = gen.IsGenerating,
                    ShouldShowProgress = gen.ShouldShowGenerationProgress
                });

            EditorGUILayout.LabelField(BuildStatusHint(gen), _mutedMiniLabelStyle);
            EndSection();
            return action;
        }

        private void DrawOrganicSettings()
        {
            SerializedProperty organicProp = serializedObject.FindProperty("organicSettings");
            if (organicProp == null)
            {
                return;
            }

            if (!BeginSection("Organic Growth Settings"))
            {
                return;
            }

            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("useRoomCountRange"), new GUIContent("Use Room Count Range"));
            if (organicProp.FindPropertyRelative("useRoomCountRange").boolValue)
            {
                EditorGUI.indentLevel++;
                SerializedProperty minRoomCount = organicProp.FindPropertyRelative("minRoomCount");
                SerializedProperty maxRoomCount = organicProp.FindPropertyRelative("maxRoomCount");
                minRoomCount.intValue = Mathf.Max(0, EditorGUILayout.IntField("Min Rooms", minRoomCount.intValue));
                maxRoomCount.intValue = Mathf.Max(minRoomCount.intValue, EditorGUILayout.IntField("Max Rooms", maxRoomCount.intValue));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("targetRoomCount"));
            }

            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("useRandomSeed"));
            using (new EditorGUI.DisabledScope(organicProp.FindPropertyRelative("useRandomSeed").boolValue))
            {
                EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("seed"));
            }
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("maxCorridorChain"));
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("corridorChance"));
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("branchingProbability"));
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("branchingBias"));
            if (organicProp.FindPropertyRelative("branchingBias").enumValueIndex != (int)OrganicBranchingBias.Balanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("branchingBiasStrength"), new GUIContent("Bias Strength"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("useDirectionalGrowthHeuristic"), new GUIContent("Directional Growth Heuristic"));
            if (organicProp.FindPropertyRelative("useDirectionalGrowthHeuristic").boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("preferredGrowthDirection"), new GUIContent("Preferred Direction"));
                EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("directionalGrowthBias"), new GUIContent("Direction Bias"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("startPrefab"));
            EditorGUILayout.PropertyField(organicProp.FindPropertyRelative("endPrefab"));

            GUILayout.Space(8);
            DrawOrganicTemplateEntries(organicProp.FindPropertyRelative("templates"));

            EndSection();
        }

        private void DrawOrganicTemplateEntries(SerializedProperty templatesProp)
        {
            if (templatesProp == null)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Templates", EditorStyles.boldLabel);
            if (GUILayout.Button("Add", GUILayout.Width(60)))
            {
                int index = templatesProp.arraySize;
                templatesProp.InsertArrayElementAtIndex(index);
                SerializedProperty entry = templatesProp.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("prefab").objectReferenceValue = null;
                entry.FindPropertyRelative("enabled").boolValue = true;
                entry.FindPropertyRelative("weight").floatValue = 1f;
                entry.FindPropertyRelative("requiredMinimumCount").intValue = 0;
                entry.FindPropertyRelative("maximumCount").intValue = 0;
                entry.FindPropertyRelative("useDirectionBias").boolValue = false;
                entry.FindPropertyRelative("preferredDirection").enumValueIndex = (int)FacingDirection.East;
                entry.FindPropertyRelative("directionBias").floatValue = 0.5f;
            }
            EditorGUILayout.EndHorizontal();

            if (templatesProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Add room, corridor, and entrance prefabs for random generation.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(18);
            EditorGUILayout.LabelField("Prefab", GUILayout.MinWidth(130));
            EditorGUILayout.LabelField("On", GUILayout.Width(28));
            EditorGUILayout.LabelField("Weight", GUILayout.Width(58));
            EditorGUILayout.LabelField("Req", GUILayout.Width(42));
            EditorGUILayout.LabelField("Max", GUILayout.Width(42));
            EditorGUILayout.LabelField("Bias", GUILayout.Width(34));
            GUILayout.Space(28);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < templatesProp.arraySize; i++)
            {
                SerializedProperty entry = templatesProp.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((i + 1).ToString(), GUILayout.Width(18));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("prefab"), GUIContent.none, GUILayout.MinWidth(130));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("enabled"), GUIContent.none, GUILayout.Width(28));

                SerializedProperty weightProp = entry.FindPropertyRelative("weight");
                weightProp.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField(weightProp.floatValue, GUILayout.Width(58)));

                SerializedProperty requiredProp = entry.FindPropertyRelative("requiredMinimumCount");
                requiredProp.intValue = Mathf.Max(0, EditorGUILayout.IntField(requiredProp.intValue, GUILayout.Width(42)));

                SerializedProperty maxProp = entry.FindPropertyRelative("maximumCount");
                maxProp.intValue = Mathf.Max(0, EditorGUILayout.IntField(maxProp.intValue, GUILayout.Width(42)));

                SerializedProperty useDirectionBiasProp = entry.FindPropertyRelative("useDirectionBias");
                useDirectionBiasProp.boolValue = EditorGUILayout.Toggle(useDirectionBiasProp.boolValue, GUILayout.Width(34));

                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    templatesProp.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();

                if (useDirectionBiasProp.boolValue)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(18);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("preferredDirection"), new GUIContent("Preferred Direction"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("directionBias"), new GUIContent("Direction Bias"));
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawInlineDefaults(DungeonGenerator gen)
        {
            if (!BeginSection("Shared Templates"))
            {
                return;
            }
            
            SerializedObject flowSO = new SerializedObject(gen.dungeonFlow);
            flowSO.Update();
            
            SerializedProperty defaultsProp = flowSO.FindProperty("defaultTemplates");
            if (defaultsProp != null)
            {
                EditorGUILayout.PropertyField(defaultsProp, true);
            }
            
            flowSO.ApplyModifiedProperties();
            EndSection();
        }

        private static string BuildStatusHint(DungeonGenerator gen)
        {
            if (gen.IsGenerating)
            {
                return "Generation is running asynchronously. Progress and cancel controls appear if it takes more than a moment.";
            }

            return "Use Generate for an editor-time run, or enable Generate On Start for runtime generation.";
        }

        private static bool BeginSection(string title)
        {
            return CollapsibleInspectorSection.Begin("DynamicDungeon.ConstraintDungeonGenerator." + title, title);
        }

        private static void EndSection()
        {
            CollapsibleInspectorSection.End();
        }

        private void CreateDungeonFlow(DungeonGenerator gen)
        {
            string folder = ConstraintDungeonAssetPaths.DungeonFlowFolder;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NewDungeonFlow.asset");
            DungeonFlow asset = ScriptableObject.CreateInstance<DungeonFlow>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            gen.dungeonFlow = asset;
            EditorUtility.SetDirty(gen);
            
            EditorGUIUtility.PingObject(asset);
        }
    }
}
