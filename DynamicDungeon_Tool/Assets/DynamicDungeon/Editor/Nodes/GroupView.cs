using System;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GroupView : Group
    {
        private readonly GenGraph _graph;
        private readonly GenGroupData _groupData;
        private readonly Action _afterMutation;

        private bool _suppressSync;

        public string GroupId
        {
            get
            {
                return _groupData != null ? _groupData.GroupId : string.Empty;
            }
        }

        public GroupView(GenGraph graph, GenGroupData groupData, Action afterMutation) : base()
        {
            _graph = graph;
            _groupData = groupData;
            _afterMutation = afterMutation;

            _suppressSync = true;
            title = groupData.Title ?? string.Empty;
            SetPosition(groupData.Position);
            _suppressSync = false;

            if (groupData.BackgroundColor.a == 0f)
            {
                style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.3f);
            }
            else
            {
                style.backgroundColor = groupData.BackgroundColor;
            }

            headerContainer.style.justifyContent = Justify.Center;
            headerContainer.style.alignItems = Align.Center;

            this.schedule.Execute(() =>
            {
                TextField titleField = headerContainer.Q<TextField>();
                Label titleLabel = headerContainer.Q<Label>();

                if (titleField != null)
                {
                    titleField.style.position = Position.Absolute;
                    titleField.style.left = Length.Percent(50);
                    titleField.style.top = 4f; // Give a slight top offset to match label

                    VisualElement textInput = titleField.Q("unity-text-input");
                    if (textInput != null)
                    {
                        textInput.style.backgroundColor = Color.clear;
                        textInput.style.borderTopWidth = 0f;
                        textInput.style.borderBottomWidth = 0f;
                        textInput.style.borderLeftWidth = 0f;
                        textInput.style.borderRightWidth = 0f;
                        textInput.style.paddingLeft = 0f;
                        textInput.style.paddingRight = 0f;
                        textInput.style.paddingTop = 0f;
                        textInput.style.paddingBottom = 0f;
                        textInput.style.unityTextAlign = TextAnchor.MiddleCenter;

                        // Hard fallback
                        textInput.style.fontSize = 24f;

                        if (titleLabel != null)
                        {
                            float fs = titleLabel.resolvedStyle.fontSize;
                            if (fs > 0f)
                            {
                                textInput.style.fontSize = fs;
                            }
                            textInput.style.unityFontStyleAndWeight = titleLabel.resolvedStyle.unityFontStyleAndWeight;
                        }

                        // Calculate and apply initial width
                        float currentFs = 24f;
                        if (titleLabel != null && titleLabel.resolvedStyle.fontSize > 0f)
                        {
                            currentFs = titleLabel.resolvedStyle.fontSize;
                        }
                        float charWidth = currentFs * 0.55f;
                        float initialWidth = (title.Length + 4) * charWidth;
                        
                        titleField.style.width = initialWidth;
                        textInput.style.width = initialWidth;
                        titleField.style.translate = new Translate(-initialWidth / 2f, 0f);

                        titleField.RegisterCallback<FocusEvent>(evt =>
                        {
                            if (titleLabel != null)
                            {
                                float fs = titleLabel.resolvedStyle.fontSize;
                                if (fs > 0f)
                                {
                                    textInput.style.fontSize = fs;
                                    
                                    float cw = fs * 0.55f;
                                    float rw = (titleField.value.Length + 4) * cw;
                                    titleField.style.width = rw;
                                    textInput.style.width = rw;
                                    titleField.style.translate = new Translate(-rw / 2f, 0f);
                                }
                                textInput.style.unityFontStyleAndWeight = titleLabel.resolvedStyle.unityFontStyleAndWeight;
                            }
                        });

                        titleField.RegisterValueChangedCallback(evt =>
                        {
                            string newText = evt.newValue ?? string.Empty;
                            float fs = 24f;
                            if (titleLabel != null && titleLabel.resolvedStyle.fontSize > 0f)
                            {
                                fs = titleLabel.resolvedStyle.fontSize;
                            }

                            float cw = fs * 0.55f;
                            float rw = (newText.Length + 4) * cw;
                            
                            titleField.style.width = rw;
                            textInput.style.width = rw;
                            titleField.style.translate = new Translate(-rw / 2f, 0f);
                        });
                    }
                }
            });

            RegisterCallback<FocusOutEvent>(OnGroupFocusOut);
            this.AddManipulator(new ContextualMenuManipulator(OnContextMenuPopulate));
        }

        private void OnContextMenuPopulate(ContextualMenuPopulateEvent contextEvent)
        {
            contextEvent.menu.AppendAction("Color/Default", _ => SetGroupColor(new Color(0.15f, 0.15f, 0.15f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Red", _ => SetGroupColor(new Color(1.0f, 0.2f, 0.2f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Green", _ => SetGroupColor(new Color(0.2f, 1.0f, 0.2f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Blue", _ => SetGroupColor(new Color(0.2f, 0.2f, 1.0f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Yellow", _ => SetGroupColor(new Color(1.0f, 1.0f, 0.2f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Purple", _ => SetGroupColor(new Color(0.8f, 0.2f, 0.8f, 0.3f)));
            contextEvent.menu.AppendAction("Color/Teal", _ => SetGroupColor(new Color(0.2f, 0.8f, 0.8f, 0.3f)));
        }

        private void SetGroupColor(Color color)
        {
            GraphView graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView == null)
            {
                ApplyColorToThis(color);
                return;
            }

            System.Collections.Generic.List<GroupView> selectedGroups = new System.Collections.Generic.List<GroupView>();
            foreach (ISelectable selectable in graphView.selection)
            {
                if (selectable is GroupView gv)
                {
                    selectedGroups.Add(gv);
                }
            }

            if (!selectedGroups.Contains(this))
            {
                selectedGroups.Add(this);
            }

            Undo.RecordObject(_graph, "Change Group Color");
            foreach (GroupView gv in selectedGroups)
            {
                gv.ApplyColorToThis(color);
            }
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        private void ApplyColorToThis(Color color)
        {
            if (_groupData != null)
            {
                _groupData.BackgroundColor = color;
                style.backgroundColor = color;
            }
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            if (_suppressSync || _graph == null || _groupData == null)
            {
                return;
            }

            if (_groupData.Position == newPos)
            {
                return;
            }

            Undo.RecordObject(_graph, "Move Group");
            _groupData.Position = newPos;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        private void OnGroupFocusOut(FocusOutEvent focusOutEvent)
        {
            if (_suppressSync || _graph == null || _groupData == null)
            {
                return;
            }

            string currentTitle = title ?? string.Empty;
            if (string.Equals(_groupData.Title, currentTitle, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Rename Group");
            _groupData.Title = currentTitle;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }
    }
}
