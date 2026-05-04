using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Shared
{
    public enum GenerationInspectorAction
    {
        None,
        Generate,
        Clear,
        Cancel
    }

    public struct GenerationInspectorOptions
    {
        public string GenerateLabel;
        public string GeneratingLabel;
        public string ClearLabel;
        public string Status;
        public float Progress;
        public bool CanGenerate;
        public bool CanClear;
        public bool IsGenerating;
        public bool ShouldShowProgress;
    }

    public static class GenerationInspectorControls
    {
        private const float GenerateButtonHeight = 40.0f;
        private const float SecondaryButtonHeight = 30.0f;

        public static GenerationInspectorAction Draw(GenerationInspectorOptions options)
        {
            if (options.IsGenerating)
            {
                return DrawGenerating(options);
            }

            return DrawReady(options);
        }

        private static GenerationInspectorAction DrawGenerating(GenerationInspectorOptions options)
        {
            Color oldBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1.0f, 0.85f, 0.35f);
            using (new EditorGUI.DisabledScope(true))
            {
                GUILayout.Button(GetLabel(options.GeneratingLabel, "GENERATING..."), GUILayout.Height(GenerateButtonHeight));
            }
            GUI.backgroundColor = oldBackground;

            if (!options.ShouldShowProgress)
            {
                return GenerationInspectorAction.None;
            }

            if (!string.IsNullOrWhiteSpace(options.Status))
            {
                EditorGUILayout.HelpBox(options.Status, MessageType.Info);
            }

            Rect progressRect = GUILayoutUtility.GetRect(18.0f, 18.0f, "TextField");
            float progress = Mathf.Clamp01(options.Progress);
            EditorGUI.ProgressBar(progressRect, progress, Mathf.RoundToInt(progress * 100.0f) + "%");

            GUI.backgroundColor = new Color(1.0f, 0.6f, 0.35f);
            bool cancelRequested = GUILayout.Button("CANCEL GENERATION", GUILayout.Height(SecondaryButtonHeight));
            GUI.backgroundColor = oldBackground;

            return cancelRequested ? GenerationInspectorAction.Cancel : GenerationInspectorAction.None;
        }

        private static GenerationInspectorAction DrawReady(GenerationInspectorOptions options)
        {
            Color oldBackground = GUI.backgroundColor;
            GenerationInspectorAction action = GenerationInspectorAction.None;

            GUI.backgroundColor = new Color(0.4f, 1.0f, 0.4f);
            using (new EditorGUI.DisabledScope(!options.CanGenerate))
            {
                if (GUILayout.Button(GetLabel(options.GenerateLabel, "GENERATE"), GUILayout.Height(GenerateButtonHeight)))
                {
                    action = GenerationInspectorAction.Generate;
                }
            }

            GUI.backgroundColor = new Color(1.0f, 0.4f, 0.4f);
            using (new EditorGUI.DisabledScope(!options.CanClear))
            {
                if (GUILayout.Button(GetLabel(options.ClearLabel, "CLEAR"), GUILayout.Height(SecondaryButtonHeight)))
                {
                    action = GenerationInspectorAction.Clear;
                }
            }

            GUI.backgroundColor = oldBackground;
            return action;
        }

        private static string GetLabel(string label, string fallback)
        {
            return string.IsNullOrWhiteSpace(label) ? fallback : label;
        }
    }
}
