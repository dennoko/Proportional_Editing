using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

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
        
        // Selection and editing state
        private int selectedVertexIndex = -1;
        private bool isInMoveMode = false;
        private AxisConstraint currentAxisConstraint = AxisConstraint.None;
        
        // Proportional editing settings
        private float influenceRadius = 2.0f;
        private AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        
        // UI state
        private bool showWireframe = true;
        private Color wireframeColor = Color.white;
        private Color influenceColor = Color.yellow;
        private Color selectedColor = Color.red;
        
        // Internal state
        private Vector3 dragStartWorldPos;
        private bool isDragging = false;
        private Vector3[] originalVertices;
        
        // Save options
        private bool saveAsShapeKey = false;
        private string shapeKeyName = "NewShape";
        
        public enum AxisConstraint
        {
            None, X, Y, Z
        }
        
        // State machine for editing flow
        private enum EditState
        {
            Selection,
            MoveModeWaiting,
            Moving,
            Finished
        }
        
        private EditState currentState = EditState.Selection;
        
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
            
            // Current state display
            EditorGUILayout.LabelField($"Current State: {GetStateDisplayName()}", EditorStyles.boldLabel);
            if (currentAxisConstraint != AxisConstraint.None)
            {
                EditorGUILayout.LabelField($"Axis Constraint: {currentAxisConstraint}", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.Space();
            
            // Proportional editing settings
            EditorGUILayout.LabelField("Proportional Editing Settings", EditorStyles.boldLabel);
            influenceRadius = EditorGUILayout.FloatField("Influence Radius", influenceRadius);
            falloffCurve = EditorGUILayout.CurveField("Falloff Curve", falloffCurve);
            
            EditorGUILayout.Space();
            
            // Visual settings
            EditorGUILayout.LabelField("Visual Settings", EditorStyles.boldLabel);
            showWireframe = EditorGUILayout.Toggle("Show Wireframe", showWireframe);
            wireframeColor = EditorGUILayout.ColorField("Wireframe Color", wireframeColor);
            influenceColor = EditorGUILayout.ColorField("Influence Color", influenceColor);
            selectedColor = EditorGUILayout.ColorField("Selected Color", selectedColor);
            
            EditorGUILayout.Space();
            
            // Save settings
            EditorGUILayout.LabelField("Save Settings", EditorStyles.boldLabel);
            saveAsShapeKey = EditorGUILayout.Toggle("Save as Shape Key", saveAsShapeKey);
            if (saveAsShapeKey)
            {
                shapeKeyName = EditorGUILayout.TextField("Shape Key Name", shapeKeyName);
            }
            
            // Save buttons
            EditorGUI.BeginDisabledGroup(!isActive || selectedObject == null);
            if (GUILayout.Button("Save Mesh"))
            {
                SaveMesh();
            }
            
            if (GUILayout.Button("Reset to Original"))
            {
                ResetToOriginal();
            }
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.Space();
            
            // Status
            if (selectedObject)
            {
                EditorGUILayout.LabelField($"Editing: {selectedObject.name}");
                if (selectedVertexIndex >= 0)
                    EditorGUILayout.LabelField($"Selected Vertex: {selectedVertexIndex}");
            }
            
            EditorGUILayout.Space();
            
            // Instructions
            EditorGUILayout.HelpBox(
                "Instructions:\n" +
                "1. Select a GameObject with MeshFilter and activate\n" +
                "2. Click vertex to select\n" +
                "3. Press X/Y/Z to enter move mode with axis constraint\n" +
                "4. Move mouse to transform, click to confirm\n" +
                "5. Use Shift+Mouse Wheel to adjust influence radius\n" +
                "6. Press Escape to cancel or reset state\n" +
                "7. Use save options to export your changes",
                MessageType.Info);
        }
        
        private string GetStateDisplayName()
        {
            switch (currentState)
            {
                case EditState.Selection: return "Waiting for vertex selection";
                case EditState.MoveModeWaiting: return "Press X/Y/Z for axis constraint or move freely";
                case EditState.Moving: return "Moving - click to confirm";
                case EditState.Finished: return "Edit complete";
                default: return "Unknown";
            }
        }
        
        private void ActivateTool()
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
                currentState = EditState.Selection;
                SceneView.RepaintAll();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject with MeshFilter component.", "OK");
            }
        }
        
        private void DeactivateTool()
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
                currentState = EditState.Selection;
                currentAxisConstraint = AxisConstraint.None;
                isInMoveMode = false;
            }
            
            isActive = false;
            SceneView.RepaintAll();
        }
        
        private void ResetToOriginal()
        {
            if (workingMesh && originalMesh)
            {
                workingMesh.vertices = originalMesh.vertices;
                workingMesh.normals = originalMesh.normals;
                workingMesh.RecalculateBounds();
                currentState = EditState.Selection;
                selectedVertexIndex = -1;
                isInMoveMode = false;
                currentAxisConstraint = AxisConstraint.None;
                SceneView.RepaintAll();
            }
        }
        
        private void OnUndoRedo()
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
            
            // Handle keyboard input
            HandleKeyboardInput(e);
            
            // Handle mouse wheel for influence radius
            HandleMouseWheel(e);
            
            // Draw wireframe
            if (showWireframe)
                DrawWireframe(transform);
            
            // Draw influence area
            if (selectedVertexIndex >= 0)
                DrawInfluenceArea(transform);
            
            // Handle mouse events based on current state
            HandleMouseEventsStateMachine(e, transform);
            
            // Draw cursor and guides
            DrawCursorAndGuides(e, transform);
            
            // Force scene view to repaint during dragging
            if (isDragging)
                sceneView.Repaint();
        }
        
        private void HandleKeyboardInput(Event e)
        {
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.X:
                        if (currentState == EditState.MoveModeWaiting)
                        {
                            currentAxisConstraint = AxisConstraint.X;
                            e.Use();
                        }
                        break;
                    case KeyCode.Y:
                        if (currentState == EditState.MoveModeWaiting)
                        {
                            currentAxisConstraint = AxisConstraint.Y;
                            e.Use();
                        }
                        break;
                    case KeyCode.Z:
                        if (currentState == EditState.MoveModeWaiting)
                        {
                            currentAxisConstraint = AxisConstraint.Z;
                            e.Use();
                        }
                        break;
                    case KeyCode.Escape:
                        CancelCurrentOperation();
                        e.Use();
                        break;
                }
            }
        }
        
        private void HandleMouseWheel(Event e)
        {
            if (e.type == EventType.ScrollWheel && e.shift)
            {
                influenceRadius = Mathf.Max(0.1f, influenceRadius - e.delta.y * 0.1f);
                Repaint();
                SceneView.RepaintAll();
                e.Use();
            }
        }
        
        private void CancelCurrentOperation()
        {
            if (isDragging)
            {
                // Reset to original vertices
                if (originalVertices != null)
                {
                    workingMesh.vertices = originalVertices;
                    workingMesh.RecalculateNormals();
                    workingMesh.RecalculateBounds();
                }
                isDragging = false;
            }
            
            currentState = EditState.Selection;
            currentAxisConstraint = AxisConstraint.None;
            isInMoveMode = false;
            selectedVertexIndex = -1;
            SceneView.RepaintAll();
        }
        
        private void HandleMouseEventsStateMachine(Event e, Transform transform)
        {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            
            switch (currentState)
            {
                case EditState.Selection:
                    HandleSelectionState(e, transform, controlID);
                    break;
                case EditState.MoveModeWaiting:
                    HandleMoveModeWaitingState(e, transform, controlID);
                    break;
                case EditState.Moving:
                    HandleMovingState(e, transform, controlID);
                    break;
            }
        }
        
        private void HandleSelectionState(Event e, Transform transform, int controlID)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                selectedVertexIndex = GetClosestVertex(e, transform);
                if (selectedVertexIndex >= 0)
                {
                    currentState = EditState.MoveModeWaiting;
                    isInMoveMode = true;
                    Repaint();
                }
                GUIUtility.hotControl = controlID;
                e.Use();
            }
        }
        
        private void HandleMoveModeWaitingState(Event e, Transform transform, int controlID)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                StartProportionalEdit(e, transform);
                currentState = EditState.Moving;
                GUIUtility.hotControl = controlID;
                e.Use();
            }
            else if (e.type == EventType.MouseMove)
            {
                // Show preview of movement
                SceneView.RepaintAll();
            }
        }
        
        private void HandleMovingState(Event e, Transform transform, int controlID)
        {
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlID)
            {
                UpdateProportionalEdit(e, transform);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlID)
            {
                FinishProportionalEdit();
                currentState = EditState.Finished;
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }
        
        private void DrawWireframe(Transform transform)
        {
            Handles.color = wireframeColor;
            Vector3[] vertices = workingMesh.vertices;
            int[] triangles = workingMesh.triangles;
            
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
            
            // Draw selected vertex
            Handles.color = selectedColor;
            if (selectedVertexIndex >= 0)
            {
                Vector3 vertexPos = transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
                Handles.SphereHandleCap(0, vertexPos, Quaternion.identity, 0.1f, EventType.Repaint);
            }
        }
        
        private void DrawCursorAndGuides(Event e, Transform transform)
        {
            if (currentState == EditState.MoveModeWaiting || currentState == EditState.Moving)
            {
                Vector3 center = GetSelectionCenter(transform);
                
                // Draw axis constraint indicators
                if (currentAxisConstraint != AxisConstraint.None)
                {
                    Handles.color = Color.green;
                    Vector3 axisDir = GetAxisDirection(currentAxisConstraint, transform);
                    Handles.DrawLine(center - axisDir * 2f, center + axisDir * 2f);
                }
                
                // Draw movement preview
                if (currentState == EditState.MoveModeWaiting)
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    Vector3 previewPos = GetMovementPreview(ray, center, transform);
                    
                    Handles.color = Color.cyan;
                    Handles.DrawDottedLine(center, previewPos, 5f);
                    Handles.SphereHandleCap(0, previewPos, Quaternion.identity, 0.05f, EventType.Repaint);
                }
            }
        }
        
        private Vector3 GetAxisDirection(AxisConstraint axis, Transform transform)
        {
            switch (axis)
            {
                case AxisConstraint.X: return transform.right;
                case AxisConstraint.Y: return transform.up;
                case AxisConstraint.Z: return transform.forward;
                default: return Vector3.zero;
            }
        }
        
        private Vector3 GetMovementPreview(Ray ray, Vector3 center, Transform transform)
        {
            Vector3 intersection = ray.GetPoint(Vector3.Distance(ray.origin, center));
            Vector3 delta = intersection - center;
            
            if (currentAxisConstraint != AxisConstraint.None)
            {
                Vector3 axis = GetAxisDirection(currentAxisConstraint, transform);
                delta = Vector3.Project(delta, axis);
            }
            
            return center + delta;
        }
        
        private Vector3 GetSelectionCenter(Transform transform)
        {
            if (selectedVertexIndex >= 0)
            {
                return transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
            }
            return Vector3.zero;
        }
        
        private int GetClosestVertex(Event e, Transform transform)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
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
        
        private void StartProportionalEdit(Event e, Transform transform)
        {
            Undo.RecordObject(meshFilter, "Proportional Edit");
            originalVertices = workingMesh.vertices.Clone() as Vector3[];
            dragStartWorldPos = GetSelectionCenter(transform);
            isDragging = true;
        }
        
        private void UpdateProportionalEdit(Event e, Transform transform)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Vector3 currentWorldPos = GetMovementPreview(ray, dragStartWorldPos, transform);
            Vector3 worldDelta = currentWorldPos - dragStartWorldPos;
            
            // Apply proportional editing
            ApplyProportionalEditing(transform, worldDelta);
            
            // Update mesh
            workingMesh.vertices = workingMesh.vertices;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
        
        private void ApplyProportionalEditing(Transform transform, Vector3 worldDelta)
        {
            Vector3 selectionCenter = transform.InverseTransformPoint(dragStartWorldPos);
            Vector3[] vertices = workingMesh.vertices;
            Vector3 localDelta = transform.InverseTransformDirection(worldDelta);
            
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
            isInMoveMode = false;
            currentAxisConstraint = AxisConstraint.None;
            EditorUtility.SetDirty(selectedObject);
            SceneView.RepaintAll();
        }
        
        private void SaveMesh()
        {
            if (workingMesh == null || originalMesh == null)
            {
                EditorUtility.DisplayDialog("Error", "No mesh to save.", "OK");
                return;
            }
            
            string originalPath = AssetDatabase.GetAssetPath(originalMesh);
            if (string.IsNullOrEmpty(originalPath))
            {
                originalPath = "Assets/EditedMeshes";
                if (!AssetDatabase.IsValidFolder(originalPath))
                {
                    AssetDatabase.CreateFolder("Assets", "EditedMeshes");
                }
            }
            
            string directory = Path.GetDirectoryName(originalPath);
            string folderName = Path.GetFileNameWithoutExtension(originalPath) + "_Edited";
            string newFolder = Path.Combine(directory, folderName);
            
            // Create folder if it doesn't exist
            if (!AssetDatabase.IsValidFolder(newFolder))
            {
                AssetDatabase.CreateFolder(directory, folderName);
            }
            
            if (saveAsShapeKey)
            {
                SaveAsShapeKey(newFolder);
            }
            else
            {
                SaveAsNewMesh(newFolder);
            }
            
            AssetDatabase.Refresh();
        }
        
        private void SaveAsNewMesh(string folder)
        {
            string fileName = selectedObject.name + "_Edited.asset";
            string path = Path.Combine(folder, fileName);
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            
            Mesh newMesh = Instantiate(workingMesh);
            newMesh.name = Path.GetFileNameWithoutExtension(path);
            
            AssetDatabase.CreateAsset(newMesh, path);
            EditorUtility.DisplayDialog("Success", $"Mesh saved to: {path}", "OK");
        }
        
        private void SaveAsShapeKey(string folder)
        {
            // Create a mesh with blend shapes
            string fileName = selectedObject.name + "_WithShapeKey.asset";
            string path = Path.Combine(folder, fileName);
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            
            Mesh newMesh = Instantiate(originalMesh);
            newMesh.name = Path.GetFileNameWithoutExtension(path);
            
            // Add blend shape
            Vector3[] deltaVertices = new Vector3[workingMesh.vertexCount];
            Vector3[] deltaNormals = new Vector3[workingMesh.vertexCount];
            Vector3[] deltaTangents = new Vector3[workingMesh.vertexCount];
            
            Vector3[] originalVerts = originalMesh.vertices;
            Vector3[] modifiedVerts = workingMesh.vertices;
            
            for (int i = 0; i < deltaVertices.Length; i++)
            {
                deltaVertices[i] = modifiedVerts[i] - originalVerts[i];
                // For simplicity, we're not calculating delta normals and tangents
                deltaNormals[i] = Vector3.zero;
                deltaTangents[i] = Vector3.zero;
            }
            
            newMesh.AddBlendShapeFrame(shapeKeyName, 100f, deltaVertices, deltaNormals, deltaTangents);
            
            AssetDatabase.CreateAsset(newMesh, path);
            EditorUtility.DisplayDialog("Success", $"Mesh with shape key '{shapeKeyName}' saved to: {path}", "OK");
        }
    }
}