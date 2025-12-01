#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;
using System;
using static UnityEngine.InputSystem.Controls.DiscreteButtonControl;

namespace MToonConverter
{
    /// <summary>
    /// Unity Editor tool to convert MToon (VRM) materials to Unity Toon Shader materials.
    /// Automatically detects material purpose (Head, Body, Outline) and applies the appropriate shader variant.
    /// </summary>
    public class MToonToToonConverter : EditorWindow
    {
        internal const string ShaderDefineIS_OUTLINE_CLIPPING_NO = "_IS_OUTLINE_CLIPPING_NO";
        internal const string ShaderDefineIS_OUTLINE_CLIPPING_YES = "_IS_OUTLINE_CLIPPING_YES";

        internal const string ShaderDefineIS_CLIPPING_OFF = "_IS_CLIPPING_OFF";
        internal const string ShaderDefineIS_CLIPPING_MODE = "_IS_CLIPPING_MODE";
        internal const string ShaderDefineIS_CLIPPING_TRANSMODE = "_IS_CLIPPING_TRANSMODE";

        internal const string ShaderDefineIS_TRANSCLIPPING_OFF = "_IS_TRANSCLIPPING_OFF";
        internal const string ShaderDefineIS_TRANSCLIPPING_ON = "_IS_TRANSCLIPPING_ON";

        internal const string ShaderDefineIS_CLIPPING_MATTE = "_IS_CLIPPING_MATTE";

        private GameObject targetObject;
        private bool includeInactive = true;
        private bool createBackup = true;
        private Vector2 scrollPosition;
        private List<MaterialConversionInfo> materialsToConvert = new List<MaterialConversionInfo>();
        private bool showAdvancedOptions = false;

        // Shader reference
        private Shader toonShader;

        // Shader names - update these to match your project's shader names
        private const string MTOON_SHADER_NAME = "VRM10/MToon10";
        private const string MTOON_SHADER_NAME_ALT = "VRM/MToon";

        private string toonShaderName = "Toon";

        [MenuItem("Tools/VRM/MToon to Toon Converter")]
        public static void ShowWindow()
        {
            var window = GetWindow<MToonToToonConverter>("MToon to Toon Converter");
            window.minSize = new Vector2(450, 400);
        }

        private void OnEnable()
        {
            RefreshShaderReferences();
        }

        private void RefreshShaderReferences()
        {
            toonShader = Shader.Find(toonShaderName);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("MToon to Toon Material Converter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool converts MToon (VRM) materials to Unity Toon Shader materials. " +
                "It automatically detects whether materials are for Head, Body, or Outline and applies the appropriate shader variant.",
                MessageType.Info);

            EditorGUILayout.Space(10);

            // Target object selection
            EditorGUI.BeginChangeCheck();
            targetObject = (GameObject)EditorGUILayout.ObjectField(
                "Target GameObject",
                targetObject,
                typeof(GameObject),
                true);

            if (EditorGUI.EndChangeCheck() && targetObject != null)
            {
                ScanForMToonMaterials();
            }

            EditorGUILayout.Space(5);

            // Options
            includeInactive = EditorGUILayout.Toggle("Include Inactive Children", includeInactive);
            createBackup = EditorGUILayout.Toggle("Create Material Backups", createBackup);

            EditorGUILayout.Space(5);

            // Advanced options
            showAdvancedOptions = EditorGUILayout.Foldout(showAdvancedOptions, "Advanced Options", true);
            if (showAdvancedOptions)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Target Shader Name:", EditorStyles.boldLabel);
                toonShaderName = EditorGUILayout.TextField("Toon Shader", toonShaderName);

                if (GUILayout.Button("Refresh Shader Reference"))
                {
                    RefreshShaderReferences();
                }

                // Show shader status
                EditorGUILayout.Space(5);
                ShowShaderStatus("Toon Shader", toonShader);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // Scan button
            if (GUILayout.Button("Scan for MToon Materials", GUILayout.Height(30)))
            {
                ScanForMToonMaterials();
            }

            EditorGUILayout.Space(10);

            // Materials list
            if (materialsToConvert.Count > 0)
            {
                EditorGUILayout.LabelField($"Found {materialsToConvert.Count} MToon Material(s):", EditorStyles.boldLabel);

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

                foreach (var info in materialsToConvert)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                    info.shouldConvert = EditorGUILayout.Toggle(info.shouldConvert, GUILayout.Width(20));

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(info.material.name, EditorStyles.boldLabel);

                    EditorGUILayout.LabelField($"Used by: {info.rendererName}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(10);

                // Select all/none buttons
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select All"))
                {
                    foreach (var info in materialsToConvert)
                        info.shouldConvert = true;
                }
                if (GUILayout.Button("Select None"))
                {
                    foreach (var info in materialsToConvert)
                        info.shouldConvert = false;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                // Convert button
                GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
                if (GUILayout.Button("Convert Selected Materials", GUILayout.Height(40)))
                {
                    ConvertMaterials();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (targetObject != null)
            {
                EditorGUILayout.HelpBox("No MToon materials found on this GameObject or its children.", MessageType.Warning);
            }
        }

        private void ShowShaderStatus(string label, Shader shader)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label + ":", GUILayout.Width(100));
            if (shader != null)
            {
                EditorGUILayout.LabelField("✓ Found", EditorStyles.boldLabel);
            }
            else
            {
                GUI.color = Color.red;
                EditorGUILayout.LabelField("✗ Not Found", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ScanForMToonMaterials()
        {
            materialsToConvert.Clear();

            if (targetObject == null)
                return;

            var renderers = targetObject.GetComponentsInChildren<Renderer>(includeInactive);
            var processedMaterials = new HashSet<Material>();

            foreach (var renderer in renderers)
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || processedMaterials.Contains(material))
                        continue;

                    if (IsMToonMaterial(material))
                    {
                        processedMaterials.Add(material);

                        var info = new MaterialConversionInfo
                        {
                            material = material,
                            rendererName = renderer.name,
                            materialType = DetectMaterialType(material, renderer),
                            shouldConvert = true
                        };

                        materialsToConvert.Add(info);
                    }
                }
            }
        }

        private bool IsMToonMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            string shaderName = material.shader.name;
            return shaderName.Contains("MToon") ||
                   shaderName.Contains("VRM10/MToon") ||
                   shaderName.Contains("VRM/MToon");
        }

        /// <summary>
        /// Detects the material type based on material name, renderer name, and material properties.
        /// </summary>
        private MaterialType DetectMaterialType(Material material, Renderer renderer)
        {
            string materialName = material.name.ToLower();
            string rendererName = renderer.name.ToLower();
            string combinedName = materialName + " " + rendererName;

            // Check for outline indicators
            if (combinedName.Contains("outline") ||
                HasOutlineEnabled(material))
            {
                return MaterialType.Outline;
            }

            // Check for head/face indicators
            string[] headKeywords = { "head", "face", "eye", "mouth", "nose", "ear", "hair", "brow", "lip", "teeth", "tongue", "cheek", "chin", "jaw", "skull", "forehead" };
            foreach (var keyword in headKeywords)
            {
                if (combinedName.Contains(keyword))
                {
                    return MaterialType.Head;
                }
            }

            // Check for body indicators
            string[] bodyKeywords = { "body", "skin", "arm", "leg", "hand", "foot", "torso", "chest", "back", "neck", "shoulder", "cloth", "clothes", "shirt", "pants", "dress", "shoe", "glove", "accessory" };
            foreach (var keyword in bodyKeywords)
            {
                if (combinedName.Contains(keyword))
                {
                    return MaterialType.Body;
                }
            }

            // Check mesh name from SkinnedMeshRenderer
            if (renderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                string meshName = smr.sharedMesh.name.ToLower();
                foreach (var keyword in headKeywords)
                {
                    if (meshName.Contains(keyword))
                        return MaterialType.Head;
                }
            }

            // Default to Body if no specific type detected
            return MaterialType.Body;
        }

        /// <summary>
        /// Checks if the MToon material has outline enabled.
        /// </summary>
        private bool HasOutlineEnabled(Material material)
        {
            // Check MToon outline properties
            if (material.HasProperty("_OutlineWidthMode"))
            {
                int mode = material.GetInt("_OutlineWidthMode");
                if (mode > 0) // 0 = None, 1+ = Some outline mode
                {
                    if (material.HasProperty("_OutlineWidth"))
                    {
                        float width = material.GetFloat("_OutlineWidth");
                        return width > 0.0001f;
                    }
                }
            }

            return false;
        }

        private void ConvertMaterials()
        {
            RefreshShaderReferences();

            var toConvert = materialsToConvert.Where(m => m.shouldConvert).ToList();

            if (toConvert.Count == 0)
            {
                EditorUtility.DisplayDialog("No Materials Selected",
                    "Please select at least one material to convert.", "OK");
                return;
            }

            int converted = 0;
            int failed = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var info in toConvert)
                {
                    EditorUtility.DisplayProgressBar("Converting Materials",
                        $"Converting {info.material.name}...",
                        (float)(converted + failed) / toConvert.Count);

                    if (ConvertMaterial(info))
                    {
                        converted++;
                        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(info.material));
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string message = $"Conversion complete!\n\nConverted: {converted}\nFailed: {failed}";
            EditorUtility.DisplayDialog("Conversion Complete", message, "OK");

            // Refresh the list
            ScanForMToonMaterials();
        }

        private bool ConvertMaterial(MaterialConversionInfo info)
        {
            Material material = info.material;

            if (toonShader == null)
            {
                Debug.LogError($"Toon shader not found. Please check the shader name in Advanced Options.");
                return false;
            }

            // Create backup if requested
            if (createBackup)
            {
                CreateMaterialBackup(material);
            }

            // Store MToon properties before changing shader
            MToonProperties mtoonProps = ExtractMToonProperties(material);

            // Record undo
            Undo.RecordObject(material, "Convert MToon to Toon");

            // Change shader
            material.shader = toonShader;

            // Apply converted properties
            ApplyToonProperties(material, mtoonProps, info.materialType);

            Debug.Log($"Converted material '{material.name}' (Type: {info.materialType})");
            return true;
        }

        private void CreateMaterialBackup(Material material)
        {
            string path = AssetDatabase.GetAssetPath(material);
            if (string.IsNullOrEmpty(path))
                return;

            string directory = System.IO.Path.GetDirectoryName(path);
            string filename = System.IO.Path.GetFileNameWithoutExtension(path);
            string extension = System.IO.Path.GetExtension(path);

            string backupPath = $"{directory}/{filename}_MToonBackup{extension}";

            // Check if backup already exists
            if (!AssetDatabase.LoadAssetAtPath<Material>(backupPath))
            {
                AssetDatabase.CopyAsset(path, backupPath);
            }
        }

        /// <summary>
        /// Extracts all relevant properties from an MToon material.
        /// </summary>
        private MToonProperties ExtractMToonProperties(Material material)
        {
            var props = new MToonProperties();

            // Base Color
            if (material.HasProperty("_Color"))
                props.baseColor = material.GetColor("_Color");

            if (material.HasProperty("_MainTex"))
                props.mainTex = material.GetTexture("_MainTex") as Texture2D;

            // Shade Color
            if (material.HasProperty("_ShadeColor"))
                props.shadeColor = material.GetColor("_ShadeColor");

            if (material.HasProperty("_ShadeTex"))
                props.shadeTex = material.GetTexture("_ShadeTex") as Texture2D;

            // Normal Map
            if (material.HasProperty("_BumpMap"))
                props.normalMap = material.GetTexture("_BumpMap") as Texture2D;

            if (material.HasProperty("_BumpScale"))
                props.normalScale = material.GetFloat("_BumpScale");

            // Shading parameters
            if (material.HasProperty("_ShadingShiftFactor"))
                props.shadingShift = material.GetFloat("_ShadingShiftFactor");

            if (material.HasProperty("_ShadingToonyFactor"))
                props.shadingToony = material.GetFloat("_ShadingToonyFactor");

            // Emission
            if (material.HasProperty("_EmissionColor"))
                props.emissionColor = material.GetColor("_EmissionColor");

            if (material.HasProperty("_EmissionMap"))
                props.emissionMap = material.GetTexture("_EmissionMap") as Texture2D;

            // Rim Lighting
            if (material.HasProperty("_RimColor"))
                props.rimColor = material.GetColor("_RimColor");

            if (material.HasProperty("_RimFresnelPower"))
                props.rimFresnelPower = material.GetFloat("_RimFresnelPower");

            if (material.HasProperty("_RimLift"))
                props.rimLift = material.GetFloat("_RimLift");

            if (material.HasProperty("_RimTex"))
                props.rimTex = material.GetTexture("_RimTex") as Texture2D;

            if (material.HasProperty("_RimLightingMix"))
                props.rimLightingMix = material.GetFloat("_RimLightingMix");

            // Matcap
            if (material.HasProperty("_MatcapColor"))
                props.matcapColor = material.GetColor("_MatcapColor");

            if (material.HasProperty("_MatcapTex"))
                props.matcapTex = material.GetTexture("_MatcapTex") as Texture2D;

            // Outline
            if (material.HasProperty("_OutlineWidthMode"))
                props.outlineWidthMode = material.GetInt("_OutlineWidthMode");

            if (material.HasProperty("_OutlineWidth"))
                props.outlineWidth = material.GetFloat("_OutlineWidth");

            if (material.HasProperty("_OutlineWidthTex"))
                props.outlineWidthTex = material.GetTexture("_OutlineWidthTex") as Texture2D;

            if (material.HasProperty("_OutlineColor"))
                props.outlineColor = material.GetColor("_OutlineColor");

            if (material.HasProperty("_OutlineLightingMix"))
                props.outlineLightingMix = material.GetFloat("_OutlineLightingMix");

            // Alpha/Rendering
            if (material.HasProperty("_AlphaMode"))
                props.alphaMode = material.GetInt("_AlphaMode");

            if (material.HasProperty("_Cutoff"))
                props.cutoff = material.GetFloat("_Cutoff");

            if (material.HasProperty("_DoubleSided"))
                props.doubleSided = material.GetInt("_DoubleSided") > 0;

            // Render queue
            props.renderQueue = material.renderQueue + material.GetInt("_RenderQueueOffset");

            return props;
        }

        /// <summary>
        /// Applies converted properties to the Toon shader material.
        /// Property names may need to be adjusted based on your specific Toon shader implementation.
        /// </summary>
        private void ApplyToonProperties(Material material, MToonProperties props, MaterialType type)
        {
            // Common properties that most Toon shaders have

            // Base Color
            TrySetColor(material, "_BaseColor", props.baseColor);
            TrySetColor(material, "_Color", props.baseColor);
            TrySetTexture(material, "_BaseMap", props.mainTex);
            TrySetTexture(material, "_MainTex", props.mainTex);

            // Shade/Shadow Color (Unity Toon Shader naming conventions)
            TrySetColor(material, "_1st_ShadeColor", props.shadeColor);
            TrySetColor(material, "_2nd_ShadeColor", props.shadeColor);
            TrySetColor(material, "_ShadeColor", props.shadeColor);
            TrySetTexture(material, "_1st_ShadeMap", props.shadeTex);
            TrySetTexture(material, "_2nd_ShadeMap", props.shadeTex);

            // Normal Map
            TrySetTexture(material, "_BumpMap", props.normalMap);
            TrySetTexture(material, "_NormalMap", props.normalMap);
            TrySetFloat(material, "_BumpScale", props.normalScale);
            TrySetFloat(material, "_NormalScale", props.normalScale);

            // Enable normal map keyword if present
            if (props.normalMap != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }

            // Shading parameters - convert MToon values to Toon shader equivalents
            // MToon uses ShadingShift (-1 to 1) and ShadingToony (0 to 1)
            // Unity Toon Shader typically uses different parameters

            // Base/Shade boundary
            float baseShadeStep = Mathf.Lerp(0.5f, 0.0f, props.shadingToony);
            TrySetFloat(material, "_BaseShade_Feather", 1.0f - props.shadingToony);
            TrySetFloat(material, "_1st_ShadeColor_Step", baseShadeStep);
            TrySetFloat(material, "_1st_ShadeColor_Feather", 1.0f - props.shadingToony);

            // Shading shift affects the shadow threshold
            TrySetFloat(material, "_Tweak_SystemShadowsLevel", props.shadingShift);

            // Emission
            TrySetColor(material, "_EmissionColor", props.emissionColor);
            TrySetColor(material, "_Emissive_Color", props.emissionColor);
            TrySetTexture(material, "_EmissionMap", props.emissionMap);
            TrySetTexture(material, "_Emissive_Tex", props.emissionMap);

            if (props.emissionColor.maxColorComponent > 0 || props.emissionMap != null)
            {
                material.EnableKeyword("_EMISSION");
                material.EnableKeyword("_EMISSIVE_SIMPLE");
            }

            // Rim Lighting
            TrySetFloat(material, "_RimLight", 1.0f);
            TrySetColor(material, "_RimLightColor", new Color(0.9f, 0.9f, 0.9f));
            TrySetFloat(material, "_RimLight_Power", 0.75f);
            TrySetFloat(material, "_RimLight_InsideMask", 0.75f);
            TrySetFloat(material, "_RimLight_FeatherOff", 1f);
            TrySetFloat(material, "_LightDirection_MaskOn", 1f);
            TrySetFloat(material, "_Tweak_LightDirection_MaskLevel", 0.5f);

            if (props.rimColor.maxColorComponent > 0)
            {
                TrySetFloat(material, "_RimLight", 1.0f);
            }

            // Matcap
            if (props.matcapColor != Color.black)
            {
                TrySetColor(material, "_MatCapColor", props.matcapColor);
                TrySetTexture(material, "_MatCap_Sampler", props.matcapTex);
            }

            if (props.matcapTex != null || props.matcapColor.maxColorComponent > 0)
            {
                TrySetFloat(material, "_MatCap", 1.0f);
            }

            // Outline (only for outline materials or if outline is enabled)
            if (type == MaterialType.Outline || props.outlineWidthMode > 0)
            {
                TrySetColor(material, "_Outline_Color", props.outlineColor);

                // Convert MToon outline width (0-0.05 range) to Unity Toon (typically 0-10 range)
                // Multiplied by 3 as requested
                float convertedWidth = props.outlineWidth * 700f;
                TrySetFloat(material, "_Outline_Width", convertedWidth);
                TrySetTexture(material, "_Outline_Sampler", props.outlineWidthTex);
                TrySetFloat(material, "_Is_LightColor_Outline", props.outlineLightingMix);

                // Enable "Blend base color to outline" by default
                TrySetFloat(material, "_Is_BlendBaseColor", 1.0f);

                // Set outline mode keywords
                if (props.outlineWidthMode == 1) // World coordinates
                {
                    material.EnableKeyword("_OUTLINE_NML");
                    material.DisableKeyword("_OUTLINE_POS");
                }
                else if (props.outlineWidthMode == 2) // Screen coordinates
                {
                    material.DisableKeyword("_OUTLINE_NML");
                    material.EnableKeyword("_OUTLINE_POS");
                }
            }

            // Alpha/Transparency
            TrySetFloat(material, "_Cutoff", props.cutoff);
            TrySetFloat(material, "_Clipping_Level", props.cutoff - 0.01f);

            // Set render mode based on alpha mode
            switch (props.alphaMode)
            {
                case 0: // Opaque
                    SetMaterialOpaque(material);
                    break;
                case 1: // Cutout
                    SetMaterialCutout(material);
                    break;
                case 2: // Transparent
                    SetMaterialTransparent(material);
                    break;
            }

            // Culling
            if (props.doubleSided)
            {
                TrySetInt(material, "_CullMode", 0);
                TrySetFloat(material, "_DoubleSidedEnable", 1.0f);
            }
            else
            {
                TrySetInt(material, "_CullMode", 2); // Back
            }

            EditorUtility.SetDirty(material);
        }

        private void SetMaterialOpaque(Material material)
        {
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        private void SetMaterialCutout(Material material)
        {
            TrySetInt(material, "_TransparentEnabled", 1);
            TrySetInt(material, "_ClippingMode", 1);
            TrySetFloat(material, "_SurfaceType", 0);
            TrySetFloat(material, "_IsBaseMapAlphaAsClippingMask", 1.0f);
            TrySetInt(material, "_AutoRenderQueue", 0);
            material.EnableKeyword("_IS_CLIPPING_MODE");
            material.EnableKeyword("_IS_ANGELRING_OFF");
            material.EnableKeyword("_IS_OUTLINE_CLIPPING_YES");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        private void SetMaterialTransparent(Material material)
        {
            TrySetInt(material, "_TransparentEnabled", 1);
            TrySetInt(material, "_ClippingMode", 2);
            TrySetFloat(material, "_Clipping_Level", 1.0f);
            material.EnableKeyword("_IS_CLIPPING_TRANSMODE");
            material.EnableKeyword("_IS_ANGELRING_OFF");
            material.EnableKeyword("_IS_OUTLINE_CLIPPING_YES");
            TrySetFloat(material, "_Tweak_transparency", 0f);
            TrySetFloat(material, "_IsBaseMapAlphaAsClippingMask", 1.0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private void TrySetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private void TrySetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private void TrySetInt(Material material, string property, int value)
        {
            if (material.HasProperty(property))
            {
                material.SetInt(property, value);
            }
        }

        private void TrySetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property) && texture != null)
            {
                material.SetTexture(property, texture);
            }
        }

        /// <summary>
        /// Holds information about a material to be converted.
        /// </summary>
        private class MaterialConversionInfo
        {
            public Material material;
            public string rendererName;
            public MaterialType materialType;
            public bool shouldConvert;
        }

        /// <summary>
        /// Stores extracted MToon material properties.
        /// </summary>
        private class MToonProperties
        {
            // Base
            public Color baseColor = Color.white;
            public Texture2D mainTex;

            // Shade
            public Color shadeColor = Color.gray;
            public Texture2D shadeTex;

            // Normal
            public Texture2D normalMap;
            public float normalScale = 1.0f;

            // Shading
            public float shadingShift = -0.05f;
            public float shadingToony = 0.95f;

            // Emission
            public Color emissionColor = Color.black;
            public Texture2D emissionMap;

            // Rim
            public Color rimColor = new Color(0.9f, 0.9f, 0.9f);
            public float rimFresnelPower = 5.0f;
            public float rimLift = 0.0f;
            public Texture2D rimTex;
            public float rimLightingMix = 1.0f;

            // Matcap
            public Color matcapColor = Color.black;
            public Texture2D matcapTex;

            // Outline
            public int outlineWidthMode = 0;
            public float outlineWidth = 0.0f;
            public Texture2D outlineWidthTex;
            public Color outlineColor = Color.black;
            public float outlineLightingMix = 1.0f;

            // Rendering
            public int alphaMode = 0;
            public float cutoff = 0.5f;
            public bool doubleSided = false;
            public int renderQueue = -1;
            public int renderOffset = 0;
        }
    }

    /// <summary>
    /// Material type classification for shader selection.
    /// </summary>
    public enum MaterialType
    {
        Body,
        Head,
        Outline
    }
}
#endif
