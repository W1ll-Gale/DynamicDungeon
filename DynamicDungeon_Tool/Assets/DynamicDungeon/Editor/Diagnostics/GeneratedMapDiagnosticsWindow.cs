using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Editor.Shared;
using DynamicDungeon.Runtime;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Diagnostics;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Editor.Diagnostics
{
    [System.Serializable]
    public sealed class TilemapLayerRuleEntry
    {
        public Tilemap Tilemap;
        public GeneratedMapDiagnosticLayerRuleMode Mode = GeneratedMapDiagnosticLayerRuleMode.BlockAny;
    }

    public sealed class GeneratedMapDiagnosticsWindow : EditorWindow, IHasCustomMenu
    {
        private const float PickDistance = 2.0f;

        // [SerializeField] fields survive domain reloads (Unity serialises EditorWindow)
        [SerializeField] private List<TilemapWorldGenerator> _targets = new List<TilemapWorldGenerator>();
        private List<TilemapWorldGenerator> _subscribedTargets = new List<TilemapWorldGenerator>();
        [SerializeField] private bool _rulesUsePhysics = true;
        [SerializeField] private LayerMask _rulesPhysicsLayerMask = ~0;
        [SerializeField] private Vector2 _rulesPhysicsQuerySize = new Vector2(0.8f, 0.8f);
        [SerializeField] private bool _rulesUseDiscoveredTilemaps = true;
        [SerializeField] private bool _rulesPreferWalkablePick = true;
        [SerializeField] private bool _rulesUsePrefabOccupiedCells = true;
        [SerializeField] private bool _rulesUseSemanticIncludeTags;
        [SerializeField] private bool _rulesUseSemanticExcludeTags;
        [SerializeField] private bool _rulesUseLayerRules;
        [SerializeField] private bool _rulesAllowDiagonal;
        [SerializeField] private bool _rulesIncludeAirCells = true;
        [SerializeField] private bool _rulesAutoDetectColliderTilemaps = true;
        [SerializeField] private int _rulesAirCellPaddingLeft;
        [SerializeField] private int _rulesAirCellPaddingRight;
        [SerializeField] private int _rulesAirCellPaddingBottom;
        [SerializeField] private int _rulesAirCellPaddingTop;
        [SerializeField] private List<string> _rulesSemanticIncludeTags = new List<string>();
        [SerializeField] private List<string> _rulesSemanticExcludeTags = new List<string>();
        [SerializeField] private List<TilemapLayerRuleEntry> _rulesLayerRuleEntries = new List<TilemapLayerRuleEntry>();
        [SerializeField] private GeneratedMapDiagnosticTool _tool;
        [SerializeField] private Vector3 _startWorldPositionSerialized;
        [SerializeField] private bool _hasStartWorldPosition;
        [SerializeField] private Vector3 _endWorldPositionSerialized;
        [SerializeField] private bool _hasEndWorldPosition;
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private int _lastTargetsHash;

        // Grid cell cache â€” survives domain reload, reconstructed without physics on OnEnable
        [SerializeField] private int[]       _cachedCellSourceIndices;
        [SerializeField] private Vector3Int[] _cachedCellPositions;
        [SerializeField] private bool[]      _cachedCellIsWalkable;
        [SerializeField] private bool[]      _cachedCellIsAirCell;
        [SerializeField] private string[]    _cachedCellBlockReasons;
        [SerializeField] private string[]    _cachedCellSourceNames;
        [SerializeField] private Vector3[]   _cachedCellWorldCenters;
        [SerializeField] private Vector3[]   _cachedCellSizes;
        [SerializeField] private Tilemap[]   _cachedCellTilemaps;
        [SerializeField] private Grid[]      _cachedCellGrids;
        [SerializeField] private string[]    _cachedGridSourceNames;
        [SerializeField] private int         _cachedTargetsHash;
        [SerializeField] private bool _cachedResultSuccess;
        [SerializeField] private string _cachedResultMessage;
        [SerializeField] private long _cachedResultElapsed;
        [SerializeField] private int _cachedResultWalkable;
        [SerializeField] private int _cachedResultBlocked;
        [SerializeField] private GeneratedMapDiagnosticCellKey[] _cachedResultVisited;
        [SerializeField] private GeneratedMapDiagnosticCellKey[] _cachedResultPath;
        [SerializeField] private GeneratedMapDiagnosticCellKey[] _cachedResultHeatKeys;
        [SerializeField] private int[] _cachedResultHeatValues;
        [SerializeField] private string _status = "Idle";
        [SerializeField] private string _lastError = string.Empty;

        // Non-serialised transient state (rebuilt on enable)
        private GeneratedMapDiagnosticRules _rules;
        private GeneratedMapDiagnosticGrid _grid;
        private GeneratedMapDiagnosticResult _result;
        private GeneratedMapDiagnosticCellKey? _start;
        private GeneratedMapDiagnosticCellKey? _end;
        private Vector3? _startWorldPosition;
        private Vector3? _endWorldPosition;
        private bool _pickStart;
        private bool _pickEnd;
        private bool _isRunning;
        private bool _showProgress;
        private float _progress;
        private CancellationTokenSource _runCancellationSource;
        private bool _gridDirty = true;
        private Mesh _visualizationMesh;
        private Material _visualizationMaterial;
        private ReorderableList _tilemapReorderableList;

        public static GeneratedMapDiagnosticsWindow ActiveWindow { get; private set; }

        [MenuItem(DynamicDungeonMenuPaths.GeneratedMapDiagnostics)]
        public static void OpenWindow()
        {
            GeneratedMapDiagnosticsWindow window = GetWindow<GeneratedMapDiagnosticsWindow>();
            window.titleContent = new GUIContent("Map Diagnostics");
            window.minSize = new Vector2(360.0f, 420.0f);
            window.Show();
        }

        public static void OpenForGenerator(TilemapWorldGenerator generator)
        {
            OpenWindow();
            if (ActiveWindow != null)
            {
                ActiveWindow.SetSingleTarget(generator);
            }
        }

        public bool IsPickingStart => _pickStart;
        public bool IsPickingEnd => _pickEnd;
        public GeneratedMapDiagnosticTool ActiveTool => _tool;
        public GeneratedMapDiagnosticGrid CurrentGrid => _grid;
        public GeneratedMapDiagnosticResult CurrentResult => _result;
        public GeneratedMapDiagnosticCellKey? StartKey => _start;
        public GeneratedMapDiagnosticCellKey? EndKey => _end;
        public bool IsRunning => _isRunning;
        public string LastError => _lastError;

        public void SetActiveTool(GeneratedMapDiagnosticTool tool)
        {
            _tool = tool;
            if (_tool != GeneratedMapDiagnosticTool.AStar)
            {
                _end = null;
            }

            Repaint();
            SceneView.RepaintAll();
        }

        public void SetPickStartMode()
        {
            _pickStart = true;
            _pickEnd = false;
            SceneView.RepaintAll();
        }

        public void SetPickEndMode()
        {
            _pickStart = false;
            _pickEnd = true;
            SceneView.RepaintAll();
        }

        public void ClearPickMode()
        {
            _pickStart = false;
            _pickEnd = false;
            SceneView.RepaintAll();
        }

        public void PickWorldPosition(Vector3 worldPosition)
        {
            EnsureGrid();
            bool preferWalkable = _rules != null ? _rules.PreferWalkablePick : _rulesPreferWalkablePick;
            GeneratedMapDiagnosticCell cell = GeneratedMapDiagnostics.PickNearestCell(_grid, worldPosition, PickDistance, preferWalkable);
            if (cell == null)
            {
                ShowNotification(new GUIContent("No generated cell near pick."));
                return;
            }

            if (_pickEnd)
            {
                _end = cell.Key;
                _endWorldPosition = cell.WorldCenter;
                _endWorldPositionSerialized = cell.WorldCenter;
                _hasEndWorldPosition = true;
            }
            else
            {
                _start = cell.Key;
                _startWorldPosition = cell.WorldCenter;
                _startWorldPositionSerialized = cell.WorldCenter;
                _hasStartWorldPosition = true;
            }

            if (_tool != GeneratedMapDiagnosticTool.AStar)
            {
                _end = null;
                _endWorldPosition = null;
                _hasEndWorldPosition = false;
            }

            ClearPickMode();
            Repaint();
        }

        public void RunActiveTool()
        {
            StartRun();
        }

        public void ClearResults()
        {
            if (_isRunning)
            {
                CancelRun();
            }

            _result = null;
            _start = null;
            _end = null;
            _startWorldPosition = null;
            _endWorldPosition = null;
            _hasStartWorldPosition = false;
            _hasEndWorldPosition = false;
            _status = "Cleared.";
            _lastError = string.Empty;
            _progress = 0.0f;
            _showProgress = false;

            if (_visualizationMesh != null)
            {
                DestroyImmediate(_visualizationMesh);
                _visualizationMesh = null;
            }

            SceneView.RepaintAll();
            Repaint();
        }

        public void CancelActiveRun()
        {
            CancelRun();
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Force Rebuild Grid"), false, InvalidateGrid);
        }

        // Written by background thread, read by main thread – must be volatile.
        private volatile float _targetProgress;
        private volatile int _nodesVisited;
        private volatile int _nodesTotal;
        private volatile string _liveStatus;
        private float _displayProgress;
        private double _lastProgressUpdateTime;

        // Per-frame snapshots of volatile fields taken on the Layout event.
        // Unity calls OnGUI twice per repaint (Layout then Repaint); the background thread can
        // write volatile fields between those two calls, changing conditional control counts and
        // causing a "Getting control N in a group with only N controls" layout mismatch.
        private int _guiNodesVisited;
        private int _guiNodesTotal;
        private string _guiLiveStatus = string.Empty;
        private void OnEnable()
        {
            ActiveWindow = this;
            SceneView.duringSceneGui += OnSceneGUI;

            // Restore serialized world positions
            _startWorldPosition = _hasStartWorldPosition ? (Vector3?)_startWorldPositionSerialized : null;
            _endWorldPosition   = _hasEndWorldPosition   ? (Vector3?)_endWorldPositionSerialized   : null;

            // Rebuild the transient rules object from serialized fields
            SyncRulesFromFields();

            // Rebuild the ReorderableList for tilemap rules (lost on domain reload)
            RebuildTilemapReorderableList();

            if (_targets.Count == 0)
            {
                AddSelectionTargets();
            }

            // Try to restore the cached grid (avoids full rebuild after domain reload)
            if (!TryRestoreGridFromCache())
            {
                _gridDirty = true;
            }
            UpdateSubscriptions();
        }

        private void OnDisable()
        {
            ClearAllSubscriptions();
            if (ActiveWindow == this)
            {
                ActiveWindow = null;
            }

            CancelRun();
            SceneView.duringSceneGui -= OnSceneGUI;

            if (_visualizationMesh != null)
            {
                DestroyImmediate(_visualizationMesh);
            }

            if (_visualizationMaterial != null)
            {
                DestroyImmediate(_visualizationMaterial);
            }
        }

        private void OnSelectionChange()
        {
            Repaint();
        }

        private void OnGUI()
        {
            // Snapshot volatile fields once per Layout pass so that the subsequent Repaint pass
            // draws the exact same set of controls even if the background thread writes new values
            // in the gap between the two calls.
            if (Event.current.type == EventType.Layout)
            {
                _guiNodesVisited = _nodesVisited;
                _guiNodesTotal   = _nodesTotal;
                _guiLiveStatus   = _liveStatus ?? string.Empty;
            }

            UpdateSubscriptions();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawTargets();
            DrawToolControls();
            DrawRules();
            DrawActions();
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargets()
        {
            EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Targets are optional. Rebuild dynamically discovers scene Tilemaps from dungeon generators, Tilemap World output, and prefab instances even when no TilemapWorldGenerator is selected.", MessageType.Info);
            int removeIndex = -1;
            int index;
            for (index = 0; index < _targets.Count; index++)
            {
                EditorGUILayout.BeginHorizontal();
                _targets[index] = (TilemapWorldGenerator)EditorGUILayout.ObjectField(_targets[index], typeof(TilemapWorldGenerator), true);
                if (GUILayout.Button("-", GUILayout.Width(24.0f)))
                {
                    removeIndex = index;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
            {
                _targets.RemoveAt(removeIndex);
                InvalidateGrid();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Selected"))
            {
                AddSelectionTargets();
                InvalidateGrid();
            }

            if (GUILayout.Button("Add Slot"))
            {
                _targets.Add(null);
                InvalidateGrid();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolControls()
        {
            GUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Tool", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _tool = (GeneratedMapDiagnosticTool)EditorGUILayout.EnumPopup("Active Tool", _tool);
            if (EditorGUI.EndChangeCheck())
            {
                ClearResults();
            }

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = _pickStart ? Color.cyan : Color.white;
            if (GUILayout.Button(_tool == GeneratedMapDiagnosticTool.AStar ? "Pick Start" : "Pick Point"))
            {
                SetPickStartMode();
            }

            GUI.backgroundColor = _pickEnd ? Color.cyan : Color.white;
            EditorGUI.BeginDisabledGroup(_tool != GeneratedMapDiagnosticTool.AStar);
            if (GUILayout.Button("Pick End"))
            {
                SetPickEndMode();
            }
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            DrawCellKey("Start", _start);
            DrawCellDetails(_start);
            if (_tool == GeneratedMapDiagnosticTool.AStar)
            {
                DrawCellKey("End", _end);
                DrawCellDetails(_end);
            }
        }

        private void DrawRules()
        {
            GUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Walkability Rules", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            _rulesUsePhysics = EditorGUILayout.Toggle("Use Physics", _rulesUsePhysics);
            if (_rulesUsePhysics)
            {
                _rulesPhysicsLayerMask = LayerMaskField("Physics Layers", _rulesPhysicsLayerMask);
                _rulesPhysicsQuerySize = EditorGUILayout.Vector2Field("Physics Query Size", _rulesPhysicsQuerySize);
            }

            _rulesUseDiscoveredTilemaps = EditorGUILayout.Toggle("Auto Discover Scene Tilemaps", _rulesUseDiscoveredTilemaps);
            _rulesPreferWalkablePick = EditorGUILayout.Toggle("Prefer Walkable Picks", _rulesPreferWalkablePick);
            _rulesUsePrefabOccupiedCells = EditorGUILayout.Toggle("Block Prefab Footprints", _rulesUsePrefabOccupiedCells);
            _rulesAllowDiagonal = EditorGUILayout.Toggle("Allow Diagonal", _rulesAllowDiagonal);
            _rulesIncludeAirCells = EditorGUILayout.Toggle("Include Air Cells", _rulesIncludeAirCells);
            if (_rulesIncludeAirCells)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Air Cell Padding (cells)", EditorStyles.miniLabel);
                _rulesAirCellPaddingTop    = EditorGUILayout.IntField("Top",    _rulesAirCellPaddingTop);
                _rulesAirCellPaddingBottom = EditorGUILayout.IntField("Bottom", _rulesAirCellPaddingBottom);
                _rulesAirCellPaddingLeft   = EditorGUILayout.IntField("Left",   _rulesAirCellPaddingLeft);
                _rulesAirCellPaddingRight  = EditorGUILayout.IntField("Right",  _rulesAirCellPaddingRight);
                EditorGUI.indentLevel--;
            }
            _rulesAutoDetectColliderTilemaps = EditorGUILayout.Toggle("Auto-Block Collider Tilemaps", _rulesAutoDetectColliderTilemaps);

            _rulesUseSemanticIncludeTags = EditorGUILayout.Toggle("Use Include Tags", _rulesUseSemanticIncludeTags);
            if (_rulesUseSemanticIncludeTags)
            {
                DrawTagCheckboxGrid("Include Tags", _rulesSemanticIncludeTags);
            }

            _rulesUseSemanticExcludeTags = EditorGUILayout.Toggle("Use Exclude Tags", _rulesUseSemanticExcludeTags);
            if (_rulesUseSemanticExcludeTags)
            {
                DrawTagCheckboxGrid("Exclude Tags", _rulesSemanticExcludeTags);
            }

            _rulesUseLayerRules = EditorGUILayout.Toggle("Use Tilemap Layer Rules", _rulesUseLayerRules);
            if (_rulesUseLayerRules)
            {
                DrawTilemapReorderableList();
            }

            if (EditorGUI.EndChangeCheck())
            {
                SyncRulesFromFields();
                InvalidateGrid();
            }
        }

        private void DrawTilemapReorderableList()
        {
            if (_tilemapReorderableList == null)
            {
                RebuildTilemapReorderableList();
            }

            EditorGUI.BeginChangeCheck();
            _tilemapReorderableList.DoLayoutList();
            if (EditorGUI.EndChangeCheck())
            {
                SyncRulesFromFields();
                InvalidateGrid();
            }
        }

        private void RebuildTilemapReorderableList()
        {
            _tilemapReorderableList = new ReorderableList(_rulesLayerRuleEntries, typeof(TilemapLayerRuleEntry), true, true, true, true);
            _tilemapReorderableList.elementHeight = EditorGUIUtility.singleLineHeight + 4.0f;
            _tilemapReorderableList.drawHeaderCallback = rect =>
            {
                float modeWidth = 120.0f;
                float tilemapWidth = rect.width - modeWidth - 4.0f;
                EditorGUI.LabelField(new Rect(rect.x, rect.y, tilemapWidth, rect.height), "Tilemap");
                EditorGUI.LabelField(new Rect(rect.x + tilemapWidth + 4.0f, rect.y, modeWidth, rect.height), "Rule Mode");
            };
            _tilemapReorderableList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                if (index < 0 || index >= _rulesLayerRuleEntries.Count)
                {
                    return;
                }

                TilemapLayerRuleEntry entry = _rulesLayerRuleEntries[index];
                rect.y += 2.0f;
                rect.height = EditorGUIUtility.singleLineHeight;
                float modeWidth = 120.0f;
                float tilemapWidth = rect.width - modeWidth - 4.0f;

                entry.Tilemap = (Tilemap)EditorGUI.ObjectField(
                    new Rect(rect.x, rect.y, tilemapWidth, rect.height),
                    entry.Tilemap, typeof(Tilemap), true);
                entry.Mode = (GeneratedMapDiagnosticLayerRuleMode)EditorGUI.EnumPopup(
                    new Rect(rect.x + tilemapWidth + 4.0f, rect.y, modeWidth, rect.height),
                    entry.Mode);
            };
            _tilemapReorderableList.onAddCallback = list =>
            {
                _rulesLayerRuleEntries.Add(new TilemapLayerRuleEntry());
                SyncRulesFromFields();
                InvalidateGrid();
            };
            _tilemapReorderableList.onRemoveCallback = list =>
            {
                if (list.index >= 0 && list.index < _rulesLayerRuleEntries.Count)
                {
                    _rulesLayerRuleEntries.RemoveAt(list.index);
                }
                SyncRulesFromFields();
                InvalidateGrid();
            };
        }

        private void DrawTagCheckboxGrid(string label, List<string> selectedTags)
        {
            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (registry == null || registry.AllTags == null || registry.AllTags.Count == 0)
            {
                EditorGUILayout.HelpBox("No semantic registry tags found.", MessageType.Info);
                return;
            }

            // Two-column checkbox grid matching the registry editor style
            int tagCount = registry.AllTags.Count;
            int columns = 2;
            int rows = Mathf.CeilToInt((float)tagCount / columns);
            float colWidth = (EditorGUIUtility.currentViewWidth - 32.0f) / columns;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int row = 0; row < rows; row++)
            {
                EditorGUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int tagIndex = row * columns + col;
                    if (tagIndex >= tagCount)
                    {
                        GUILayout.FlexibleSpace();
                        break;
                    }

                    string tag = registry.AllTags[tagIndex];
                    bool isSelected = ContainsTag(selectedTags, tag);
                    bool newSelected = EditorGUILayout.ToggleLeft(tag, isSelected, GUILayout.Width(colWidth));
                    if (newSelected != isSelected)
                    {
                        if (newSelected)
                        {
                            selectedTags.Add(tag);
                        }
                        else
                        {
                            RemoveTag(selectedTags, tag);
                        }
                        SyncRulesFromFields();
                        InvalidateGrid();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private static bool ContainsTag(List<string> tags, string tag)
        {
            if (tags == null)
            {
                return false;
            }

            int index;
            for (index = 0; index < tags.Count; index++)
            {
                if (string.Equals(tags[index], tag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveTag(List<string> tags, string tag)
        {
            if (tags == null)
            {
                return;
            }

            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (string.Equals(tags[i], tag, System.StringComparison.OrdinalIgnoreCase))
                {
                    tags.RemoveAt(i);
                    return;
                }
            }
        }

        private void DrawActions()
        {
            GUILayout.Space(8.0f);
            GenerationInspectorAction action = GenerationInspectorControls.Draw(
                new GenerationInspectorOptions
                {
                    GenerateLabel = "RUN DIAGNOSTIC",
                    GeneratingLabel = "RUNNING DIAGNOSTIC...",
                    ClearLabel = "CLEAR DIAGNOSTIC",
                    Status = _status,
                    Progress = _progress,
                    CanGenerate = !_isRunning,
                    CanClear = !_isRunning,
                    IsGenerating = _isRunning,
                    ShouldShowProgress = _showProgress,
                    LiveStats = GetLiveStats()
                });

            if (action == GenerationInspectorAction.Generate)
            {
                StartRun();
            }
            else if (action == GenerationInspectorAction.Clear)
            {
                ClearResults();
            }
            else if (action == GenerationInspectorAction.Cancel)
            {
                CancelRun();
            }
        }

        private void DrawStatus()
        {
            GUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
            if (_grid != null)
            {
                EditorGUILayout.LabelField("Cells", _grid.Cells.Count.ToString());
                EditorGUILayout.LabelField("Sources", _grid.SourceNames.Count.ToString());
                int errorIndex;
                for (errorIndex = 0; errorIndex < _grid.Errors.Count; errorIndex++)
                {
                    EditorGUILayout.HelpBox(_grid.Errors[errorIndex], MessageType.Warning);
                }
            }

            if (_result == null)
            {
                EditorGUILayout.LabelField("No diagnostic result yet.");
                if (!string.IsNullOrWhiteSpace(_lastError))
                {
                    EditorGUILayout.HelpBox(_lastError, MessageType.Error);
                }

                if (!string.IsNullOrWhiteSpace(_status))
                {
                    EditorGUILayout.HelpBox(_status, MessageType.Info);
                }

                if (_isRunning)
                {
                    EditorGUILayout.LabelField("Live Stats", EditorStyles.boldLabel);
                    if (!string.IsNullOrEmpty(_guiLiveStatus)) EditorGUILayout.LabelField("Task", _guiLiveStatus);
                    EditorGUILayout.LabelField("Nodes Visited", _guiNodesVisited.ToString());
                    if (_guiNodesTotal > 0) EditorGUILayout.LabelField("Nodes Total", _guiNodesTotal.ToString());
                }
                return;
            }

            EditorGUILayout.LabelField("Result", _result.Success ? "Success" : "Failed");
            EditorGUILayout.LabelField("Message", _result.Message);
            EditorGUILayout.LabelField("Elapsed", _result.ElapsedMilliseconds + " ms");
            EditorGUILayout.LabelField("Walkable / Blocked", _result.WalkableCellCount + " / " + _result.BlockedCellCount);
            EditorGUILayout.LabelField("Visited", _result.Visited.Count.ToString());
            if (_result.Path.Count > 0)
            {
                EditorGUILayout.LabelField("Path Length", _result.Path.Count.ToString());
            }

            if (_result.Islands.Count > 0)
            {
                EditorGUILayout.LabelField("Islands", _result.Islands.Count.ToString());
                GeneratedMapDiagnosticIsland largest = null;
                int index;
                for (index = 0; index < _result.Islands.Count; index++)
                {
                    if (largest == null || _result.Islands[index].Cells.Count > largest.Cells.Count)
                    {
                        largest = _result.Islands[index];
                    }
                }

                if (largest != null)
                {
                    EditorGUILayout.LabelField("Largest Island", largest.Cells.Count.ToString());
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            DrawSceneVisualization();
            if (!_pickStart && !_pickEnd)
            {
                return;
            }

            Event current = Event.current;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(12.0f, 12.0f, 260.0f, 46.0f), EditorStyles.helpBox);
            GUILayout.Label(_pickEnd ? "Click a scene cell for A* end." : "Click a scene cell for start/point.");
            GUILayout.EndArea();
            Handles.EndGUI();

            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                Plane plane = new Plane(Vector3.forward, Vector3.zero);
                if (plane.Raycast(ray, out float enter))
                {
                    PickWorldPosition(ray.GetPoint(enter));
                    current.Use();
                }
            }
        }

        private void DrawSceneVisualization()
        {
            if (_grid == null)
            {
                return;
            }

            if (_result != null)
            {
                if (Event.current.type == EventType.Repaint && _visualizationMesh != null)
                {
                    if (_visualizationMaterial == null)
                    {
                        Shader shader = Shader.Find("Hidden/Internal-Colored");
                        if (shader != null)
                        {
                            _visualizationMaterial = new Material(shader);
                            _visualizationMaterial.hideFlags = HideFlags.HideAndDontSave;
                        }
                    }

                    if (_visualizationMaterial != null)
                    {
                        _visualizationMaterial.SetPass(0);
                        Graphics.DrawMeshNow(_visualizationMesh, Matrix4x4.identity);
                    }
                }
                DrawPath();
            }

            DrawPickedCell(_start, Color.green);
            DrawPickedCell(_end, Color.red);
        }

        private void DrawPath()
        {
            if (_result.Path.Count == 0)
            {
                return;
            }

            Handles.color = Color.green;
            int index;
            for (index = 1; index < _result.Path.Count; index++)
            {
                if (_grid.TryGetCell(_result.Path[index - 1], out GeneratedMapDiagnosticCell previous) && _grid.TryGetCell(_result.Path[index], out GeneratedMapDiagnosticCell current))
                {
                    Handles.DrawAAPolyLine(4.0f, previous.WorldCenter, current.WorldCenter);
                }
            }
        }

        private void DrawPickedCell(GeneratedMapDiagnosticCellKey? key, Color color)
        {
            if (!key.HasValue)
            {
                return;
            }

            DrawCell(key.Value, new Color(color.r, color.g, color.b, 0.55f));
        }

        private void DrawCell(GeneratedMapDiagnosticCellKey key, Color color)
        {
            if (_grid == null || !_grid.TryGetCell(key, out GeneratedMapDiagnosticCell cell) || cell.Grid == null)
            {
                return;
            }

            Vector3 size = cell.CellSize;
            if (size == Vector3.zero)
            {
                size = Vector3.one;
            }

            Vector3 center = cell.WorldCenter;
            Vector3[] verts =
            {
                center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0.0f),
                center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0.0f),
                center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0.0f),
                center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0.0f)
            };
            Handles.DrawSolidRectangleWithOutline(verts, color, new Color(color.r, color.g, color.b, 0.9f));
        }

        private void StartRun()
        {
            if (_isRunning)
            {
                return;
            }

            if (_tool == GeneratedMapDiagnosticTool.AStar && (!_startWorldPosition.HasValue || !_endWorldPosition.HasValue))
            {
                ShowNotification(new GUIContent("Pick start and end first."));
                return;
            }

            if (_tool == GeneratedMapDiagnosticTool.BfsHeatmap && !_startWorldPosition.HasValue)
            {
                ShowNotification(new GUIContent("Pick a start point first."));
                return;
            }

            _runCancellationSource = new CancellationTokenSource();
            _ = RunToolAsync(_runCancellationSource.Token);
        }

        private async Task RunToolAsync(CancellationToken cancellationToken)
        {
            _isRunning = true; _nodesVisited = 0; _nodesTotal = 0; _liveStatus = string.Empty;
            _showProgress = true;
            _progress = 0.05f;
            _lastError = string.Empty;
            _result = null;
            Repaint();

            try
            {
                if (_grid == null || _gridDirty)
                {
                    _status = "Rebuilding diagnostic grid...";
                    Repaint();
                    RebuildGrid();
                }

                cancellationToken.ThrowIfCancellationRequested();
                RemapPickedCells();
                if (_tool == GeneratedMapDiagnosticTool.AStar && (!_start.HasValue || !_end.HasValue))
                {
                    _status = "Could not remap start or end onto the rebuilt diagnostic grid.";
                    _lastError = _status;
                    return;
                }

                if (_tool == GeneratedMapDiagnosticTool.BfsHeatmap && !_start.HasValue)
                {
                    _status = "Could not remap start onto the rebuilt diagnostic grid.";
                    _lastError = _status;
                    return;
                }

                _lastProgressUpdateTime = EditorApplication.timeSinceStartup;
                _displayProgress = 0.25f;
                _targetProgress = 0.25f;
                _status = "Running " + _tool + "...";
                Repaint();

                GeneratedMapDiagnosticGrid runGrid = _grid;
                GeneratedMapDiagnosticRules runRules = CloneRules(_rules);
                GeneratedMapDiagnosticCellKey? runStart = _start;
                GeneratedMapDiagnosticCellKey? runEnd = _end;
                GeneratedMapDiagnosticTool runTool = _tool;

                IProgress<GeneratedMapDiagnosticProgress> p = new DirectProgress(this);
                _result = await Task.Run(() =>
                {
                    if (runTool == GeneratedMapDiagnosticTool.AStar)
                    {
                        return GeneratedMapDiagnostics.RunAStar(runGrid, runStart.Value, runEnd.Value, runRules, cancellationToken, p);
                    }

                    if (runTool == GeneratedMapDiagnosticTool.BfsHeatmap)
                    {
                        return GeneratedMapDiagnostics.RunBfs(runGrid, runStart.Value, runRules, cancellationToken, p);
                    }

                    return GeneratedMapDiagnostics.RunFloodFill(runGrid, runRules, cancellationToken, p);
                });

                _targetProgress = 1.0f; _displayProgress = 1.0f; _progress = 1.0f;
                _status = _result.Message;
                BuildVisualizationMesh();
                Repaint();
                SceneView.RepaintAll();
            }
            catch (OperationCanceledException)
            {
                _status = "Diagnostic run cancelled.";
                _progress = 0.0f;
            }
            catch (Exception exception)
            {
                _lastError = exception.GetType().Name + ": " + exception.Message;
                _status = "Diagnostic run failed.";
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Diagnostic run failed. See Stats/Console."));
            }
            finally
            {
                _isRunning = false;
                _showProgress = false;
                _runCancellationSource?.Dispose();
                _runCancellationSource = null;
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void CancelRun()
        {
            _runCancellationSource?.Cancel();
        }

        private static GeneratedMapDiagnosticRules CloneRules(GeneratedMapDiagnosticRules source)
        {
            GeneratedMapDiagnosticRules clone = new GeneratedMapDiagnosticRules();
            clone.PhysicsLayerMask = source.PhysicsLayerMask;
            clone.PhysicsQuerySize = source.PhysicsQuerySize;
            clone.UsePhysics = source.UsePhysics;
            clone.UseDiscoveredTilemaps = source.UseDiscoveredTilemaps;
            clone.PreferWalkablePick = source.PreferWalkablePick;
            clone.UseSemanticIncludeTags = source.UseSemanticIncludeTags;
            clone.UseSemanticExcludeTags = source.UseSemanticExcludeTags;
            clone.UseLayerRules = source.UseLayerRules;
            clone.UsePrefabOccupiedCells = source.UsePrefabOccupiedCells;
            clone.AllowDiagonal = source.AllowDiagonal;
            clone.SemanticIncludeTags.AddRange(source.SemanticIncludeTags);
            clone.SemanticExcludeTags.AddRange(source.SemanticExcludeTags);
            foreach (TilemapLayerRule rule in source.TilemapLayerRules)
            {
                clone.TilemapLayerRules.Add(new TilemapLayerRule { Tilemap = rule.Tilemap, Mode = rule.Mode });
            }
            return clone;
        }

        private void RemapPickedCells()
        {
            if (_startWorldPosition.HasValue)
            {
                GeneratedMapDiagnosticCell startCell = GeneratedMapDiagnostics.PickNearestCell(_grid, _startWorldPosition.Value, PickDistance, _rules.PreferWalkablePick);
                _start = startCell != null ? startCell.Key : (GeneratedMapDiagnosticCellKey?)null;
            }

            if (_endWorldPosition.HasValue)
            {
                GeneratedMapDiagnosticCell endCell = GeneratedMapDiagnostics.PickNearestCell(_grid, _endWorldPosition.Value, PickDistance, _rules.PreferWalkablePick);
                _end = endCell != null ? endCell.Key : (GeneratedMapDiagnosticCellKey?)null;
            }
        }

        private void EnsureGrid()
        {
            if (_grid == null || _gridDirty)
            {
                RebuildGrid();
            }
        }

        private void BuildVisualizationMesh()
        {
            if (_visualizationMesh != null)
            {
                DestroyImmediate(_visualizationMesh);
                _visualizationMesh = null;
            }

            if (_result == null || _grid == null)
            {
                return;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();

            if (_result.Visited.Count > 0 && _result.Heat.Count == 0 && _result.Islands.Count == 0)
            {
                Color color = new Color(1.0f, 0.85f, 0.1f, 0.25f);
                for(int i = 0; i < _result.Visited.Count; i++)
                {
                    AddCellToMesh(_result.Visited[i], color, vertices, colors, indices);
                }
            }

            if (_result.Heat.Count > 0)
            {
                int max = 1;
                foreach (int value in _result.Heat.Values)
                {
                    if (value > max) max = value;
                }

                foreach (KeyValuePair<GeneratedMapDiagnosticCellKey, int> pair in _result.Heat)
                {
                    float t = (float)pair.Value / max;
                    Color color = Color.Lerp(new Color(0.0f, 0.35f, 1.0f, 0.25f), new Color(1.0f, 0.0f, 0.0f, 0.45f), t);
                    AddCellToMesh(pair.Key, color, vertices, colors, indices);
                }
            }

            if (_result.Islands.Count > 0)
            {
                for(int i = 0; i < _result.Islands.Count; i++)
                {
                    GeneratedMapDiagnosticIsland island = _result.Islands[i];
                    Color color = island.Color;
                    color.a = 0.28f;
                    for(int j = 0; j < island.Cells.Count; j++)
                    {
                        AddCellToMesh(island.Cells[j], color, vertices, colors, indices);
                    }
                }
            }

            if (vertices.Count > 0)
            {
                _visualizationMesh = new Mesh();
                _visualizationMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _visualizationMesh.SetVertices(vertices);
                _visualizationMesh.SetColors(colors);
                _visualizationMesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);
            }
        }

        private void AddCellToMesh(GeneratedMapDiagnosticCellKey key, Color color, List<Vector3> vertices, List<Color> colors, List<int> indices)
        {
            if (!_grid.TryGetCell(key, out GeneratedMapDiagnosticCell cell) || cell.Grid == null)
            {
                return;
            }

            Vector3 size = cell.CellSize;
            if (size == Vector3.zero) size = Vector3.one;

            Vector3 center = cell.WorldCenter;
            int startIndex = vertices.Count;
            
            vertices.Add(center + new Vector3(-size.x * 0.5f, -size.y * 0.5f, 0.0f));
            vertices.Add(center + new Vector3(-size.x * 0.5f, size.y * 0.5f, 0.0f));
            vertices.Add(center + new Vector3(size.x * 0.5f, size.y * 0.5f, 0.0f));
            vertices.Add(center + new Vector3(size.x * 0.5f, -size.y * 0.5f, 0.0f));

            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);

            indices.Add(startIndex);
            indices.Add(startIndex + 1);
            indices.Add(startIndex + 2);
            indices.Add(startIndex);
            indices.Add(startIndex + 2);
            indices.Add(startIndex + 3);
        }

        private void RebuildGrid()
        {
            SyncRulesFromFields();
            _grid = GeneratedMapDiagnostics.BuildGrid(_targets, _rules, TileSemanticRegistry.GetOrLoad());
            _lastTargetsHash = GetTargetsHash();
            _gridDirty = false;
            _result = null;
            SerializeGridToCache();
            SceneView.RepaintAll();
        }

        private void SerializeGridToCache()
        {
            if (_grid == null || _grid.Cells.Count == 0)
            {
                _cachedCellSourceIndices = null;
                return;
            }

            int count = _grid.Cells.Count;
            _cachedCellSourceIndices = new int[count];
            _cachedCellPositions     = new Vector3Int[count];
            _cachedCellIsWalkable    = new bool[count];
            _cachedCellIsAirCell     = new bool[count];
            _cachedCellBlockReasons  = new string[count];
            _cachedCellSourceNames   = new string[count];
            _cachedCellWorldCenters  = new Vector3[count];
            _cachedCellSizes         = new Vector3[count];
            _cachedCellTilemaps      = new Tilemap[count];
            _cachedCellGrids         = new Grid[count];

            for (int i = 0; i < count; i++)
            {
                GeneratedMapDiagnosticCell cell = _grid.Cells[i];
                _cachedCellSourceIndices[i] = cell.Key.SourceIndex;
                _cachedCellPositions[i]     = cell.Key.Cell;
                _cachedCellIsWalkable[i]    = cell.IsWalkable;
                _cachedCellIsAirCell[i]     = cell.IsAirCell;
                _cachedCellBlockReasons[i]  = cell.BlockReason ?? string.Empty;
                _cachedCellSourceNames[i]   = cell.SourceName  ?? string.Empty;
                _cachedCellWorldCenters[i]  = cell.WorldCenter;
                _cachedCellSizes[i]         = cell.CellSize;
                _cachedCellTilemaps[i]      = cell.Tilemap;
                _cachedCellGrids[i]         = cell.Grid;
            }

            _cachedGridSourceNames = _grid.SourceNames.ToArray();
            _cachedTargetsHash     = _lastTargetsHash;
        }

        private bool TryRestoreGridFromCache()
        {
            if (_cachedCellSourceIndices == null || _cachedCellSourceIndices.Length == 0)
            {
                return false;
            }

            // Invalidate if the set of generator targets has changed
            if (GetTargetsHash() != _cachedTargetsHash)
            {
                _cachedCellSourceIndices = null;
                return false;
            }

            _grid = GeneratedMapDiagnostics.RestoreGrid(
                _cachedCellSourceIndices,
                _cachedCellPositions,
                _cachedCellIsWalkable,
                _cachedCellIsAirCell,
                _cachedCellBlockReasons,
                _cachedCellSourceNames,
                _cachedCellWorldCenters,
                _cachedCellSizes,
                _cachedCellTilemaps,
                _cachedCellGrids,
                _cachedGridSourceNames);

            _lastTargetsHash = _cachedTargetsHash;
            _gridDirty = false;
            _status = "Grid restored from cache.";

            // Re-map any previously picked world positions
            if (_startWorldPosition.HasValue || _endWorldPosition.HasValue)
            {
                RemapPickedCells();
            }

            return true;
        }

        private void SyncRulesFromFields()
        {
            if (_rules == null)
            {
                _rules = new GeneratedMapDiagnosticRules();
            }

            _rules.UsePhysics = _rulesUsePhysics;
            _rules.PhysicsLayerMask = _rulesPhysicsLayerMask;
            _rules.PhysicsQuerySize = _rulesPhysicsQuerySize;
            _rules.UseDiscoveredTilemaps = _rulesUseDiscoveredTilemaps;
            _rules.PreferWalkablePick = _rulesPreferWalkablePick;
            _rules.UsePrefabOccupiedCells = _rulesUsePrefabOccupiedCells;
            _rules.UseSemanticIncludeTags = _rulesUseSemanticIncludeTags;
            _rules.UseSemanticExcludeTags = _rulesUseSemanticExcludeTags;
            _rules.UseLayerRules = _rulesUseLayerRules;
            _rules.AllowDiagonal = _rulesAllowDiagonal;
            _rules.IncludeAirCells = _rulesIncludeAirCells;
            _rules.AutoDetectColliderTilemaps = _rulesAutoDetectColliderTilemaps;
            _rules.AirCellPaddingLeft   = _rulesAirCellPaddingLeft;
            _rules.AirCellPaddingRight  = _rulesAirCellPaddingRight;
            _rules.AirCellPaddingBottom = _rulesAirCellPaddingBottom;
            _rules.AirCellPaddingTop    = _rulesAirCellPaddingTop;
            _rules.SemanticIncludeTags.Clear();
            _rules.SemanticIncludeTags.AddRange(_rulesSemanticIncludeTags);
            _rules.SemanticExcludeTags.Clear();
            _rules.SemanticExcludeTags.AddRange(_rulesSemanticExcludeTags);
            _rules.TilemapLayerRules.Clear();
            if (_rulesLayerRuleEntries != null)
            {
                for (int i = 0; i < _rulesLayerRuleEntries.Count; i++)
                {
                    TilemapLayerRuleEntry entry = _rulesLayerRuleEntries[i];
                    if (entry != null)
                    {
                        _rules.TilemapLayerRules.Add(new TilemapLayerRule { Tilemap = entry.Tilemap, Mode = entry.Mode });
                    }
                }
            }
        }

        private int GetTargetsHash()
        {
            // Use only instance IDs so the hash is stable across domain reloads.
            // Snapshot-change detection is handled by the OnGenerationCompleted subscription
            // which calls InvalidateGrid(), making the snapshot hash here redundant and harmful
            // (object identity hash changes after every domain reload, busting the cache).
            int hash = 17;
            for (int i = 0; i < _targets.Count; i++)
            {
                TilemapWorldGenerator target = _targets[i];
                hash = hash * 31 + (target != null ? target.GetInstanceID() : 0);
            }
            return hash;
        }

        private void InvalidateGrid()
        {
            _gridDirty = true;
            _result = null;
            if (_visualizationMesh != null)
            {
                DestroyImmediate(_visualizationMesh);
                _visualizationMesh = null;
            }
        }

        private void SetSingleTarget(TilemapWorldGenerator generator)
        {
            _targets.Clear();
            if (generator != null)
            {
                _targets.Add(generator);
            }

            InvalidateGrid();
            Repaint();
        }

        private void AddSelectionTargets()
        {
            foreach (GameObject gameObject in Selection.gameObjects)
            {
                TilemapWorldGenerator generator = gameObject.GetComponent<TilemapWorldGenerator>();
                if (generator == null)
                {
                    generator = gameObject.GetComponentInParent<TilemapWorldGenerator>();
                }

                if (generator != null && !_targets.Contains(generator))
                {
                    _targets.Add(generator);
                }
            }
        }

        private static void DrawCellKey(string label, GeneratedMapDiagnosticCellKey? key)
        {
            string value = key.HasValue ? "Source " + key.Value.SourceIndex + " Cell " + key.Value.Cell : "Not set";
            EditorGUILayout.LabelField(label, value);
        }

        private void DrawCellDetails(GeneratedMapDiagnosticCellKey? key)
        {
            if (_grid == null || !key.HasValue || !_grid.TryGetCell(key.Value, out GeneratedMapDiagnosticCell cell))
            {
                return;
            }

            string label = cell.IsWalkable ? "Walkable" : "Blocked: " + cell.BlockReason;
            EditorGUILayout.LabelField("Picked Source", string.IsNullOrWhiteSpace(cell.SourceName) ? "-" : cell.SourceName);
            EditorGUILayout.LabelField("Picked State", label);
        }

        private static LayerMask LayerMaskField(string label, LayerMask selected)
        {
            int concatenatedMask = UnityEditorInternal.InternalEditorUtility.LayerMaskToConcatenatedLayersMask(selected.value);
            concatenatedMask = EditorGUILayout.MaskField(label, concatenatedMask, UnityEditorInternal.InternalEditorUtility.layers);
            
            LayerMask result = new LayerMask();
            result.value = UnityEditorInternal.InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(concatenatedMask);
            return result;
        }
        private void UpdateSubscriptions()
        {
            // Unsubscribe from anything no longer in targets or now null
            for (int i = _subscribedTargets.Count - 1; i >= 0; i--)
            {
                var target = _subscribedTargets[i];
                if (target == null || !_targets.Contains(target))
                {
                    Unsubscribe(target);
                    _subscribedTargets.RemoveAt(i);
                }
            }

            // Subscribe to new targets
            foreach (var target in _targets)
            {
                if (target != null && !_subscribedTargets.Contains(target))
                {
                    Subscribe(target);
                    _subscribedTargets.Add(target);
                }
            }
        }

        private void Subscribe(TilemapWorldGenerator target)
        {
            if (target == null) return;
            target.OnGenerationStarted += HandleGeneratorStarted;
            target.OnGenerationCompleted += HandleGeneratorCompleted;
        }

        private void Unsubscribe(TilemapWorldGenerator target)
        {
            if (target == null) return;
            target.OnGenerationStarted -= HandleGeneratorStarted;
            target.OnGenerationCompleted -= HandleGeneratorCompleted;
        }

        private void ClearAllSubscriptions()
        {
            foreach (var target in _subscribedTargets)
            {
                Unsubscribe(target);
            }
            _subscribedTargets.Clear();
        }

        private void HandleGeneratorStarted()
        {
            EditorApplication.delayCall += () => {
                if (this == null) return;
                InvalidateGrid();
                _status = "Generation started. Diagnostic grid is now dirty.";
                Repaint();
            };
        }

        private void HandleGeneratorCompleted(GenerationCompletedArgs args)
        {
            EditorApplication.delayCall += () => {
                if (this == null) return;
                InvalidateGrid();
                _status = "Generation completed. Diagnostic grid is now dirty.";
                Repaint();
            };
        }

        private void Update()
        {
            if (!_isRunning) return;

            double currentTime = EditorApplication.timeSinceStartup;
            double deltaTime = currentTime - _lastProgressUpdateTime;
            _lastProgressUpdateTime = currentTime;

            // Smoothly move display progress towards target
            if (_displayProgress < _targetProgress)
            {
                _displayProgress = Mathf.MoveTowards(_displayProgress, _targetProgress, (float)deltaTime * 0.5f);
            }
            else if (_displayProgress < 0.99f)
            {
                // Slow "alive" crawl if we haven't reached the end
                _displayProgress = Mathf.MoveTowards(_displayProgress, 0.99f, (float)deltaTime * 0.02f);
            }

            // Update the real progress field used for drawing
            _progress = _displayProgress;
            Repaint();
        }
    

        



        // Writes progress fields directly from the background thread without marshalling through
        // SynchronizationContext. Progress<T> queues callbacks to run on the main thread, but Unity
        // Editor processes that queue in the same batch as the await continuation, so all callbacks
        // arrive after _isRunning is already false and the live display never updates.
        private sealed class DirectProgress : IProgress<GeneratedMapDiagnosticProgress>
        {
            private readonly GeneratedMapDiagnosticsWindow _window;
            internal DirectProgress(GeneratedMapDiagnosticsWindow window) => _window = window;
            public void Report(GeneratedMapDiagnosticProgress value)
            {
                float p = value.Progress;
                if (p > _window._targetProgress) _window._targetProgress = p;
                _window._nodesVisited = value.NodesVisited;
                _window._nodesTotal   = value.NodesTotal;
                _window._liveStatus   = value.Status;
            }
        }

        private string[] GetLiveStats()
        {
            if (!_isRunning) return null;
            List<string> stats = new List<string>();
            if (!string.IsNullOrEmpty(_guiLiveStatus)) stats.Add("Task: " + _guiLiveStatus);
            stats.Add("Nodes Visited: " + _guiNodesVisited);
            if (_guiNodesTotal > 0) stats.Add("Nodes Total: " + _guiNodesTotal);
            return stats.ToArray();
        }

        private void SerializeResultToCache()
        {
            if (_result == null)
            {
                _cachedResultVisited = null;
                return;
            }

            _cachedResultSuccess = _result.Success;
            _cachedResultMessage = _result.Message;
            _cachedResultElapsed = _result.ElapsedMilliseconds;
            _cachedResultWalkable = _result.WalkableCellCount;
            _cachedResultBlocked = _result.BlockedCellCount;
            _cachedResultVisited = _result.Visited.ToArray();
            _cachedResultPath    = _result.Path.ToArray();

            _cachedResultHeatKeys   = new GeneratedMapDiagnosticCellKey[_result.Heat.Count];
            _cachedResultHeatValues = new int[_result.Heat.Count];
            int i = 0;
            foreach (var kvp in _result.Heat)
            {
                _cachedResultHeatKeys[i]   = kvp.Key;
                _cachedResultHeatValues[i] = kvp.Value;
                i++;
            }
        }

        private void TryRestoreResultFromCache()
        {
            if (_cachedResultVisited == null || _cachedResultVisited.Length == 0) return;

            _result = new GeneratedMapDiagnosticResult();
            _result.Success = _cachedResultSuccess;
            _result.Message = _cachedResultMessage;
            _result.ElapsedMilliseconds = _cachedResultElapsed;
            _result.WalkableCellCount = _cachedResultWalkable;
            _result.BlockedCellCount = _cachedResultBlocked;
            _result.Visited.AddRange(_cachedResultVisited);
            _result.Path.AddRange(_cachedResultPath);

            if (_cachedResultHeatKeys != null)
            {
                for (int i = 0; i < _cachedResultHeatKeys.Length; i++)
                {
                    _result.Heat[_cachedResultHeatKeys[i]] = _cachedResultHeatValues[i];
                }
            }

            BuildVisualizationMesh();
        }
    }
}