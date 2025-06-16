using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace MeshEditing
{
    public class ProportionalEditingTool : EditorWindow
    {
        // Tool state
        private bool isActive = false;
        private GameObject selectedObject;
        private MeshFilter meshFilter;
        private Mesh originalMesh;
        private Mesh workingMesh;
        
        // Selection
        private int selectedVertexIndex = -1;
        private int selectedEdgeIndex = -1;
        private Vector2 selectedEdgeVertices = Vector2.zero;
        private bool isSelectingVertex = true;
        
        // Proportional editing settings
        private float influenceRadius = 2.0f;
        private AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        private AxisConstraint axisConstraint = AxisConstraint.Y;
        
        // UI state
        private bool showWireframe = true;
        private Color wireframeColor = Color.white;
        private Color influenceColor = Color.yellow;
        private Color selectedColor = Color.red;
        
        // Internal state
        private Vector2 dragStartPos;
        private bool isDragging = false;
        private Vector3[] originalVertices;
        
        public enum AxisConstraint
        {
            X, Y, Z
        }
        
        [MenuItem("Tools/Proportional Editing Tool")]
        public static void ShowWindow()
        {
            GetWindow<ProportionalEditingTool>("Proportional Editing");
        }
        
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedo;
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedo;
            DeactivateTool();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Proportional Editing Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Tool activation
            EditorGUI.BeginChangeCheck();
            bool newActive = EditorGUILayout.Toggle("Activate Tool", isActive);
            if (EditorGUI.EndChangeCheck())
            {
                if (newActive)
                    ActivateTool();
                else
                    DeactivateTool();
            }
            
            if (!isActive)
            {
                EditorGUILayout.HelpBox("Select a GameObject with MeshFilter and activate the tool.", MessageType.Info);
                return;
            }
            
            EditorGUILayout.Space();
            
            // Selection mode
            EditorGUILayout.LabelField("Selection Mode", EditorStyles.boldLabel);
            isSelectingVertex = EditorGUILayout.Toggle("Select Vertices", isSelectingVertex);
            EditorGUILayout.Toggle("Select Edges", !isSelectingVertex);
            
            EditorGUILayout.Space();
            
            // Proportional editing settings
            EditorGUILayout.LabelField("Proportional Editing Settings", EditorStyles.boldLabel);
            influenceRadius = EditorGUILayout.FloatField("Influence Radius", influenceRadius);
            axisConstraint = (AxisConstraint)EditorGUILayout.EnumPopup("Axis Constraint", axisConstraint);
            falloffCurve = EditorGUILayout.CurveField("Falloff Curve", falloffCurve);
            
            EditorGUILayout.Space();
            
            // Visual settings
            EditorGUILayout.LabelField("Visual Settings", EditorStyles.boldLabel);
            showWireframe = EditorGUILayout.Toggle("Show Wireframe", showWireframe);
            wireframeColor = EditorGUILayout.ColorField("Wireframe Color", wireframeColor);
            influenceColor = EditorGUILayout.ColorField("Influence Color", influenceColor);
            selectedColor = EditorGUILayout.ColorField("Selected Color", selectedColor);
            
            EditorGUILayout.Space();
            
            // Status
            if (selectedObject)
            {
                EditorGUILayout.LabelField($"Editing: {selectedObject.name}");
                if (selectedVertexIndex >= 0)
                    EditorGUILayout.LabelField($"Selected Vertex: {selectedVertexIndex}");
                else if (selectedEdgeIndex >= 0)
                    EditorGUILayout.LabelField($"Selected Edge: {selectedEdgeIndex}");
            }
            
            EditorGUILayout.Space();
            
            // Instructions
            EditorGUILayout.HelpBox(
                "Instructions:\n" +
                "1. Select a GameObject with MeshFilter\n" +
                "2. Activate the tool\n" +
                "3. Click on vertices/edges to select\n" +
                "4. Hold Shift and drag to edit proportionally\n" +
                "5. Use Ctrl+Z to undo changes",
                MessageType.Info);
        }
        
        private void ActivateTool() // if the game object is selected and has a MeshFilter compornent, activate the tool, then create a working copy of the mesh
        {
            if (Selection.activeGameObject?.GetComponent<MeshFilter>() != null)
            {
                selectedObject = Selection.activeGameObject;
                meshFilter = selectedObject.GetComponent<MeshFilter>();
                
                // Create working copy of mesh
                originalMesh = meshFilter.sharedMesh;
                workingMesh = Instantiate(originalMesh);
                meshFilter.mesh = workingMesh;
                
                isActive = true;
                SceneView.RepaintAll();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject with MeshFilter component.", "OK");
            }
        }
        
        private void DeactivateTool() // when the tool is deactivated, clean up the mesh and reset the state
        {
            if (isActive && selectedObject && meshFilter)
            {
                // Clean up
                if (workingMesh)
                    DestroyImmediate(workingMesh);
                
                meshFilter.mesh = originalMesh;
                selectedObject = null;
                meshFilter = null;
                selectedVertexIndex = -1;
                selectedEdgeIndex = -1;
            }
            
            isActive = false;
            SceneView.RepaintAll();
        }
        
        private void OnUndoRedo() // when an undo or redo operation is performed, repaint the scene view to reflect changes
        {
            if (isActive && workingMesh)
            {
                SceneView.RepaintAll();
            }
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isActive || !selectedObject || !meshFilter || !workingMesh)
                return;
            
            Event e = Event.current;
            Transform transform = selectedObject.transform;
            
            // Draw wireframe
            if (showWireframe)
                DrawWireframe(transform);
            
            // Draw influence area
            if (selectedVertexIndex >= 0 || selectedEdgeIndex >= 0)
                DrawInfluenceArea(transform);
            
            // Handle selection and editing
            HandleMouseEvents(e, transform);
            
            // Force scene view to repaint during dragging
            if (isDragging)
                sceneView.Repaint();
        }
        
        private void DrawWireframe(Transform transform)
        {
            Handles.color = wireframeColor;
            Vector3[] vertices = workingMesh.vertices;
            int[] triangles = workingMesh.triangles;
            
            // Draw triangles as wireframe
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v0 = transform.TransformPoint(vertices[triangles[i]]);
                Vector3 v1 = transform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 v2 = transform.TransformPoint(vertices[triangles[i + 2]]);
                
                Handles.DrawLine(v0, v1);
                Handles.DrawLine(v1, v2);
                Handles.DrawLine(v2, v0);
            }
        }
        
        private void DrawInfluenceArea(Transform transform)
        {
            Vector3 center = GetSelectionCenter(transform);
            
            // Draw influence sphere
            Handles.color = influenceColor;
            Handles.DrawWireDisc(center, transform.up, influenceRadius);
            Handles.DrawWireDisc(center, transform.right, influenceRadius);
            Handles.DrawWireDisc(center, transform.forward, influenceRadius);
            
            // Draw selected element
            Handles.color = selectedColor;
            if (selectedVertexIndex >= 0)
            {
                Vector3 vertexPos = transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
                Handles.SphereHandleCap(0, vertexPos, Quaternion.identity, 0.1f, EventType.Repaint);
            }
            else if (selectedEdgeIndex >= 0)
            {
                Vector3[] vertices = workingMesh.vertices;
                Vector3 v0 = transform.TransformPoint(vertices[(int)selectedEdgeVertices.x]);
                Vector3 v1 = transform.TransformPoint(vertices[(int)selectedEdgeVertices.y]);
                Handles.DrawLine(v0, v1, 3.0f);
            }
        }
        
        private Vector3 GetSelectionCenter(Transform transform)
        {
            if (selectedVertexIndex >= 0)
            {
                return transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
            }
            else if (selectedEdgeIndex >= 0)
            {
                Vector3[] vertices = workingMesh.vertices;
                Vector3 v0 = vertices[(int)selectedEdgeVertices.x];
                Vector3 v1 = vertices[(int)selectedEdgeVertices.y];
                return transform.TransformPoint((v0 + v1) * 0.5f);
            }
            return Vector3.zero;
        }
        
        private void HandleMouseEvents(Event e, Transform transform)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.shift)
                    {
                        HandleSelection(e, transform);
                        GUIUtility.hotControl = controlID;
                        e.Use();
                    }
                    else if (e.button == 0 && e.shift && (selectedVertexIndex >= 0 || selectedEdgeIndex >= 0))
                    {
                        StartProportionalEdit(e, transform);
                        GUIUtility.hotControl = controlID;
                        e.Use();
                    }
                    break;
                    
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID && isDragging)
                    {
                        UpdateProportionalEdit(e, transform);
                        e.Use();
                    }
                    break;
                    
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        if (isDragging)
                            FinishProportionalEdit();
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }
        
        private void HandleSelection(Event e, Transform transform)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            
            if (isSelectingVertex)
            {
                selectedVertexIndex = GetClosestVertex(ray, transform);
                selectedEdgeIndex = -1;
            }
            else
            {
                selectedEdgeIndex = GetClosestEdge(ray, transform, out selectedEdgeVertices);
                selectedVertexIndex = -1;
            }
            
            SceneView.RepaintAll();
        }
        
        private int GetClosestVertex(Ray ray, Transform transform)
        {
            Vector3[] vertices = workingMesh.vertices;
            float closestDistance = float.MaxValue;
            int closestIndex = -1;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = transform.TransformPoint(vertices[i]);
                float distance = HandleUtility.DistancePointLine(worldPos, ray.origin, ray.origin + ray.direction * 1000f);
                
                if (distance < closestDistance && distance < 0.5f)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }
            
            return closestIndex;
        }
        
        private int GetClosestEdge(Ray ray, Transform transform, out Vector2 edgeVertices)
        {
            edgeVertices = Vector2.zero;
            Vector3[] vertices = workingMesh.vertices;
            int[] triangles = workingMesh.triangles;
            
            float closestDistance = float.MaxValue;
            int closestEdge = -1;
            
            HashSet<Vector2> processedEdges = new HashSet<Vector2>();
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // Check all three edges of the triangle
                for (int j = 0; j < 3; j++)
                {
                    int v0 = triangles[i + j];
                    int v1 = triangles[i + (j + 1) % 3];
                    
                    Vector2 edge = new Vector2(Mathf.Min(v0, v1), Mathf.Max(v0, v1));
                    if (processedEdges.Contains(edge))
                        continue;
                    
                    processedEdges.Add(edge);
                    
                    Vector3 worldV0 = transform.TransformPoint(vertices[v0]);
                    Vector3 worldV1 = transform.TransformPoint(vertices[v1]);
                    
                    Vector3 closestPoint = GetClosestPointOnLineSegment(ray.origin, worldV0, worldV1);
                    float distance = Vector3.Distance(ray.origin, closestPoint);
                    
                    if (distance < closestDistance && distance < 0.5f)
                    {
                        closestDistance = distance;
                        closestEdge = processedEdges.Count - 1;
                        edgeVertices = edge;
                    }
                }
            }
            
            return closestEdge;
        }
        
        private Vector3 GetClosestPointOnLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
        {
            Vector3 lineDirection = lineEnd - lineStart;
            float lineLength = lineDirection.magnitude;
            lineDirection.Normalize();
            
            Vector3 pointDirection = point - lineStart;
            float t = Vector3.Dot(pointDirection, lineDirection);
            t = Mathf.Clamp(t, 0f, lineLength);
            
            return lineStart + lineDirection * t;
        }
        
        private void StartProportionalEdit(Event e, Transform transform)
        {
            Undo.RecordObject(meshFilter, "Proportional Edit");
            originalVertices = workingMesh.vertices.Clone() as Vector3[];
            dragStartPos = e.mousePosition;
            isDragging = true;
        }
        
        private void UpdateProportionalEdit(Event e, Transform transform)
        {
            Vector2 mouseDelta = e.mousePosition - (Vector2)dragStartPos;
            Vector3 selectionCenter = GetSelectionCenter(transform);
            
            // Convert mouse delta to world space movement
            Vector3 worldDelta = GetWorldDeltaFromMouseDelta(mouseDelta, selectionCenter);
            
            // Apply axis constraint
            worldDelta = ApplyAxisConstraint(worldDelta);
            
            // Apply proportional editing
            ApplyProportionalEditing(transform, worldDelta);
            
            // Update mesh
            workingMesh.vertices = workingMesh.vertices;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
        
        private Vector3 GetWorldDeltaFromMouseDelta(Vector2 mouseDelta, Vector3 worldPos)
        {
            Camera sceneCamera = SceneView.lastActiveSceneView.camera;
            Vector3 screenPos = sceneCamera.WorldToScreenPoint(worldPos);
            screenPos.x += mouseDelta.x;
            screenPos.y -= mouseDelta.y; // Flip Y because screen coordinates are inverted
            
            Vector3 newWorldPos = sceneCamera.ScreenToWorldPoint(screenPos);
            return newWorldPos - worldPos;
        }
        
        private Vector3 ApplyAxisConstraint(Vector3 delta)
        {
            switch (axisConstraint)
            {
                case AxisConstraint.X:
                    return new Vector3(delta.x, 0, 0);
                case AxisConstraint.Y:
                    return new Vector3(0, delta.y, 0);
                case AxisConstraint.Z:
                    return new Vector3(0, 0, delta.z);
                default:
                    return delta;
            }
        }
        
        private void ApplyProportionalEditing(Transform transform, Vector3 delta)
        {
            Vector3 selectionCenter = transform.InverseTransformPoint(GetSelectionCenter(transform));
            Vector3[] vertices = workingMesh.vertices;
            Vector3 localDelta = transform.InverseTransformDirection(delta);
            
            for (int i = 0; i < vertices.Length; i++)
            {
                float distance = Vector3.Distance(vertices[i], selectionCenter);
                
                if (distance <= influenceRadius)
                {
                    float influence = falloffCurve.Evaluate(distance / influenceRadius);
                    vertices[i] = originalVertices[i] + localDelta * influence;
                }
                else
                {
                    vertices[i] = originalVertices[i];
                }
            }
            
            workingMesh.vertices = vertices;
        }
        
        private void FinishProportionalEdit()
        {
            isDragging = false;
            originalVertices = null;
            EditorUtility.SetDirty(selectedObject);
        }
    }
}