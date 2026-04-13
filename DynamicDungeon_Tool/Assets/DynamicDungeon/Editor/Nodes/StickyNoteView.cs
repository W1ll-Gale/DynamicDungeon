using System;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class StickyNoteView : GraphElement
    {
        private static readonly Color NoteBackgroundColour = new Color(0.97f, 0.95f, 0.6f, 1.0f);
        private static readonly Color NoteForegroundColour = new Color(0.08f, 0.08f, 0.08f, 1.0f);
        private static readonly Color NoteSelectedBorderColour = new Color(0.24f, 0.54f, 0.84f, 1.0f);
        private static readonly Color NoteUnselectedBorderColour = new Color(0.8f, 0.78f, 0.45f, 1.0f);

        private readonly GenGraph _graph;
        private readonly GenStickyNoteData _noteData;
        private readonly Action _afterMutation;

        private readonly Label _contentsLabel;
        private readonly TextField _contentsField;

        private bool _suppressSync;

        public string NoteId
        {
            get
            {
                return _noteData != null ? _noteData.NoteId : string.Empty;
            }
        }

        public StickyNoteView(GenGraph graph, GenStickyNoteData noteData, Action afterMutation)
        {
            _graph = graph;
            _noteData = noteData;
            _afterMutation = afterMutation;

            capabilities = Capabilities.Movable | Capabilities.Deletable | Capabilities.Selectable;

            style.position = Position.Absolute;
            style.minWidth = 120.0f;
            style.minHeight = 60.0f;
            style.backgroundColor = NoteBackgroundColour;
            style.borderTopLeftRadius = 4.0f;
            style.borderTopRightRadius = 4.0f;
            style.borderBottomLeftRadius = 4.0f;
            style.borderBottomRightRadius = 4.0f;
            style.borderTopWidth = 1.0f;
            style.borderBottomWidth = 1.0f;
            style.borderLeftWidth = 1.0f;
            style.borderRightWidth = 1.0f;
            style.borderTopColor = NoteUnselectedBorderColour;
            style.borderBottomColor = NoteUnselectedBorderColour;
            style.borderLeftColor = NoteUnselectedBorderColour;
            style.borderRightColor = NoteUnselectedBorderColour;
            style.paddingTop = 6.0f;
            style.paddingBottom = 6.0f;
            style.paddingLeft = 8.0f;
            style.paddingRight = 8.0f;

            _contentsLabel = new Label();
            _contentsLabel.style.color = NoteForegroundColour;
            _contentsLabel.style.whiteSpace = WhiteSpace.Normal;
            _contentsLabel.style.flexGrow = 1.0f;
            _contentsLabel.RegisterCallback<MouseDownEvent>(OnContentsMouseDown);
            Add(_contentsLabel);

            _contentsField = new TextField();
            _contentsField.multiline = true;
            _contentsField.style.display = DisplayStyle.None;
            _contentsField.style.flexGrow = 1.0f;
            _contentsField.style.color = NoteForegroundColour;
            _contentsField.style.backgroundColor = Color.clear;
            _contentsField.style.borderTopWidth = 0.0f;
            _contentsField.style.borderBottomWidth = 0.0f;
            _contentsField.style.borderLeftWidth = 0.0f;
            _contentsField.style.borderRightWidth = 0.0f;
            _contentsField.RegisterCallback<FocusOutEvent>(OnContentsFocusOut);
            _contentsField.RegisterCallback<KeyDownEvent>(OnContentsFieldKeyDown);

            VisualElement textInput = _contentsField.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.style.backgroundColor = Color.clear;
                textInput.style.borderTopWidth = 0.0f;
                textInput.style.borderBottomWidth = 0.0f;
                textInput.style.borderLeftWidth = 0.0f;
                textInput.style.borderRightWidth = 0.0f;
                textInput.style.color = NoteForegroundColour;
                textInput.style.paddingTop = 0.0f;
                textInput.style.paddingBottom = 0.0f;
                textInput.style.paddingLeft = 0.0f;
                textInput.style.paddingRight = 0.0f;
            }

            Add(_contentsField);
            this.AddManipulator(new ContextualMenuManipulator(OnContextMenuPopulate));

            _suppressSync = true;
            SetPosition(noteData.Position);
            _contentsLabel.text = noteData.Text ?? string.Empty;
            _contentsField.SetValueWithoutNotify(noteData.Text ?? string.Empty);
            _suppressSync = false;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            style.width = newPos.width;
            style.height = newPos.height;

            if (_suppressSync || _graph == null || _noteData == null)
            {
                return;
            }

            if (_noteData.Position == newPos)
            {
                return;
            }

            Undo.RecordObject(_graph, "Move Sticky Note");
            _noteData.Position = newPos;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        private void OnContentsMouseDown(MouseDownEvent e)
        {
            if (e.clickCount != 2)
            {
                return;
            }

            EnterEditMode();

            e.StopPropagation();
            focusController?.IgnoreEvent(e);
        }

        private void EnterEditMode()
        {
            _contentsField.SetValueWithoutNotify(_contentsLabel.text);
            _contentsLabel.style.display = DisplayStyle.None;
            _contentsField.style.display = DisplayStyle.Flex;

            VisualElement textInput = _contentsField.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.style.backgroundColor = Color.clear;
                textInput.style.borderTopWidth = 0.0f;
                textInput.style.borderBottomWidth = 0.0f;
                textInput.style.borderLeftWidth = 0.0f;
                textInput.style.borderRightWidth = 0.0f;
                textInput.style.color = NoteForegroundColour;
                textInput.style.paddingTop = 0.0f;
                textInput.style.paddingBottom = 0.0f;
                textInput.style.paddingLeft = 0.0f;
                textInput.style.paddingRight = 0.0f;
                textInput.Focus();
            }
            else
            {
                _contentsField.Focus();
            }
        }

        private void OnContextMenuPopulate(ContextualMenuPopulateEvent contextEvent)
        {
            contextEvent.menu.AppendAction("Edit Note", _ => EnterEditMode());
            contextEvent.menu.AppendAction("Duplicate Note", _ => DuplicateNote());
            contextEvent.menu.AppendAction("Delete", _ => DeleteNote());
            contextEvent.menu.AppendSeparator();
            contextEvent.menu.AppendAction("Copy Text", _ => CopyTextToClipboard(), _ => string.IsNullOrEmpty(_noteData?.Text) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            contextEvent.menu.AppendAction("Clear Text", _ => ClearText(), _ => string.IsNullOrEmpty(_noteData?.Text) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            contextEvent.menu.AppendSeparator();
            contextEvent.menu.AppendAction("Select All Notes", _ => SelectAllNotes());
        }

        private void DuplicateNote()
        {
            DynamicDungeon.Editor.Windows.DynamicDungeonGraphView graphView = GetFirstAncestorOfType<DynamicDungeon.Editor.Windows.DynamicDungeonGraphView>();
            if (graphView != null && _noteData != null)
            {
                Rect currentUIPos = GetPosition();
                Vector2 newUIPos = new Vector2(currentUIPos.x + 20.0f, currentUIPos.y + 20.0f);
                graphView.CreateStickyNote(newUIPos, _noteData.Text);
            }
        }

        private void CopyTextToClipboard()
        {
            if (_noteData != null && !string.IsNullOrEmpty(_noteData.Text))
            {
                GUIUtility.systemCopyBuffer = _noteData.Text;
            }
        }

        private void ClearText()
        {
            if (_noteData != null)
            {
                _contentsField.SetValueWithoutNotify(string.Empty);
                _contentsLabel.text = string.Empty;
                
                if (_graph != null)
                {
                    Undo.RecordObject(_graph, "Clear Sticky Note Text");
                    _noteData.Text = string.Empty;
                    EditorUtility.SetDirty(_graph);
                    _afterMutation?.Invoke();
                }
            }
        }

        private void SelectAllNotes()
        {
            GraphView graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView != null)
            {
                graphView.ClearSelection();
                graphView.Query<StickyNoteView>().ForEach(note => graphView.AddToSelection(note));
            }
        }

        private void DeleteNote()
        {
            GraphView graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView != null)
            {
                System.Collections.Generic.List<GraphElement> elementsToDelete = new System.Collections.Generic.List<GraphElement> { this };
                graphView.DeleteElements(elementsToDelete);
            }
        }

        private void OnContentsFocusOut(FocusOutEvent e)
        {
            string newText = _contentsField.value ?? string.Empty;

            _contentsField.style.display = DisplayStyle.None;
            _contentsLabel.style.display = DisplayStyle.Flex;
            _contentsLabel.text = newText;

            if (_graph == null || _noteData == null)
            {
                return;
            }

            if (string.Equals(_noteData.Text, newText, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Edit Sticky Note");
            _noteData.Text = newText;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        private void OnContentsFieldKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _contentsField.SetValueWithoutNotify(_contentsLabel.text);
                _contentsField.style.display = DisplayStyle.None;
                _contentsLabel.style.display = DisplayStyle.Flex;
                e.StopPropagation();
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
            style.borderTopWidth = 2.0f;
            style.borderBottomWidth = 2.0f;
            style.borderLeftWidth = 2.0f;
            style.borderRightWidth = 2.0f;
            style.borderTopColor = NoteSelectedBorderColour;
            style.borderBottomColor = NoteSelectedBorderColour;
            style.borderLeftColor = NoteSelectedBorderColour;
            style.borderRightColor = NoteSelectedBorderColour;
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            style.borderTopWidth = 1.0f;
            style.borderBottomWidth = 1.0f;
            style.borderLeftWidth = 1.0f;
            style.borderRightWidth = 1.0f;
            style.borderTopColor = NoteUnselectedBorderColour;
            style.borderBottomColor = NoteUnselectedBorderColour;
            style.borderLeftColor = NoteUnselectedBorderColour;
            style.borderRightColor = NoteUnselectedBorderColour;
        }
    }
}
