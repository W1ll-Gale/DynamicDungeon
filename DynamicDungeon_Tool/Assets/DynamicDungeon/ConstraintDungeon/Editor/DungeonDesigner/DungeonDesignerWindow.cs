using System;
using System.Collections.Generic;
using System.Linq;
using DynamicDungeon.Editor.Shared;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonDesignerWindow : EditorWindow
    {
        private const string WindowTitle = "Constraint Dungeon Designer";
        private const float DiagnosticsPanelHeight = 120.0f;
        private const double AutoSaveDebounceSeconds = 1.0;
        private const string AutoSavePrefsKey = "DynamicDungeon.ConstraintDungeon.Designer.AutoSave";
        private const string PanelViewSettingsPrefsKey = "DynamicDungeon.ConstraintDungeon.Designer.PanelViewSettings";
        private const string InspectorLayoutPrefsKey = "DynamicDungeon.ConstraintDungeon.Designer.InspectorLayout";
        private const string MiniMapLayoutPrefsKey = "DynamicDungeon.ConstraintDungeon.Designer.MiniMapLayout";

        [SerializeField]
        private DungeonFlow activeFlow;

        [SerializeField]
        private bool _isDiagnosticsPanelCollapsed;

        [SerializeField]
        private float _diagnosticsPanelExpandedHeight = DiagnosticsPanelHeight;

        private DungeonGraphView graphView;
        private ObjectField flowAssetField;
        private Label _statusLabel;
        private Label _saveStateLabel;
        private ToolbarToggle _autoSaveToggle;
        private ToolbarButton _discardChangesButton;
        private ToolbarToggle _settingsToggle;
        private ToolbarToggle _miniMapToggle;
        private ToolbarToggle _diagnosticsToggle;
        private FloatingPanelWindow _graphSettingsWindow;
        private MiniMapWindow _miniMapWindow;
        private DiagnosticsPanel _diagnosticsPanel;
        private VisualElement _graphSettingsContent;
        private VisualElement _nodeInspectorContent;
        private VisualElement _graphSettingsBody;
        private VisualElement _nodeInspectorBody;
        private ToolbarToggle _graphSettingsTab;
        private ToolbarToggle _nodeInspectorTab;
        private List<RoomNode> _currentSelection;
        private DungeonEdge _currentSelectedEdge;
        private PanelViewSettings _panelViewSettings;
        private FloatingWindowLayout _inspectorLayout;
        private FloatingWindowLayout _miniMapLayout;
        private bool _autoSavePending;
        private double _nextAutoSaveTime;

        [Serializable]
        internal sealed class PanelViewSettings
        {
            public bool IsInspectorVisible = true;
            public bool IsMiniMapVisible = true;
            public bool IsDiagnosticsVisible = true;
            public bool IsInspectorCollapsed;
            public bool IsMiniMapCollapsed;
        }

        private bool AutoSave
        {
            get => EditorPrefs.GetBool(AutoSavePrefsKey, true);
            set => EditorPrefs.SetBool(AutoSavePrefsKey, value);
        }

        [MenuItem(ConstraintDungeonMenuPaths.DungeonDesigner)]
        public static void Open()
        {
            GetWindow<DungeonDesignerWindow>(WindowTitle);
        }

        public static void Open(DungeonFlow flow)
        {
            DungeonDesignerWindow window = GetWindow<DungeonDesignerWindow>(WindowTitle);
            window.Show();
            window.LoadFlow(flow);
            window.Focus();
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            DungeonFlow flow = EditorUtility.EntityIdToObject(instanceId) as DungeonFlow;
            if (flow != null)
            {
                Open(flow);
                return true;
            }

            return false;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            BuildLayout();
            LoadFlow(activeFlow);
        }

        private void OnDisable()
        {
            SavePanelState();
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanelExpandedHeight = _diagnosticsPanel.GetExpandedHeight();
                _isDiagnosticsPanelCollapsed = _diagnosticsPanel.IsCollapsed();
            }

            ClearQueuedAutoSave();
            if (AutoSave)
            {
                SaveDirtyFlowImmediately();
            }
        }

        public void LoadFlow(DungeonFlow flow)
        {
            activeFlow = flow;

            if (flowAssetField != null)
            {
                flowAssetField.SetValueWithoutNotify(activeFlow);
            }

            if (graphView != null)
            {
                graphView.LoadFlow(activeFlow);
            }

            RebuildGraphSettings();
            UpdateInspector(null, null);
            _diagnosticsPanel?.Populate(Array.Empty<SharedGraphDiagnostic>());
            SetStatus(activeFlow == null ? "No Flow" : "Loaded " + activeFlow.name);
            RefreshSaveStateIndicator();
        }

        private void BuildLayout()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            LoadPanelState();

            rootVisualElement.Add(BuildToolbar());

            VisualElement contentArea = new VisualElement();
            contentArea.style.flexDirection = FlexDirection.Row;
            contentArea.style.flexGrow = 1.0f;
            rootVisualElement.Add(contentArea);

            graphView = new DungeonGraphView(this)
            {
                name = "Dungeon Graph"
            };
            graphView.style.flexGrow = 1.0f;
            graphView.OnSelectionChanged = UpdateInspector;
            graphView.SetAfterMutationCallback(OnAfterFlowMutation);
            graphView.RegisterCallback<GeometryChangedEvent>(OnGraphViewGeometryChanged);
            contentArea.Add(graphView);

            _graphSettingsWindow = BuildGraphSettingsWindow();
            _graphSettingsWindow.SetLayoutChangedCallback(SavePanelState);
            _graphSettingsWindow.SetCollapsed(_panelViewSettings.IsInspectorCollapsed);
            graphView.Add(_graphSettingsWindow);

            _miniMapWindow = new MiniMapWindow(_miniMapLayout, graphView, CreateMiniMapCallbacks());
            _miniMapWindow.SetLayoutChangedCallback(SavePanelState);
            _miniMapWindow.SetCollapsed(_panelViewSettings.IsMiniMapCollapsed);
            graphView.Add(_miniMapWindow);

            _diagnosticsPanel = BuildDiagnosticsPanel();
            rootVisualElement.Add(_diagnosticsPanel);

            ApplyPanelVisibility();
            ApplyPanelLayouts();
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            GraphEditorToolbarControls.ApplyStandardToolbarStyle(toolbar);

            flowAssetField = new ObjectField("Dungeon Flow")
            {
                objectType = typeof(DungeonFlow),
                allowSceneObjects = false
            };
            flowAssetField.label = string.Empty;
            flowAssetField.style.minWidth = 220.0f;
            flowAssetField.style.width = 220.0f;
            flowAssetField.style.marginRight = 6.0f;
            flowAssetField.RegisterValueChangedCallback(evt => LoadFlow(evt.newValue as DungeonFlow));
            toolbar.Add(flowAssetField);

            ToolbarButton validateButton = new ToolbarButton(ValidateActiveFlow);
            validateButton.text = "Validate";
            validateButton.style.marginRight = 6.0f;
            toolbar.Add(validateButton);

            _autoSaveToggle = GraphEditorToolbarControls.BuildAutoSaveToggle(
                AutoSave,
                "Save dungeon flow assets automatically after edits.",
                OnAutoSaveToggleChanged);
            toolbar.Add(_autoSaveToggle);

            _statusLabel = GraphEditorToolbarControls.BuildStatusLabel("Idle");
            toolbar.Add(_statusLabel);

            _saveStateLabel = GraphEditorToolbarControls.BuildSaveStateLabel();
            toolbar.Add(_saveStateLabel);

            _discardChangesButton = GraphEditorToolbarControls.BuildDiscardButton(
                "Delete unsaved changes on the loaded dungeon flow and reload it from disk.",
                DiscardUnsavedFlowChanges);
            toolbar.Add(_discardChangesButton);

            toolbar.Add(GraphEditorToolbarControls.BuildToolbarSpacer());

            VisualElement panelToggleGroup = GraphEditorToolbarControls.BuildPanelToggleGroup();
            toolbar.Add(panelToggleGroup);

            _settingsToggle = GraphEditorToolbarControls.BuildPanelToggle(
                GraphEditorToolbarControls.LoadInspectorIcon(),
                "Toggle Graph Settings and Node Inspector window",
                _panelViewSettings.IsInspectorVisible,
                OnSettingsToggleChanged);
            panelToggleGroup.Add(_settingsToggle);

            _miniMapToggle = GraphEditorToolbarControls.BuildPanelToggle(
                GraphEditorToolbarControls.LoadMiniMapIcon(),
                "Toggle MiniMap window",
                _panelViewSettings.IsMiniMapVisible,
                OnMiniMapToggleChanged);
            panelToggleGroup.Add(_miniMapToggle);

            _diagnosticsToggle = GraphEditorToolbarControls.BuildPanelToggle(
                GraphEditorToolbarControls.LoadDiagnosticsIcon(),
                "Toggle Diagnostics panel",
                _panelViewSettings.IsDiagnosticsVisible,
                OnDiagnosticsToggleChanged);
            panelToggleGroup.Add(_diagnosticsToggle);

            return toolbar;
        }

        private FloatingPanelWindow BuildGraphSettingsWindow()
        {
            FloatingPanelWindow window = new FloatingPanelWindow("Graph Settings", _inspectorLayout);
            window.name = "ConstraintDungeonGraphSettingsWindow";

            Toolbar tabToolbar = new Toolbar();
            _graphSettingsTab = BuildTab("Graph Settings", true, () => ShowInspectorTab(false));
            _nodeInspectorTab = BuildTab("Node Inspector", false, () => ShowInspectorTab(true));
            tabToolbar.Add(_graphSettingsTab);
            tabToolbar.Add(_nodeInspectorTab);
            window.contentContainer.Add(tabToolbar);

            _graphSettingsContent = BuildScrollableContent(out _graphSettingsBody);
            _nodeInspectorContent = BuildScrollableContent(out _nodeInspectorBody);
            window.contentContainer.Add(_graphSettingsContent);
            window.contentContainer.Add(_nodeInspectorContent);
            ShowInspectorTab(false);
            return window;
        }

        private ToolbarToggle BuildTab(string label, bool isSelected, Action selected)
        {
            ToolbarToggle toggle = new ToolbarToggle();
            toggle.text = label;
            toggle.SetValueWithoutNotify(isSelected);
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    selected?.Invoke();
                    return;
                }

                toggle.SetValueWithoutNotify(true);
            });
            return toggle;
        }

        private VisualElement BuildScrollableContent(out VisualElement content)
        {
            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1.0f;
            content = new VisualElement();
            content.style.paddingLeft = 8.0f;
            content.style.paddingRight = 8.0f;
            content.style.paddingTop = 8.0f;
            content.style.paddingBottom = 8.0f;
            scrollView.Add(content);
            return scrollView;
        }

        private DiagnosticsPanel BuildDiagnosticsPanel()
        {
            return DiagnosticsPanelUtility.BuildDockedPanel(
                ResolveDiagnosticElementName,
                FocusDiagnosticElement,
                _diagnosticsPanelExpandedHeight,
                _isDiagnosticsPanelCollapsed,
                new Color(0.08f, 0.08f, 0.08f, 1.0f));
        }

        private MiniMapGraphCallbacks CreateMiniMapCallbacks()
        {
            return new MiniMapGraphCallbacks
            {
                RegisterViewTransformChanged = callback => graphView.SetViewTransformChangedCallback(callback),
                GetViewportState = graphView.GetViewportState,
                ShouldIncludeElement = element => element is Node,
                GetElementId = element => element is Node node ? node.viewDataKey : null,
                FocusElement = elementId => graphView != null && graphView.FocusElement(elementId)
            };
        }

        private void ShowInspectorTab(bool showNodeInspector)
        {
            if (_graphSettingsContent != null)
            {
                _graphSettingsContent.style.display = showNodeInspector ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_nodeInspectorContent != null)
            {
                _nodeInspectorContent.style.display = showNodeInspector ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _graphSettingsTab?.SetValueWithoutNotify(!showNodeInspector);
            _nodeInspectorTab?.SetValueWithoutNotify(showNodeInspector);
        }

        private VisualElement GetGraphSettingsBody()
        {
            return _graphSettingsBody;
        }

        private VisualElement GetNodeInspectorBody()
        {
            return _nodeInspectorBody;
        }

        private void RebuildGraphSettings()
        {
            VisualElement body = GetGraphSettingsBody();
            if (body == null)
            {
                return;
            }

            body.Clear();
            if (activeFlow == null)
            {
                AddPlaceholder(body, "No dungeon flow loaded.");
                return;
            }

            BuildCorridorSettings(body);

            foreach (RoomType type in Enum.GetValues(typeof(RoomType)))
            {
                BuildDefaultTemplateGroup(body, type);
            }
        }

        private void BuildCorridorSettings(VisualElement body)
        {
            VisualElement group = new VisualElement();
            group.AddToClassList("settings-type-group");
            group.style.marginBottom = 10.0f;

            Label header = new Label("CORRIDOR LINKS");
            header.AddToClassList("settings-type-header");
            group.Add(header);

            EnumField modeField = new EnumField("Placement", activeFlow.corridorPlacementMode);
            IntegerField fixedCountField = new IntegerField("Fixed Count") { value = activeFlow.fixedCorridorCount };
            FloatField dynamicSpacingField = new FloatField("Dynamic Spacing") { value = activeFlow.dynamicCorridorSpacing };
            IntegerField dynamicMaxField = new IntegerField("Dynamic Max") { value = activeFlow.maxDynamicCorridorCount };
            group.Add(modeField);
            group.Add(fixedCountField);
            group.Add(dynamicSpacingField);
            group.Add(dynamicMaxField);

            Action refreshFieldState = () =>
            {
                bool fixedMode = activeFlow.corridorPlacementMode == CorridorPlacementMode.Fixed;
                fixedCountField.SetEnabled(fixedMode);
                dynamicSpacingField.SetEnabled(!fixedMode);
                dynamicMaxField.SetEnabled(!fixedMode);
            };

            modeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Corridor Placement");
                activeFlow.corridorPlacementMode = (CorridorPlacementMode)evt.newValue;
                ApplyCorridorPlacementAndReload();
                refreshFieldState();
            });

            fixedCountField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Fixed Corridor Count");
                activeFlow.fixedCorridorCount = Mathf.Max(0, evt.newValue);
                fixedCountField.SetValueWithoutNotify(activeFlow.fixedCorridorCount);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Fixed)
                {
                    ApplyCorridorPlacementAndReload();
                }
                MarkFlowDirty();
            });

            dynamicSpacingField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Dynamic Corridor Spacing");
                activeFlow.dynamicCorridorSpacing = Mathf.Max(1.0f, evt.newValue);
                dynamicSpacingField.SetValueWithoutNotify(activeFlow.dynamicCorridorSpacing);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Dynamic)
                {
                    ApplyCorridorPlacementAndReload();
                }
                MarkFlowDirty();
            });

            dynamicMaxField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Dynamic Corridor Max");
                activeFlow.maxDynamicCorridorCount = Mathf.Max(0, evt.newValue);
                dynamicMaxField.SetValueWithoutNotify(activeFlow.maxDynamicCorridorCount);
                if (activeFlow.corridorPlacementMode == CorridorPlacementMode.Dynamic)
                {
                    ApplyCorridorPlacementAndReload();
                }
                MarkFlowDirty();
            });

            refreshFieldState();
            body.Add(group);
        }

        private void BuildDefaultTemplateGroup(VisualElement body, RoomType type)
        {
            VisualElement group = new VisualElement();
            group.AddToClassList("settings-type-group");

            VisualElement headerRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };
            Label typeLabel = new Label(type.ToString().ToUpper());
            typeLabel.AddToClassList("settings-type-header");
            headerRow.Add(typeLabel);

            Button addButton = new Button(() => AddDefaultTemplate(type)) { text = "+", style = { width = 20.0f, height = 20.0f } };
            headerRow.Add(addButton);
            group.Add(headerRow);

            DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
            if (mapping != null)
            {
                for (int index = 0; index < mapping.templates.Count; index++)
                {
                    int templateIndex = index;
                    VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2.0f } };

                    ObjectField field = new ObjectField { objectType = typeof(GameObject), value = mapping.templates[templateIndex], allowSceneObjects = false };
                    field.style.flexGrow = 1.0f;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(activeFlow, "Change Default Template");
                        mapping.templates[templateIndex] = (GameObject)evt.newValue;
                        MarkFlowDirty();
                    });
                    row.Add(field);

                    Button removeButton = new Button(() => RemoveDefaultTemplate(type, templateIndex)) { text = "x", style = { width = 20.0f } };
                    row.Add(removeButton);
                    group.Add(row);
                }
            }

            body.Add(group);
        }

        private void UpdateInspector(List<RoomNode> selection, DungeonEdge selectedEdge)
        {
            _currentSelection = selection != null ? new List<RoomNode>(selection) : new List<RoomNode>();
            _currentSelectedEdge = selectedEdge;

            VisualElement body = GetNodeInspectorBody();
            if (body == null)
            {
                return;
            }

            body.Clear();
            if (selectedEdge != null)
            {
                DrawCorridorLinkControls(body, selectedEdge);
                ShowInspectorTab(true);
            }

            if (_currentSelection == null || _currentSelection.Count == 0)
            {
                if (selectedEdge == null)
                {
                    AddPlaceholder(body, "Select a node or corridor link to inspect and edit properties.");
                }

                return;
            }

            DrawNodeControls(body, _currentSelection, selectedEdge);
            ShowInspectorTab(true);
        }

        private void DrawNodeControls(VisualElement body, List<RoomNode> selection, DungeonEdge selectedEdge)
        {
            string initialName = selection.Count == 1 ? selection[0].displayName : GetMixedValue(selection, n => n.displayName, "Mixed Values");
            TextField nameField = new TextField("Display Name") { value = initialName };
            if (selection.Count > 1 && IsMixed(selection, n => n.displayName))
            {
                nameField.AddToClassList("mixed-value");
            }

            nameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Node Name");
                foreach (RoomNode node in selection)
                {
                    node.displayName = evt.newValue;
                    graphView.RefreshNodeDisplayName(node.id, evt.newValue);
                }
                MarkFlowDirty();
            });
            body.Add(nameField);

            RoomType initialType = selection[0].type;
            bool mixedType = IsMixed(selection, n => n.type);
            EnumField typeField = new EnumField("Room Type", mixedType ? (Enum)null : initialType);
            if (mixedType)
            {
                typeField.AddToClassList("mixed-value");
            }

            typeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Room Type");
                foreach (RoomNode node in selection)
                {
                    node.type = (RoomType)evt.newValue;
                    graphView.RefreshNodeStyle(node.id, node.type);
                }
                MarkFlowDirty();
            });
            body.Add(typeField);

            VisualElement templatesHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginTop = 10.0f } };
            templatesHeader.Add(new Label("Templates") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            if (selection.Count == 1)
            {
                Button addButton = new Button(() =>
                {
                    selection[0].allowedTemplates.Add(null);
                    UpdateInspector(selection, selectedEdge);
                    MarkFlowDirty();
                })
                { text = "+", style = { width = 20.0f, height = 18.0f } };
                templatesHeader.Add(addButton);
                body.Add(templatesHeader);

                VisualElement templatesContainer = new VisualElement();
                RefreshTemplatesList(templatesContainer, selection[0]);
                body.Add(templatesContainer);
            }
            else
            {
                body.Add(templatesHeader);
                Label singleNodeHint = new Label("Select a single node to edit template list.");
                singleNodeHint.style.fontSize = 10.0f;
                singleNodeHint.style.opacity = 0.5f;
                body.Add(singleNodeHint);
            }

            ObjectField addBatchField = new ObjectField(selection.Count > 1 ? "Add Template to All" : "Quick Add")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false
            };
            addBatchField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is GameObject template)
                {
                    Undo.RecordObject(activeFlow, "Add Template to Selection");
                    foreach (RoomNode node in selection)
                    {
                        if (!node.allowedTemplates.Contains(template))
                        {
                            node.allowedTemplates.Add(template);
                        }
                    }
                    UpdateInspector(selection, selectedEdge);
                    MarkFlowDirty();
                    addBatchField.value = null;
                }
            });
            body.Add(addBatchField);
        }

        private void DrawCorridorLinkControls(VisualElement body, DungeonEdge selectedEdge)
        {
            VisualElement linkGroup = new VisualElement { style = { marginBottom = 10.0f } };

            IntegerField countField = new IntegerField("Corridor Count")
            {
                value = Mathf.Max(0, selectedEdge.associatedCorridors.Count)
            };
            countField.RegisterValueChangedCallback(evt =>
            {
                int newCount = Mathf.Max(0, evt.newValue);
                countField.SetValueWithoutNotify(newCount);
                graphView.SetCorridorLinkCount(selectedEdge, newCount);
                MarkFlowDirty();
            });
            linkGroup.Add(countField);

            Button dynamicButton = new Button(() =>
            {
                graphView.ApplyDynamicCorridorCount(selectedEdge);
                MarkFlowDirty();
            })
            {
                text = "Apply Dynamic Count"
            };
            linkGroup.Add(dynamicButton);
            body.Add(linkGroup);
        }

        private void RefreshTemplatesList(VisualElement container, RoomNode node)
        {
            container.Clear();
            for (int index = 0; index < node.allowedTemplates.Count; index++)
            {
                GameObject template = node.allowedTemplates[index];
                VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2.0f, marginTop = 2.0f } };

                int templateIndex = index;
                ObjectField objectField = new ObjectField
                {
                    objectType = typeof(GameObject),
                    value = template,
                    allowSceneObjects = false,
                    style = { flexGrow = 1.0f }
                };

                objectField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(activeFlow, "Change Template");
                    node.allowedTemplates[templateIndex] = evt.newValue as GameObject;
                    MarkFlowDirty();
                });
                row.Add(objectField);

                Button removeButton = new Button(() =>
                {
                    Undo.RecordObject(activeFlow, "Remove Template");
                    node.allowedTemplates.RemoveAt(templateIndex);
                    RefreshTemplatesList(container, node);
                    MarkFlowDirty();
                })
                { text = "x", style = { height = 18.0f, width = 18.0f, marginLeft = 4.0f } };
                row.Add(removeButton);
                container.Add(row);
            }
        }

        private void AddDefaultTemplate(RoomType type)
        {
            if (activeFlow == null)
            {
                return;
            }

            Undo.RecordObject(activeFlow, "Add Default Template");
            DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
            if (mapping == null)
            {
                mapping = new DefaultTemplateMapping { type = type };
                activeFlow.defaultTemplates.Add(mapping);
            }

            mapping.templates.Add(null);
            MarkFlowDirty();
            RebuildGraphSettings();
        }

        private void RemoveDefaultTemplate(RoomType type, int index)
        {
            if (activeFlow == null)
            {
                return;
            }

            Undo.RecordObject(activeFlow, "Remove Default Template");
            DefaultTemplateMapping mapping = activeFlow.defaultTemplates.Find(m => m.type == type);
            if (mapping != null && index >= 0 && index < mapping.templates.Count)
            {
                mapping.templates.RemoveAt(index);
            }

            MarkFlowDirty();
            RebuildGraphSettings();
        }

        private void ApplyCorridorPlacementAndReload()
        {
            graphView.ApplyCurrentCorridorPlacementToAllLinks();
            graphView.LoadFlow(activeFlow);
            MarkFlowDirty();
        }

        private void ValidateActiveFlow()
        {
            DungeonFlowValidator.Result result = DungeonFlowValidator.Validate(activeFlow);
            IReadOnlyList<SharedGraphDiagnostic> diagnostics = ConvertValidationIssues(result, activeFlow);
            _diagnosticsPanel?.Populate(diagnostics);
            if (diagnostics.Count > 0)
            {
                ShowDiagnosticsPanel();
            }

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning("[DungeonDesigner] " + warning, activeFlow);
            }

            foreach (string error in result.Errors)
            {
                Debug.LogError("[DungeonDesigner] " + error, activeFlow);
            }

            if (result.IsValid)
            {
                string flowName = activeFlow != null ? activeFlow.name : "No Flow";
                Debug.Log("[DungeonDesigner] '" + flowName + "' passed validation with " + result.Warnings.Count + " warning(s).", activeFlow);
            }

            SetStatus(result.Errors.Count + " errors, " + result.Warnings.Count + " warnings");
        }

        private IReadOnlyList<SharedGraphDiagnostic> ConvertValidationIssues(DungeonFlowValidator.Result result, DungeonFlow flow)
        {
            if (result == null)
            {
                return Array.Empty<SharedGraphDiagnostic>();
            }

            if (result.IsValid && result.Warnings.Count == 0)
            {
                string flowName = flow != null ? flow.name : "No Flow";
                return new[]
                {
                    new SharedGraphDiagnostic(
                        SharedDiagnosticSeverity.Info,
                        "'" + flowName + "' passed validation with 0 warnings.",
                        null)
                };
            }

            if (result.Issues.Count > 0)
            {
                List<SharedGraphDiagnostic> diagnostics = new List<SharedGraphDiagnostic>(result.Issues.Count);
                foreach (DungeonFlowValidator.Issue issue in result.Issues)
                {
                    string elementId = ResolveIssueElementId(issue);
                    SharedDiagnosticSeverity severity = issue.IsError ? SharedDiagnosticSeverity.Error : SharedDiagnosticSeverity.Warning;
                    diagnostics.Add(new SharedGraphDiagnostic(severity, issue.Message, elementId));
                }

                return diagnostics;
            }

            List<SharedGraphDiagnostic> fallbackDiagnostics = new List<SharedGraphDiagnostic>(result.Errors.Count + result.Warnings.Count);
            foreach (string error in result.Errors)
            {
                fallbackDiagnostics.Add(new SharedGraphDiagnostic(SharedDiagnosticSeverity.Error, error, null));
            }

            foreach (string warning in result.Warnings)
            {
                fallbackDiagnostics.Add(new SharedGraphDiagnostic(SharedDiagnosticSeverity.Warning, warning, null));
            }

            return fallbackDiagnostics;
        }

        private static string ResolveIssueElementId(DungeonFlowValidator.Issue issue)
        {
            if (issue == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(issue.NodeId))
            {
                return issue.NodeId;
            }

            if (!string.IsNullOrWhiteSpace(issue.FromNodeId) && !string.IsNullOrWhiteSpace(issue.ToNodeId))
            {
                return DungeonGraphView.BuildLinkElementId(issue.FromNodeId, issue.ToNodeId);
            }

            return !string.IsNullOrWhiteSpace(issue.FromNodeId) ? issue.FromNodeId : issue.ToNodeId;
        }

        private string ResolveDiagnosticElementName(string elementId)
        {
            return graphView != null ? graphView.ResolveElementName(elementId) : "Dungeon Flow";
        }

        private bool FocusDiagnosticElement(string elementId)
        {
            return graphView != null && graphView.FocusElement(elementId);
        }

        private void MarkFlowDirty()
        {
            if (activeFlow != null)
            {
                EditorUtility.SetDirty(activeFlow);
            }

            OnAfterFlowMutation();
        }

        private void OnAfterFlowMutation()
        {
            RefreshSaveStateIndicator();
            QueueDirtyFlowAutoSave();
        }

        private void OnAutoSaveToggleChanged(ChangeEvent<bool> changeEvent)
        {
            AutoSave = changeEvent.newValue;
            if (!AutoSave)
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            QueueDirtyFlowAutoSave();
        }

        private void QueueDirtyFlowAutoSave()
        {
            if (!AutoSave || activeFlow == null || !EditorUtility.IsDirty(activeFlow))
            {
                return;
            }

            _autoSavePending = true;
            _nextAutoSaveTime = EditorApplication.timeSinceStartup + AutoSaveDebounceSeconds;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!_autoSavePending || EditorApplication.timeSinceStartup < _nextAutoSaveTime)
            {
                return;
            }

            SaveDirtyFlowImmediately();
        }

        private void ClearQueuedAutoSave()
        {
            _autoSavePending = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void SaveDirtyFlowImmediately()
        {
            if (activeFlow == null || !EditorUtility.IsDirty(activeFlow))
            {
                ClearQueuedAutoSave();
                RefreshSaveStateIndicator();
                return;
            }

            string path = AssetDatabase.GetAssetPath(activeFlow);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            AssetDatabase.SaveAssets();
            ClearQueuedAutoSave();
            RefreshSaveStateIndicator();
        }

        private void DiscardUnsavedFlowChanges()
        {
            if (activeFlow == null || !EditorUtility.IsDirty(activeFlow))
            {
                RefreshSaveStateIndicator();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Discard Unsaved Dungeon Flow Changes",
                    "Discard all unsaved changes on '" + activeFlow.name + "' and reload it from disk?",
                    "Discard",
                    "Cancel"))
            {
                return;
            }

            ClearQueuedAutoSave();
            if (RestoreFlowFromSavedAsset(activeFlow))
            {
                LoadFlow(activeFlow);
                return;
            }

            RefreshSaveStateIndicator();
        }

        private static bool RestoreFlowFromSavedAsset(DungeonFlow flow)
        {
            return SavedAssetRestoreUtility.RestoreFromSavedAsset(
                flow,
                "__ConstraintDungeonDiscardTemp_",
                "Could not find saved dungeon flow asset file at",
                "Could not load saved dungeon flow copy from",
                "Could not discard unsaved dungeon flow changes for");
        }

        private void RefreshSaveStateIndicator()
        {
            if (_saveStateLabel == null)
            {
                return;
            }

            if (activeFlow == null)
            {
                _saveStateLabel.text = "No Flow";
                _saveStateLabel.tooltip = "No dungeon flow asset is loaded.";
                _saveStateLabel.style.color = new Color(0.55f, 0.55f, 0.55f, 1.0f);
                SetDiscardChangesButtonEnabled(false);
                return;
            }

            bool isUnsaved = EditorUtility.IsDirty(activeFlow);
            _saveStateLabel.text = isUnsaved ? "Unsaved" : "Saved";
            _saveStateLabel.tooltip = isUnsaved
                ? (AutoSave
                    ? "The current dungeon flow has unsaved changes. Auto Save will save it after editing pauses."
                    : "The current dungeon flow has unsaved changes. Auto Save is off.")
                : "The current dungeon flow asset is saved.";
            _saveStateLabel.style.color = isUnsaved
                ? new Color(1.0f, 0.62f, 0.2f, 1.0f)
                : new Color(0.62f, 0.82f, 0.62f, 1.0f);
            SetDiscardChangesButtonEnabled(isUnsaved);
        }

        private void SetDiscardChangesButtonEnabled(bool isEnabled)
        {
            GraphEditorToolbarControls.SetButtonEnabledWithOpacity(_discardChangesButton, isEnabled);
        }

        private void SetStatus(string status)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = status ?? "Idle";
            }
        }

        private void OnSettingsToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsInspectorVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnMiniMapToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsMiniMapVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void OnDiagnosticsToggleChanged(ChangeEvent<bool> changeEvent)
        {
            _panelViewSettings.IsDiagnosticsVisible = changeEvent.newValue;
            ApplyPanelVisibility();
            SavePanelState();
        }

        private void ShowDiagnosticsPanel()
        {
            _panelViewSettings.IsDiagnosticsVisible = true;
            _isDiagnosticsPanelCollapsed = false;
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.SetCollapsed(false);
                _diagnosticsPanelExpandedHeight = _diagnosticsPanel.GetExpandedHeight();
            }

            ApplyPanelVisibility();
            SavePanelState();
        }

        private void ApplyPanelVisibility()
        {
            _graphSettingsWindow?.SetVisible(_panelViewSettings.IsInspectorVisible);
            _miniMapWindow?.SetVisible(_panelViewSettings.IsMiniMapVisible);
            if (_diagnosticsPanel != null)
            {
                _diagnosticsPanel.style.display = _panelViewSettings.IsDiagnosticsVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _settingsToggle?.SetValueWithoutNotify(_panelViewSettings.IsInspectorVisible);
            _miniMapToggle?.SetValueWithoutNotify(_panelViewSettings.IsMiniMapVisible);
            _diagnosticsToggle?.SetValueWithoutNotify(_panelViewSettings.IsDiagnosticsVisible);
        }

        private void OnGraphViewGeometryChanged(GeometryChangedEvent geometryChangedEvent)
        {
            ApplyPanelLayouts();
        }

        private void ApplyPanelLayouts()
        {
            if (graphView == null || graphView.layout.width <= 0.0f || graphView.layout.height <= 0.0f)
            {
                return;
            }

            Rect graphViewRect = new Rect(0.0f, 0.0f, graphView.layout.width, graphView.layout.height);
            _graphSettingsWindow?.UpdateParentRect(graphViewRect);
            _miniMapWindow?.UpdateParentRect(graphViewRect);
        }

        private void LoadPanelState()
        {
            _panelViewSettings = LoadPanelViewSettings();
            _inspectorLayout = LoadLayout(InspectorLayoutPrefsKey, CreateDefaultInspectorLayout());
            _miniMapLayout = LoadLayout(MiniMapLayoutPrefsKey, CreateDefaultMiniMapLayout());
        }

        private void SavePanelState()
        {
            Rect graphViewRect = graphView == null ? new Rect(0.0f, 0.0f, 1200.0f, 800.0f) : new Rect(0.0f, 0.0f, graphView.layout.width, graphView.layout.height);
            if (_graphSettingsWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _graphSettingsWindow.CaptureCurrentLayout();
                _panelViewSettings.IsInspectorCollapsed = _graphSettingsWindow.IsCollapsedForTesting;
            }

            if (_miniMapWindow != null && graphViewRect.width > 0.0f && graphViewRect.height > 0.0f)
            {
                _miniMapWindow.CaptureCurrentLayout();
                _panelViewSettings.IsMiniMapCollapsed = _miniMapWindow.IsCollapsedForTesting;
            }

            EditorPreferenceUtility.SaveJson(PanelViewSettingsPrefsKey, _panelViewSettings ?? new PanelViewSettings());
            EditorPreferenceUtility.SaveJson(InspectorLayoutPrefsKey, _inspectorLayout ?? CreateDefaultInspectorLayout());
            EditorPreferenceUtility.SaveJson(MiniMapLayoutPrefsKey, _miniMapLayout ?? CreateDefaultMiniMapLayout());
        }

        private static PanelViewSettings LoadPanelViewSettings()
        {
            return EditorPreferenceUtility.LoadJson(PanelViewSettingsPrefsKey, new PanelViewSettings());
        }

        private static FloatingWindowLayout LoadLayout(string prefsKey, FloatingWindowLayout defaultLayout)
        {
            return EditorPreferenceUtility.LoadJson(prefsKey, defaultLayout);
        }

        private static FloatingWindowLayout CreateDefaultInspectorLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = false,
                DockToTop = true,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(340.0f, 460.0f)
            };
        }

        private static FloatingWindowLayout CreateDefaultMiniMapLayout()
        {
            return new FloatingWindowLayout
            {
                DockToLeft = false,
                DockToTop = false,
                HorizontalOffset = 8.0f,
                VerticalOffset = 8.0f,
                Size = new Vector2(240.0f, 180.0f)
            };
        }

        private static void AddPlaceholder(VisualElement parent, string message)
        {
            Label placeholder = new Label(message);
            placeholder.style.whiteSpace = WhiteSpace.Normal;
            placeholder.style.opacity = 0.55f;
            placeholder.style.marginTop = 10.0f;
            parent.Add(placeholder);
        }

        private static string GetMixedValue<T>(List<T> list, Func<T, string> selector, string placeholder)
        {
            if (list == null || list.Count == 0)
            {
                return string.Empty;
            }

            string first = selector(list[0]);
            return list.All(item => selector(item) == first) ? first : placeholder;
        }

        private static bool IsMixed<T, TValue>(List<T> list, Func<T, TValue> selector)
        {
            if (list == null || list.Count <= 1)
            {
                return false;
            }

            TValue first = selector(list[0]);
            return !list.All(item => EqualityComparer<TValue>.Default.Equals(selector(item), first));
        }
    }
}
