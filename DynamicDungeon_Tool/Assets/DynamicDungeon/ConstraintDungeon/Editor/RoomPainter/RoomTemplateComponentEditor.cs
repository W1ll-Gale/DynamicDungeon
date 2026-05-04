using UnityEngine;
using UnityEditor;
using DynamicDungeon.ConstraintDungeon;
using System.Collections.Generic;
using System.IO;

namespace DynamicDungeon.ConstraintDungeon.Editor
{
    [CustomEditor(typeof(RoomTemplateComponent))]
    public class RoomTemplateComponentEditor : UnityEditor.Editor
    {
        private enum PaintingMode { Off, Manual, Auto }

        private RoomTemplateComponent room;
        private RoomValidator.ValidationResult lastResult;
        private PaintingMode paintingMode = PaintingMode.Off;
        private UnityEditor.Editor cachedSocketEditor;

        private List<SocketTypeAsset> allProjectSockets;
        private double nextScanTime = 0;
        
        private int autoBrushSize = 1;

        private void OnEnable()
        {
            room = (RoomTemplateComponent)target;
            Validate();
        }

        private void OnDisable()
        {
            SetPaintingMode(PaintingMode.Off);
            if (cachedSocketEditor != null) DestroyImmediate(cachedSocketEditor);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Room Management", EditorStyles.boldLabel);
            DrawPropertiesExcluding(serializedObject, "m_Script", "manualDoorPoints", "autoDoorPoints", "activeSocket");

            DrawRoomTools();

            GUILayout.Space(10);

            DrawSocketSystem();

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Door Painting Mode", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            Color oldBg = GUI.backgroundColor;
            
            bool manualActive = paintingMode == PaintingMode.Manual;
            GUI.backgroundColor = manualActive ? Color.cyan : Color.white;
            if (GUILayout.Button("MANUAL", GetSlickStyle()))
            {
                SetPaintingMode(manualActive ? PaintingMode.Off : PaintingMode.Manual);
            }
            GUI.backgroundColor = oldBg;

            GUILayout.Space(5);

            bool autoActive = paintingMode == PaintingMode.Auto;
            GUI.backgroundColor = autoActive ? Color.magenta : Color.white;
            if (GUILayout.Button("AUTO", GetSlickStyle()))
            {
                SetPaintingMode(autoActive ? PaintingMode.Off : PaintingMode.Auto);
            }
            GUI.backgroundColor = oldBg;
            
            EditorGUILayout.EndHorizontal();

            if (paintingMode != PaintingMode.Off)
            {
                EditorGUILayout.HelpBox(paintingMode == PaintingMode.Manual ? 
                    "MANUAL: Click/Drag to paint fixed door tiles. Size is derived from number of tiles." : 
                    "AUTO: Paint an area where a door can spawn. Set the spawn size below.", MessageType.Info);
                
                if (paintingMode == PaintingMode.Auto)
                {
                    EditorGUILayout.BeginVertical("box");
                    int maxPossible = CalculateMaxAutoSize();
                    
                    EditorGUI.BeginChangeCheck();
                    int newSize = EditorGUILayout.IntSlider("Auto Door Size", room.fallbackDoorSize, 1, maxPossible);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(room, "Change Door Size");
                        room.fallbackDoorSize = newSize;
                        autoBrushSize = newSize;
                        room.Bake();
                        Validate();
                        SceneView.RepaintAll();
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                room.Bake();
                Validate();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRoomTools()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Authoring Tools", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate Room"))
            {
                Validate();
                Debug.Log(lastResult.isValid
                    ? $"[RoomTemplate] '{room.name}' is valid."
                    : $"[RoomTemplate] '{room.name}' is invalid: {lastResult.message}", room);
            }

            if (GUILayout.Button("Bake Room"))
            {
                Undo.RecordObject(room, "Bake Room Template");
                room.Bake();
                Validate();
                EditorUtility.SetDirty(room);
                Debug.Log($"[RoomTemplate] Baked '{room.name}' ({room.bakedData.cells.Count} cells, {room.bakedData.anchors.Count} anchors).", room);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Active Socket"))
            {
                Undo.RecordObject(room, "Apply Active Socket To Doors");
                ApplyActiveSocketToDoors(room.manualDoorPoints);
                ApplyActiveSocketToDoors(room.autoDoorPoints);
                room.Bake();
                Validate();
                EditorUtility.SetDirty(room);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Show Door Directions"))
            {
                room.showGizmos = true;
                room.Bake();
                SceneView.RepaintAll();
                EditorUtility.SetDirty(room);
                Debug.Log($"[RoomTemplate] Door direction gizmos enabled for '{room.name}'.", room);
            }
            EditorGUILayout.EndHorizontal();

            Validate();
            MessageType messageType = lastResult.isValid ? MessageType.Info : MessageType.Error;
            EditorGUILayout.HelpBox(lastResult.message, messageType);
            EditorGUILayout.EndVertical();
        }

        private void ApplyActiveSocketToDoors(List<DoorTileData> points)
        {
            if (points == null)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                DoorTileData data = points[i];
                data.socket = room.activeSocket;
                points[i] = data;
            }
        }

        private void SetPaintingMode(PaintingMode mode)
        {
            paintingMode = mode;
            RoomTemplateDoorGizmoState.SetEditing(room, paintingMode != PaintingMode.Off);
            SceneView.RepaintAll();
        }

        private void DrawSocketSystem()
        {
            EditorGUILayout.BeginVertical("box");
            
            string currentName = room.activeSocket != null ? room.activeSocket.name : "Standard (Default)";
            EditorGUILayout.LabelField("Active Brush: " + currentName, EditorStyles.boldLabel);
            
            DrawSocketPalette();
            
            GUILayout.Space(5);

            if (room.activeSocket != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Active Socket Settings", EditorStyles.miniBoldLabel);

                string oldName = room.activeSocket.name;
                string newName = EditorGUILayout.DelayedTextField("Asset Name", oldName);
                if (newName != oldName && !string.IsNullOrEmpty(newName))
                {
                    string path = AssetDatabase.GetAssetPath(room.activeSocket);
                    AssetDatabase.RenameAsset(path, newName);
                    AssetDatabase.SaveAssets();
                    allProjectSockets = null;
                }

                if (cachedSocketEditor == null || cachedSocketEditor.target != room.activeSocket)
                {
                    if (cachedSocketEditor != null) DestroyImmediate(cachedSocketEditor);
                    cachedSocketEditor = UnityEditor.Editor.CreateEditor(room.activeSocket);
                }
                EditorGUI.BeginChangeCheck();
                cachedSocketEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck())
                {
                    room.Bake();
                    Validate();
                    SceneView.RepaintAll();
                    EditorUtility.SetDirty(room);
                }
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("Using Standard Socket (Size 1, Cyan). Click [+] above to create a custom override.", MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSocketPalette()
        {
            if (allProjectSockets == null || EditorApplication.timeSinceStartup > nextScanTime)
            {
                allProjectSockets = new List<SocketTypeAsset>();
                string[] guids = AssetDatabase.FindAssets("t:SocketTypeAsset");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    SocketTypeAsset s = AssetDatabase.LoadAssetAtPath<SocketTypeAsset>(path);
                    if (s != null) allProjectSockets.Add(s);
                }
                nextScanTime = EditorApplication.timeSinceStartup + 2.0;
            }

            EditorGUILayout.LabelField("Project Sockets", EditorStyles.miniBoldLabel);
            
            float width = EditorGUIUtility.currentViewWidth - 60;
            int buttonsPerRow = Mathf.Max(1, Mathf.FloorToInt(width / 95));

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            
            int count = 0;

            if (DrawPaletteButton("Standard", room.activeSocket == null, Color.cyan))
            {
                room.activeSocket = null;
                room.Bake();
                Validate();
                SceneView.RepaintAll();
                GUI.FocusControl(null);
            }
            count++;

            foreach (SocketTypeAsset socket in allProjectSockets)
            {
                if (count > 0 && count % buttonsPerRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
                
                if (DrawPaletteButton(socket.name, room.activeSocket == socket, socket.gizmoColor))
                {
                    room.activeSocket = socket;
                    room.Bake();
                    Validate();
                    SceneView.RepaintAll();
                    GUI.FocusControl(null);
                }
                count++;
            }

            if (count > 0 && count % buttonsPerRow == 0)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.7f, 1.0f, 0.7f, 1.0f); 
            if (GUILayout.Button("+", GUILayout.Width(30), GUILayout.Height(24)))
            {
                CreateAndAssignNewSocket();
            }
            GUI.backgroundColor = oldBg;
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private bool DrawPaletteButton(string label, bool isActive, Color color)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1.0f);
            
            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.fontSize = 11;
            style.fontStyle = isActive ? FontStyle.Bold : FontStyle.Normal;
            style.padding = new RectOffset(5, 5, 2, 8);
            style.fixedWidth = 90;
            
            bool clicked = GUILayout.Button(new GUIContent(label), style);
            
            Rect lastRect = GUILayoutUtility.GetLastRect();
            GUI.backgroundColor = color;
            GUI.Box(new Rect(lastRect.x + 2, lastRect.y + lastRect.height - 4, lastRect.width - 4, 3), "");
            
            GUI.backgroundColor = oldBg;
            return clicked;
        }

        private void CreateAndAssignNewSocket()
        {
            string path = "Assets/DynamicDungeon/ConstraintDungeon/Samples/Data/Sockets";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(path + "/New Socket.asset");
            SocketTypeAsset newSocket = ScriptableObject.CreateInstance<SocketTypeAsset>();
            AssetDatabase.CreateAsset(newSocket, assetPath);
            AssetDatabase.SaveAssets();
            
            room.activeSocket = newSocket;
            allProjectSockets = null; 
            EditorUtility.SetDirty(room);
        }

        private GUIStyle GetSlickStyle()
        {
            GUIStyle slickButton = new GUIStyle(GUI.skin.button);
            slickButton.fixedHeight = 35;
            slickButton.fontSize = 13;
            slickButton.fontStyle = FontStyle.Bold;
            slickButton.alignment = TextAnchor.MiddleCenter;
            return slickButton;
        }

        private void OnSceneGUI()
        {
            if (paintingMode != PaintingMode.Off) HandlePainting();
        }

        private void HandlePainting()
        {
            Event e = Event.current;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlID);
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane plane = new Plane(Vector3.forward, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 worldPos = ray.GetPoint(enter);
                Vector3Int cellPos = room.floorMap.WorldToCell(worldPos);
                
                bool isWall = room.wallMap != null && room.wallMap.HasTile(cellPos);
                bool isFloor = room.floorMap.HasTile(cellPos);
                
                bool adjacentToFloor = false;
                bool adjacentToAir = false;
                Vector3Int[] neighbours = { cellPos + Vector3Int.up, cellPos + Vector3Int.down, cellPos + Vector3Int.left, cellPos + Vector3Int.right };
                foreach (Vector3Int neighbour in neighbours)
                {
                    if (room.floorMap.HasTile(neighbour)) adjacentToFloor = true;
                    bool neighbourIsWall = room.wallMap != null && room.wallMap.HasTile(neighbour);
                    if (!room.floorMap.HasTile(neighbour) && !neighbourIsWall) adjacentToAir = true;
                }

                bool canBeDoor = (isWall && adjacentToFloor) || (isFloor && adjacentToAir);
                
                Color brushColor = (room.activeSocket != null) ? room.activeSocket.gizmoColor : Color.cyan;
                
                Handles.color = canBeDoor ? (e.control ? Color.red : brushColor) : Color.gray;
                Handles.DrawWireDisc(new Vector3(cellPos.x + 0.5f, cellPos.y + 0.5f, 0), Vector3.forward, 0.5f);

                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0)
                {
                    if (canBeDoor || e.control) 
                    {
                        Vector2Int p = new Vector2Int(cellPos.x, cellPos.y);
                        bool changed = false;
                        
                        List<DoorTileData> targetList = paintingMode == PaintingMode.Manual ? room.manualDoorPoints : room.autoDoorPoints;
                        List<DoorTileData> otherList = paintingMode == PaintingMode.Manual ? room.autoDoorPoints : room.manualDoorPoints;

                        if (e.control) // ERASE
                        {
                            if (RemovePointFromList(targetList, p)) changed = true;
                            if (RemovePointFromList(otherList, p)) changed = true;
                        }
                        else // PAINT
                        {
                            if (!IsPointInList(targetList, p))
                            {
                                Undo.RecordObject(room, "Paint Door Point");
                                if (RemovePointFromList(otherList, p)) { }
                                int sizeToApply = (paintingMode == PaintingMode.Auto) ? autoBrushSize : 1;
                                targetList.Add(new DoorTileData(p, room.activeSocket, sizeToApply));
                                changed = true;
                            }
                            else
                            {
                                // Check if we need to update socket OR auto-size
                                if (UpdatePointData(targetList, p, room.activeSocket, autoBrushSize))
                                {
                                    changed = true;
                                }
                            }
                        }

                        if (changed)
                        {
                            room.Bake();
                            Validate();
                            e.Use();
                        }
                    }
                }
            }
        }

        private bool IsPointInList(List<DoorTileData> list, Vector2Int p)
        {
            foreach(DoorTileData door in list) if (door.pos == p) return true;
            return false;
        }

        private bool RemovePointFromList(List<DoorTileData> list, Vector2Int p)
        {
            for(int i=0; i<list.Count; i++)
            {
                if (list[i].pos == p)
                {
                    Undo.RecordObject(room, "Erase Door Asset");
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private bool UpdatePointData(List<DoorTileData> list, Vector2Int p, SocketTypeAsset s, int autoSize)
        {
            for(int i=0; i<list.Count; i++)
            {
                if (list[i].pos == p)
                {
                    bool socketChanged = list[i].socket != s;
                    bool sizeChanged = (paintingMode == PaintingMode.Auto) && list[i].autoSize != autoSize;

                    if (socketChanged || sizeChanged)
                    {
                        Undo.RecordObject(room, "Change Door Data");
                        DoorTileData data = list[i];
                        data.socket = s;
                        data.autoSize = autoSize;
                        list[i] = data;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool UpdatePointSocket(List<DoorTileData> list, Vector2Int p, SocketTypeAsset s)
        {
            return UpdatePointData(list, p, s, autoBrushSize);
        }

        private void Validate()
        {
            lastResult = RoomValidator.Validate(room);
        }

        private int CalculateMaxAutoSize()
        {
            List<Vector2Int> tilesToEvaluate = new List<Vector2Int>();
            if (room.autoDoorPoints.Count > 0)
            {
                foreach(DoorTileData door in room.autoDoorPoints) tilesToEvaluate.Add(door.pos);
            }
            else
            {
                tilesToEvaluate = GetAllEdgeWalls();
            }

            if (tilesToEvaluate.Count == 0) return 10;
            HashSet<Vector2Int> remaining = new HashSet<Vector2Int>(tilesToEvaluate);
            int minLen = int.MaxValue;
            while (remaining.Count > 0)
            {
                HashSet<Vector2Int>.Enumerator it = remaining.GetEnumerator(); it.MoveNext();
                Vector2Int start = it.Current; remaining.Remove(start);
                List<Vector2Int> group = new List<Vector2Int> { start };
                Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(start);
                while(q.Count > 0)
                {
                    Vector2Int point = q.Dequeue();
                    Vector3Int[] neighbours = { new Vector3Int(point.x, point.y + 1, 0), new Vector3Int(point.x, point.y - 1, 0), new Vector3Int(point.x - 1, point.y, 0), new Vector3Int(point.x + 1, point.y, 0) };
                    foreach(Vector3Int neighbour in neighbours)
                    {
                        Vector2Int neighbour2 = new Vector2Int(neighbour.x, neighbour.y);
                        if (remaining.Contains(neighbour2)) { remaining.Remove(neighbour2); group.Add(neighbour2); q.Enqueue(neighbour2); }
                    }
                }
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach(Vector2Int point in group) { minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y); maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y); }
                int len = Mathf.Max(maxX - minX + 1, maxY - minY + 1);
                if (len < minLen) minLen = len;
            }
            return minLen == int.MaxValue ? 10 : minLen;
        }

        private List<Vector2Int> GetAllEdgeWalls()
        {
            List<Vector2Int> edgeWalls = new List<Vector2Int>();
            if (room.wallMap == null) return edgeWalls;
            BoundsInt bounds = room.wallMap.cellBounds;
            foreach (Vector3Int pos in bounds.allPositionsWithin)
                if (room.wallMap.HasTile(pos))
                {
                    bool adjToFloor = false;
                    Vector3Int[] neighbours = { pos + Vector3Int.up, pos + Vector3Int.down, pos + Vector3Int.left, pos + Vector3Int.right };
                    foreach (Vector3Int neighbour in neighbours) if (room.floorMap != null && room.floorMap.HasTile(neighbour)) adjToFloor = true;
                    if (adjToFloor) edgeWalls.Add(new Vector2Int(pos.x, pos.y));
                }
            return edgeWalls;
        }
    }
}
