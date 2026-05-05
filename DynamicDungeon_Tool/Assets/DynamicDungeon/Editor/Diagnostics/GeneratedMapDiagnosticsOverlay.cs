using DynamicDungeon.Runtime.Diagnostics;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace DynamicDungeon.Editor.Diagnostics
{
    [Overlay(typeof(SceneView), "Generated Map Diagnostics")]
    public sealed class GeneratedMapDiagnosticsOverlay : IMGUIOverlay
    {
        public override void OnGUI()
        {
            GeneratedMapDiagnosticsWindow window = GeneratedMapDiagnosticsWindow.ActiveWindow;
            if (window == null)
            {
                if (GUILayout.Button("Open Diagnostics"))
                {
                    GeneratedMapDiagnosticsWindow.OpenWindow();
                }

                return;
            }

            EditorGUILayout.LabelField("Map Diagnostics", EditorStyles.boldLabel);
            GeneratedMapDiagnosticTool selectedTool = (GeneratedMapDiagnosticTool)EditorGUILayout.EnumPopup("Tool", window.ActiveTool);
            if (selectedTool != window.ActiveTool)
            {
                window.SetActiveTool(selectedTool);
            }

            if (window.IsPickingStart || window.IsPickingEnd)
            {
                EditorGUILayout.HelpBox(window.IsPickingEnd ? "Click an end cell in the Scene view." : "Click a start/point cell in the Scene view.", MessageType.Info);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(window.ActiveTool == GeneratedMapDiagnosticTool.AStar ? "Pick Start" : "Pick Point"))
            {
                window.SetPickStartMode();
            }

            EditorGUI.BeginDisabledGroup(window.ActiveTool != GeneratedMapDiagnosticTool.AStar);
            if (GUILayout.Button("Pick End"))
            {
                window.SetPickEndMode();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(window.IsRunning);
            if (GUILayout.Button("Run"))
            {
                window.RunActiveTool();
            }
            EditorGUI.EndDisabledGroup();

            if (window.IsRunning)
            {
                if (GUILayout.Button("Cancel"))
                {
                    window.CancelActiveRun();
                }
            }
            else if (GUILayout.Button("Clear"))
            {
                window.ClearResults();
            }
            GUILayout.EndHorizontal();

            window.DrawResultSummaryUI();
        }
    }
}
