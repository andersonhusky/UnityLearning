using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public class LayerRenderingConfiguration : IMapLayeringConfiguration
{
    public static class Layer
    {
        //Use nds level value as RenderingLayerMaskFactor
        //NDS Levels provided by MXNavi
        //Used for multi-layer data rendering with clear tile
        public const uint k_13 = (uint)1 << 13;
        public const uint k_11 = (uint)1 << 11;
        public const uint k_9 = (uint)1 << 9;
        public const uint k_7 = (uint)1 << 7;
        public const uint k_6 = (uint)1 << 6;
        public const uint k_3 = (uint)1 << 3;
        public const uint k_Vehicle_Shadow = (uint)1 << 2;
        public const uint k_Vehicle = (uint)1 << 1;
        public const uint k_Default = 1;
        public const uint k_LLNTransparent = (uint)1 << 30;
        public const uint k_RecommendText = (uint)1 << 14;
        public const uint k_ManeuverArrow = (uint)1 << 15;
        public const uint k_NoSSPR = (uint)1 << 29;

        //View mask maybe for future use
        public const uint k_HU = 1;
        public const uint k_IC = (uint)1 << 1;
        public const uint k_HUJV = (uint)1 << 2;
        public const uint k_HUMiniMap = (uint)1 << 3;
        public const uint k_ICJV = (uint)1 << 4;
        public const uint k_HUD = (uint)1 << 5;
        public const uint k_ViewAll = k_HU | k_IC | k_HUJV | k_HUMiniMap | k_ICJV | k_HUD;

        //Because of requirements of multi-layer data rednering, now roads and clear tile use this as its RenderingLayerMask
        public const uint k_3to13Level = k_3 | k_6 | k_7 | k_9 | k_11 | k_13;

        public static uint GetRenderingLayerMask(int dataLevel)
        {
            return (uint)1 << (dataLevel);
        }

        // Scales vs Nds Levels
        public static readonly int[] NdsScales = new int[] { 499, 20000, 80000, 210000, 1280000, 5120000, 20480000, 50480000, 163840000 };

        public static readonly int[] NdsLevels = new[]
        {
            /*15, */13, 11, 9, 7, 6, 3, 2, 1
        };

        //When zoomlevel/scale is changed, update level related RenderingLayerMasks for MapLayeringManager
        // At most three levels of data are rendered - Upper Level -> Current Level -> Lower Level
        //Currently only roads and clear tile have multiple levels
        public static void SetScale(int scale, int viewID)
        {
            int levelIndex = 0;

            for (int i = 0; i < 8; i++)
            {
                if (scale > NdsScales[i] && scale <= NdsScales[i + 1])
                {
                    levelIndex = i;
                    break;
                }
            }

            int ndsLevel = NdsLevels[levelIndex];
            int upperLevel = ndsLevel >= 13 ? -1 : 1 << NdsLevels[levelIndex - 1];
            int currentLevel = 1 << ndsLevel;
            int lowerLevel = ndsLevel <= 3 ? -1 : 1 << NdsLevels[levelIndex + 1];

            var levelInfo = MapLayeringManager.GetLevelsInfoByViewID(viewID);

            var levels = levelInfo.levels;
            if (levels.Count != 3)
            {
                levels.Clear();
                levels.Add(upperLevel);
                levels.Add(currentLevel);
                levels.Add(lowerLevel);
            }
            else
            {
                levels[0] = upperLevel;
                levels[1] = currentLevel;
                levels[2] = lowerLevel;
            }

            levelInfo.levelAll = currentLevel;
            if (upperLevel > 0)
                levelInfo.levelAll |= upperLevel;
            if (lowerLevel > 0)
                levelInfo.levelAll |= lowerLevel;
        }
    }

    [AutoGenToString]
    public enum MeshPart
    {
        Frame = 1 << 0,
        Body = 1 << 1,
    }
    private List<LayeringInfo> transparentLayeringInfos = new List<LayeringInfo>();
    Array meshParts = Enum.GetValues(typeof(MeshPart));

    #region 关键字和变量
    private ScreenInfo screenInfo = ScreenInfo.HU;
    private static readonly string SG_QueueControl = "_QueueControl";
    private int opaqueRenderQueue;
    private int transparentRenderQueue;
    private int opaqueElementIndexAbove = -1;
    #endregion

    #region 测试用
    public static bool isDebug = false;
    public static bool isLogRenderBlock = false;
    private LayeringController layeringController;
    private List<RendererBlock> rendererSequence = new List<RendererBlock>();
    #endregion

    public LayerRenderingConfiguration(LayeringController layerCtrl)
    {
        layeringController = layerCtrl;
        InitConfig();
    }

    public LayerRenderingConfiguration()
    {
        InitConfig();
    }

    private void InitConfig()
    {
        if(LayeringInfos == null)
        {
            LayeringInfos = new List<LayeringInfo>();
        }

        LayeringInfos.Clear();
        transparentLayeringInfos.Clear();
        
        opaqueRenderQueue = 100; //Start queue number can be any number that make sense
        transparentRenderQueue = 2500;
        SetUpSharedInfos();

        SetUpClearTile();
        SetUp2DObjects();
        SetUp3DObjects();
        SetUpAreas();
    }

    private void SetUpSharedInfos()
    {
        meshParts = Enum.GetValues(typeof(MeshPart));
    }

    private void SetUpClearTile()
    {
        int clearQueue = opaqueRenderQueue++;

        AddToList(new LayeringInfo()
        {
            GeoType = GeometryType.Grounded,
            Output = OutputType.StencilOnlyMask,
            RenderQueue = clearQueue,
            RenderPass = LayerPassType.Forward,
            Mode = MapMode.MapAll,
            RenderingLayerMask = Layer.k_3to13Level,
            OpaqueIndexAbove = opaqueElementIndexAbove
        });

        TestSettingToRenderBlock(LayerPassType.PrePass, clearQueue, 1, LayeringInfos.Last());
    }

    private void SetUp2DObjects()
    {
        SetUp2DTransparent();
    }

    private void SetUp2DTransparent()
    {
        for(int i = 0; i < layeringController.test2DTransparentObjectMaterials.Length; ++i)
        {
            int objectQueue = transparentRenderQueue--;
            SetQueue(layeringController.test2DTransparentObjectMaterials, i, objectQueue);
            // AddToList(new LayeringInfo()
            // {
            //     GeoType = GeometryType.Arbitrary,
            //     Output = OutputType.CommonTransparent,
            //     RenderQueue = objectQueue,
            //     RenderPass = LayerPassType.Forward,
            //     Mode = MapMode.Map3D,
            //     RenderingLayerMask = Layer.k_Default,
            //     OpaqueIndexAbove = opaqueElementIndexAbove,
            // });
        }
        TestSettingToRenderBlock(LayerPassType.PrePass, transparentRenderQueue + 1, 1, LayeringInfos.Last());
    }

    private void SetUp3DObjects()
    {
        SetUp3DOpaque();
    }

    private void SetUp3DOpaque()
    {
        opaqueRenderQueue = 2000;
        for(int i = 0; i < layeringController.test3DOpaqueObjectMaterials.Length; ++i)
        {
            int objectQueue = opaqueRenderQueue++;
            SetQueue(layeringController.test3DOpaqueObjectMaterials, i, objectQueue);
            AddToList(new LayeringInfo()
            {
                GeoType = GeometryType.AboveGround,
                Output = OutputType.CommonOpaque,
                RenderQueue = objectQueue,
                RenderPass = LayerPassType.All,
                RenderingLayerMask = Layer.k_Default,
                OpaqueIndexAbove = opaqueElementIndexAbove,
            });
        }
        TestSettingToRenderBlock(LayerPassType.PrePass, opaqueRenderQueue - 1, 1, LayeringInfos.Last());
    }

    private void SetUpAreas()
    {
        for(int i = 0; i < layeringController.testPlaneMaterials.Length; ++i)
        {
            int polygonQueue = opaqueRenderQueue++;
            SetQueue(layeringController.testPlaneMaterials, i, polygonQueue);
            AddToList(new LayeringInfo()
            {
                GeoType = GeometryType.Grounded,
                Output = OutputType.CommonOpaque,
                RenderQueue = polygonQueue,
                RenderPass = LayerPassType.Forward,
                Mode = MapMode.Map2D,
                RenderingLayerMask = Layer.k_Default,
                OpaqueIndexAbove = opaqueElementIndexAbove
            });
        }
        TestSettingToRenderBlock(LayerPassType.PrePass, opaqueRenderQueue - 1, 1, LayeringInfos.Last());
    }

    private static bool SetQueue(Material[] materials, int index, int queue, string log = "")
    {
        if(materials is null || materials.Length <= index || index < 0)
        {
            Debug.LogError($"Cannot find material index {index}! " + log);
            return false;
        }

        if(materials[index] is null)
        {
            Debug.LogError($"Cannot find material for queue {queue} in unity map editor! " + log);
            return false;
        }

        if(isDebug)
        {
            Debug.Log($"{queue}, {materials[index].name} {log}");
        }
        if(materials[index].HasProperty(SG_QueueControl))
        {
            materials[index].SetFloat(SG_QueueControl, 1);
        }
        materials[index].renderQueue = queue;
        return true;
    }

    private void AddToList(LayeringInfo info)
    {
        var output = info.Output;
        bool isTransparent = CheckTransparent(output);
        info.OpaqueIndexAbove = opaqueElementIndexAbove;

        LayeringInfos.Add(info);
        if(!isTransparent)
        {
            opaqueElementIndexAbove = LayeringInfos.Count -1;
        }
    }

    private bool CheckTransparent(OutputType output)
    {
        return output == OutputType.CommonTransparent
            || output == OutputType.CommonDefault
            || output == OutputType.OverlayDefault
            || output == OutputType.OverlayTransparent
            || output == OutputType.Custom;
    }

    private void TestSettingToRenderBlock(LayerPassType excludePass, int queue, int stencilRef, LayeringInfo layeringInfo)
    {
        if(!isLogRenderBlock)    return;

        MapLayeringManager.LayerPassInfoPair layerPassInfoPair = new MapLayeringManager.LayerPassInfoPair
        {
            excludePass = excludePass,
            currentMinQueue = queue,
            currentMaxQueue = queue,
            currentStencilRef = stencilRef,
            IsLogBlock = true
        };
        MapLayeringManager.TestCreateRenderBlock(ref rendererSequence, layeringInfo, ref layerPassInfoPair);
    }
}