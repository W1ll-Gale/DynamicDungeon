using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using DynamicDungeon.ConstraintDungeon;
using UnityEditor.Experimental.GraphView;
using System.Collections.Generic;
using System.Linq;
using System;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonDesignerWindow : EditorWindow
    {
        private DungeonGraphView graphView;
        private DungeonFlow activeFlow;

        [MenuItem(ConstraintDungeonMenuPaths.DungeonDesigner)]
        public static void Open()
        {
            GetWindow<DungeonDesignerWindow>("Dungeon Designer");
        }

        private void OnEnable()
        {
            GenerateLayout();
            GenerateToolbar();
        }

        private void GenerateLayout()
        {
            graphView = new DungeonGraphView(this)
            {
                name = "Dungeon Graph"
            };
            graphView.style.flexGrow = 1;
            rootVisualElement.Add(graphView);

            graphView.OnSelectionChanged = UpdateInspector;
            
            // Initialise inspector.
            UpdateInspector(null, null);
        }

        private void UpdateInspector(List<RoomNode> selection, DungeonEdge selectedEdge)
        {
            Blackboard blackboard = graphView.Blackboard;
            blackboard.Clear();

            blackboard.title = selectedEdge != null ? "Corridor Link" : (selection == null || selection.Count == 0 ? "Dungeon Inspector" : (selection.Count == 1 ? "Node Settings" : "Multi-Node Settings"));
            blackboard.subTitle = selectedEdge != null ? $"{Mathf.Max(0, selectedEdge.associatedCorridors.Count)} Corridors" : (selection != null && selection.Count > 0 ? $"{selection.Count} Selected" : "Select nodes to edit");

            if (selection == null || selection.Count == 0)
            {
                if (selectedEdge == null)
                    blackboard.Add(new Label("Select a node to inspect and edit properties.") { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.5f, marginTop = 10, marginLeft = 10 } });
                else
                    DrawCorridorLinkControls(blackboard, selectedEdge);
                return;
            }

            // Create a container for fields to add padding
            VisualElement container = new VisualElement { style = { paddingLeft = 10, paddingRight = 10, paddingTop = 5, paddingBottom = 10 } };
            blackboard.Add(container);

            if (selectedEdge != null)
            {
                DrawCorridorLinkControls(container, selectedEdge);
            }

            // Display Name
            string initialName = selection.Count == 1 ? selection[0].displayName : GetMixedValue(selection, n => n.displayName, "Mixed Values");
            TextField nameField = new TextField("Display Name") { value = initialName };
            if (selection.Count > 1 && IsMixed(selection, n => n.displayName)) nameField.AddToClassList("mixed-value");
            
            nameField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Node Name");
                foreach (RoomNode node in selection)
                {
                    node.displayName = evt.newValue;
                    graphView.RefreshNodeDisplayName(node.id, evt.newValue);
                }
                EditorUtility.SetDirty(activeFlow);
            });
            container.Add(nameField);

            // Room Type
            RoomType initialType = selection[0].type;
            bool mixedType = IsMixed(selection, n => n.type);
            EnumField typeField = new EnumField("Room Type", mixedType ? (Enum)null : initialType);
            if (mixedType) typeField.AddToClassList("mixed-value");

            typeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(activeFlow, "Change Room Type");
                foreach (RoomNode node in selection)
                {
                    node.type = (RoomType)evt.newValue;
                    graphView.RefreshNodeStyle(node.id, node.type);
                }
                EditorUtility.SetDirty(activeFlow);
            });
            container.Add(typeField);

            // Templates 
            VisualElement templatesHeader = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginTop = 10 } };
            templatesHeader.Add(new Label("Templates") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            
            if (selection.Count == 1)
            {
                Button addBtn = new Button(() => {
                    selection[0].allowedTemplates.Add(null);
                    UpdateInspector(selection, selectedEdge);
                    EditorUtility.SetDirty(activeFlow);
                }) { text = "+", style = { width = 20, height = 18 } };
                templatesHeader.Add(addBtn);
                container.Add(templatesHeader);

                VisualElement templatesContainer = new VisualElement();
                RefreshTemplatesList(templatesContainer, selection[0]);
                container.Add(templatesContainer);
            }
            else
            {
                container.Add(templatesHeader);
                container.Add(new Label("Select single node to edit template list") { style = { fontSize = 10, opacity = 0.5f } });
            }

            // Batch Add Field (Useful for Multi-Select)
            ObjectField addBatchField = new ObjectField(selection.Count > 1 ? "Add Template to All" : "Quick Add") { objectType = typeof(GameObject), allowSceneObjects = false };
            addBatchField.RegisterValueChangedCallback(evt => {
                if (evt.newValue is GameObject temp)
                {
                    Undo.RecordObject(activeFlow, "Add Template to Selection");
                    foreach (RoomNode node in selection)
                    {
                        if (!node.allowedTemplates.Contains(temp))
                            node.allowedTemplates.Add(temp);
                    }
                    UpdateInspector(selection, selectedEdge);
                    EditorUtility.SetDirty(activeFlow);
                    addBatchField.value = null;
                }
            });
            container.Add(addBatchField);
        }

        private void DrawCorridorLinkControls(VisualElement container, DungeonEdge selectedEdge)
        {
            VisualElement linkGroup = new VisualElement { style = { marginBottom = 10 } };

            IntegerField countField = new IntegerField("Corridor Count")
            {
                value = Mathf.Max(0, selectedEdge.associatedCorridors.Count)
            };
            countField.RegisterValueChangedCallback(evt =>
            {
                int newCount = Mathf.Max(0, evt.newValue);
                countField.SetValueWithoutNotify(newCount);
                graphView.SetCorridorLinkCount(selectedEdge, newCount);
            });
            linkGroup.Add(countField);

            Button dynamicButton = new Button(() => graphView.ApplyDynamicCorridorCount(selectedEdge))
            {
                text = "Apply Dynamic Count"
            };
            linkGroup.Add(dynamicButton);

            container.Add(linkGroup);
        }

        private string GetMixedValue<T>(List<T> list, System.Func<T, string> selector, string placeholder)
        {
            if (list == null || list.Count == 0) return "";
            string first = selector(list[0]);
            return list.All(x => selector(x) == first) ? first : placeholder;
        }

        private bool IsMixed<T, V>(List<T> list, System.Func<T, V> selector)
        {
            if (list == null || list.Count <= 1) return false;
            V first = selector(list[0]);
            return !list.All(x => EqualityComparer<V>.Default.Equals(selector(x), first));
        }

        private void RefreshTemplatesList(VisualElement container, RoomNode node)
        {
            container.Clear();
            for (int i = 0; i < node.allowedTemplates.Count; i++)
            {
                GameObject template = node.allowedTemplates[i];
                VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2, marginTop = 2 } };
                
                int index = i;
                ObjectField objField = new ObjectField {
                    objectType = typeof(GameObject), 
                    value = template, 
                    allowSceneObjects = false,
                    style = { flexGrow = 1 } 
                };
                
                objField.RegisterValueChangedCallback(evt => {
                    Undo.RecordObject(activeFlow, "Change Template");
                    node.allowedTemplates[index] = evt.newValue as GameObject;
                    EditorUtility.SetDirty(activeFlow);
                });
                
                row.Add(objField);
                
                Button removeBtn = new Button(() => {
                    Undo.RecordObject(activeFlow, "Remove Template");
                    node.allowedTemplates.RemoveAt(index);
                    RefreshTemplatesList(container, node);
                    EditorUtility.SetDirty(activeFlow);
                }) { text = "X", style = { height = 18, width = 18, marginLeft = 4 } };
                
                row.Add(removeBtn);
                container.Add(row);
            }
        }


        private void GenerateToolbar()
        {
            Toolbar toolbar = new Toolbar();
            
            ObjectField assetField = new ObjectField("Dungeon Flow")
            {
                objectType = typeof(DungeonFlow),
                allowSceneObjects = false
            };
            assetField.RegisterValueChangedCallback(evt =>
            {
                activeFlow = evt.newValue as DungeonFlow;
                graphView.LoadFlow(activeFlow);
            });
            
            toolbar.Add(assetField);
            
            Button saveButton = new Button(() => {
                if (activeFlow != null) {
                    EditorUtility.SetDirty(activeFlow);
                    AssetDatabase.SaveAssets();
                }
            }) { text = "Save Assets" };
            toolbar.Add(saveButton);

            Button validateButton = new Button(ValidateActiveFlow) { text = "Validate Flow" };
            toolbar.Add(validateButton);

            rootVisualElement.Insert(0, toolbar);
        }

        private void ValidateActiveFlow()
        {
            DungeonFlowValidator.Result result = DungeonFlowValidator.Validate(activeFlow);
            graphView.ShowValidationResults(result);

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning($"[DungeonDesigner] {warning}", activeFlow);
            }

            foreach (string error in result.Errors)
            {
                Debug.LogError($"[DungeonDesigner] {error}", activeFlow);
            }

            if (result.IsValid)
            {
                string flowName = activeFlow != null ? activeFlow.name : "No Flow";
                Debug.Log($"[DungeonDesigner] '{flowName}' passed validation with {result.Warnings.Count} warning(s).", activeFlow);
            }
        }
    }
}
