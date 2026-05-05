using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.IO;
using DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner;
using DynamicDungeon.Editor.Diagnostics;
using DynamicDungeon.Editor.Shared;

namespace DynamicDungeon.ConstraintDungeon
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : UnityEditor.Editor
    {
        private const float CompactButtonWidth = 118.0f;
        private const string HeaderTitle = "Constraint Dungeon Generator";
        private GUIStyle _mutedMiniLabelStyle;
        private DungeonGenerator _subscribedGen;

        protected override void OnHeaderGUI()
        {
            ComponentHeaderControls.DrawScriptlessHeader(target, HeaderTitle);
        }

        private void OnEnable()
        {
            _subscribedGen = target as DungeonGenerator;
            if (_subscribedGen != null)
            {
                _subscribedGen.OnGenerationCompleted += HandleAutoRunDiagnostics;
            }
        }

        private void OnDisable()
        {
            if (_subscribedGen != null)
            {
                _subscribedGen.OnGenerationCompleted -= HandleAutoRunDiagnostics;
                _subscribedGen = null;
            }
        }

        private void HandleAutoRunDiagnostics()
        {
            DungeonGenerator gen = _subscribedGen;
            if (gen == null) return;
            bool autoRun     = gen.autoRunMapDiagnostics;
            bool autoRebuild = gen.autoRebuildMapDiagnosticGrid;
            if (!autoRun && !autoRebuild) return;

            if (autoRun)
            {
                // Start the full async run on the next frame.
                EditorApplication.delayCall += () =>
                {
                    if (gen != null)
                        GeneratedMapDiagnosticsWindow.TriggerAutoFloodFill(null);
                };
            }
            else
            {
                // Rebuild synchronously right now so the DisplayProgressBar appears as an
                // immediate continuation of generation — no separate popup window.
                GeneratedMapDiagnosticsWindow.TriggerAutoRebuildGrid();
            }
        }

        private bool DrawDiagnosticsSection()
        {
            if (!BeginSection("Diagnostics"))
            {
                return false;
            }

            EditorGUILayout.LabelField("Open scene-map diagnostic tools for pathfinding, reachability, and island checks.", _mutedMiniLabelStyle);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRunMapDiagnostics"), new GUIContent("Auto Run After Generate", "Automatically opens the Map Diagnostics window and runs a flood-fill after each successful generation."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRebuildMapDiagnosticGrid"), new GUIContent("Auto Rebuild Grid After Generate", "Rebuilds the diagnostic grid immediately after generation completes (using the progress bar) so the grid is ready without any loading delay when you next run diagnostics."));
            GUILayout.Space(2.0f);
            bool openRequested = GUILayout.Button("Open Generated Map Diagnostics");
            EndSection();
            return openRequested;
        }

        public override void OnInspectorGUI()
        {
            DungeonGenerator gen = (DungeonGenerator)target;

            EnsureStyles();
            serializedObject.Update();

            DrawSourceAssetSection(gen);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generationMode"));
            DrawGenerationSettingsSection(gen);

            if (gen.generationMode == DungeonGenerationMode.OrganicGrowth)
            {
                DrawOrganicSettings();
            }

            bool openDiagnosticsRequested = DrawDiagnosticsSection();
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

            if (openDiagnosticsRequested)
            {
                GeneratedMapDiagnosticsWindow.OpenWindow();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void EnsureStyles()
        {
            _mutedMiniLabelStyle = InspectorSharedControls.GetMutedMiniLabelStyle(_mutedMiniLabelStyle);
        }

        private void DrawSourceAssetSection(DungeonGenerator gen)
        {
            if (gen.generationMode == DungeonGenerationMode.FlowGraph)
            {
                DrawFlowAssetSection(gen);
                return;
            }

            DrawOrganicSettingsAssetSection(gen);
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

        private void DrawOrganicSettingsAssetSection(DungeonGenerator gen)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("organicSettings"), new GUIContent("Organic Growth Profile"));

            using (new EditorGUI.DisabledScope(gen.organicSettings == null))
            {
                if (GUILayout.Button("Select Asset", GUILayout.Width(CompactButtonWidth)))
                {
                    Selection.activeObject = gen.organicSettings;
                    EditorGUIUtility.PingObject(gen.organicSettings);
                }
            }

            EditorGUILayout.EndHorizontal();

            if (gen.organicSettings == null)
            {
                if (GUILayout.Button("Create New Organic Growth Profile", GUILayout.Height(30)))
                {
                    CreateOrganicSettings(gen);
                }
            }
        }

        private void DrawGenerationSettingsSection(DungeonGenerator gen)
        {
            if (!BeginSection("Generation Settings"))
            {
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("layoutAttempts"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSearchSteps"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generateOnStart"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableDiagnostics"));

            if (gen.generationMode == DungeonGenerationMode.FlowGraph)
            {
                GUILayout.Space(6.0f);
                DrawFlowSeedSettings();

                if (gen.dungeonFlow == null)
                {
                    EditorGUILayout.HelpBox("Assign a Dungeon Flow asset to enable flow graph generation.", MessageType.Error);
                }
                else
                {
                    GUILayout.Space(8.0f);
                    DrawInlineDefaults(gen);
                }
            }
            else
            {
                GUILayout.Space(6.0f);
                DrawOrganicSeedSettings(gen);
            }

            EndSection();
        }

        private void DrawFlowSeedSettings()
        {
            DrawSharedSeedSettings((DungeonGenerator)target);
        }

        private void DrawSharedSeedSettings(DungeonGenerator gen)
        {
            InspectorSharedControls.DrawSeedSettings(
                serializedObject.FindProperty("seedMode"),
                serializedObject.FindProperty("stableSeed"),
                gen.LastUsedSeed,
                true);
        }

        private void DrawOrganicSeedSettings(DungeonGenerator gen)
        {
            if (gen.organicSettings == null)
            {
                EditorGUILayout.HelpBox("Assign an Organic Growth Profile asset to enable organic growth generation.", MessageType.Error);
                return;
            }

            DrawSharedSeedSettings(gen);
        }

        public override bool RequiresConstantRepaint()
        {
            DungeonGenerator gen = (DungeonGenerator)target;
            return gen != null && gen.IsGenerating;
        }

        private GenerationInspectorAction DrawGenerationControls(DungeonGenerator gen)
        {
            GUILayout.Space(8.0f);
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
            return action;
        }

        private void DrawOrganicSettings()
        {
            DungeonGenerator gen = (DungeonGenerator)target;
            if (gen.organicSettings == null)
            {
                if (BeginSection("Organic Growth Profile"))
                {
                    EditorGUILayout.HelpBox("Assign an Organic Growth Profile asset to enable organic growth generation.", MessageType.Error);
                    EndSection();
                }

                return;
            }

            SerializedObject settingsObject = new SerializedObject(gen.organicSettings);
            settingsObject.Update();

            if (!BeginSection("Organic Growth Profile"))
            {
                return;
            }

            SerializedProperty useRoomCountRange = settingsObject.FindProperty("useRoomCountRange");
            EditorGUILayout.PropertyField(useRoomCountRange, new GUIContent("Use Room Count Range"));
            if (useRoomCountRange.boolValue)
            {
                EditorGUI.indentLevel++;
                SerializedProperty minRoomCount = settingsObject.FindProperty("minRoomCount");
                SerializedProperty maxRoomCount = settingsObject.FindProperty("maxRoomCount");
                minRoomCount.intValue = Mathf.Max(0, EditorGUILayout.IntField("Min Rooms", minRoomCount.intValue));
                maxRoomCount.intValue = Mathf.Max(minRoomCount.intValue, EditorGUILayout.IntField("Max Rooms", maxRoomCount.intValue));
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.PropertyField(settingsObject.FindProperty("targetRoomCount"));
            }

            EditorGUILayout.PropertyField(settingsObject.FindProperty("maxCorridorChain"));
            EditorGUILayout.PropertyField(settingsObject.FindProperty("corridorChance"));
            EditorGUILayout.PropertyField(settingsObject.FindProperty("branchingProbability"));
            SerializedProperty branchingBias = settingsObject.FindProperty("branchingBias");
            EditorGUILayout.PropertyField(branchingBias);
            if (branchingBias.enumValueIndex != (int)OrganicBranchingBias.Balanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(settingsObject.FindProperty("branchingBiasStrength"), new GUIContent("Bias Strength"));
                EditorGUI.indentLevel--;
            }

            SerializedProperty useDirectionalGrowthHeuristic = settingsObject.FindProperty("useDirectionalGrowthHeuristic");
            EditorGUILayout.PropertyField(
                useDirectionalGrowthHeuristic,
                new GUIContent("Directional Growth Heuristic", "Biases which open door/frontier direction the organic solver expands next."));
            if (useDirectionalGrowthHeuristic.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(settingsObject.FindProperty("preferredGrowthDirection"), new GUIContent("Preferred Direction"));
                EditorGUILayout.PropertyField(settingsObject.FindProperty("directionalGrowthBias"), new GUIContent("Direction Bias"));
                EditorGUI.indentLevel--;
            }

            SerializedProperty useGlobalTemplateDirectionBias = settingsObject.FindProperty("useGlobalTemplateDirectionBias");
            EditorGUILayout.PropertyField(
                useGlobalTemplateDirectionBias,
                new GUIContent("Template Direction Bias", "Default directional weighting for template selection. Individual templates can override this."));
            if (useGlobalTemplateDirectionBias.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(settingsObject.FindProperty("globalTemplateDirection"), new GUIContent("Preferred Direction"));
                EditorGUILayout.PropertyField(settingsObject.FindProperty("globalTemplateDirectionBias"), new GUIContent("Bias Strength"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(settingsObject.FindProperty("startPrefab"));
            EditorGUILayout.PropertyField(settingsObject.FindProperty("endPrefab"));

            GUILayout.Space(8);
            SerializedProperty templatesProp = settingsObject.FindProperty("templates");
            if (templatesProp != null)
            {
                DrawTemplatePaletteList(settingsObject, templatesProp);
                DrawOrganicRequiredMinimumSummary(settingsObject, templatesProp);
            }

            settingsObject.ApplyModifiedProperties();
            EndSection();
        }

        private void DrawInlineDefaults(DungeonGenerator gen)
        {
            SerializedObject flowSO = new SerializedObject(gen.dungeonFlow);
            flowSO.Update();

            SerializedProperty defaultsProp = flowSO.FindProperty("defaultTemplates");
            if (defaultsProp != null)
            {
                DrawDefaultTemplateMappings(defaultsProp);
            }

            flowSO.ApplyModifiedProperties();
        }

        private static void DrawDefaultTemplateMappings(SerializedProperty defaultsProp)
        {
            EditorGUILayout.BeginHorizontal();
            defaultsProp.isExpanded = EditorGUILayout.Foldout(
                defaultsProp.isExpanded,
                new GUIContent("Default Templates", "Fallback room templates used by flow graph nodes when they do not define their own allowed templates."),
                true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(BuildDefaultTemplateSummary(defaultsProp), EditorStyles.miniLabel, GUILayout.Width(130.0f));
            EditorGUILayout.EndHorizontal();

            if (!defaultsProp.isExpanded)
            {
                return;
            }

            ReorderableList defaultList = new ReorderableList(defaultsProp.serializedObject, defaultsProp, true, false, true, true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) => DrawDefaultTemplateMappingElement(defaultsProp, rect, index),
                elementHeightCallback = index => GetDefaultTemplateMappingHeight(defaultsProp, index),
                onAddCallback = list =>
                {
                    int newIndex = defaultsProp.arraySize;
                    defaultsProp.InsertArrayElementAtIndex(newIndex);
                    SerializedProperty mappingProp = defaultsProp.GetArrayElementAtIndex(newIndex);
                    mappingProp.FindPropertyRelative("type").enumValueIndex = 0;
                    SerializedProperty templatesProp = mappingProp.FindPropertyRelative("templates");
                    if (templatesProp != null)
                    {
                        templatesProp.ClearArray();
                    }

                    mappingProp.isExpanded = false;
                }
            };

            defaultList.DoLayoutList();
        }

        private static void DrawDefaultTemplateMappingElement(SerializedProperty defaultsProp, Rect rect, int index)
        {
            SerializedProperty mappingProp = defaultsProp.GetArrayElementAtIndex(index);
            SerializedProperty typeProp = mappingProp.FindPropertyRelative("type");
            SerializedProperty templatesProp = mappingProp.FindPropertyRelative("templates");

            rect.y += 3.0f;
            rect.height = EditorGUIUtility.singleLineHeight;

            Rect foldoutRect = new Rect(rect.x + 18.0f, rect.y, 16.0f, rect.height);
            mappingProp.isExpanded = EditorGUI.Foldout(foldoutRect, mappingProp.isExpanded, GUIContent.none, true);

            Rect labelRect = new Rect(rect.x + 36.0f, rect.y, 64.0f, rect.height);
            Rect typeRect = new Rect(labelRect.xMax + 4.0f, rect.y, rect.width - labelRect.width - 108.0f, rect.height);
            Rect countRect = new Rect(typeRect.xMax + 4.0f, rect.y, 80.0f, rect.height);

            EditorGUI.LabelField(labelRect, "Room Type");
            EditorGUI.PropertyField(typeRect, typeProp, GUIContent.none);
            EditorGUI.LabelField(countRect, BuildTemplateCountLabel(templatesProp), EditorStyles.miniLabel);

            if (!mappingProp.isExpanded || templatesProp == null)
            {
                return;
            }

            ReorderableList nestedList = CreateDefaultTemplatePrefabList(templatesProp);
            float bodyHeight = nestedList.GetHeight() + 8.0f;
            Rect bodyRect = new Rect(
                rect.x + 20.0f,
                rect.yMax + 4.0f,
                rect.width - 20.0f,
                bodyHeight);
            GUI.Box(bodyRect, GUIContent.none, EditorStyles.helpBox);

            Rect listRect = new Rect(
                bodyRect.x + 6.0f,
                bodyRect.y + 4.0f,
                bodyRect.width - 12.0f,
                bodyRect.height - 8.0f);
            nestedList.DoList(listRect);
        }

        private static float GetDefaultTemplateMappingHeight(SerializedProperty defaultsProp, int index)
        {
            SerializedProperty mappingProp = defaultsProp.GetArrayElementAtIndex(index);
            float baseHeight = EditorGUIUtility.singleLineHeight + 6.0f;
            if (!mappingProp.isExpanded)
            {
                return baseHeight;
            }

            SerializedProperty templatesProp = mappingProp.FindPropertyRelative("templates");
            ReorderableList nestedList = CreateDefaultTemplatePrefabList(templatesProp);
            return baseHeight + 4.0f + nestedList.GetHeight() + 8.0f;
        }

        private static ReorderableList CreateDefaultTemplatePrefabList(SerializedProperty templatesProp)
        {
            ReorderableList templateList = new ReorderableList(templatesProp.serializedObject, templatesProp, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Templates"),
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    SerializedProperty elementProp = templatesProp.GetArrayElementAtIndex(index);
                    rect.y += 2.0f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.PropertyField(rect, elementProp, GUIContent.none);
                },
                onAddCallback = list =>
                {
                    int newIndex = templatesProp.arraySize;
                    templatesProp.InsertArrayElementAtIndex(newIndex);
                    templatesProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = null;
                }
            };

            return templateList;
        }

        private static string BuildDefaultTemplateSummary(SerializedProperty defaultsProp)
        {
            int templateCount = 0;
            for (int index = 0; index < defaultsProp.arraySize; index++)
            {
                SerializedProperty templatesProp = defaultsProp.GetArrayElementAtIndex(index).FindPropertyRelative("templates");
                templateCount += templatesProp != null ? templatesProp.arraySize : 0;
            }

            return defaultsProp.arraySize + " groups, " + templateCount + " templates";
        }

        private static string BuildTemplateCountLabel(SerializedProperty templatesProp)
        {
            if (templatesProp == null)
            {
                return "0 templates";
            }

            return templatesProp.arraySize == 1 ? "1 template" : templatesProp.arraySize + " templates";
        }

        private static void DrawTemplatePaletteList(SerializedObject settingsObject, SerializedProperty templatesProp)
        {
            EditorGUILayout.BeginHorizontal();
            templatesProp.isExpanded = EditorGUILayout.Foldout(
                templatesProp.isExpanded,
                new GUIContent("Template Palette", "Organic growth room templates and their weighted selection chances."),
                true);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(BuildTemplatePaletteSummary(templatesProp), EditorStyles.miniLabel, GUILayout.Width(130.0f));
            EditorGUILayout.EndHorizontal();

            if (!templatesProp.isExpanded)
            {
                return;
            }

            ReorderableList templateList = new ReorderableList(settingsObject, templatesProp, true, false, true, true)
            {
                drawElementCallback = (rect, index, isActive, isFocused) => DrawTemplatePaletteElement(templatesProp, rect, index),
                elementHeightCallback = index => GetTemplatePaletteElementHeight(templatesProp, index),
                onAddCallback = list =>
                {
                    int newIndex = templatesProp.arraySize;
                    templatesProp.InsertArrayElementAtIndex(newIndex);
                    SerializedProperty newElement = templatesProp.GetArrayElementAtIndex(newIndex);
                    newElement.FindPropertyRelative("prefab").objectReferenceValue = null;
                    newElement.FindPropertyRelative("enabled").boolValue = true;
                    newElement.FindPropertyRelative("weight").floatValue = 1.0f;
                    newElement.FindPropertyRelative("requiredMinimumCount").intValue = 0;
                    newElement.FindPropertyRelative("maximumCount").intValue = 0;
                    newElement.FindPropertyRelative("useDirectionBias").boolValue = false;
                    newElement.isExpanded = false;
                }
            };

            templateList.DoLayoutList();
        }

        private static void DrawTemplatePaletteElement(SerializedProperty templatesProp, Rect rect, int index)
        {
            SerializedProperty elementProp = templatesProp.GetArrayElementAtIndex(index);
            SerializedProperty prefabProp = elementProp.FindPropertyRelative("prefab");
            SerializedProperty enabledProp = elementProp.FindPropertyRelative("enabled");

            rect.y += 3.0f;
            rect.height = EditorGUIUtility.singleLineHeight;

            Rect foldoutRect = new Rect(rect.x + 18.0f, rect.y, 16.0f, rect.height);
            elementProp.isExpanded = EditorGUI.Foldout(foldoutRect, elementProp.isExpanded, GUIContent.none, true);

            Rect enabledRect = new Rect(rect.x + 36.0f, rect.y, 18.0f, rect.height);
            Rect enabledLabelRect = new Rect(enabledRect.xMax + 4.0f, rect.y, 22.0f, rect.height);
            Rect fieldRect = new Rect(
                enabledLabelRect.xMax + 4.0f,
                rect.y,
                rect.width - enabledRect.width - enabledLabelRect.width - 44.0f,
                rect.height);

            enabledProp.boolValue = EditorGUI.Toggle(enabledRect, enabledProp.boolValue);
            EditorGUI.LabelField(enabledLabelRect, "On");
            EditorGUI.PropertyField(fieldRect, prefabProp, GUIContent.none);

            if (!elementProp.isExpanded)
            {
                return;
            }

            string chanceInfo = BuildTemplateChanceInfo(templatesProp, index);
            float bodyHeight = GetTemplatePaletteDetailsHeight(elementProp, true);
            Rect bodyRect = new Rect(
                rect.x + 20.0f,
                rect.yMax + 4.0f,
                rect.width - 20.0f,
                bodyHeight + 8.0f);
            GUI.Box(bodyRect, GUIContent.none, EditorStyles.helpBox);

            Rect contentRect = new Rect(
                bodyRect.x + 6.0f,
                bodyRect.y + 4.0f,
                bodyRect.width - 12.0f,
                bodyRect.height - 8.0f);
            DrawTemplatePaletteDetails(elementProp, contentRect, chanceInfo);
        }

        internal static void DrawTemplatePaletteDetails(SerializedProperty elementProp, Rect contentRect, string chanceInfo)
        {
            Rect rowRect = new Rect(
                contentRect.x,
                contentRect.y,
                contentRect.width,
                EditorGUIUtility.singleLineHeight);

            SerializedProperty weightProp = elementProp.FindPropertyRelative("weight");
            SerializedProperty requiredProp = elementProp.FindPropertyRelative("requiredMinimumCount");
            SerializedProperty maximumProp = elementProp.FindPropertyRelative("maximumCount");
            SerializedProperty useDirectionBiasProp = elementProp.FindPropertyRelative("useDirectionBias");
            SerializedProperty preferredDirectionProp = elementProp.FindPropertyRelative("preferredDirection");
            SerializedProperty directionBiasProp = elementProp.FindPropertyRelative("directionBias");

            float originalLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(155.0f, rowRect.width * 0.48f);

            EditorGUI.PropertyField(rowRect, weightProp, new GUIContent("Weight"));
            weightProp.floatValue = Mathf.Max(0.0f, weightProp.floatValue);

            rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
            if (!string.IsNullOrEmpty(chanceInfo))
            {
                EditorGUI.LabelField(
                    rowRect,
                    new GUIContent("Selection Chance", "Calculated from enabled templates with assigned prefabs and positive weights. Direction bias is previewed for the shown growth direction; placement limits can still change the runtime choice."),
                    new GUIContent(chanceInfo));
                rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
            }

            EditorGUI.PropertyField(rowRect, requiredProp, new GUIContent("Required Minimum"));
            requiredProp.intValue = Mathf.Max(0, requiredProp.intValue);

            rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
            EditorGUI.PropertyField(rowRect, maximumProp, new GUIContent("Maximum Count"));
            maximumProp.intValue = Mathf.Max(0, maximumProp.intValue);

            rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
            EditorGUI.PropertyField(
                rowRect,
                useDirectionBiasProp,
                new GUIContent("Override Direction Bias", "When enabled, this template uses its own directional weighting instead of the profile default."));

            if (useDirectionBiasProp.boolValue)
            {
                rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
                EditorGUI.PropertyField(rowRect, preferredDirectionProp, new GUIContent("Preferred Direction"));

                rowRect.y += EditorGUIUtility.singleLineHeight + 2.0f;
                EditorGUI.PropertyField(rowRect, directionBiasProp, new GUIContent("Direction Bias"));
            }

            EditorGUIUtility.labelWidth = originalLabelWidth;
        }

        private static float GetTemplatePaletteElementHeight(SerializedProperty templatesProp, int index)
        {
            SerializedProperty elementProp = templatesProp.GetArrayElementAtIndex(index);
            float baseHeight = EditorGUIUtility.singleLineHeight + 6.0f;
            if (!elementProp.isExpanded)
            {
                return baseHeight;
            }

            return baseHeight + 4.0f + GetTemplatePaletteDetailsHeight(elementProp, true) + 8.0f;
        }

        internal static float GetTemplatePaletteDetailsHeight(SerializedProperty elementProp, bool includeChanceInfo)
        {
            SerializedProperty useDirectionBiasProp = elementProp.FindPropertyRelative("useDirectionBias");
            int rowCount = useDirectionBiasProp != null && useDirectionBiasProp.boolValue ? 6 : 4;
            if (includeChanceInfo)
            {
                rowCount++;
            }

            return rowCount * EditorGUIUtility.singleLineHeight + (rowCount - 1) * 2.0f;
        }

        private static string BuildTemplatePaletteSummary(SerializedProperty templatesProp)
        {
            int activeCount = 0;
            for (int index = 0; index < templatesProp.arraySize; index++)
            {
                if (GetEffectiveTemplateWeight(templatesProp.GetArrayElementAtIndex(index)) > 0.0f)
                {
                    activeCount++;
                }
            }

            return activeCount + "/" + templatesProp.arraySize + " active";
        }

        private static string BuildTemplateChanceInfo(SerializedProperty templatesProp, int index)
        {
            SerializedProperty elementProp = templatesProp.GetArrayElementAtIndex(index);
            SerializedProperty prefabProp = elementProp.FindPropertyRelative("prefab");
            SerializedProperty enabledProp = elementProp.FindPropertyRelative("enabled");
            SerializedProperty weightProp = elementProp.FindPropertyRelative("weight");

            if (!enabledProp.boolValue)
            {
                return "Disabled";
            }

            if (prefabProp.objectReferenceValue == null)
            {
                return "Assign a prefab";
            }

            FacingDirection previewDirection = GetTemplateChancePreviewDirection(templatesProp);
            float weight = GetEffectiveTemplateWeight(elementProp, previewDirection);
            if (weight <= 0.0f)
            {
                return "0% (weight is 0)";
            }

            float totalWeight = 0.0f;
            for (int candidateIndex = 0; candidateIndex < templatesProp.arraySize; candidateIndex++)
            {
                totalWeight += GetEffectiveTemplateWeight(templatesProp.GetArrayElementAtIndex(candidateIndex), previewDirection);
            }

            if (totalWeight <= 0.0f)
            {
                return "0% (no active weights)";
            }

            float percentage = weight / totalWeight * 100.0f;
            return percentage.ToString("0.#") + "% @ " + previewDirection + " (" + weight.ToString("0.##") + " / " + totalWeight.ToString("0.##") + ")";
        }

        private static float GetEffectiveTemplateWeight(SerializedProperty elementProp)
        {
            return GetBaseTemplateWeight(elementProp);
        }

        private static float GetEffectiveTemplateWeight(SerializedProperty elementProp, FacingDirection growthDirection)
        {
            float baseWeight = GetBaseTemplateWeight(elementProp);
            if (baseWeight <= 0.0f)
            {
                return 0.0f;
            }

            SerializedProperty useDirectionBiasProp = elementProp.FindPropertyRelative("useDirectionBias");
            SerializedProperty preferredDirectionProp = elementProp.FindPropertyRelative("preferredDirection");
            SerializedProperty directionBiasProp = elementProp.FindPropertyRelative("directionBias");
            if (useDirectionBiasProp.boolValue)
            {
                return ApplyTemplateDirectionBias(
                    baseWeight,
                    (FacingDirection)preferredDirectionProp.enumValueIndex,
                    directionBiasProp.floatValue,
                    growthDirection);
            }

            SerializedObject settingsObject = elementProp.serializedObject;
            SerializedProperty useGlobalBiasProp = settingsObject.FindProperty("useGlobalTemplateDirectionBias");
            if (useGlobalBiasProp != null && useGlobalBiasProp.boolValue)
            {
                SerializedProperty globalDirectionProp = settingsObject.FindProperty("globalTemplateDirection");
                SerializedProperty globalBiasProp = settingsObject.FindProperty("globalTemplateDirectionBias");
                return ApplyTemplateDirectionBias(
                    baseWeight,
                    (FacingDirection)globalDirectionProp.enumValueIndex,
                    globalBiasProp.floatValue,
                    growthDirection);
            }

            return baseWeight;
        }

        private static float GetBaseTemplateWeight(SerializedProperty elementProp)
        {
            SerializedProperty prefabProp = elementProp.FindPropertyRelative("prefab");
            SerializedProperty enabledProp = elementProp.FindPropertyRelative("enabled");
            SerializedProperty weightProp = elementProp.FindPropertyRelative("weight");

            if (prefabProp.objectReferenceValue == null || !enabledProp.boolValue)
            {
                return 0.0f;
            }

            return Mathf.Max(0.0f, weightProp.floatValue);
        }

        private static void DrawOrganicRequiredMinimumSummary(SerializedObject settingsObject, SerializedProperty templatesProp)
        {
            int requiredCountedRooms = 0;
            for (int index = 0; index < templatesProp.arraySize; index++)
            {
                SerializedProperty elementProp = templatesProp.GetArrayElementAtIndex(index);
                SerializedProperty prefabProp = elementProp.FindPropertyRelative("prefab");
                SerializedProperty requiredProp = elementProp.FindPropertyRelative("requiredMinimumCount");

                GameObject prefab = prefabProp.objectReferenceValue as GameObject;
                if (!IsCountedTemplatePrefab(prefab))
                {
                    continue;
                }

                requiredCountedRooms += Mathf.Max(0, requiredProp.intValue);
            }

            int minimumTargetRoomCount = GetMinimumTargetRoomCount(settingsObject);
            if (requiredCountedRooms > minimumTargetRoomCount)
            {
                EditorGUILayout.HelpBox(
                    "Required counted room minimums total " + requiredCountedRooms +
                    ", which exceeds the minimum target room count of " + minimumTargetRoomCount + ".",
                    MessageType.Error);
                return;
            }

            if (requiredCountedRooms > 0)
            {
                EditorGUILayout.LabelField(
                    "Required counted room minimums: " + requiredCountedRooms + "/" + minimumTargetRoomCount,
                    EditorStyles.miniLabel);
            }
        }

        private static int GetMinimumTargetRoomCount(SerializedObject settingsObject)
        {
            SerializedProperty useRoomCountRangeProp = settingsObject.FindProperty("useRoomCountRange");
            if (useRoomCountRangeProp != null && useRoomCountRangeProp.boolValue)
            {
                SerializedProperty minRoomCountProp = settingsObject.FindProperty("minRoomCount");
                return minRoomCountProp != null ? Mathf.Max(0, minRoomCountProp.intValue) : 0;
            }

            SerializedProperty targetRoomCountProp = settingsObject.FindProperty("targetRoomCount");
            return targetRoomCountProp != null ? Mathf.Max(0, targetRoomCountProp.intValue) : 0;
        }

        private static bool IsCountedTemplatePrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            RoomTemplateComponent component = prefab.GetComponent<RoomTemplateComponent>();
            return component != null && component.roomType != RoomType.Corridor;
        }

        private static FacingDirection GetTemplateChancePreviewDirection(SerializedProperty templatesProp)
        {
            SerializedObject settingsObject = templatesProp.serializedObject;
            SerializedProperty useGlobalBiasProp = settingsObject.FindProperty("useGlobalTemplateDirectionBias");
            if (useGlobalBiasProp != null && useGlobalBiasProp.boolValue)
            {
                SerializedProperty globalDirectionProp = settingsObject.FindProperty("globalTemplateDirection");
                if (globalDirectionProp != null)
                {
                    return (FacingDirection)globalDirectionProp.enumValueIndex;
                }
            }

            SerializedProperty preferredGrowthDirectionProp = settingsObject.FindProperty("preferredGrowthDirection");
            return preferredGrowthDirectionProp != null ? (FacingDirection)preferredGrowthDirectionProp.enumValueIndex : FacingDirection.East;
        }

        private static float ApplyTemplateDirectionBias(float baseWeight, FacingDirection preferredDirection, float directionBias, FacingDirection growthDirection)
        {
            float bias = Mathf.Clamp01(directionBias);
            return preferredDirection == growthDirection
                ? baseWeight * Mathf.Lerp(1.0f, 3.0f, bias)
                : baseWeight * Mathf.Lerp(1.0f, 0.25f, bias);
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

            serializedObject.FindProperty("dungeonFlow").objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
            gen.dungeonFlow = asset;
            EditorUtility.SetDirty(gen);

            EditorGUIUtility.PingObject(asset);
        }

        private void CreateOrganicSettings(DungeonGenerator gen)
        {
            string folder = ConstraintDungeonAssetPaths.OrganicGrowthProfileFolder;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/NewOrganicGrowthProfile.asset");
            OrganicGenerationSettings asset = ScriptableObject.CreateInstance<OrganicGenerationSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            serializedObject.FindProperty("organicSettings").objectReferenceValue = asset;
            serializedObject.ApplyModifiedProperties();
            gen.organicSettings = asset;
            EditorUtility.SetDirty(gen);

            EditorGUIUtility.PingObject(asset);
        }
    }

    [CustomPropertyDrawer(typeof(TemplateEntry))]
    internal sealed class TemplateEntryDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUIUtility.singleLineHeight + 6.0f;
            if (!property.isExpanded)
            {
                return baseHeight;
            }

            return baseHeight + 4.0f + DungeonGeneratorEditor.GetTemplatePaletteDetailsHeight(property, false) + 8.0f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect rowRect = new Rect(
                position.x,
                position.y + 3.0f,
                position.width,
                EditorGUIUtility.singleLineHeight);

            SerializedProperty prefabProp = property.FindPropertyRelative("prefab");
            SerializedProperty enabledProp = property.FindPropertyRelative("enabled");

            Rect foldoutRect = new Rect(rowRect.x, rowRect.y, 16.0f, rowRect.height);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);

            Rect enabledRect = new Rect(foldoutRect.xMax + 2.0f, rowRect.y, 18.0f, rowRect.height);
            Rect enabledLabelRect = new Rect(enabledRect.xMax + 4.0f, rowRect.y, 22.0f, rowRect.height);
            Rect prefabRect = new Rect(
                enabledLabelRect.xMax + 4.0f,
                rowRect.y,
                rowRect.width - enabledRect.width - enabledLabelRect.width - foldoutRect.width - 10.0f,
                rowRect.height);
            enabledProp.boolValue = EditorGUI.Toggle(enabledRect, enabledProp.boolValue);
            EditorGUI.LabelField(enabledLabelRect, "On");
            EditorGUI.PropertyField(prefabRect, prefabProp, GUIContent.none);

            if (property.isExpanded)
            {
                Rect detailsRect = new Rect(
                    position.x + 20.0f,
                    rowRect.yMax + 4.0f,
                    position.width - 20.0f,
                    DungeonGeneratorEditor.GetTemplatePaletteDetailsHeight(property, false) + 8.0f);
                GUI.Box(detailsRect, GUIContent.none, EditorStyles.helpBox);

                Rect contentRect = new Rect(
                    detailsRect.x + 6.0f,
                    detailsRect.y + 4.0f,
                    detailsRect.width - 12.0f,
                    detailsRect.height - 8.0f);
                DungeonGeneratorEditor.DrawTemplatePaletteDetails(property, contentRect, null);
            }

            EditorGUI.EndProperty();
        }
    }
}
