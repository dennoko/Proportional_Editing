using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

public class ProportionalEditTool : EditorWindow
{
    [MenuItem("Tools/Proportional Edit Tool")]
    public static void ShowWindow()
    {
        GetWindow<ProportionalEditTool>("Proportional Edit");
    }

    // ツール状態
    private bool isToolActive = false;
    private GameObject targetObject;
    private Mesh originalMesh;
    private Mesh workingMesh;
    private MeshFilter meshFilter;
    
    // 頂点選択関連
    private HashSet<int> selectedVertices = new HashSet<int>();
    private Vector3[] vertices;
    private Vector3[] originalVertices;
    
    // プロポーショナル編集関連
    private float influenceRadius = 1.0f;
    private AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    // 移動モード
    public enum MovementMode
    {
        WorldAxis,
        CameraRelative
    }
    private MovementMode movementMode = MovementMode.CameraRelative;
    private Vector3 worldAxisDirection = Vector3.up;
    
    // ドラッグ操作関連
    private bool isDragging = false;
    private Vector3 dragStartWorldPos;
    private Vector3 dragStartMousePos;
    private Vector3[] dragStartVertices;
    
    // 保存設定
    public enum SaveMode
    {
        BlendShape,
        NewMesh
    }
    private SaveMode saveMode = SaveMode.BlendShape;
    private string blendShapeName = "ProportionalEdit";
    private string newMeshName = "EditedMesh";

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Selection.selectionChanged += OnSelectionChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Selection.selectionChanged -= OnSelectionChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        DeactivateTool();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Proportional Edit Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // ツールの有効/無効切り替え
        EditorGUI.BeginChangeCheck();
        bool newActive = EditorGUILayout.Toggle("Tool Active", isToolActive);
        if (EditorGUI.EndChangeCheck())
        {
            if (newActive)
                ActivateTool();
            else
                DeactivateTool();
        }

        if (!isToolActive)
        {
            EditorGUILayout.HelpBox("Select a GameObject with MeshFilter and activate the tool to begin editing.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();

        // 対象オブジェクト表示
        if (targetObject != null)
        {
            EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
            EditorGUILayout.LabelField($"Selected Vertices: {selectedVertices.Count}");
        }

        EditorGUILayout.Space();

        // 影響範囲設定
        EditorGUILayout.LabelField("Influence Settings", EditorStyles.boldLabel);
        influenceRadius = EditorGUILayout.Slider("Influence Radius", influenceRadius, 0.1f, 10f);
        falloffCurve = EditorGUILayout.CurveField("Falloff Curve", falloffCurve);

        EditorGUILayout.Space();

        // 移動モード設定
        EditorGUILayout.LabelField("Movement Mode", EditorStyles.boldLabel);
        movementMode = (MovementMode)EditorGUILayout.EnumPopup("Mode", movementMode);
        
        if (movementMode == MovementMode.WorldAxis)
        {
            worldAxisDirection = EditorGUILayout.Vector3Field("World Axis Direction", worldAxisDirection);
            if (GUILayout.Button("Normalize"))
                worldAxisDirection = worldAxisDirection.normalized;
        }

        EditorGUILayout.Space();

        // 保存設定
        EditorGUILayout.LabelField("Save Settings", EditorStyles.boldLabel);
        saveMode = (SaveMode)EditorGUILayout.EnumPopup("Save Mode", saveMode);
        
        if (saveMode == SaveMode.BlendShape)
        {
            blendShapeName = EditorGUILayout.TextField("BlendShape Name", blendShapeName);
        }
        else
        {
            newMeshName = EditorGUILayout.TextField("New Mesh Name", newMeshName);
        }

        EditorGUI.BeginDisabledGroup(targetObject == null || vertices == null);
        if (GUILayout.Button("Save Changes"))
        {
            SaveChanges();
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Reset to Original"))
        {
            ResetToOriginal();
        }

        EditorGUILayout.Space();

        // 操作説明
        EditorGUILayout.LabelField("Controls:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("• Shift + Click: Select vertices");
        EditorGUILayout.LabelField("• Ctrl + Drag: Move selected vertices");
        EditorGUILayout.LabelField("• Mouse Wheel: Adjust influence radius");
        EditorGUILayout.LabelField("• Alt + Click: Deselect all vertices");
    }

    void ActivateTool()
    {
        if (Selection.activeGameObject == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a GameObject with MeshFilter component.", "OK");
            return;
        }

        GameObject selected = Selection.activeGameObject;
        MeshFilter mf = selected.GetComponent<MeshFilter>();
        
        if (mf == null || mf.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "Selected object must have a MeshFilter with a valid mesh.", "OK");
            return;
        }

        targetObject = selected;
        meshFilter = mf;
        originalMesh = mf.sharedMesh;
        
        // 作業用メッシュを作成
        workingMesh = Instantiate(originalMesh);
        vertices = workingMesh.vertices;
        originalVertices = originalMesh.vertices;
        
        selectedVertices.Clear();
        isToolActive = true;
        
        // 作業用メッシュを適用
        meshFilter.mesh = workingMesh;
        
        Tools.current = Tool.None;
        SceneView.RepaintAll();
    }

    void DeactivateTool()
    {
        if (isToolActive && workingMesh != null)
        {
            DestroyImmediate(workingMesh);
            if (meshFilter != null)
                meshFilter.mesh = originalMesh;
        }
        
        isToolActive = false;
        targetObject = null;
        meshFilter = null;
        selectedVertices.Clear();
        isDragging = false;
        
        SceneView.RepaintAll();
    }

    void OnSelectionChanged()
    {
        if (isToolActive && Selection.activeGameObject != targetObject)
        {
            DeactivateTool();
        }
    }

    void OnUndoRedo()
    {
        if (isToolActive && workingMesh != null)
        {
            vertices = workingMesh.vertices;
            SceneView.RepaintAll();
        }
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!isToolActive || targetObject == null || vertices == null)
            return;

        Event e = Event.current;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        // マウスホイールで影響範囲調整
        if (e.type == EventType.ScrollWheel && selectedVertices.Count > 0)
        {
            influenceRadius = Mathf.Max(0.1f, influenceRadius - e.delta.y * 0.1f);
            e.Use();
            Repaint();
            SceneView.RepaintAll();
        }

        // 頂点選択処理
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (e.alt)
            {
                // Alt + クリックで全選択解除
                selectedVertices.Clear();
                e.Use();
                SceneView.RepaintAll();
            }
            else if (e.shift)
            {
                // Shift + クリックで頂点選択
                int vertexIndex = GetVertexUnderMouse(e.mousePosition);
                if (vertexIndex >= 0)
                {
                    if (selectedVertices.Contains(vertexIndex))
                        selectedVertices.Remove(vertexIndex);
                    else
                        selectedVertices.Add(vertexIndex);
                    
                    e.Use();
                    SceneView.RepaintAll();
                }
            }
            else if (e.control && selectedVertices.Count > 0)
            {
                // Ctrl + ドラッグで移動開始
                StartDrag(e.mousePosition);
                e.Use();
            }
        }

        // ドラッグ処理
        if (isDragging)
        {
            if (e.type == EventType.MouseDrag)
            {
                UpdateDrag(e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                EndDrag();
                e.Use();
            }
        }

        // 頂点とプロポーショナル範囲の描画
        DrawVertices();
        DrawInfluenceRadius();
        
        // ツールアクティブ表示
        Handles.BeginGUI();
        GUI.color = Color.green;
        GUI.Label(new Rect(10, 10, 200, 20), "Proportional Edit Tool: ACTIVE");
        GUI.color = Color.white;
        Handles.EndGUI();
    }

    int GetVertexUnderMouse(Vector2 mousePosition)
    {
        Camera camera = SceneView.lastActiveSceneView.camera;
        float minDistance = float.MaxValue;
        int closestVertex = -1;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = targetObject.transform.TransformPoint(vertices[i]);
            
            // カメラから頂点への方向ベクトル
            Vector3 cameraToVertex = worldPos - camera.transform.position;
            
            // 頂点の法線を計算（簡易版）
            Vector3 vertexNormal = CalculateVertexNormal(i);
            Vector3 worldNormal = targetObject.transform.TransformDirection(vertexNormal);
            
            // カメラ方向と法線の内積で表裏判定
            float dot = Vector3.Dot(cameraToVertex.normalized, worldNormal);
            
            // 裏面の頂点はスキップ（内積が正の場合は裏面）
            if (dot > 0.1f) continue;
            
            Vector3 screenPos = camera.WorldToScreenPoint(worldPos);
            
            // カメラの後ろにある頂点はスキップ
            if (screenPos.z < 0) continue;
            
            // スクリーン座標をGUI座標に変換
            screenPos.y = camera.pixelHeight - screenPos.y;
            
            float distance = Vector2.Distance(mousePosition, screenPos);
            if (distance < 15f && distance < minDistance) // 15ピクセル以内
            {
                minDistance = distance;
                closestVertex = i;
            }
        }

        return closestVertex;
    }

    Vector3 CalculateVertexNormal(int vertexIndex)
    {
        Vector3 normal = Vector3.zero;
        int[] triangles = workingMesh.triangles;
        int triangleCount = 0;

        // この頂点を含む三角形を探して平均法線を計算
        for (int i = 0; i < triangles.Length; i += 3)
        {
            if (triangles[i] == vertexIndex || triangles[i + 1] == vertexIndex || triangles[i + 2] == vertexIndex)
            {
                Vector3 v0 = vertices[triangles[i]];
                Vector3 v1 = vertices[triangles[i + 1]];
                Vector3 v2 = vertices[triangles[i + 2]];
                
                Vector3 triangleNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                normal += triangleNormal;
                triangleCount++;
            }
        }

        return triangleCount > 0 ? (normal / triangleCount).normalized : Vector3.up;
    }

    void StartDrag(Vector2 mousePosition)
    {
        isDragging = true;
        dragStartMousePos = new Vector3(mousePosition.x, mousePosition.y, 0);
        dragStartVertices = (Vector3[])vertices.Clone();
        
        // 選択頂点の中心を計算
        Vector3 center = Vector3.zero;
        foreach (int index in selectedVertices)
        {
            center += targetObject.transform.TransformPoint(vertices[index]);
        }
        center /= selectedVertices.Count;
        dragStartWorldPos = center;

        Undo.RegisterCompleteObjectUndo(workingMesh, "Proportional Edit");
    }

    void UpdateDrag(Vector2 mousePosition)
    {
        Vector3 currentMousePos = new Vector3(mousePosition.x, mousePosition.y, 0);
        Vector3 mouseDelta = currentMousePos - dragStartMousePos;
        // Y軸を反転してマウスの上下方向を修正
        Vector3 movementVector = CalculateMovementVector(new Vector2(mouseDelta.x, -mouseDelta.y));
        
        // 選択頂点の移動量を計算
        float movementScale = mouseDelta.magnitude * 0.01f; // スケール調整
        Vector3 totalMovement = movementVector * movementScale;
        
        ApplyProportionalMovement(totalMovement);
        
        // メッシュ更新
        workingMesh.vertices = vertices;
        workingMesh.RecalculateNormals();
        workingMesh.RecalculateBounds();
    }

    Vector3 CalculateMovementVector(Vector2 mouseDelta)
    {
        Camera camera = SceneView.lastActiveSceneView.camera;
        
        if (movementMode == MovementMode.WorldAxis)
        {
            return worldAxisDirection.normalized;
        }
        else
        {
            // カメラに直交する移動ベクトルを計算
            Vector3 cameraForward = camera.transform.forward;
            Vector3 cameraRight = camera.transform.right;
            Vector3 cameraUp = camera.transform.up;
            
            // マウスの移動方向に基づいて移動ベクトルを計算
            Vector3 screenMovement = cameraRight * mouseDelta.x + cameraUp * mouseDelta.y;
            return screenMovement.normalized;
        }
    }

    void ApplyProportionalMovement(Vector3 movement)
    {
        // 選択頂点の中心を計算
        Vector3 center = Vector3.zero;
        foreach (int index in selectedVertices)
        {
            center += dragStartVertices[index];
        }
        center /= selectedVertices.Count;
        
        // 全頂点に対してプロポーショナル影響を適用
        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = Vector3.Distance(dragStartVertices[i], center);
            float influence = CalculateInfluence(distance);
            
            if (influence > 0.001f)
            {
                vertices[i] = dragStartVertices[i] + movement * influence;
            }
            else
            {
                vertices[i] = dragStartVertices[i];
            }
        }
    }

    float CalculateInfluence(float distance)
    {
        if (distance > influenceRadius)
            return 0f;
            
        float normalizedDistance = distance / influenceRadius;
        return falloffCurve.Evaluate(normalizedDistance);
    }

    void EndDrag()
    {
        isDragging = false;
    }

    void DrawVertices()
    {
        if (vertices == null) return;

        Camera camera = SceneView.lastActiveSceneView.camera;

        // 選択頂点を描画
        Handles.color = Color.yellow;
        foreach (int index in selectedVertices)
        {
            Vector3 worldPos = targetObject.transform.TransformPoint(vertices[index]);
            
            // 表面の頂点のみ描画
            if (IsVertexVisible(index, camera))
            {
                Handles.SphereHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.05f, EventType.Repaint);
            }
        }

        // 表面の頂点を小さく表示
        Handles.color = Color.white;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!selectedVertices.Contains(i) && IsVertexVisible(i, camera))
            {
                Vector3 worldPos = targetObject.transform.TransformPoint(vertices[i]);
                Handles.DotHandleCap(0, worldPos, Quaternion.identity, HandleUtility.GetHandleSize(worldPos) * 0.02f, EventType.Repaint);
            }
        }
    }

    bool IsVertexVisible(int vertexIndex, Camera camera)
    {
        Vector3 worldPos = targetObject.transform.TransformPoint(vertices[vertexIndex]);
        
        // カメラから頂点への方向ベクトル
        Vector3 cameraToVertex = worldPos - camera.transform.position;
        
        // 頂点の法線を計算
        Vector3 vertexNormal = CalculateVertexNormal(vertexIndex);
        Vector3 worldNormal = targetObject.transform.TransformDirection(vertexNormal);
        
        // カメラ方向と法線の内積で表裏判定
        float dot = Vector3.Dot(cameraToVertex.normalized, worldNormal);
        
        // 表面の頂点のみ表示（内積が負の場合は表面）
        return dot <= 0.1f;
    }

    void DrawInfluenceRadius()
    {
        if (selectedVertices.Count == 0) return;

        // 選択頂点の中心を計算
        Vector3 center = Vector3.zero;
        foreach (int index in selectedVertices)
        {
            center += targetObject.transform.TransformPoint(vertices[index]);
        }
        center /= selectedVertices.Count;

        Handles.color = new Color(1f, 0.5f, 0f, 0.3f);
        Handles.DrawWireDisc(center, SceneView.lastActiveSceneView.camera.transform.forward, influenceRadius);
        
        // 3D球体として表示
        Handles.color = new Color(1f, 0.5f, 0f, 0.1f);
        Handles.SphereHandleCap(0, center, Quaternion.identity, influenceRadius * 2, EventType.Repaint);
    }

    void SaveChanges()
    {
        if (saveMode == SaveMode.BlendShape)
        {
            SaveAsBlendShape();
        }
        else
        {
            SaveAsNewMesh();
        }
    }

    void SaveAsBlendShape()
    {
        if (string.IsNullOrEmpty(blendShapeName))
        {
            EditorUtility.DisplayDialog("Error", "Please enter a BlendShape name.", "OK");
            return;
        }

        // 差分を計算
        Vector3[] deltaVertices = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            deltaVertices[i] = vertices[i] - originalVertices[i];
        }

        // BlendShapeを追加
        Mesh targetMesh = originalMesh;
        Mesh newMesh = Instantiate(targetMesh);
        newMesh.AddBlendShapeFrame(blendShapeName, 100f, deltaVertices, null, null);

        // アセットとして保存
        string path = EditorUtility.SaveFilePanel("Save Mesh with BlendShape", "Assets", targetMesh.name + "_BlendShape", "asset");
        if (!string.IsNullOrEmpty(path))
        {
            path = FileUtil.GetProjectRelativePath(path);
            AssetDatabase.CreateAsset(newMesh, path);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", $"BlendShape '{blendShapeName}' saved successfully!", "OK");
        }
    }

    void SaveAsNewMesh()
    {
        if (string.IsNullOrEmpty(newMeshName))
        {
            EditorUtility.DisplayDialog("Error", "Please enter a mesh name.", "OK");
            return;
        }

        Mesh newMesh = Instantiate(workingMesh);
        newMesh.name = newMeshName;

        string path = EditorUtility.SaveFilePanel("Save New Mesh", "Assets", newMeshName, "asset");
        if (!string.IsNullOrEmpty(path))
        {
            path = FileUtil.GetProjectRelativePath(path);
            AssetDatabase.CreateAsset(newMesh, path);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Success", $"Mesh '{newMeshName}' saved successfully!", "OK");
        }
    }

    void ResetToOriginal()
    {
        if (originalVertices != null)
        {
            Undo.RegisterCompleteObjectUndo(workingMesh, "Reset Mesh");
            vertices = (Vector3[])originalVertices.Clone();
            workingMesh.vertices = vertices;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
            SceneView.RepaintAll();
        }
    }
}