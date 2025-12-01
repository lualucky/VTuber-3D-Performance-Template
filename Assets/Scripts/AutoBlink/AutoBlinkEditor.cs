using UnityEngine;
using UnityEditor;
using UnityChan;

[CustomEditor(typeof(AutoBlink))]
public class AutoBlinkEditor : Editor
{
    private SerializedProperty isActive;
    private SerializedProperty Head;
    private SerializedProperty useIndividualEyeblink;
    private SerializedProperty blinkBlendshape;
    private SerializedProperty leftBlinkBlendshape;
    private SerializedProperty rightBlinkBlendshape;
    private SerializedProperty ratio_Close;
    private SerializedProperty ratio_HalfClose;
    private SerializedProperty ratio_Open;
    private SerializedProperty timeBlink;
    private SerializedProperty threshold;
    private SerializedProperty interval;
    private SerializedProperty exclusionShapes;

    private bool showBlendshapeSettings = true;
    private bool showExclusionShapes = false;
    private bool showTimingSettings = true;

    // Cached blendshape names for dropdown
    private string[] blendshapeNames;
    private SkinnedMeshRenderer cachedHead;

    private void OnEnable()
    {
        isActive = serializedObject.FindProperty("isActive");
        Head = serializedObject.FindProperty("Head");
        useIndividualEyeblink = serializedObject.FindProperty("useIndividualEyeblink");
        blinkBlendshape = serializedObject.FindProperty("blinkBlendshape");
        leftBlinkBlendshape = serializedObject.FindProperty("leftBlinkBlendshape");
        rightBlinkBlendshape = serializedObject.FindProperty("rightBlinkBlendshape");
        ratio_Close = serializedObject.FindProperty("ratio_Close");
        ratio_HalfClose = serializedObject.FindProperty("ratio_HalfClose");
        ratio_Open = serializedObject.FindProperty("ratio_Open");
        timeBlink = serializedObject.FindProperty("timeBlink");
        threshold = serializedObject.FindProperty("threshold");
        interval = serializedObject.FindProperty("interval");
        exclusionShapes = serializedObject.FindProperty("exclusionShapes");

        RefreshBlendshapeNames();
    }

    private void RefreshBlendshapeNames()
    {
        SkinnedMeshRenderer headRenderer = Head.objectReferenceValue as SkinnedMeshRenderer;

        if (headRenderer != null && headRenderer.sharedMesh != null)
        {
            cachedHead = headRenderer;
            int blendShapeCount = headRenderer.sharedMesh.blendShapeCount;
            blendshapeNames = new string[blendShapeCount + 1];
            blendshapeNames[0] = "(None)";

            for (int i = 0; i < blendShapeCount; i++)
            {
                blendshapeNames[i + 1] = headRenderer.sharedMesh.GetBlendShapeName(i);
            }
        }
        else
        {
            cachedHead = null;
            blendshapeNames = new string[] { "(No Head Mesh)" };
        }
    }

    private int BlendshapeDropdown(string label, string tooltip, SerializedProperty property)
    {
        int currentIndex = property.intValue + 1; // +1 because index 0 is "(None)"

        // Clamp to valid range
        if (currentIndex < 0 || currentIndex >= blendshapeNames.Length)
        {
            currentIndex = 0;
        }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));

        int newIndex = EditorGUILayout.Popup(currentIndex, blendshapeNames);

        EditorGUILayout.EndHorizontal();

        // Convert back to blendshape index (-1 for "None")
        int blendshapeIndex = newIndex - 1;

        if (blendshapeIndex != property.intValue)
        {
            property.intValue = blendshapeIndex;
        }

        return blendshapeIndex;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Check if head reference changed and refresh blendshape names
        SkinnedMeshRenderer currentHead = Head.objectReferenceValue as SkinnedMeshRenderer;
        if (currentHead != cachedHead)
        {
            RefreshBlendshapeNames();
        }

        // Header
        EditorGUILayout.LabelField("Auto Eye Blink Controller", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Active Toggle with colored background
        EditorGUILayout.BeginHorizontal();
        Color originalBg = GUI.backgroundColor;
        GUI.backgroundColor = isActive.boolValue ? Color.green : Color.gray;
        if (GUILayout.Button(isActive.boolValue ? "● ENABLED" : "○ DISABLED", GUILayout.Height(25)))
        {
            isActive.boolValue = !isActive.boolValue;
        }
        GUI.backgroundColor = originalBg;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Head Reference
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(Head, new GUIContent("Head Mesh", "SkinnedMeshRenderer containing blink blendshapes"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            RefreshBlendshapeNames();
        }

        if (Head.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Head mesh reference is required for eye blink to work.", MessageType.Warning);
        }

        EditorGUILayout.Space(10);

        // Blendshape Settings
        showBlendshapeSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showBlendshapeSettings, "Blendshape Settings");
        if (showBlendshapeSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(useIndividualEyeblink, new GUIContent("Use Individual Eyes", "Use separate left/right eye blendshapes"));

            EditorGUILayout.Space(5);

            // Only show dropdowns if we have a valid head mesh
            if (Head.objectReferenceValue != null)
            {
                if (useIndividualEyeblink.boolValue)
                {
                    BlendshapeDropdown("Left Eye Blendshape", "Blendshape for left eye blink", leftBlinkBlendshape);
                    BlendshapeDropdown("Right Eye Blendshape", "Blendshape for right eye blink", rightBlinkBlendshape);
                }
                else
                {
                    BlendshapeDropdown("Blink Blendshape", "Blendshape for both eyes blink", blinkBlendshape);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Assign a Head Mesh to select blendshapes.", MessageType.Info);
            }

            // Exclusion Shapes List
            EditorGUILayout.Space(10);

            showExclusionShapes = EditorGUILayout.Foldout(showExclusionShapes, new GUIContent("Other Eye Closed Blendshapes", "Blendshapes that will pause blinking when active"), true);
            if (showExclusionShapes)
            {
                EditorGUILayout.LabelField("Some blendshapes may conflict with blinking, such as Joy, because they also close the eyes. If this happens then the blendshapes will stack," +
                " causing the eyes to look strange when blinking.\nList all other blendshapes that cause the eyes to close so that the character will not blink when they are active.", EditorStyles.wordWrappedLabel);

                if (Head.objectReferenceValue != null)
                {
                    DrawExclusionShapesList();
                }
                else
                {
                    EditorGUILayout.HelpBox("Assign a Head Mesh to configure exclusion shapes.", MessageType.Info);
                }
            }

            // Visual representation of eye states
            EditorGUILayout.Space(5);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(10);

        // Timing Settings
        showTimingSettings = EditorGUILayout.BeginFoldoutHeaderGroup(showTimingSettings, "Timing Settings");
        if (showTimingSettings)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(timeBlink, new GUIContent("Blink Duration", "How long a single blink takes (seconds)"));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Random Blink Settings", EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(interval, new GUIContent("Check Interval", "Time between random blink checks (seconds)"));

            // Threshold as percentage
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Blink Chance", "Probability of blinking each interval"));
            threshold.floatValue = EditorGUILayout.Slider(threshold.floatValue, 0f, 1f);
            EditorGUILayout.LabelField($"{(threshold.floatValue * 100f):F0}%", GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            // Estimated blinks per minute
            float estimatedBlinksPerMinute = (60f / interval.floatValue) * threshold.floatValue;
            EditorGUILayout.HelpBox($"Estimated blinks per minute: ~{estimatedBlinksPerMinute:F1}", MessageType.Info);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(10);

        // Test buttons (only in play mode)
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);
            if (GUILayout.Button("Trigger Blink", GUILayout.Height(30)))
            {
                // You can call a public method on your component here
                // ((AutoBlink)target).TriggerBlink();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawExclusionShapesList()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header with add button
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Other Eye Closed Blendshapes ({exclusionShapes.arraySize} shapes)", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("+", GUILayout.Width(25)))
        {
            exclusionShapes.arraySize++;
            exclusionShapes.GetArrayElementAtIndex(exclusionShapes.arraySize - 1).intValue = -1;
        }
        EditorGUILayout.EndHorizontal();

        // Draw each element
        for (int i = 0; i < exclusionShapes.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();

            SerializedProperty element = exclusionShapes.GetArrayElementAtIndex(i);
            int currentIndex = element.intValue + 1; // +1 because index 0 is "(None)"

            // Clamp to valid range
            if (currentIndex < 0 || currentIndex >= blendshapeNames.Length)
            {
                currentIndex = 0;
            }

            // Dropdown
            int newIndex = EditorGUILayout.Popup(currentIndex, blendshapeNames);
            int blendshapeIndex = newIndex - 1;

            if (blendshapeIndex != element.intValue)
            {
                element.intValue = blendshapeIndex;
            }

            // Remove button
            if (GUILayout.Button("−", GUILayout.Width(25)))
            {
                exclusionShapes.DeleteArrayElementAtIndex(i);
                break; // Exit loop since array changed
            }

            EditorGUILayout.EndHorizontal();
        }

        if (exclusionShapes.arraySize == 0)
        {
            EditorGUILayout.LabelField("No exclusion shapes defined.", EditorStyles.centeredGreyMiniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawEyeStatePreview()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        // Draw three eye state boxes
        DrawEyeBox("Open", ratio_Open.floatValue, Color.green);
        GUILayout.Space(10);
        DrawEyeBox("Half", ratio_HalfClose.floatValue, Color.yellow);
        GUILayout.Space(10);
        DrawEyeBox("Closed", ratio_Close.floatValue, Color.red);

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawEyeBox(string label, float value, Color color)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(60));

        Rect rect = GUILayoutUtility.GetRect(50, 20);

        // Background
        EditorGUI.DrawRect(rect, Color.black);

        // Fill based on value (inverted - more fill = more closed)
        Rect fillRect = new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * (value / 100f), rect.height - 2);
        EditorGUI.DrawRect(fillRect, color);

        // Label
        EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
        EditorGUILayout.LabelField($"{value:F0}%", EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.EndVertical();
    }
}