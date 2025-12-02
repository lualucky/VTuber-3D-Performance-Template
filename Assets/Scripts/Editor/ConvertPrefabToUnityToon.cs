using System.Collections;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UniGLTF;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UniVRM10;
using VRM10.MToon10;
using System.Linq;
using VRM10.MToon10.MToon0X;

public class ConvertPrefabToUnityToon : EditorWindow
{
    // State tied to inpector UI

    private Object m_sourcePrefab;
    
    private bool m_isPrefabValid;
    
    // Internal state
    private Dictionary<string, Material> m_convertedMats;

    private Shader m_unityToonShader;
    private UnityEngine.Rendering.RenderPipelineAsset m_renderPipeline;

    // Using IDs is for shader properties is faster than using strings.
    // We could also group these by shader but ugh
    
    struct VRM10Props
    {
        public static int AlphaMode = Shader.PropertyToID("_AlphaMode");
        public static int TransparentWithZWrite = Shader.PropertyToID("_TransparentWithZWrite");
        public static int AlphaCutoff = Shader.PropertyToID("_Cutoff");
        public static int RenderQueueOffsetNumber = Shader.PropertyToID("_RenderQueueOffset");
        public static int DoubleSided = Shader.PropertyToID("_DoubleSided");

        public static int BaseColorFactor = Shader.PropertyToID("_Color");
        public static int BaseColorTexture = Shader.PropertyToID("_MainTex");
        public static int ShadeColorFactor = Shader.PropertyToID("_ShadeColor");
        public static int ShadeColorTexture = Shader.PropertyToID("_ShadeTex");
        public static int NormalTexture = Shader.PropertyToID("_BumpMap");
        public static int NormalTextureScale = Shader.PropertyToID("_BumpScale");
        public static int ShadingShiftFactor = Shader.PropertyToID("_ShadingShiftFactor");
        public static int ShadingShiftTexture = Shader.PropertyToID("_ShadingShiftTex");
        public static int ShadingShiftTextureScale = Shader.PropertyToID("_ShadingShiftTexScale");
        public static int ShadingToonyFactor = Shader.PropertyToID("_ShadingToonyFactor");

        public static int GiEqualizationFactor = Shader.PropertyToID("_GiEqualization");

        public static int EmissiveFactor = Shader.PropertyToID("_EmissionColor");
        public static int EmissiveTexture = Shader.PropertyToID("_EmissionMap");

        public static int MatcapColorFactor = Shader.PropertyToID("_MatcapColor");
        public static int MatcapTexture = Shader.PropertyToID("_MatcapTex");
        public static int ParametricRimColorFactor = Shader.PropertyToID("_RimColor");
        public static int ParametricRimFresnelPowerFactor = Shader.PropertyToID("_RimFresnelPower");
        public static int ParametricRimLiftFactor = Shader.PropertyToID("_RimLift");
        public static int RimMultiplyTexture = Shader.PropertyToID("_RimTex");
        public static int RimLightingMixFactor = Shader.PropertyToID("_RimLightingMix");

        public static int OutlineWidthMode = Shader.PropertyToID("_OutlineWidthMode");
        public static int OutlineWidthFactor = Shader.PropertyToID("_OutlineWidth");
        public static int OutlineWidthMultiplyTexture = Shader.PropertyToID("_OutlineWidthTex");
        public static int OutlineColorFactor = Shader.PropertyToID("_OutlineColor");
        public static int OutlineLightingMixFactor = Shader.PropertyToID("_OutlineLightingMix");

        public static int UvAnimationMaskTexture = Shader.PropertyToID("_UvAnimMaskTex");
        public static int UvAnimationScrollXSpeedFactor = Shader.PropertyToID("_UvAnimScrollXSpeed");
        public static int UvAnimationScrollYSpeedFactor = Shader.PropertyToID("_UvAnimScrollYSpeed");
        public static int UvAnimationRotationSpeedFactor = Shader.PropertyToID("_UvAnimRotationSpeed");

        public static int UnityCullMode = Shader.PropertyToID("_M_CullMode");
        public static int UnitySrcBlend = Shader.PropertyToID("_M_SrcBlend");
        public static int UnityDstBlend = Shader.PropertyToID("_M_DstBlend");
        public static int UnityZWrite = Shader.PropertyToID("_M_ZWrite");
        public static int UnityAlphaToMask = Shader.PropertyToID("_M_AlphaToMask");

        public static int EditorEditMode = Shader.PropertyToID("_M_EditMode");
    }

    struct UnityToonProps
    {
        public static int MainTex = Shader.PropertyToID("_MainTex");
        public static int Color = Shader.PropertyToID("_Color");
    
        public static int UseBaseAs1st = Shader.PropertyToID("_Use_BaseAs1st");
        public static int Use1stAs2nd = Shader.PropertyToID("_Use_1stAs2nd");

        public static int AutoRenderQueue = Shader.PropertyToID("_AutoRenderQueue");
        public static int ClippingMode = Shader.PropertyToID("_ClippingMode");
    
        public static int BlendMode = Shader.PropertyToID("_BlendMode");
        public static int Cutoff = Shader.PropertyToID("_Cutoff");
        public static int CullMode = Shader.PropertyToID("_CullMode");
    
        public static int IsBaseMapAlphaAsClippingMask = Shader.PropertyToID("_IsBaseMapAlphaAsClippingMask");
    
        public static int TransparentEnabled = Shader.PropertyToID("_TransparentEnabled");
    
        public static int ClippingLevel = Shader.PropertyToID("_Clipping_Level");

        // Maybe corresponds to world vs screen?
        public static int OutlineMode = Shader.PropertyToID("_OUTLINE");
        public static int OutlineWidth = Shader.PropertyToID("_Outline_Width");
        public static int OutlineColor = Shader.PropertyToID("_Outline_Color");
    }

    // Intermediate representation for the properties we know how to transfer.
    // Let's see if this grows into an unwieldy representation in itself
    public struct MaterialData
    {
        public enum RenderMode
        {
            Opaque = 0,
            Cutout = 1,
            Transparent = 2
        }

        public enum CullMode
        {
            Off = 0,
            Front = 1,
            Back = 2
        }

        public enum OutlineMode
        {
            Off = 0,
            ScreenSpace = 1,
            WorldSpace = 2
        }

        public Texture mainTex;
        public Vector2 mainTexOffset;
        public Vector2 mainTexScale;

        public Color color;

        // Opaque, Cutout, Transparent
        public RenderMode renderMode;
        
        // Off, Front, Back
        public CullMode cullMode;

        public float cutoff;

        public OutlineMode outline;
        public float outlineWidth;
        public Color outlineColor;

        public int renderQueueOffset;
    }

    
    
   
    
    private List<Material> materialsToResave = new();
    
    [MenuItem("Window/VRM/ConvertPrefabToUnityToon")]
    public static void ShowExample()
    {
        ConvertPrefabToUnityToon wnd = GetWindow<ConvertPrefabToUnityToon>();
        wnd.titleContent = new GUIContent("ConvertPrefabToUnityToon");
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Convert VRM Prefab Material Shaders", EditorStyles.boldLabel);
        
        GUILayout.Label("Currently only converts to Unity Toon Shader.");
        
        m_sourcePrefab = EditorGUILayout.ObjectField("Source Prefab", m_sourcePrefab, typeof(Object), allowSceneObjects: true);
        
        if (m_unityToonShader == null)
        {
            m_unityToonShader = Shader.Find("Toon");
        }

        m_unityToonShader = (Shader)EditorGUILayout.ObjectField("Target Shader", m_unityToonShader, typeof(Shader), false);

        // If the user passed in a VRM0 asset, unconverted:
        // m_sourcePrefab is UnityEngine.DefaultAsset
        // VRM0 asset converted, or VRM1:
        // m_sourcePrefab is VRM

        m_isPrefabValid = ValidateProject() && ValidateTarget(m_sourcePrefab);
        
        // Unity Toon shader gui doesn't implement ShaderGUI.ValidateMaterial!
        // Instead it's setting keywords in OnGUI.
        
        // Options:
        // - Legitimately create an editor window for one frame
        // - Hackily create something that pretends to be an editor window, hack into UTS3GUI's assembly and namespace
        // - Copy the logic from UTS3GUI - will execute fast, but be harder to maintain
        // - Hackily derive from UTS3GUI and provide a ValidateMaterial implementation; Override the shader gui per material.

        // The least bad option is to force grab the focus and force embed an open inspector for the material
   
        if (materialsToResave.Count > 0)
        {
            // Draw all the inspectors, open, on top of each other, just to force UTS3GUI.OnGUI to run
            // Somehow, this works
            
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < materialsToResave.Count; ++i)
            {
                GUILayout.BeginArea(new Rect (0,120,1920,1080));
                var mat = materialsToResave[i];
                var materialEditor = (MaterialEditor)Editor.CreateEditor(mat);
                // For some reason, this flag has to be set on the material. Not the editor.
                UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(mat, true);
                materialEditor.DrawHeader();
                materialEditor.OnInspectorGUI();
                GUILayout.EndArea();
                DestroyImmediate(materialEditor); // Destroy after end of frame
            }
            materialsToResave.Clear();
            AssetDatabase.StopAssetEditing();
        }

        using (new EditorGUI.DisabledScope(!m_isPrefabValid))
        {
            if (GUILayout.Button("Convert"))
            {
                ConvertPrefab();
            }
        }
    }

    private bool ValidateProject()
    {
      
        if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset)
        {
            CoreEditorUtils.DrawFixMeBox("Invalid project settings:\n- Project must be using URP", EnableURP);
            return false;
        }

        return true;
    }

    private bool ValidateTarget(Object obj)
    {
        if (!obj)
        {
            return false;
        }
        string assetPath = AssetDatabase.GetAssetPath(obj);
        AssetImporter importer = AssetImporter.GetAtPath(assetPath);
        var vrmImporter = importer as UniVRM10.VrmScriptedImporter;
        return ValidateImporter(obj, vrmImporter);
    }

    private bool ValidateImporter(Object obj, VrmScriptedImporter importer)
    {
        if (importer == null)
        {
            CoreEditorUtils.DrawHeader("Not a VRM!");
            return false;
        }
        
        // Implicit: Render pipeline must be URP

        bool validMigrate = (obj is not DefaultAsset) || importer.MigrateToVrm1;
        bool validVrmPipeline = importer.RenderPipeline !=
                                ImporterRenderPipelineTypes.BuiltinRenderPipeline;
        bool validImportSettings = validMigrate && validVrmPipeline;
        if (!validImportSettings)
        {
            string msg = "Invalid VRM import settings:";
            if (!validMigrate)
            {
                msg += "\n- VRM must be migrated to VRM 1.0";
            }
            if (!validVrmPipeline)
            {
                msg += "\n- VRM must use URP materials";
            }

            CoreEditorUtils.DrawFixMeBox(msg,
                () => SetVRMImportSettings(importer));
            return false;
        }

        return true;
    }


    private void EnableURP()
    {
        string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        if (guids.Length == 0)
        {
            Debug.LogError("No URP assets found!");
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Debug.Log($"Choosing '{path}' as URP asset");

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        GraphicsSettings.defaultRenderPipeline = urp;
    }

    // Logic based on FilmGrainEditor.cs, haven't seen another good example...
    private void SetVRMImportSettings(VrmScriptedImporter importer)
    {
        var assetPath = importer.assetPath;
        importer.MigrateToVrm1 = true;
        importer.RenderPipeline = ImporterRenderPipelineTypes.UniversalRenderPipeline;
        EditorUtility.SetDirty(importer); // Okay, this is the crucial step that was missing
        importer.SaveAndReimport();
        AssetDatabase.Refresh(); // Shouldn't be required...
        // Restore the field after reimport
        m_sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
    }

    public void ConvertPrefab()
    {
        if (m_unityToonShader == null)
        {
            Debug.LogError("Unity toon shader not found!");
            return;
        }
        
        // If the user passed in a VRM0 asset, unconverted:
        // m_sourcePrefab is UnityEngine.DefaultAsset
        // VRM0 asset converted, or VRM1:
        // m_sourcePrefab is VRM

        string assetPath = AssetDatabase.GetAssetPath(m_sourcePrefab);
        Debug.Log($"Object '{m_sourcePrefab.name}' has path '{assetPath}'");

        // We've ensured that the asset has been reimported as a VRM10 prefab with URP shaders,
        // now grab it.
        var gameObject = m_sourcePrefab as GameObject;

        if (gameObject == null)
        {
            Debug.LogError($"'{m_sourcePrefab.name}' is not a GameObject!");
            return;
        }
        
        if (PrefabUtility.GetPrefabAssetType(gameObject) == PrefabAssetType.NotAPrefab)
        {
            Debug.LogError($"'{m_sourcePrefab.name}' is not a prefab!");
            return;
        }
        
        // Create required paths and directories
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
        Debug.Log($"Prefab path: '{prefabPath}'");

        string prefabDir = Path.GetDirectoryName(prefabPath);
        string prefabName = $"{Path.GetFileNameWithoutExtension(prefabPath)}_UnityToon";
        
        string newPrefabPath =
            $"{prefabDir}\\{prefabName}.prefab";
        Debug.Log($"Converted prefab path: '{newPrefabPath}'");
        
        string parentDir = Path.GetDirectoryName(assetPath);
        string matDirName = $"{prefabName}.Materials";
        string matDirPath = $"{parentDir}\\{matDirName}";
        
        if (!AssetDatabase.IsValidFolder(matDirPath))
        {
            AssetDatabase.CreateFolder(parentDir, matDirName);
        }
        
        // Create prefab instance
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        var prefabInstance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        prefabInstance.name += "_UnityToon";

        EditorCoroutineUtility.StartCoroutine( ConvertMaterialsCoroutine(prefabInstance, newPrefabPath, matDirPath), this );
    }

    // Async not well supported in 2022.3, so let's use Editor Coroutines
    // Maybe need to look into UniTask or whatever it was called
    public IEnumerator ConvertMaterialsCoroutine(GameObject prefabInstance, string newPrefabPath, string matDirPath)
    {
        m_convertedMats = new();
        var renderers = prefabInstance.GetComponentsInChildren<Renderer>();
        
        int totalRenderers = renderers.Length;
        int currentIdx = 0;
        
        int id = Progress.Start("Converting Materials...");
        
        foreach (var renderer in renderers)
        {
            Progress.Report(id, currentIdx, totalRenderers);
            currentIdx++;
            Material[] sharedMaterials = renderer.sharedMaterials;
            for (int i = 0; i < sharedMaterials.Length; ++i)
            {
                sharedMaterials[i] = ConvertMaterial(matDirPath, sharedMaterials[i]);
                materialsToResave.Add(sharedMaterials[i]);
            }
            renderer.sharedMaterials = sharedMaterials;
        }

        Focus();
        yield return null;
        
        // wait a frame
        PrefabUtility.SaveAsPrefabAssetAndConnect(prefabInstance, newPrefabPath, InteractionMode.UserAction);
        AssetDatabase.SaveAssets();
        Progress.Remove(id);
    }


    // Parse the properties we know about
    // TODO register in a map instead
    public MaterialData ParseMaterial(Material mat)
    {
        if (mat.shader.name == "VRM10/MToon10" || mat.shader.name == "VRM10/Universal Render Pipeline/MToon10")
        {
            return ParseMaterialMToon10(mat);
        }

        Debug.LogError($"No handler for material shader type: {mat.shader.name}");
        return new MaterialData();
    }

    public MaterialData ParseMaterialMToon10(Material mat)
    {
        var data = new MaterialData();
        
        // TODO why does MToon have reflection over all the shader properties, but still end up accessing them by string name?
        
        // Some primary properties:
        // MToon10Prop.AlphaMode, enum matches standard values
        data.renderMode = (VRM10.MToon10.MToon10AlphaMode)mat.GetFloat(VRM10Props.AlphaMode) switch
        {
            MToon10AlphaMode.Opaque => MaterialData.RenderMode.Opaque,
            MToon10AlphaMode.Cutout => MaterialData.RenderMode.Cutout,
            MToon10AlphaMode.Transparent => MaterialData.RenderMode.Transparent,
            _ => MaterialData.RenderMode.Opaque
        };
        
        // MToon10Prop.TransparentWithZWrite
        // NYI
        
        // MToon10Prop.RenderQueueOffsetNumber
        data.renderQueueOffset = mat.GetInt(VRM10Props.RenderQueueOffsetNumber);
        
        // MToon10Prop.DoubleSided
        data.cullMode = (MToon10DoubleSidedMode)mat.GetFloat(VRM10Props.DoubleSided) switch
        {
            MToon10DoubleSidedMode.Off => MaterialData.CullMode.Back,
            MToon10DoubleSidedMode.On => MaterialData.CullMode.Off,
            _ => MaterialData.CullMode.Back
        };

        // MToon10Prop.AlphaCutoff
        data.cutoff = mat.GetFloat(VRM10Props.AlphaCutoff);
        
        // MToon10Prop.BaseColorTexture
        data.mainTex = mat.GetTexture(VRM10Props.BaseColorTexture);
        
        // MToon10Prop.BaseColorFactor
        data.color = mat.GetColor(VRM10Props.BaseColorFactor);
        
        // MToon10Prop.ShadeColorTexture
        // MToon10Prop.ShadeColorFactor
        // MToon10Prop.NormalTexture
        // MToon10Prop.NormalTextureScale
        // MToon10Prop.ShadingToonyFactor
        // MToon10Prop.ShadingShiftFactor
        // MToon10Prop.ShadingShiftTexture
        // MToon10Prop.ShadingShiftTextureScale
        // MToon10Prop.GiEqualizationFactor
        // MToon10Prop.EmissiveTexture
        // MToon10Prop.EmissiveFactor
        // MToon10Prop.RimMultiplyTexture
        // MToon10Prop.RimLightingMixFactor
        // MToon10Prop.MatcapTexture
        // MToon10Prop.MatcapColorFactor
        // MToon10Prop.ParametricRimColorFactor
        // MToon10Prop.ParametricRimFresnelPowerFactor
        // MToon10Prop.ParametricRimLiftFactor
        
        // MToon10Prop.OutlineWidthMode
        data.outline = (MToon0XOutlineWidthMode)mat.GetFloat(VRM10Props.OutlineWidthMode) switch
        {
            MToon0XOutlineWidthMode.None => MaterialData.OutlineMode.Off,
            MToon0XOutlineWidthMode.ScreenCoordinates => MaterialData.OutlineMode.ScreenSpace,
            MToon0XOutlineWidthMode.WorldCoordinates => MaterialData.OutlineMode.WorldSpace
        };
            
        // MToon10Prop.OutlineWidthMultiplyTexture
        // MToon10Prop.OutlineWidthFactor
        data.outlineWidth = mat.GetFloat(VRM10Props.OutlineWidthFactor);
        // MToon10Prop.OutlineColorFactor
        data.outlineColor = mat.GetColor(VRM10Props.OutlineColorFactor);
        // MToon10Prop.OutlineLightingMixFactor
        
        // MToon10Prop.UvAnimationMaskTexture
        // MToon10Prop.UvAnimationScrollXSpeedFactor
        // MToon10Prop.UvAnimationScrollYSpeedFactor
        // MToon10Prop.UvAnimationRotationSpeedFactor
        
        // class MToonValidator used to validate?
        // Also doesn't implement standard ValidateMaterial...but we know
        // MToonValidator.Validate is how the validation happens, and
        // MToonInspector.OnGUI is where the primary properties are edited.
        
        return data;
    }
    
    public Material CreateUnityToon(MaterialData data)
    {
        var mat = new Material(m_unityToonShader);
        
        // Here we need to set a minimal subset of properties, if we pick the right ones then the
        // shader gui will do the rest, after we force it to run in our OnGUI
        
        // Let's start with the simplest thing
        mat.SetTexture(UnityToonProps.MainTex, data.mainTex);
        mat.SetTextureOffset(UnityToonProps.MainTex, data.mainTexOffset);
        // struct can't have default ctor, have to deal with this somewhere
        if (data.mainTexScale.x == 0) { data.mainTexScale.x = 1; }
        if (data.mainTexScale.y == 0) { data.mainTexScale.y = 1; }
        mat.SetTextureScale(UnityToonProps.MainTex, data.mainTexScale);

        // TODO: check this prop has same effects, there are multiple in Unity Toon with similar names
        mat.SetColor(UnityToonProps.Color, data.color);

        // Needed to for tex to be used for all 3 shade levels
        mat.SetFloat(UnityToonProps.UseBaseAs1st, 1);
        mat.SetFloat(UnityToonProps.Use1stAs2nd, 1);

        // Opaque, Cutout, Transparent
        // UTS3GUI is internal to the Unity Toon Shader editor assembly, and I'm already being nice trying to use their
        // enums, I'm not going to use an assembly reference to inject myself into their namespace and extract the data
        // etc.
        
        // Corresponds to internal enum: UnityEditor.Rendering.Toon.UTS3GUI.UTS_ClippingMode
        // Off: no clipping or transparency
        // On: use a separate clipping mask for transparency
        // TransClippingMode: Alpha clip if opaque, alpha blend if transparent
        mat.SetFloat(UnityToonProps.ClippingMode, data.renderMode switch
        {
            MaterialData.RenderMode.Opaque => 0,
            MaterialData.RenderMode.Cutout => 2,
            MaterialData.RenderMode.Transparent => 2,
            _ => 0
        });
        
        // May be a legacy property?
        mat.SetFloat(UnityToonProps.TransparentEnabled, data.renderMode switch
        {
            MaterialData.RenderMode.Opaque => 0,
            MaterialData.RenderMode.Cutout => 0,
            MaterialData.RenderMode.Transparent => 1,
            _ => 0
        });
        
        // Auto render queue on; set based on Cutout/Transparent/Opaque mode
        // We have to convert a render queue offset to a specified render queue value
        mat.SetFloat(UnityToonProps.AutoRenderQueue, (data.renderQueueOffset == 0) ? 1 : 0);
        mat.renderQueue = (int)(data.renderMode switch
        {
            MaterialData.RenderMode.Opaque => RenderQueue.Geometry,
            MaterialData.RenderMode.Cutout => RenderQueue.AlphaTest,
            MaterialData.RenderMode.Transparent => RenderQueue.Transparent,
            _ => RenderQueue.Geometry
        }) + data.renderQueueOffset;
        
        // Cutoff for alpha clip; < vs <= discrepancy
        mat.SetFloat(UnityToonProps.ClippingLevel, Mathf.Max(data.cutoff - 0.001f, 0));
        
        // In our case this should always default to 1?
        mat.SetFloat(UnityToonProps.IsBaseMapAlphaAsClippingMask, 1);
        
        // Corresponds to internal enum: UnityEditor.Rendering.Toon.UTS3GUI.CullingMode
        mat.SetFloat(UnityToonProps.CullMode, data.cullMode switch
        {
            MaterialData.CullMode.Off => 0,
            MaterialData.CullMode.Front => 1,
            MaterialData.CullMode.Back => 2,
            _ => 2
        });

        mat.SetShaderPassEnabled("SRPDefaultUnlit", data.outline > 0);
        
        // Outline width: for world space, convert mm => m
        mat.SetFloat(UnityToonProps.OutlineWidth, data.outlineWidth * 1000);
        
        mat.SetColor(UnityToonProps.OutlineColor, data.outlineColor);
        
        return mat;
    }

    public Material ConvertMaterial(string destDirPath, Material mat)
    {
        string oldPath = AssetDatabase.GetAssetPath(mat);
        string key = $"{oldPath}:{mat.name}";
        
        if (m_convertedMats.TryGetValue(key, out Material newValue))
        {
            return newValue;
        }

        // Extract known properties from known shaders
        MaterialData materialData = ParseMaterial(mat);
        
        var newMat = CreateUnityToon( materialData );
        
        AssetDatabase.CreateAsset(
            newMat,
            $"{destDirPath}\\{mat.name}_UnityToon.mat");
        
        m_convertedMats[key] = newMat;
        
        return newMat;
    }
}
