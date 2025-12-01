using UnityEngine;
using UnityEditor;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using System;
using System.Collections.Generic;
using System.Linq;

using Unity.Cinemachine;

namespace CinemachineTools
{
    /// <summary>
    /// Unity Editor tool for creating Cinemachine cameras and automatically generating 
    /// camera shots on a Timeline based on character audio tracks.
    /// </summary>
    public class TimelineCameraShotDirector : EditorWindow
    {
        #region Data Classes

        [Serializable]
        public class CharacterAudioMapping
        {
            public string characterName = "Character";
            public Animator characterAnimator;
            public AudioTrack audioTrack;
            public List<CinemachineCamera> characterCameras = new List<CinemachineCamera>();
            public float audioThreshold = 0.01f;
            public bool isExpanded = true;
            public Color editorColor = Color.white;
        }

        [Serializable]
        public class CharacterGroup
        {
            public string groupName = "Duet";
            public List<int> characterIndices = new List<int>();
            public List<CinemachineCamera> groupCameras = new List<CinemachineCamera>();
            public bool isExpanded = true;
        }

        [Serializable]
        public class SingingSegment
        {
            public float startTime;
            public float endTime;
            public List<int> singingCharacters = new List<int>();
            public SegmentType type;

            public float Duration => endTime - startTime;
        }

        public enum SegmentType
        {
            Silent,
            Solo,
            Group
        }

        #endregion

        #region Fields

        // Timeline references
        private PlayableDirector playableDirector;
        private TimelineAsset timelineAsset;
        private CinemachineTrack targetCinemachineTrack;

        // Character mappings
        private List<CharacterAudioMapping> characterMappings = new List<CharacterAudioMapping>();
        private List<CharacterGroup> characterGroups = new List<CharacterGroup>();

        // Stage cameras (for silent segments)
        private List<CinemachineCamera> stageCameras = new List<CinemachineCamera>();

        // Settings
        private float silenceThreshold = 1.0f;
        private float minimumShotDuration = 1f;
        private float maximumShotDuration = 1f;
        private float analysisResolution = 0.1f;
        private float audioLevelThreshold = 0.01f;
        private bool useRandomCameraSelection = true;
        private bool avoidConsecutiveSameCamera = true;
        private int maxConsecutiveSameCameraShots = 2;

        // Shot variety settings
        private float closeUpProbability = 0.4f;
        private float mediumShotProbability = 0.35f;
        private float wideShotProbability = 0.25f;

        // Analysis results
        private List<SingingSegment> analyzedSegments = new List<SingingSegment>();
        private bool hasAnalyzedAudio = false;

        // ============ Camera Creator Fields ============
        // Character camera settings
        private Animator targetHumanoid;
        private Transform customLookTarget;
        private Transform customFollowTarget;

        // Camera preset options
        private bool createCloseUpHead = true;
        private bool createMediumShot = true;
        private bool createFullBody = true;
        private bool createOverShoulder = false;
        private bool createLowAngle = false;
        private bool createHighAngle = false;
        private bool createProfile = false;
        private bool createThreeQuarter = false;

        // Stage camera settings
        private Transform stageCenter;
        private float stageRadius = 10f;
        private float stageHeight = 2f;
        private int numberOfAngles = 4;
        private bool createOverheadCamera = false;
        private bool createGroundLevelCamera = false;
        private float groundLevelHeight = 0.5f;
        private float overheadHeight = 15f;

        // Common camera settings
        private float defaultFOV = 40f;
        private float defaultNearClip = 0.01f;
        private int basePriority = 10;
        private string cameraPrefix = "VCam";

        // Camera Creator sub-tab
        private int cameraCreatorSubTab = 0;
        private readonly string[] cameraCreatorSubTabNames = { "Character Cameras", "Stage Cameras" };

        // UI State
        private Vector2 mainScrollPos;
        private Vector2 previewScrollPos;
        private Vector2 characterScrollPos;
        private Vector2 stageScrollPos;
        private Vector2 cameraCreatorScrollPos;
        private int selectedTab = 0;
        private readonly string[] tabNames = { "Camera Creator", "Timeline", "Characters", "Character Groups", "Stage Cameras", "Audio", "Generate" };

        // Styles
        private GUIStyle headerStyle;
        private GUIStyle sectionStyle;
        private GUIStyle segmentBoxStyle;
        private bool stylesInitialized = false;

        // Preview
        private bool showSegmentPreview = true;
        private float previewZoom = 1f;

        #endregion

        [MenuItem("Tools/Cinemachine/Timeline Camera Shot Director")]
        public static void ShowWindow()
        {
            var window = GetWindow<TimelineCameraShotDirector>("Camera Shot Director");
            window.minSize = new Vector2(500, 600);
        }

        private void OnEnable()
        {
            // Initialize with some default colors for characters
            if (characterMappings.Count == 0)
            {
                // Pre-populate color palette
            }
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            sectionStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            segmentBoxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(2, 2, 2, 2)
            };

            stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitializeStyles();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Timeline Camera Shot Director", headerStyle);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Create cameras and generate shots based on character audio", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(10);

            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            EditorGUILayout.Space(10);

            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);

            switch (selectedTab)
            {
                case 0: DrawCameraCreatorTab(); break;
                case 1: DrawTimelineTab(); break;
                case 2: DrawCharactersTab(); break;
                case 3: DrawGroupsTab(); break;
                case 4: DrawStageCamerasTab(); break;
                case 5: DrawSettingsTab(); break;
                case 6: DrawGenerateTab(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        #region Camera Creator Tab

        private void DrawCameraCreatorTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Cinemachine Camera Creator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Create character and stage cameras. Created cameras will automatically be added to the Characters and Stage Cameras tabs.", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            cameraCreatorSubTab = GUILayout.Toolbar(cameraCreatorSubTab, cameraCreatorSubTabNames);
            EditorGUILayout.Space(10);

            switch (cameraCreatorSubTab)
            {
                case 0:
                    DrawCharacterCameraCreator();
                    break;
                case 1:
                    DrawStageCameraCreator();
                    break;
            }

            EditorGUILayout.Space(10);
            DrawCommonCameraSettings();
        }

        private void DrawCharacterCameraCreator()
        {
            cameraCreatorScrollPos = EditorGUILayout.BeginScrollView(cameraCreatorScrollPos);

            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Target Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            targetHumanoid = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("Target Humanoid", "Assign a humanoid character with an Animator component"),
                targetHumanoid,
                typeof(Animator),
                true
            );

            if (targetHumanoid != null && !targetHumanoid.isHuman)
            {
                EditorGUILayout.HelpBox("The selected Animator does not have a humanoid rig. Some bone targets may not work correctly.", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
            customLookTarget = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Custom Look Target (Optional)", "Override the automatic look target"),
                customLookTarget,
                typeof(Transform),
                true
            );

            customFollowTarget = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Custom Follow Target (Optional)", "Override the automatic follow target"),
                customFollowTarget,
                typeof(Transform),
                true
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Head/Face Cameras
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Head/Face Cameras", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            createCloseUpHead = EditorGUILayout.Toggle(
                new GUIContent("Close-Up (Head)", "Tight shot focused on the face"),
                createCloseUpHead
            );
            createProfile = EditorGUILayout.Toggle(
                new GUIContent("Profile Shot", "Side view of the face"),
                createProfile
            );
            createThreeQuarter = EditorGUILayout.Toggle(
                new GUIContent("Three-Quarter Shot", "45-degree angle on the face"),
                createThreeQuarter
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Body Cameras
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Body Cameras", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            createMediumShot = EditorGUILayout.Toggle(
                new GUIContent("Medium Shot (Waist Up)", "Shows character from waist up"),
                createMediumShot
            );
            createFullBody = EditorGUILayout.Toggle(
                new GUIContent("Full Body Shot", "Shows entire character"),
                createFullBody
            );
            createOverShoulder = EditorGUILayout.Toggle(
                new GUIContent("Over-the-Shoulder", "Behind character looking over shoulder"),
                createOverShoulder
            );
            createLowAngle = EditorGUILayout.Toggle(
                new GUIContent("Low Angle (Hero Shot)", "Looking up at the character"),
                createLowAngle
            );
            createHighAngle = EditorGUILayout.Toggle(
                new GUIContent("High Angle", "Looking down at the character"),
                createHighAngle
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Create Button
            EditorGUI.BeginDisabledGroup(targetHumanoid == null);
            if (GUILayout.Button("Create Character Cameras", GUILayout.Height(35)))
            {
                CreateCharacterCameras();
            }
            EditorGUI.EndDisabledGroup();

            if (targetHumanoid == null)
            {
                EditorGUILayout.HelpBox("Please assign a humanoid target to create cameras.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStageCameraCreator()
        {
            stageScrollPos = EditorGUILayout.BeginScrollView(stageScrollPos);

            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Stage Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            stageCenter = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Stage Center", "The center point of your stage/scene"),
                stageCenter,
                typeof(Transform),
                true
            );

            EditorGUILayout.Space(5);

            stageRadius = EditorGUILayout.Slider(
                new GUIContent("Camera Distance", "Distance from stage center"),
                stageRadius,
                2f,
                50f
            );

            stageHeight = EditorGUILayout.Slider(
                new GUIContent("Camera Height", "Height of circular cameras"),
                stageHeight,
                0f,
                20f
            );

            numberOfAngles = EditorGUILayout.IntSlider(
                new GUIContent("Number of Angles", "How many cameras around the stage"),
                numberOfAngles,
                2,
                12
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Special Cameras
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Special Stage Cameras", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            createOverheadCamera = EditorGUILayout.Toggle(
                new GUIContent("Overhead Camera", "Top-down view of the stage"),
                createOverheadCamera
            );

            if (createOverheadCamera)
            {
                EditorGUI.indentLevel++;
                overheadHeight = EditorGUILayout.Slider("Overhead Height", overheadHeight, 5f, 50f);
                EditorGUI.indentLevel--;
            }

            createGroundLevelCamera = EditorGUILayout.Toggle(
                new GUIContent("Ground Level Camera", "Low angle dramatic shots"),
                createGroundLevelCamera
            );

            if (createGroundLevelCamera)
            {
                EditorGUI.indentLevel++;
                groundLevelHeight = EditorGUILayout.Slider("Ground Height", groundLevelHeight, 0.1f, 2f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Preview
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            DrawStagePreview();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Create Button
            EditorGUI.BeginDisabledGroup(stageCenter == null);
            if (GUILayout.Button("Create Stage Cameras", GUILayout.Height(35)))
            {
                CreateStageCameras();
            }
            EditorGUI.EndDisabledGroup();

            if (stageCenter == null)
            {
                EditorGUILayout.HelpBox("Please assign a stage center transform to create cameras.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawStagePreview()
        {
            Rect previewRect = GUILayoutUtility.GetRect(200, 150);
            EditorGUI.DrawRect(previewRect, new Color(0.2f, 0.2f, 0.2f));

            if (stageCenter == null) return;

            Vector2 center = previewRect.center;
            float previewRadius = Mathf.Min(previewRect.width, previewRect.height) * 0.35f;

            // Draw stage circle
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Handles.DrawWireDisc(new Vector3(center.x, center.y, 0), Vector3.forward, previewRadius * 0.3f);

            // Draw camera positions
            Handles.color = Color.cyan;
            for (int i = 0; i < numberOfAngles; i++)
            {
                float angle = (360f / numberOfAngles) * i * Mathf.Deg2Rad;
                Vector2 camPos = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * previewRadius;
                Handles.DrawSolidDisc(new Vector3(camPos.x, camPos.y, 0), Vector3.forward, 5f);

                // Draw line to center
                Handles.color = new Color(0, 1, 1, 0.3f);
                Handles.DrawLine(
                    new Vector3(camPos.x, camPos.y, 0),
                    new Vector3(center.x, center.y, 0)
                );
                Handles.color = Color.cyan;
            }

            // Draw overhead camera indicator
            if (createOverheadCamera)
            {
                Handles.color = Color.yellow;
                Handles.DrawSolidDisc(new Vector3(center.x, center.y, 0), Vector3.forward, 7f);
            }

            // Legend
            EditorGUI.LabelField(new Rect(previewRect.x + 5, previewRect.y + 5, 100, 20), "● Cameras", EditorStyles.miniLabel);
        }

        private void DrawCommonCameraSettings()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Common Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            cameraPrefix = EditorGUILayout.TextField(
                new GUIContent("Camera Prefix", "Prefix for camera names"),
                cameraPrefix
            );

            defaultFOV = EditorGUILayout.Slider(
                new GUIContent("Default FOV", "Field of view for created cameras"),
                defaultFOV,
                10f,
                90f
            );

            defaultNearClip = EditorGUILayout.Slider(
                new GUIContent("Near Clip Plane", "Near clipping distance"),
                defaultNearClip,
                0.01f,
                1f
            );

            basePriority = EditorGUILayout.IntField(
                new GUIContent("Base Priority", "Starting priority for cameras"),
                basePriority
            );

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Character Camera Creation

        private void CreateCharacterCameras()
        {
            if (targetHumanoid == null) return;

            Undo.SetCurrentGroupName("Create Character Cameras");
            int undoGroup = Undo.GetCurrentGroup();

            // Create parent object
            GameObject cameraParent = new GameObject($"{cameraPrefix}_CharacterCameras_{targetHumanoid.name}");
            Undo.RegisterCreatedObjectUndo(cameraParent, "Create Camera Parent");

            List<CinemachineCamera> createdCameras = new List<CinemachineCamera>();
            int priorityOffset = 0;

            // Head cameras
            if (createCloseUpHead)
            {
                var cam = CreateHeadCloseUp(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createProfile)
            {
                var cam = CreateProfileShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createThreeQuarter)
            {
                var cam = CreateThreeQuarterShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            // Body cameras
            if (createMediumShot)
            {
                var cam = CreateMediumShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createFullBody)
            {
                var cam = CreateFullBodyShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createOverShoulder)
            {
                var cam = CreateOverShoulderShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createLowAngle)
            {
                var cam = CreateLowAngleShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createHighAngle)
            {
                var cam = CreateHighAngleShot(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = cameraParent;

            // Add new character to Characters tab with created cameras
            AddCharacterWithCameras(targetHumanoid, createdCameras);

            Debug.Log($"Created {priorityOffset} character cameras for {targetHumanoid.name} and added to Characters tab");
        }

        private void AddCharacterWithCameras(Animator animator, List<CinemachineCamera> cameras)
        {
            var newMapping = new CharacterAudioMapping
            {
                characterName = animator.name,
                characterAnimator = animator,
                editorColor = GetRandomColor(),
                characterCameras = new List<CinemachineCamera>(cameras)
            };
            characterMappings.Add(newMapping);
        }

        private Transform GetBone(HumanBodyBones bone)
        {
            if (targetHumanoid == null || !targetHumanoid.isHuman) return targetHumanoid?.transform;
            Transform boneTransform = targetHumanoid.GetBoneTransform(bone);
            return boneTransform != null ? boneTransform : targetHumanoid.transform;
        }

        private Transform GetLookTarget(HumanBodyBones preferredBone)
        {
            if (customLookTarget != null) return customLookTarget;
            return GetBone(preferredBone);
        }

        private Transform GetFollowTarget(HumanBodyBones preferredBone)
        {
            if (customFollowTarget != null) return customFollowTarget;
            return GetBone(preferredBone);
        }

        private CinemachineCamera CreateHeadCloseUp(Transform parent, ref int priorityOffset)
        {
            Transform head = GetBone(HumanBodyBones.Head);
            Vector3 position = head.position + head.forward * 0.8f + Vector3.up * 0.05f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_CloseUp_Head", parent, position, Quaternion.Euler(0f, 180f, 0f), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Head));
            vcam.Lens.FieldOfView = 30f;
            return vcam;
        }

        private CinemachineCamera CreateProfileShot(Transform parent, ref int priorityOffset)
        {
            Transform head = GetBone(HumanBodyBones.Head);
            Vector3 position = head.position + head.right;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_Profile", parent, position, Quaternion.Euler(0f, -90f, 0f), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Head));
            vcam.Lens.FieldOfView = 35f;
            return vcam;
        }

        private CinemachineCamera CreateThreeQuarterShot(Transform parent, ref int priorityOffset)
        {
            Transform head = GetBone(HumanBodyBones.Head);
            Vector3 direction = (head.forward + head.right).normalized;
            Vector3 position = head.position + direction * 1f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_ThreeQuarter", parent, position, Quaternion.Euler(0f, 45f, 0f), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Chest));
            vcam.Lens.FieldOfView = 35f;
            return vcam;
        }

        private CinemachineCamera CreateMediumShot(Transform parent, ref int priorityOffset)
        {
            Transform spine = GetBone(HumanBodyBones.Spine);
            Vector3 position = spine.position + targetHumanoid.transform.forward * 2f + Vector3.up * 0.5f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_MediumShot", parent, position, Quaternion.Euler(-3f, 180f, 0f), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Hips));
            vcam.Lens.FieldOfView = 40f;
            return vcam;
        }

        private CinemachineCamera CreateFullBodyShot(Transform parent, ref int priorityOffset)
        {
            Transform hips = GetBone(HumanBodyBones.Hips);
            Vector3 position = hips.position + targetHumanoid.transform.forward * 4f + Vector3.up * 0.8f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_FullBody", parent, position, new Quaternion(), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Hips));
            vcam.Lens.FieldOfView = 50f;
            return vcam;
        }

        private CinemachineCamera CreateOverShoulderShot(Transform parent, ref int priorityOffset)
        {
            Transform head = GetBone(HumanBodyBones.Head);
            Transform rightShoulder = GetBone(HumanBodyBones.RightShoulder);
            Vector3 position = rightShoulder.position - targetHumanoid.transform.forward * 0.5f + Vector3.up * 0.3f + targetHumanoid.transform.right * 0.3f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_OverShoulder", parent, position, new Quaternion(), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetLookTarget(HumanBodyBones.Chest));
            vcam.Lens.FieldOfView = 45f;
            return vcam;
        }

        private CinemachineCamera CreateLowAngleShot(Transform parent, ref int priorityOffset)
        {
            Transform hips = GetBone(HumanBodyBones.Hips);
            Vector3 position = hips.position + targetHumanoid.transform.forward * 3f + Vector3.down * 0.5f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_LowAngle", parent, position, new Quaternion(), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Hips));
            vcam.Lens.FieldOfView = 45f;
            return vcam;
        }

        private CinemachineCamera CreateHighAngleShot(Transform parent, ref int priorityOffset)
        {
            Transform head = GetBone(HumanBodyBones.Head);
            Vector3 position = head.position + targetHumanoid.transform.forward * 2.5f + Vector3.up * 2f;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_HighAngle", parent, position, new Quaternion(), priorityOffset++);
            SetupCharacterCamera(vcam, null, GetFollowTarget(HumanBodyBones.Hips));
            vcam.Lens.FieldOfView = 45f;
            return vcam;
        }

        #endregion

        #region Stage Camera Creation

        private void CreateStageCameras()
        {
            if (stageCenter == null) return;

            Undo.SetCurrentGroupName("Create Stage Cameras");
            int undoGroup = Undo.GetCurrentGroup();

            // Create parent object
            GameObject cameraParent = new GameObject($"{cameraPrefix}_StageCameras");
            Undo.RegisterCreatedObjectUndo(cameraParent, "Create Stage Camera Parent");

            List<CinemachineCamera> createdCameras = new List<CinemachineCamera>();
            int priorityOffset = 0;

            // Create circular cameras
            for (int i = 0; i < numberOfAngles; i++)
            {
                float angle = (360f / numberOfAngles) * i;
                var cam = CreateStageCameraAtAngle(cameraParent.transform, angle, stageHeight, stageRadius, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            // Create special cameras
            if (createOverheadCamera)
            {
                var cam = CreateOverheadStageCamera(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            if (createGroundLevelCamera)
            {
                var cam = CreateGroundLevelStageCamera(cameraParent.transform, ref priorityOffset);
                if (cam != null) createdCameras.Add(cam);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = cameraParent;

            // Auto-populate Stage Cameras tab
            AddStageCamerasToList(createdCameras);

            Debug.Log($"Created {priorityOffset} stage cameras and added to Stage Cameras tab");
        }

        private void AddStageCamerasToList(List<CinemachineCamera> cameras)
        {
            foreach (var cam in cameras)
            {
                if (cam != null && !stageCameras.Contains(cam))
                {
                    stageCameras.Add(cam);
                }
            }
        }

        private CinemachineCamera CreateStageCameraAtAngle(Transform parent, float angle, float height, float distance, ref int priorityOffset)
        {
            float rad = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad)) * distance;
            offset.y = height;

            Vector3 position = stageCenter.position + offset;

            string angleName = GetAngleName(angle);
            var vcam = CreateVirtualCamera($"{cameraPrefix}_Stage_{angleName}", parent, position, new Quaternion(), priorityOffset++);
            SetupStageCamera(vcam, stageCenter);
            return vcam;
        }

        private string GetAngleName(float angle)
        {
            // Normalize angle to 0-360
            angle = ((angle % 360) + 360) % 360;

            if (angle < 22.5f || angle >= 337.5f) return "Front";
            if (angle < 67.5f) return "FrontRight";
            if (angle < 112.5f) return "Right";
            if (angle < 157.5f) return "BackRight";
            if (angle < 202.5f) return "Back";
            if (angle < 247.5f) return "BackLeft";
            if (angle < 292.5f) return "Left";
            return "FrontLeft";
        }

        private CinemachineCamera CreateOverheadStageCamera(Transform parent, ref int priorityOffset)
        {
            Vector3 position = stageCenter.position + Vector3.up * overheadHeight;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_Stage_Overhead", parent, position, new Quaternion(), priorityOffset++);
            SetupStageCamera(vcam, stageCenter);

            // Point straight down
            vcam.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            return vcam;
        }

        private CinemachineCamera CreateGroundLevelStageCamera(Transform parent, ref int priorityOffset)
        {
            Vector3 position = stageCenter.position + Vector3.forward * stageRadius;
            position.y = groundLevelHeight;

            var vcam = CreateVirtualCamera($"{cameraPrefix}_Stage_GroundLevel", parent, position, new Quaternion(), priorityOffset++);
            SetupStageCamera(vcam, stageCenter);
            vcam.Lens.FieldOfView = 60f;
            return vcam;
        }

        #endregion

        #region Camera Helper Methods

        private CinemachineCamera CreateVirtualCamera(string name, Transform parent, Vector3 position, Quaternion rotation, int priorityOffset)
        {
            GameObject camObj = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(camObj, $"Create {name}");

            camObj.transform.SetParent(parent);
            camObj.transform.position = position;
            camObj.transform.rotation = rotation;

            var vcam = camObj.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = defaultFOV;
            vcam.Lens.NearClipPlane = defaultNearClip;

            return vcam;
        }

        private void SetupCharacterCamera(CinemachineCamera vcam, Transform lookAt, Transform follow)
        {
            vcam.LookAt = lookAt;
            vcam.Follow = follow;

            // Add composer for look at behavior
            var composer = vcam.gameObject.AddComponent<CinemachineRotationComposer>();

            // Add transposer for follow behavior if we have a follow target
            if (follow != null)
            {
                var transposer = vcam.gameObject.AddComponent<CinemachinePositionComposer>();
                Vector3 offset = vcam.transform.position - follow.position;
                transposer.TargetOffset = new Vector3(offset.x, offset.y, 0f);
                transposer.CameraDistance = Vector3.Magnitude(vcam.transform.position - follow.position);
            }

            // Point camera at target
            if (lookAt != null)
            {
                vcam.transform.LookAt(lookAt);
            }
        }

        private void SetupStageCamera(CinemachineCamera vcam, Transform lookAt)
        {
            vcam.LookAt = lookAt;

            // Add composer for smooth look at
            CinemachineRotationComposer composer = vcam.gameObject.AddComponent<CinemachineRotationComposer>();

            // Point camera at stage center
            vcam.transform.LookAt(lookAt);
        }

        #endregion

        #region Tab Drawing Methods

        private void DrawTimelineTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Timeline Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Playable Director
            EditorGUI.BeginChangeCheck();
            playableDirector = (PlayableDirector)EditorGUILayout.ObjectField(
                new GUIContent("Playable Director", "The PlayableDirector containing your Timeline"),
                playableDirector,
                typeof(PlayableDirector),
                true
            );

            if (EditorGUI.EndChangeCheck() && playableDirector != null)
            {
                timelineAsset = playableDirector.playableAsset as TimelineAsset;
                targetCinemachineTrack = null;
            }

            // Timeline Asset (read-only display)
            EditorGUI.BeginDisabledGroup(true);
            timelineAsset = (TimelineAsset)EditorGUILayout.ObjectField(
                new GUIContent("Timeline Asset", "Automatically populated from PlayableDirector"),
                timelineAsset,
                typeof(TimelineAsset),
                false
            );
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);

            // Cinemachine Track Selection
            if (timelineAsset != null)
            {
                EditorGUILayout.LabelField("Cinemachine Track", EditorStyles.boldLabel);

                var cinemachineTracks = GetCinemachineTracks();

                if (cinemachineTracks.Count == 0)
                {
                    EditorGUILayout.HelpBox("No Cinemachine tracks found in Timeline. Please add a Cinemachine Track first.", MessageType.Warning);

                    if (GUILayout.Button("Create Cinemachine Track"))
                    {
                        CreateCinemachineTrack();
                    }
                }
                else
                {
                    string[] trackNames = cinemachineTracks.Select(t => t.name).ToArray();
                    int currentIndex = cinemachineTracks.IndexOf(targetCinemachineTrack);
                    if (currentIndex < 0) currentIndex = 0;

                    int newIndex = EditorGUILayout.Popup("Target Track", currentIndex, trackNames);
                    targetCinemachineTrack = cinemachineTracks[newIndex];
                }

                EditorGUILayout.Space(5);

                // Display available audio tracks
                EditorGUILayout.LabelField("Available Audio Tracks:", EditorStyles.boldLabel);
                var audioTracks = GetAudioTracks();

                if (audioTracks.Count == 0)
                {
                    EditorGUILayout.HelpBox("No audio tracks found in Timeline.", MessageType.Info);
                }
                else
                {
                    EditorGUI.indentLevel++;
                    foreach (var track in audioTracks)
                    {
                        EditorGUILayout.LabelField($"• {track.name}");
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Please assign a PlayableDirector with a Timeline to continue.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCharactersTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Character Audio Mappings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Map each character to their audio track and assign cameras for when they're singing. Characters created in the Camera Creator tab will appear here automatically.", MessageType.Info);
            EditorGUILayout.Space(5);

            // Add character button
            if (GUILayout.Button("+ Add Character Manually", GUILayout.Height(25)))
            {
                var newMapping = new CharacterAudioMapping
                {
                    characterName = $"Character {characterMappings.Count + 1}",
                    editorColor = GetRandomColor()
                };
                characterMappings.Add(newMapping);
            }

            EditorGUILayout.Space(10);

            // Draw each character mapping
            for (int i = 0; i < characterMappings.Count; i++)
            {
                DrawCharacterMapping(i);
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCharacterMapping(int index)
        {
            var mapping = characterMappings[index];

            // Header with color indicator
            EditorGUILayout.BeginHorizontal();

            // Color indicator
            Rect colorRect = GUILayoutUtility.GetRect(20, 20, GUILayout.Width(20));
            EditorGUI.DrawRect(colorRect, mapping.editorColor);

            mapping.isExpanded = EditorGUILayout.Foldout(mapping.isExpanded, mapping.characterName, true);

            GUILayout.FlexibleSpace();

            // Camera count badge
            int validCameras = mapping.characterCameras.Count(c => c != null);
            if (validCameras > 0)
            {
                GUILayout.Label($"[{validCameras} cameras]", EditorStyles.miniLabel);
            }

            // Move buttons
            EditorGUI.BeginDisabledGroup(index == 0);
            if (GUILayout.Button("▲", GUILayout.Width(25)))
            {
                SwapCharacters(index, index - 1);
                return;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(index == characterMappings.Count - 1);
            if (GUILayout.Button("▼", GUILayout.Width(25)))
            {
                SwapCharacters(index, index + 1);
                return;
            }
            EditorGUI.EndDisabledGroup();

            // Delete button
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("✕", GUILayout.Width(25)))
            {
                if (EditorUtility.DisplayDialog("Remove Character",
                    $"Are you sure you want to remove '{mapping.characterName}'?", "Yes", "Cancel"))
                {
                    characterMappings.RemoveAt(index);
                    UpdateGroupIndicesAfterRemoval(index);
                    return;
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            if (!mapping.isExpanded) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Character name
            mapping.characterName = EditorGUILayout.TextField("Name", mapping.characterName);

            // Character Animator reference
            mapping.characterAnimator = (Animator)EditorGUILayout.ObjectField(
                "Character Animator",
                mapping.characterAnimator,
                typeof(Animator),
                true
            );

            // Editor color
            mapping.editorColor = EditorGUILayout.ColorField("Editor Color", mapping.editorColor);

            // Audio track
            var audioTracks = GetAudioTracks();
            if (audioTracks.Count > 0)
            {
                string[] trackNames = new string[] { "(None)" }.Concat(audioTracks.Select(t => t.name)).ToArray();
                int currentIndex = mapping.audioTrack != null ? audioTracks.IndexOf(mapping.audioTrack) + 1 : 0;

                int newIndex = EditorGUILayout.Popup("Audio Track", currentIndex, trackNames);
                mapping.audioTrack = newIndex > 0 ? audioTracks[newIndex - 1] : null;
            }
            else
            {
                EditorGUILayout.HelpBox("No audio tracks available. Set up Timeline in the Setup tab.", MessageType.Warning);
            }

            // Audio threshold
            mapping.audioThreshold = EditorGUILayout.Slider(
                new GUIContent("Audio Threshold", "Minimum audio level to consider as singing"),
                mapping.audioThreshold, 0.001f, 0.5f
            );

            EditorGUILayout.Space(5);

            // Character cameras
            EditorGUILayout.LabelField("Character Cameras", EditorStyles.boldLabel);

            for (int i = 0; i < mapping.characterCameras.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                mapping.characterCameras[i] = (CinemachineCamera)EditorGUILayout.ObjectField(
                    $"Camera {i + 1}",
                    mapping.characterCameras[i],
                    typeof(CinemachineCamera),
                    true
                );

                if (GUILayout.Button("-", GUILayout.Width(25)))
                {
                    mapping.characterCameras.RemoveAt(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Camera"))
            {
                mapping.characterCameras.Add(null);
            }

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        private void DrawGroupsTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Character Groups", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Define groups for when multiple characters sing together (duets, trios, etc.)", MessageType.Info);
            EditorGUILayout.Space(5);

            if (characterMappings.Count < 2)
            {
                EditorGUILayout.HelpBox("Add at least 2 characters to create groups.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            // Add group button
            if (GUILayout.Button("+ Add Group", GUILayout.Height(25)))
            {
                characterGroups.Add(new CharacterGroup
                {
                    groupName = $"Group {characterGroups.Count + 1}"
                });
            }

            EditorGUILayout.Space(10);

            // Auto-generate common groups
            if (GUILayout.Button("Auto-Generate Pair Groups"))
            {
                AutoGeneratePairGroups();
            }

            EditorGUILayout.Space(10);

            // Draw each group
            for (int i = 0; i < characterGroups.Count; i++)
            {
                DrawCharacterGroup(i);
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCharacterGroup(int index)
        {
            var group = characterGroups[index];

            EditorGUILayout.BeginHorizontal();
            group.isExpanded = EditorGUILayout.Foldout(group.isExpanded, group.groupName, true);

            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("✕", GUILayout.Width(25)))
            {
                characterGroups.RemoveAt(index);
                return;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            if (!group.isExpanded) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            group.groupName = EditorGUILayout.TextField("Group Name", group.groupName);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Members", EditorStyles.boldLabel);

            // Character checkboxes
            for (int i = 0; i < characterMappings.Count; i++)
            {
                bool isInGroup = group.characterIndices.Contains(i);
                bool newValue = EditorGUILayout.Toggle(characterMappings[i].characterName, isInGroup);

                if (newValue && !isInGroup)
                {
                    group.characterIndices.Add(i);
                    group.characterIndices.Sort();
                }
                else if (!newValue && isInGroup)
                {
                    group.characterIndices.Remove(i);
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Group Cameras", EditorStyles.boldLabel);

            for (int i = 0; i < group.groupCameras.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                group.groupCameras[i] = (CinemachineCamera)EditorGUILayout.ObjectField(
                    $"Camera {i + 1}",
                    group.groupCameras[i],
                    typeof(CinemachineCamera),
                    true
                );

                if (GUILayout.Button("-", GUILayout.Width(25)))
                {
                    group.groupCameras.RemoveAt(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add Camera"))
            {
                group.groupCameras.Add(null);
            }

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        private void DrawStageCamerasTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Stage Cameras", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These cameras will be used during silent periods when no character is singing. Cameras created in the Camera Creator tab will appear here automatically.", MessageType.Info);
            EditorGUILayout.Space(5);

            for (int i = 0; i < stageCameras.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                stageCameras[i] = (CinemachineCamera)EditorGUILayout.ObjectField(
                    $"Stage Camera {i + 1}",
                    stageCameras[i],
                    typeof(CinemachineCamera),
                    true
                );

                if (GUILayout.Button("-", GUILayout.Width(25)))
                {
                    stageCameras.RemoveAt(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);

            if (GUILayout.Button("+ Add Stage Camera", GUILayout.Height(25)))
            {
                stageCameras.Add(null);
            }

            EditorGUILayout.Space(10);

            // Quick add from scene
            EditorGUILayout.LabelField("Quick Add", EditorStyles.boldLabel);

            if (GUILayout.Button("Add All 'Stage' Cameras from Scene"))
            {
                var allVCams = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);
                foreach (var vcam in allVCams)
                {
                    if (vcam.name.ToLower().Contains("stage") && !stageCameras.Contains(vcam))
                    {
                        stageCameras.Add(vcam);
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSettingsTab()
        {
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Timing Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            silenceThreshold = EditorGUILayout.Slider(
                new GUIContent("Silence Threshold (sec)", "How long before switching to stage cameras"),
                silenceThreshold, 0.5f, 5f
            );

            minimumShotDuration = EditorGUILayout.Slider(
                new GUIContent("Minimum Shot Duration (sec)", "Shortest allowed camera shot"),
                minimumShotDuration, 0.5f, 10f
            );

            maximumShotDuration = EditorGUILayout.Slider(
                new GUIContent("Maximum Shot Duration (sec)", "Longest allowed camera shot"),
                maximumShotDuration, 0.5f, 20f
            );

            analysisResolution = EditorGUILayout.Slider(
                new GUIContent("Analysis Resolution (sec)", "Time step for audio analysis (lower = more precise)"),
                analysisResolution, 0.01f, 0.5f
            );

            audioLevelThreshold = EditorGUILayout.Slider(
                new GUIContent("Global Audio Threshold", "Default threshold for detecting audio"),
                audioLevelThreshold, 0.001f, 0.1f
            );

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Camera Selection Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            useRandomCameraSelection = EditorGUILayout.Toggle(
                new GUIContent("Random Camera Selection", "Randomly select from available cameras"),
                useRandomCameraSelection
            );

            avoidConsecutiveSameCamera = EditorGUILayout.Toggle(
                new GUIContent("Avoid Same Camera Twice", "Try not to use the same camera consecutively"),
                avoidConsecutiveSameCamera
            );

            if (avoidConsecutiveSameCamera)
            {
                EditorGUI.indentLevel++;
                maxConsecutiveSameCameraShots = EditorGUILayout.IntSlider(
                    "Max Consecutive Same Camera",
                    maxConsecutiveSameCameraShots, 1, 5
                );
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Shot Type Probability", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("If cameras are tagged with shot types in their names (CloseUp, Medium, Wide), these probabilities will be used.", MessageType.Info);
            EditorGUILayout.Space(5);

            closeUpProbability = EditorGUILayout.Slider("Close-Up Probability", closeUpProbability, 0f, 1f);
            mediumShotProbability = EditorGUILayout.Slider("Medium Shot Probability", mediumShotProbability, 0f, 1f);
            wideShotProbability = EditorGUILayout.Slider("Wide Shot Probability", wideShotProbability, 0f, 1f);

            // Normalize
            float total = closeUpProbability + mediumShotProbability + wideShotProbability;
            if (total > 0)
            {
                EditorGUILayout.LabelField($"Normalized: Close {closeUpProbability / total:P0}, Medium {mediumShotProbability / total:P0}, Wide {wideShotProbability / total:P0}");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawGenerateTab()
        {
            // Validation
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            bool isValid = ValidateSetup(out string validationMessage);

            if (isValid)
            {
                EditorGUILayout.HelpBox("Setup is valid. Ready to analyze and generate.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Error);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Analysis
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Audio Analysis", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(!isValid);

            if (GUILayout.Button("Analyze Audio Tracks", GUILayout.Height(30)))
            {
                AnalyzeAudioTracks();
            }

            EditorGUI.EndDisabledGroup();

            if (hasAnalyzedAudio)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Found {analyzedSegments.Count} segments", EditorStyles.boldLabel);

                int soloCount = analyzedSegments.Count(s => s.type == SegmentType.Solo);
                int groupCount = analyzedSegments.Count(s => s.type == SegmentType.Group);
                int silentCount = analyzedSegments.Count(s => s.type == SegmentType.Silent);

                EditorGUILayout.LabelField($"  Solo: {soloCount}, Group: {groupCount}, Silent: {silentCount}");
            }

            EditorGUILayout.EndVertical();

            // Segment Preview
            if (hasAnalyzedAudio && analyzedSegments.Count > 0)
            {
                EditorGUILayout.Space(10);
                DrawSegmentPreview();
            }

            EditorGUILayout.Space(10);

            // Generation
            EditorGUILayout.BeginVertical(sectionStyle);
            EditorGUILayout.LabelField("Generate Camera Shots", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.BeginDisabledGroup(!isValid || !hasAnalyzedAudio);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Preview Shots", GUILayout.Height(30)))
            {
                PreviewGeneratedShots();
            }

            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("Generate Shots to Timeline", GUILayout.Height(30)))
            {
                GenerateCameraShots();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(5);

            // Clear existing
            if (targetCinemachineTrack != null)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.5f);
                if (GUILayout.Button("Clear Existing Clips on Track"))
                {
                    if (EditorUtility.DisplayDialog("Clear Track",
                        "Are you sure you want to remove all clips from the Cinemachine track?", "Yes", "Cancel"))
                    {
                        ClearCinemachineTrack();
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSegmentPreview()
        {
            EditorGUILayout.BeginVertical(sectionStyle);

            EditorGUILayout.BeginHorizontal();
            showSegmentPreview = EditorGUILayout.Foldout(showSegmentPreview, "Segment Timeline Preview", true);
            GUILayout.FlexibleSpace();
            previewZoom = EditorGUILayout.Slider("Zoom", previewZoom, 0.1f, 3f, GUILayout.Width(200));
            EditorGUILayout.EndHorizontal();

            if (!showSegmentPreview)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(5);

            float totalDuration = timelineAsset != null ? (float)timelineAsset.duration : 0f;
            if (totalDuration <= 0)
            {
                EditorGUILayout.HelpBox("Timeline has no duration", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // Preview area
            previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.Height(120));

            float previewWidth = Mathf.Max(position.width - 60, totalDuration * 50 * previewZoom);
            Rect previewRect = GUILayoutUtility.GetRect(previewWidth, 80);

            // Background
            EditorGUI.DrawRect(previewRect, new Color(0.15f, 0.15f, 0.15f));

            // Draw segments
            foreach (var segment in analyzedSegments)
            {
                float startX = previewRect.x + (segment.startTime / totalDuration) * previewRect.width;
                float endX = previewRect.x + (segment.endTime / totalDuration) * previewRect.width;
                float segmentWidth = endX - startX;

                Rect segmentRect = new Rect(startX, previewRect.y + 5, segmentWidth, previewRect.height - 10);

                Color segmentColor = GetSegmentColor(segment);
                EditorGUI.DrawRect(segmentRect, segmentColor);

                // Border
                Handles.color = Color.white * 0.5f;
                Handles.DrawLine(new Vector3(segmentRect.x, segmentRect.y), new Vector3(segmentRect.xMax, segmentRect.y));
                Handles.DrawLine(new Vector3(segmentRect.x, segmentRect.yMax), new Vector3(segmentRect.xMax, segmentRect.yMax));
                Handles.DrawLine(new Vector3(segmentRect.x, segmentRect.y), new Vector3(segmentRect.x, segmentRect.yMax));
                Handles.DrawLine(new Vector3(segmentRect.xMax, segmentRect.y), new Vector3(segmentRect.xMax, segmentRect.yMax));

                // Label
                if (segmentWidth > 30)
                {
                    string label = GetSegmentLabel(segment);
                    GUI.Label(segmentRect, label, EditorStyles.miniLabel);
                }
            }

            // Time markers
            int markerCount = Mathf.CeilToInt(totalDuration / 5f);
            for (int i = 0; i <= markerCount; i++)
            {
                float time = i * 5f;
                float x = previewRect.x + (time / totalDuration) * previewRect.width;

                Handles.color = Color.gray;
                Handles.DrawLine(new Vector3(x, previewRect.yMax), new Vector3(x, previewRect.yMax + 15));

                GUI.Label(new Rect(x - 15, previewRect.yMax, 30, 20), $"{time:0}s", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();

            // Legend
            EditorGUILayout.BeginHorizontal();
            DrawLegendItem("Silent", new Color(0.3f, 0.3f, 0.3f));
            DrawLegendItem("Solo", new Color(0.2f, 0.6f, 0.9f));
            DrawLegendItem("Group", new Color(0.9f, 0.6f, 0.2f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawLegendItem(string label, Color color)
        {
            Rect colorRect = GUILayoutUtility.GetRect(15, 15, GUILayout.Width(15));
            EditorGUI.DrawRect(colorRect, color);
            EditorGUILayout.LabelField(label, GUILayout.Width(50));
        }

        #endregion

        #region Helper Methods

        private List<CinemachineTrack> GetCinemachineTracks()
        {
            var tracks = new List<CinemachineTrack>();

            if (timelineAsset == null) return tracks;

            foreach (var track in timelineAsset.GetOutputTracks())
            {
                if (track is CinemachineTrack cmTrack)
                {
                    tracks.Add(cmTrack);
                }
            }

            return tracks;
        }

        private List<AudioTrack> GetAudioTracks()
        {
            var tracks = new List<AudioTrack>();

            if (timelineAsset == null) return tracks;

            foreach (var track in timelineAsset.GetOutputTracks())
            {
                if (track is AudioTrack audioTrack)
                {
                    tracks.Add(audioTrack);
                }
            }

            return tracks;
        }

        private void CreateCinemachineTrack()
        {
            if (timelineAsset == null) return;

            Undo.RecordObject(timelineAsset, "Create Cinemachine Track");
            var newTrack = timelineAsset.CreateTrack<CinemachineTrack>(null, "Cinemachine Track");
            targetCinemachineTrack = newTrack;

            EditorUtility.SetDirty(timelineAsset);
            AssetDatabase.SaveAssets();
        }

        private Color GetRandomColor()
        {
            return Color.HSVToRGB(UnityEngine.Random.value, 0.6f, 0.9f);
        }

        private void SwapCharacters(int indexA, int indexB)
        {
            if (indexA < 0 || indexB < 0 || indexA >= characterMappings.Count || indexB >= characterMappings.Count)
                return;

            var temp = characterMappings[indexA];
            characterMappings[indexA] = characterMappings[indexB];
            characterMappings[indexB] = temp;

            // Update group references
            foreach (var group in characterGroups)
            {
                for (int i = 0; i < group.characterIndices.Count; i++)
                {
                    if (group.characterIndices[i] == indexA)
                        group.characterIndices[i] = indexB;
                    else if (group.characterIndices[i] == indexB)
                        group.characterIndices[i] = indexA;
                }
            }
        }

        private void UpdateGroupIndicesAfterRemoval(int removedIndex)
        {
            foreach (var group in characterGroups)
            {
                group.characterIndices.Remove(removedIndex);

                for (int i = 0; i < group.characterIndices.Count; i++)
                {
                    if (group.characterIndices[i] > removedIndex)
                        group.characterIndices[i]--;
                }
            }
        }

        private void AutoGeneratePairGroups()
        {
            for (int i = 0; i < characterMappings.Count; i++)
            {
                for (int j = i + 1; j < characterMappings.Count; j++)
                {
                    // Check if group already exists
                    bool exists = characterGroups.Any(g =>
                        g.characterIndices.Count == 2 &&
                        g.characterIndices.Contains(i) &&
                        g.characterIndices.Contains(j));

                    if (!exists)
                    {
                        characterGroups.Add(new CharacterGroup
                        {
                            groupName = $"{characterMappings[i].characterName} + {characterMappings[j].characterName}",
                            characterIndices = new List<int> { i, j }
                        });
                    }
                }
            }
        }

        private bool ValidateSetup(out string message)
        {
            if (playableDirector == null)
            {
                message = "Please assign a PlayableDirector.";
                return false;
            }

            if (timelineAsset == null)
            {
                message = "PlayableDirector has no Timeline assigned.";
                return false;
            }

            if (targetCinemachineTrack == null)
            {
                message = "Please select a Cinemachine track.";
                return false;
            }

            if (characterMappings.Count == 0)
            {
                message = "Please add at least one character.";
                return false;
            }

            bool hasValidCharacter = characterMappings.Any(m =>
                m.audioTrack != null && m.characterCameras.Any(c => c != null));

            if (!hasValidCharacter)
            {
                message = "At least one character needs an audio track and camera assigned.";
                return false;
            }

            if (stageCameras.Count == 0 || stageCameras.All(c => c == null))
            {
                message = "Please add at least one stage camera for silent segments.";
                return false;
            }

            message = "Valid";
            return true;
        }

        private Color GetSegmentColor(SingingSegment segment)
        {
            switch (segment.type)
            {
                case SegmentType.Silent:
                    return new Color(0.3f, 0.3f, 0.3f);
                case SegmentType.Solo:
                    if (segment.singingCharacters.Count > 0)
                    {
                        int charIndex = segment.singingCharacters[0];
                        if (charIndex < characterMappings.Count)
                            return characterMappings[charIndex].editorColor * 0.8f;
                    }
                    return new Color(0.2f, 0.6f, 0.9f);
                case SegmentType.Group:
                    return new Color(0.9f, 0.6f, 0.2f);
                default:
                    return Color.gray;
            }
        }

        private string GetSegmentLabel(SingingSegment segment)
        {
            switch (segment.type)
            {
                case SegmentType.Silent:
                    return "Silent";
                case SegmentType.Solo:
                    if (segment.singingCharacters.Count > 0)
                    {
                        int charIndex = segment.singingCharacters[0];
                        if (charIndex < characterMappings.Count)
                            return characterMappings[charIndex].characterName;
                    }
                    return "Solo";
                case SegmentType.Group:
                    return $"Group ({segment.singingCharacters.Count})";
                default:
                    return "";
            }
        }

        #endregion

        #region Audio Analysis

        private void AnalyzeAudioTracks()
        {
            analyzedSegments.Clear();
            hasAnalyzedAudio = false;

            if (timelineAsset == null) return;

            float duration = (float)timelineAsset.duration;
            if (duration <= 0) return;

            EditorUtility.DisplayProgressBar("Analyzing Audio", "Extracting audio data...", 0f);

            try
            {
                // Build a timeline of who is singing at each moment
                var singingTimeline = new List<(float time, List<int> singingCharacters)>();

                // Sample at regular intervals
                int sampleCount = Mathf.CeilToInt(duration / analysisResolution);

                for (int i = 0; i <= sampleCount; i++)
                {
                    float time = i * analysisResolution;
                    var singingNow = GetSingingCharactersAtTime(time);
                    singingTimeline.Add((time, singingNow));

                    if (i % 100 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Analyzing Audio",
                            $"Analyzing time {time:F1}s / {duration:F1}s",
                            (float)i / sampleCount);
                    }
                }

                // Convert timeline to segments
                ConvertToSegments(singingTimeline, duration);

                // Merge short segments
                MergeShortSegments();

                // Apply silence threshold
                ApplySilenceThreshold();

                hasAnalyzedAudio = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Audio analysis complete. Found {analyzedSegments.Count} segments.");
        }

        private List<int> GetSingingCharactersAtTime(float time)
        {
            var singing = new List<int>();

            for (int i = 0; i < characterMappings.Count; i++)
            {
                var mapping = characterMappings[i];
                if (mapping.audioTrack == null) continue;

                float audioLevel = GetAudioLevelAtTime(mapping.audioTrack, time);

                if (audioLevel > mapping.audioThreshold)
                {
                    singing.Add(i);
                }
            }

            return singing;
        }

        private float GetAudioLevelAtTime(AudioTrack audioTrack, float time)
        {
            // Get clips on the track
            foreach (var clip in audioTrack.GetClips())
            {
                if (time >= clip.start && time < clip.end)
                {
                    var audioClip = (clip.asset as AudioPlayableAsset)?.clip;
                    if (audioClip == null) continue;

                    // Calculate position within the audio clip
                    float clipTime = (float)(time - clip.start + clip.clipIn);

                    // Sample the audio
                    return SampleAudioLevel(audioClip, clipTime);
                }
            }

            return 0f;
        }

        private float SampleAudioLevel(AudioClip clip, float time)
        {
            if (clip == null) return 0f;

            int samplePosition = Mathf.FloorToInt(time * clip.frequency);
            if (samplePosition < 0 || samplePosition >= clip.samples) return 0f;

            // Get a small window of samples
            int windowSize = Mathf.Min(1024, clip.samples - samplePosition);
            float[] samples = new float[windowSize * clip.channels];

            try
            {
                clip.GetData(samples, samplePosition);
            }
            catch
            {
                return 0f;
            }

            // Calculate RMS
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            return Mathf.Sqrt(sum / samples.Length);
        }

        private void ConvertToSegments(List<(float time, List<int> singingCharacters)> timeline, float duration)
        {
            if (timeline.Count == 0) return;

            var currentSegment = new SingingSegment
            {
                startTime = 0,
                singingCharacters = new List<int>(timeline[0].singingCharacters)
            };

            for (int i = 1; i < timeline.Count; i++)
            {
                var (time, singing) = timeline[i];

                // Check if singing characters changed
                bool changed = !singing.SequenceEqual(currentSegment.singingCharacters);

                if (changed)
                {
                    // Close current segment
                    currentSegment.endTime = time;
                    currentSegment.type = DetermineSegmentType(currentSegment.singingCharacters);
                    analyzedSegments.Add(currentSegment);

                    // Start new segment
                    currentSegment = new SingingSegment
                    {
                        startTime = time,
                        singingCharacters = new List<int>(singing)
                    };
                }
            }

            // Close final segment
            currentSegment.endTime = duration;
            currentSegment.type = DetermineSegmentType(currentSegment.singingCharacters);
            analyzedSegments.Add(currentSegment);
        }

        private SegmentType DetermineSegmentType(List<int> singingCharacters)
        {
            if (singingCharacters.Count == 0) return SegmentType.Silent;
            if (singingCharacters.Count == 1) return SegmentType.Solo;
            return SegmentType.Group;
        }

        private void MergeShortSegments()
        {
            if (analyzedSegments.Count < 2) return;

            var merged = new List<SingingSegment>();
            var current = analyzedSegments[0];

            for (int i = 1; i < analyzedSegments.Count; i++)
            {
                var next = analyzedSegments[i];

                // If current segment is too short and matches type with next
                if (current.Duration < minimumShotDuration &&
                    current.type == next.type &&
                    current.singingCharacters.SequenceEqual(next.singingCharacters))
                {
                    // Extend current to include next
                    current.endTime = next.endTime;
                }
                else if (current.Duration < minimumShotDuration * 0.5f)
                {
                    // Very short segment - merge with next regardless
                    current.endTime = next.endTime;
                    current.singingCharacters = next.singingCharacters;
                    current.type = next.type;
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            analyzedSegments = merged;
        }

        private void ApplySilenceThreshold()
        {
            // Mark short silent segments as belonging to adjacent singing segments
            for (int i = 0; i < analyzedSegments.Count; i++)
            {
                var segment = analyzedSegments[i];

                if (segment.type == SegmentType.Silent && segment.Duration < silenceThreshold)
                {
                    // Find adjacent non-silent segments
                    SingingSegment before = i > 0 ? analyzedSegments[i - 1] : null;
                    SingingSegment after = i < analyzedSegments.Count - 1 ? analyzedSegments[i + 1] : null;

                    // Prefer the longer adjacent segment
                    if (before != null && before.type != SegmentType.Silent &&
                        (after == null || after.type == SegmentType.Silent || before.Duration >= after.Duration))
                    {
                        segment.type = before.type;
                        segment.singingCharacters = new List<int>(before.singingCharacters);
                    }
                    else if (after != null && after.type != SegmentType.Silent)
                    {
                        segment.type = after.type;
                        segment.singingCharacters = new List<int>(after.singingCharacters);
                    }
                }
            }

            // Merge consecutive same-type segments
            MergeConsecutiveSameTypeSegments();
        }

        private void MergeConsecutiveSameTypeSegments()
        {
            if (analyzedSegments.Count < 2) return;

            var merged = new List<SingingSegment>();
            var current = analyzedSegments[0];

            for (int i = 1; i < analyzedSegments.Count; i++)
            {
                var next = analyzedSegments[i];

                if (current.type == next.type &&
                    current.singingCharacters.SequenceEqual(next.singingCharacters))
                {
                    current.endTime = next.endTime;
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            analyzedSegments = merged;
        }

        #endregion

        #region Shot Generation

        private void PreviewGeneratedShots()
        {
            var shots = GenerateShotList();

            Debug.Log($"=== Camera Shot Preview ({shots.Count} shots) ===");

            foreach (var shot in shots)
            {
                string cameraName = shot.camera != null ? shot.camera.name : "(null)";
                Debug.Log($"[{shot.startTime:F2}s - {shot.endTime:F2}s] {cameraName} ({shot.segmentType})");
            }
        }

        private void GenerateCameraShots()
        {
            if (targetCinemachineTrack == null) return;

            var shots = GenerateShotList();

            Undo.RecordObject(timelineAsset, "Generate Camera Shots");

            // Clear existing clips
            ClearCinemachineTrack();

            // Add new clips
            foreach (var shot in shots)
            {
                if (shot.camera == null) continue;

                var clip = targetCinemachineTrack.CreateClip<CinemachineShot>();
                clip.start = shot.startTime;
                clip.duration = shot.endTime - shot.startTime;
                clip.displayName = shot.camera.name;

                var shotAsset = clip.asset as CinemachineShot;
                if (shotAsset != null)
                {
                    // Set the virtual camera reference
                    var exposedRef = new ExposedReference<CinemachineVirtualCameraBase>
                    {
                        exposedName = GUID.Generate().ToString()
                    };

                    playableDirector.SetReferenceValue(exposedRef.exposedName, shot.camera);
                    shotAsset.VirtualCamera = exposedRef;
                }
            }

            EditorUtility.SetDirty(timelineAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"Generated {shots.Count} camera shots on Timeline.");
        }

        private List<(float startTime, float endTime, CinemachineCamera camera, SegmentType segmentType)> GenerateShotList()
        {
            var shots = new List<(float startTime, float endTime, CinemachineCamera camera, SegmentType segmentType)>();

            CinemachineCamera lastCamera = null;
            int consecutiveSameCameraCount = 0;

            foreach (var segment in analyzedSegments)
            {
                var availableCameras = GetCamerasForSegment(segment);

                if (availableCameras.Count == 0)
                {
                    Debug.LogWarning($"No cameras available for segment at {segment.startTime}s ({segment.type})");
                    continue;
                }

                // Determine how many shots for this segment
                float segmentDuration = segment.Duration;
                int shotCount = Mathf.Max(1, Mathf.FloorToInt(segmentDuration / minimumShotDuration));

                // For longer segments, add variety
                if (segmentDuration > minimumShotDuration * 3)
                {
                    shotCount = Mathf.Min(shotCount, Mathf.CeilToInt(segmentDuration / (minimumShotDuration * 2)));
                }

                float shotDuration = segmentDuration / shotCount;

                for (int i = 0; i < shotCount; i++)
                {
                    float startTime = segment.startTime + (i * shotDuration);
                    float endTime = startTime + shotDuration;

                    // Select camera
                    var camera = SelectCamera(availableCameras, lastCamera, ref consecutiveSameCameraCount);

                    if (camera != null)
                    {
                        shots.Add((startTime, endTime, camera, segment.type));
                        lastCamera = camera;
                    }
                }
            }

            return shots;
        }

        private List<CinemachineCamera> GetCamerasForSegment(SingingSegment segment)
        {
            var cameras = new List<CinemachineCamera>();

            switch (segment.type)
            {
                case SegmentType.Silent:
                    cameras.AddRange(stageCameras.Where(c => c != null));
                    break;

                case SegmentType.Solo:
                    if (segment.singingCharacters.Count > 0)
                    {
                        int charIndex = segment.singingCharacters[0];
                        if (charIndex < characterMappings.Count)
                        {
                            cameras.AddRange(characterMappings[charIndex].characterCameras.Where(c => c != null));
                        }
                    }
                    break;

                case SegmentType.Group:
                    // Find matching group
                    var matchingGroup = FindMatchingGroup(segment.singingCharacters);
                    if (matchingGroup != null && matchingGroup.groupCameras.Any(c => c != null))
                    {
                        cameras.AddRange(matchingGroup.groupCameras.Where(c => c != null));
                    }
                    else
                    {
                        // Fall back to stage cameras for groups without specific cameras
                        cameras.AddRange(stageCameras.Where(c => c != null));

                        // Also add character cameras from singing characters
                        foreach (int charIndex in segment.singingCharacters)
                        {
                            if (charIndex < characterMappings.Count)
                            {
                                cameras.AddRange(characterMappings[charIndex].characterCameras.Where(c => c != null));
                            }
                        }
                    }
                    break;
            }

            return cameras.Distinct().ToList();
        }

        private CharacterGroup FindMatchingGroup(List<int> singingCharacters)
        {
            // Find exact match first
            var exactMatch = characterGroups.FirstOrDefault(g =>
                g.characterIndices.Count == singingCharacters.Count &&
                g.characterIndices.All(i => singingCharacters.Contains(i)));

            if (exactMatch != null) return exactMatch;

            // Find best partial match (group that contains all singing characters)
            return characterGroups
                .Where(g => singingCharacters.All(i => g.characterIndices.Contains(i)))
                .OrderBy(g => g.characterIndices.Count)
                .FirstOrDefault();
        }

        private CinemachineCamera SelectCamera(
            List<CinemachineCamera> available,
            CinemachineCamera lastCamera,
            ref int consecutiveCount)
        {
            if (available.Count == 0) return null;
            if (available.Count == 1) return available[0];

            CinemachineCamera selected;

            if (useRandomCameraSelection)
            {
                // Filter by shot type probability
                var categorized = CategorizeBysShotType(available);
                var weighted = BuildWeightedList(categorized);

                if (weighted.Count > 0)
                {
                    selected = weighted[UnityEngine.Random.Range(0, weighted.Count)];
                }
                else
                {
                    selected = available[UnityEngine.Random.Range(0, available.Count)];
                }
            }
            else
            {
                // Sequential selection
                int index = available.IndexOf(lastCamera);
                index = (index + 1) % available.Count;
                selected = available[index];
            }

            // Avoid consecutive same camera
            if (avoidConsecutiveSameCamera && selected == lastCamera)
            {
                consecutiveCount++;

                if (consecutiveCount >= maxConsecutiveSameCameraShots)
                {
                    var alternatives = available.Where(c => c != lastCamera).ToList();
                    if (alternatives.Count > 0)
                    {
                        selected = alternatives[UnityEngine.Random.Range(0, alternatives.Count)];
                        consecutiveCount = 0;
                    }
                }
            }
            else
            {
                consecutiveCount = selected == lastCamera ? consecutiveCount + 1 : 0;
            }

            return selected;
        }

        private Dictionary<string, List<CinemachineCamera>> CategorizeBysShotType(List<CinemachineCamera> cameras)
        {
            var categorized = new Dictionary<string, List<CinemachineCamera>>
            {
                { "closeup", new List<CinemachineCamera>() },
                { "medium", new List<CinemachineCamera>() },
                { "wide", new List<CinemachineCamera>() },
                { "other", new List<CinemachineCamera>() }
            };

            foreach (var cam in cameras)
            {
                string nameLower = cam.name.ToLower();

                if (nameLower.Contains("close") || nameLower.Contains("tight") || nameLower.Contains("head"))
                    categorized["closeup"].Add(cam);
                else if (nameLower.Contains("medium") || nameLower.Contains("mid") || nameLower.Contains("waist"))
                    categorized["medium"].Add(cam);
                else if (nameLower.Contains("wide") || nameLower.Contains("full") || nameLower.Contains("stage"))
                    categorized["wide"].Add(cam);
                else
                    categorized["other"].Add(cam);
            }

            return categorized;
        }

        private List<CinemachineCamera> BuildWeightedList(Dictionary<string, List<CinemachineCamera>> categorized)
        {
            var weighted = new List<CinemachineCamera>();

            float total = closeUpProbability + mediumShotProbability + wideShotProbability;
            if (total <= 0) total = 1f;

            int closeUpWeight = Mathf.RoundToInt((closeUpProbability / total) * 10);
            int mediumWeight = Mathf.RoundToInt((mediumShotProbability / total) * 10);
            int wideWeight = Mathf.RoundToInt((wideShotProbability / total) * 10);

            for (int i = 0; i < closeUpWeight && categorized["closeup"].Count > 0; i++)
                weighted.AddRange(categorized["closeup"]);

            for (int i = 0; i < mediumWeight && categorized["medium"].Count > 0; i++)
                weighted.AddRange(categorized["medium"]);

            for (int i = 0; i < wideWeight && categorized["wide"].Count > 0; i++)
                weighted.AddRange(categorized["wide"]);

            // Always include "other" cameras
            weighted.AddRange(categorized["other"]);

            return weighted;
        }

        private void ClearCinemachineTrack()
        {
            if (targetCinemachineTrack == null) return;

            Undo.RecordObject(timelineAsset, "Clear Cinemachine Track");

            var clips = targetCinemachineTrack.GetClips().ToList();
            foreach (var clip in clips)
            {
                timelineAsset.DeleteClip(clip);
            }

            EditorUtility.SetDirty(timelineAsset);
        }

        #endregion
    }
}
