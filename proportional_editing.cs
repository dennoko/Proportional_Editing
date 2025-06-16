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
        // private bool isSelectingVertex = true; // Replaced by combined logic
        
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
        private Vector3[] originalVertices; // This seems to be for the original mesh state, keep it.
        private Vector3[] verticesAtDragStart; // Vertices state at the beginning of a drag operation
        private Vector3 dragInitialSelectedVertexPosition; // Position of the selected vertex when drag began
        private Vector3 lastDragAppliedPosition; // Last calculated target position for the selected vertex during a drag
        private const float scrollWheelSensitivity = 0.1f; // Sensitivity for influence radius adjustment
        private bool isProcessingMouseDrag = false; // Flag to manage drag state more explicitly

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
            selectedVertexIndex = -1; // Ensure reset on enable
            isDragging = false;
            // isSelectingVertex = true; // Ensure reset
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
            }
            
            isActive = false;
            isDragging = false;
            // isSelectingVertex = true;
            verticesAtDragStart = null;
            SceneView.RepaintAll();
        }
        
        private void OnUndoRedo() // when an undo or redo operation is performed, repaint the scene view to reflect changes
        {
            if (isActive && workingMesh)
            {
                // workingMesh, influenceRadius, and lastDragAppliedPosition (if 'this' was recorded)
                // are automatically reverted by Unity's Undo system.
                SceneView.RepaintAll();
                Repaint(); // Repaint this EditorWindow to reflect changes like influenceRadius in its GUI
            }
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isActive || !selectedObject || !meshFilter || !workingMesh)
                return;
            
            Event e = Event.current;
            Transform objectTransform = selectedObject.transform;
            int controlID = GUIUtility.GetControlID(FocusType.Passive);

            // Draw wireframe
            if (showWireframe)
                DrawWireframe(objectTransform);
            
            // Draw influence area
            if (selectedVertexIndex >= 0)
                DrawInfluenceArea(objectTransform); // Ensure this method uses current influenceRadius

            // Handle vertex picking if not currently dragging
            if (!isDragging && e.type == EventType.MouseDown && e.button == 0)
            {
                int pickedIndex = PickVertex(e.mousePosition, objectTransform);
                if (pickedIndex != -1)
                {
                    selectedVertexIndex = pickedIndex;
                    Repaint(); // Update GUI to show selected vertex
                    e.Use();
                }
                // If clicked on empty space, could implement deselection here:
                // else { selectedVertexIndex = -1; Repaint(); }
            }
            
            switch (e.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && selectedVertexIndex != -1 && !isDragging)
                    {
                        // Check if the click is reasonably close to the selected vertex to start drag
                        // For simplicity, we assume any mousedown when a vertex is selected and not dragging, starts a drag.
                        GUIUtility.hotControl = controlID;
                        isDragging = true;
                        isProcessingMouseDrag = false; // Will be set true on first actual drag motion
                        dragStartPos = e.mousePosition; // Screen position
                        
                        verticesAtDragStart = workingMesh.vertices.ToArray();
                        dragInitialSelectedVertexPosition = verticesAtDragStart[selectedVertexIndex];
                        lastDragAppliedPosition = dragInitialSelectedVertexPosition;

                        // Undo.RegisterCompleteObjectUndo(workingMesh, "Begin Proportional Edit");
                        Undo.RecordObjects(new Object[] { workingMesh, this }, "Begin Proportional Edit");
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (e.button == 0 && GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        if (isDragging)
                        {
                            // If there was any drag processing, ensure the final state is applied.
                            // This might be redundant if MouseDrag always applies, but good for safety.
                            // if(isProcessingMouseDrag) {
                            // ApplyProportionalEditFromDrag(e, objectTransform, true); // Apply final position
                            // }
                            isDragging = false;
                            isProcessingMouseDrag = false;
                            verticesAtDragStart = null; // Clear at the end of a drag operation
                            e.Use();
                            SceneView.RepaintAll();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID && isDragging && selectedVertexIndex != -1)
                    {
                        isProcessingMouseDrag = true; // Mark that actual dragging has occurred
                        // Undo.RegisterCompleteObjectUndo(workingMesh, "Proportional Edit Drag");
                        Undo.RecordObjects(new Object[] { workingMesh, this }, "Proportional Edit Drag");
                        ApplyProportionalEditFromDrag(e, objectTransform, false);
                        e.Use();
                        // SceneView.RepaintAll(); // Repaint is handled by sceneView.Repaint() below
                    }
                    break;

                case EventType.ScrollWheel:
                    if (isDragging && e.shift && selectedVertexIndex != -1)
                    {
                        // Undo.RegisterCompleteObjectUndo(workingMesh, "Adjust Influence Radius");
                        Undo.RecordObjects(new Object[] { workingMesh, this }, "Adjust Influence Radius");
                        influenceRadius -= e.delta.y * scrollWheelSensitivity;
                        influenceRadius = Mathf.Max(0.01f, influenceRadius);
                        Repaint(); // For the EditorWindow GUI to update radius field

                        ApplyProportionalEditAfterRadiusChange(objectTransform);
                        e.Use();
                        // SceneView.RepaintAll(); // Repaint is handled by sceneView.Repaint() below
                    }
                    break;
                
                case EventType.Layout:
                    // Allow default controls (like selection) when not dragging or when hotControl is not this tool
                    if (GUIUtility.hotControl == 0 || GUIUtility.hotControl == controlID)
                         HandleUtility.AddDefaultControl(controlID);
                    break;
            }
            
            // Remove the old HandleMouseEvents call
            // HandleMouseEvents(e, transform); 
            
            // Force scene view to repaint during dragging or if changes occurred
            if (isDragging || e.type == EventType.ScrollWheel && e.shift || (e.type == EventType.MouseUp && GUIUtility.hotControl == 0) )
                sceneView.Repaint();
        }

        // Placeholder for DrawInfluenceArea if it needs to be updated or added
        private void DrawInfluenceArea(Transform transform)
        {
            if (selectedVertexIndex < 0 || selectedVertexIndex >= workingMesh.vertexCount) return;

            Handles.color = influenceColor;
            Vector3 worldPos = transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
            Handles.DrawWireDisc(worldPos, SceneView.currentDrawingSceneView.camera.transform.forward, influenceRadius);
            // Consider drawing a sphere in 3D space if appropriate:
            // Handles.DrawWireArc(worldPos, transform.up, -transform.right, 360, influenceRadius);
            // Handles.DrawWireArc(worldPos, transform.right, transform.up, 360, influenceRadius);
            // Handles.DrawWireArc(worldPos, transform.forward, transform.right, 360, influenceRadius);
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
        
        private Vector3 GetSelectionCenter(Transform transform)
        {
            if (selectedVertexIndex >= 0)
            {
                return transform.TransformPoint(workingMesh.vertices[selectedVertexIndex]);
            }
            return Vector3.zero;
        }
        
        // Add PickVertex, ApplyProportionalEditFromDrag, ApplyProportionalEditAfterRadiusChange, PerformVertexUpdate methods here
        private int PickVertex(Vector2 mouseGuiPosition, Transform objectTransform)
        {
            float pickDistanceThresholdPixels = 15f; // Screen space pixel radius for picking
            int closestVertex = -1;
            float minSqrDistance = pickDistanceThresholdPixels * pickDistanceThresholdPixels;

            if (workingMesh == null) return -1;
            Vector3[] vertices = workingMesh.vertices;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = objectTransform.TransformPoint(vertices[i]);
                Vector3 screenPos = HandleUtility.WorldToGUIPoint(worldPos);

                // Ensure the vertex is in front of the camera and within the view
                if (screenPos.z < 0) continue; // Behind camera

                Rect screenRect = SceneView.currentDrawingSceneView.position;
                if (!screenRect.Contains(screenPos)) continue; // Outside view

                float sqrDist = (screenPos - mouseGuiPosition).sqrMagnitude;
                if (sqrDist < minSqrDistance)
                {
                    minSqrDistance = sqrDist;
                    closestVertex = i;
                }
            }
            return closestVertex;
        }

        private void ApplyProportionalEditFromDrag(Event e, Transform objectTransform, bool isFinalAdjustment)
        {
            if (selectedVertexIndex < 0 || verticesAtDragStart == null) return;

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane editPlane;
            Vector3 planeNormal;
            Vector3 selectedVertexOriginalWorldPos = objectTransform.TransformPoint(dragInitialSelectedVertexPosition);

            // Determine plane based on axis constraint
            // The plane passes through the vertex's original position at drag start, normal to the constraint axis
            switch (axisConstraint)
            {
                case AxisConstraint.X: planeNormal = objectTransform.right; break;
                case AxisConstraint.Y: planeNormal = objectTransform.up; break;
                case AxisConstraint.Z: planeNormal = objectTransform.forward; break;
                default: planeNormal = SceneView.currentDrawingSceneView.camera.transform.forward; break; // Fallback
            }
            editPlane = new Plane(planeNormal, selectedVertexOriginalWorldPos);

            float enter;
            if (editPlane.Raycast(mouseRay, out enter))
            {
                Vector3 worldHitPoint = mouseRay.GetPoint(enter);
                Vector3 localHitPoint = objectTransform.InverseTransformPoint(worldHitPoint);
                
                Vector3 displacementFromOriginalStart = localHitPoint - dragInitialSelectedVertexPosition;
                Vector3 constrainedDisplacement = Vector3.zero;

                switch (axisConstraint)
                {
                    case AxisConstraint.X: constrainedDisplacement.x = displacementFromOriginalStart.x; break;
                    case AxisConstraint.Y: constrainedDisplacement.y = displacementFromOriginalStart.y; break;
                    case AxisConstraint.Z: constrainedDisplacement.z = displacementFromOriginalStart.z; break;
                }
                
                Vector3 newSelectedVertexPos = dragInitialSelectedVertexPosition + constrainedDisplacement;
                lastDragAppliedPosition = newSelectedVertexPos;

                PerformVertexUpdate(newSelectedVertexPos);
            }
        }

        private void ApplyProportionalEditAfterRadiusChange(Transform objectTransform)
        {
            // Re-apply the edit using the last known dragged position of the selected vertex
            // and the new influenceRadius.
            PerformVertexUpdate(lastDragAppliedPosition);
        }

        private void PerformVertexUpdate(Vector3 selectedVertexTargetLocalPos)
        {
            if (selectedVertexIndex < 0 || verticesAtDragStart == null) return;

            Vector3[] newVertices = verticesAtDragStart.ToArray(); // Always start from the state at drag begin

            Vector3 primaryVertexDisplacementFromDragStart = selectedVertexTargetLocalPos - dragInitialSelectedVertexPosition;
            newVertices[selectedVertexIndex] = selectedVertexTargetLocalPos;

            for (int i = 0; i < newVertices.Length; i++)
            {
                if (i == selectedVertexIndex) continue;

                float dist = Vector3.Distance(verticesAtDragStart[i], dragInitialSelectedVertexPosition);
                if (dist < influenceRadius)
                {
                    float weight = falloffCurve.Evaluate(dist / influenceRadius);
                    Vector3 displacementToApply = primaryVertexDisplacementFromDragStart;

                    // Constrain the displacement for other vertices along the same axis
                    switch (axisConstraint)
                    {
                        case AxisConstraint.X:
                            displacementToApply.y = 0; displacementToApply.z = 0;
                            break;
                        case AxisConstraint.Y:
                            displacementToApply.x = 0; displacementToApply.z = 0;
                            break;
                        case AxisConstraint.Z:
                            displacementToApply.x = 0; displacementToApply.y = 0;
                            break;
                    }
                    newVertices[i] = verticesAtDragStart[i] + displacementToApply * weight;
                }
                // else: vertex remains as it was in verticesAtDragStart
            }

            workingMesh.vertices = newVertices;
            workingMesh.RecalculateNormals();
            workingMesh.RecalculateBounds();
        }
    }
}