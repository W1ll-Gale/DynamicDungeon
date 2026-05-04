using UnityEngine;
using UnityEditor;
using System.IO;

namespace DynamicDungeon.ConstraintDungeon
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : UnityEditor.Editor
    {
        private string _newName;

        public override void OnInspectorGUI()
        {
            DungeonGenerator gen = (DungeonGenerator)target;

            serializedObject.Update();
            
            // Draw mode selection explicitly
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generationMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("layoutAttempts"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSearchSteps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generateOnStart"));
            
            GUILayout.Space(10);

            if (gen.generationMode == DungeonGenerationMode.FlowGraph)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("useRandomFlowSeed"));
                using (new EditorGUI.DisabledScope(serializedObject.FindProperty("useRandomFlowSeed").boolValue))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("flowSeed"));
                }

                if (gen.dungeonFlow == null)
                {
                    EditorGUILayout.HelpBox("No Dungeon Flow assigned! Use the Flow Designer or create one below.", MessageType.Error);
                    if (GUILayout.Button("Create New Dungeon Flow", GUILayout.Height(30))) CreateDungeonFlow(gen);
                }
                else
                {
                    DrawAssetRenaming(gen);
                    DrawInlineDefaults(gen);
                }
            }
            else
            {
                DrawOrganicSettings();
            }

            GUILayout.Space(20);

            DrawGenerationControls(gen);

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("CLEAR DUNGEON", GUILayout.Height(30)) && !gen.IsGenerating)
            {
                gen.Clear();
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Open Dungeon Designer"))
            {
                EditorApplication.ExecuteMenuItem(ConstraintDungeonMenuPaths.DungeonDesigner);
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        public override bool RequiresConstantRepaint()
        {
            DungeonGenerator gen = (DungeonGenerator)target;
            return gen != null && gen.IsGenerating;
        }

        private void DrawGenerationControls(DungeonGenerator gen)
        {
            if (gen.ShouldShowGenerationProgress)
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.35f);
                using (new EditorGUI.DisabledScope(true))
                {
                    GUILayout.Button("GENERATING...", GUILayout.Height(40));
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.HelpBox(gen.GenerationStatus, MessageType.Info);
                Rect progressRect = GUILayoutUtility.GetRect(18f, 18f, "TextField");
                EditorGUI.ProgressBar(progressRect, gen.GenerationProgress, $"{Mathf.RoundToInt(gen.GenerationProgress * 100f)}%");

                GUI.backgroundColor = new Color(1f, 0.6f, 0.35f);
                if (GUILayout.Button("CANCEL GENERATION", GUILayout.Height(30)))
                {
                    gen.CancelGeneration();
                }
                GUI.backgroundColor = Color.white;

                return;
            }

            GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
            if (GUILayout.Button("GENERATE DUNGEON", GUILayout.Height(40)) && !gen.IsGenerating)
            {
                gen.Generate();
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawOrganicSettings()
        {
            SerializedProperty organicProp = serializedObject.FindProperty("organicSettings");
            if (organicProp == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Organic Random Growth Settings", EditorStyles.boldLabel);

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

            EditorGUILayout.EndVertical();
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

        private void DrawAssetRenaming(DungeonGenerator gen)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Asset Management", EditorStyles.boldLabel);
            
            if (string.IsNullOrEmpty(_newName)) _newName = gen.dungeonFlow.name;

            EditorGUILayout.BeginHorizontal();
            _newName = EditorGUILayout.TextField("Flow Asset Name", _newName);
            if (GUILayout.Button("Rename", GUILayout.Width(60)))
            {
                string path = AssetDatabase.GetAssetPath(gen.dungeonFlow);
                string result = AssetDatabase.RenameAsset(path, _newName);
                if (!string.IsNullOrEmpty(result))
                {
                    Debug.LogError($"[DungeonGenerator] Rename failed: {result}");
                }
                else
                {
                    AssetDatabase.SaveAssets();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawInlineDefaults(DungeonGenerator gen)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Shared Templates (Global Defaults)", EditorStyles.boldLabel);
            
            SerializedObject flowSO = new SerializedObject(gen.dungeonFlow);
            flowSO.Update();
            
            SerializedProperty defaultsProp = flowSO.FindProperty("defaultTemplates");
            if (defaultsProp != null)
            {
                EditorGUILayout.PropertyField(defaultsProp, true);
            }
            
            flowSO.ApplyModifiedProperties();
            EditorGUILayout.EndVertical();
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
            _newName = asset.name;
            
            EditorGUIUtility.PingObject(asset);
        }
    }
}
