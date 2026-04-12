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
            _contentsField.RegisterCallback<FocusOutEvent>(OnContentsFocusOut);
            _contentsField.RegisterCallback<KeyDownEvent>(OnContentsFieldKeyDown);
            Add(_contentsField);

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

            _contentsField.SetValueWithoutNotify(_contentsLabel.text);
            _contentsLabel.style.display = DisplayStyle.None;
            _contentsField.style.display = DisplayStyle.Flex;

            VisualElement textInput = _contentsField.Q("unity-text-input");
            if (textInput != null)
            {
                textInput.Focus();
            }
            else
            {
                _contentsField.Focus();
            }

            e.StopPropagation();
            focusController?.IgnoreEvent(e);
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
    }
}
