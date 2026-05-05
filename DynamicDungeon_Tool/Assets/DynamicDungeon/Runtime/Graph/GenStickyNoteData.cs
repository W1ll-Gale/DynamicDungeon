using System;
using UnityEngine;

namespace DynamicDungeon.Runtime.Graph
{
    [Serializable]
    public sealed class GenStickyNoteData
    {
        public string NoteId;
        public string Text;
        public Rect Position;
    }
}
