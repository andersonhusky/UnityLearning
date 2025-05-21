using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal.LibTessDotNet;

public partial class MapLayeringManager
{
    public struct LayerPassInfoPair
    {
        public LayerPassType excludePass;   // 用exclude主要是为了All类型同时参与两个SetUpSequence
        public int viewID;
        public int currentMinQueue;
        public int currentMaxQueue;
        public int currentStencilRef;

        public int index;
        public LayeringInfo currentLayerInfo;
        public LayeringInfo previousLayerInfo;
        public bool IsLogBlock;

        public bool IsPrevExcludePass()
        {
            return previousLayerInfo.RenderPass == excludePass;
        }

        public bool IsCurExcludePass()
        {
            return currentLayerInfo.RenderPass == excludePass;
        }

        public bool DetermineStencilIncrement()
        {
            bool condition1 = IsOpaque(previousLayerInfo) && !IsOpaque(currentLayerInfo);       // opaque切换到tran
            bool condition2 = IsOpaque(previousLayerInfo) && Is3D(previousLayerInfo) && !Is3D(currentLayerInfo);     // 3D切换到2D
            bool condition3 = Is3D(currentLayerInfo) && currentLayerInfo.Output == OutputType.OverlayOpaque && Is3D(previousLayerInfo);    // 3D下碰到overlayOpaque
            bool condition4 = previousLayerInfo.Output == OutputType.StencilOnlyMask || currentLayerInfo.Output == OutputType.DepthOnlyMask;    // mask

            return condition1 || condition2 || condition3 || condition4;
        }

        public void IncreaseStencil()
        {
            currentStencilRef++;
        }

        public void ClearRenderQueue()
        {
            currentMinQueue = currentLayerInfo.RenderQueue;
            currentMaxQueue = currentLayerInfo.RenderQueue;
        }

        public bool ShouldCreateNewRenderBlock()
        {
            bool renderStateChange = previousLayerInfo.Output != currentLayerInfo.Output
                                    || previousLayerInfo.GeoType != currentLayerInfo.GeoType;
            bool renderingLayerMaskChange = previousLayerInfo.RenderingLayerMask != currentLayerInfo.RenderingLayerMask;
            bool isExcludePass = currentLayerInfo.RenderPass == excludePass;
            return renderStateChange || isExcludePass || renderingLayerMaskChange;
        }

        public void UpdateQueueRange()
        {
            if (IsPrevExcludePass())
            {
                ClearRenderQueue();
            }
            else if (IsOpaque(currentLayerInfo))
            {
                // 对于不透明物体，取最小的RenderQueue（最先渲染）
                currentMinQueue = Mathf.Min(currentMinQueue, currentLayerInfo.RenderQueue);
            }
            else
            {
                // 对于透明物体，取最大的RenderQueue（最后渲染）
                currentMaxQueue = Mathf.Max(currentMaxQueue, currentLayerInfo.RenderQueue);
            }
            // 和上面的else if + else其实是等价的
            // else
            // {
            //     currentMinQueue = Mathf.Min(currentMinQueue, currentLayerInfo.RenderQueue);
            //     currentMaxQueue = Mathf.Max(currentMaxQueue, currentLayerInfo.RenderQueue);
            // }
        }
    }
    public struct RendererBlockParam
    {
        public bool isOpaque;
        public bool needDepthTest;
        public bool needDepthWrite;
        public bool hasDepthConflict;
        public bool needStencilTest;
        public bool needStencilWrite;
        public bool is3D;
        public int destRenderingLayerMask;
        public bool noSSPR;

        public RendererBlockParam(LayeringInfo info)
        {
            GeometryType geoType = info.GeoType;
            OutputType output = info.Output;
            int opaqueIndexAbove = info.OpaqueIndexAbove;

            isOpaque = IsOpaque(info);
            needDepthTest = IsNeedDepthTest(output, geoType);
            needDepthWrite = IsNeedDepthWrite(output);
            //When elements can not use depth test and covered by opaque elements, they need stencil test
            hasDepthConflict = true;//!(geoType == GeometryType.AboveGround);
            needStencilTest = IsNeedStencilTest(needDepthTest, hasDepthConflict, isOpaque, output, geoType, opaqueIndexAbove);
            needStencilWrite = IsNeedStencilWrite(isOpaque, output);
            is3D = Is3D(info);
            destRenderingLayerMask = (int)info.RenderingLayerMask;
            noSSPR = IsNoSSPR(ref destRenderingLayerMask);
        }
    }
    private static IMapLayeringConfiguration _layeringConfig;
    public static MapMode MapViewMode;

    public static List<RendererBlock> _forwardRendererSequence = new List<RendererBlock>();
    public static List<RendererBlock> _forwardRendererTransparentSequence = new List<RendererBlock>();
    public static List<RendererBlock> _prepassRendererSequence = new List<RendererBlock>();

    // levels存三个数，高一级别level，当前level，低一级别level，不存在时为-1
    // levelAll用位存这三个数（有效时），1<<高一级别level，1 << 当前level，1 << 低一级别level
    public class LevelInfo
    {
        public List<int> levels = new List<int>();
        public int levelAll;
    }

    private static Dictionary<int, LevelInfo> m_levelInfos = new Dictionary<int, LevelInfo>();
    public static LevelInfo GetLevelsInfoByViewID(int viewID)
    {
        if(!m_levelInfos.ContainsKey(viewID))
        {
            m_levelInfos[viewID] = new LevelInfo();
        }
        return m_levelInfos[viewID];
    }

    //To rendering three levels of data, the stencil value are rearranged now
    //First three bits of stencil value are used to record area of three levels of clear tile
    //And only data with multiple levels(currently roads only) will do stencil test against the first three bits of stencil buffer
    public static readonly byte[] k_levelMasks = new byte[]
    {
        1 << 7, 1 << 6, 1 << 5
    };

    //Special stencil references
    private const byte k_levelMaskAll = (1 << 7) | (1 << 6) | (1 << 5); //Only used in forward pass
    private const byte k_noLevelMask = 31;
    private const byte k_noSSPR_StencilMask = 1 << 7;// Only used in prepass

    //Special rendering layer masks
    private const uint k_NoSSPR_RenderingLayerMask = (uint)1 << 29;
    private const uint k_LLNTransparent = (uint)1 << 30;

    private const int buildingStencilReference = k_levelMaskAll | 31;
    private static RenderStateBlock GroundMaskStateBlock()
    {
        RenderStateBlock groundMaskStateBlock = new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil)
        {
            // blend
            blendState = BlendStateTransparent,
            // depth
            depthState = new DepthState()
            {
                compareFunction = CompareFunction.Always,
                writeEnabled = false
            },
            // stencil
            stencilReference = buildingStencilReference,
            stencilState = new StencilState()
            {
                readMask = k_noLevelMask,
                writeMask = 255,
                compareFunctionFront = CompareFunction.NotEqual,
                compareFunctionBack = CompareFunction.NotEqual,
                passOperationBack = StencilOp.Keep,
                passOperationFront = StencilOp.Keep,
                enabled = true
            }
        };
        groundMaskStateBlock.mask = RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil;
        return groundMaskStateBlock;
    }

    private static RenderStateBlock GroundDepthOverwrite()
    {
        RenderStateBlock GroundDepthOverwrite =
            new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil)
            {
                // blend
                blendState = BlendStateOpaqueNoColor,
                // depth
                depthState = new DepthState()
                {
                    compareFunction = CompareFunction.Always,
                    writeEnabled = true
                },
                // stencil
                stencilReference = buildingStencilReference,
                stencilState = new StencilState()
                {
                    readMask = k_noLevelMask,
                    writeMask = 255,
                    compareFunctionFront = CompareFunction.NotEqual,
                    compareFunctionBack = CompareFunction.NotEqual,
                    passOperationBack = StencilOp.Keep,
                    passOperationFront = StencilOp.Keep,
                    enabled = true
                }
            };
        GroundDepthOverwrite.mask = RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil;
        return GroundDepthOverwrite;
    }

    public static RenderStateBlock LLNTransparentStateBlock(bool enableDepthWrite)
    {
        RenderStateBlock block = new RenderStateBlock(RenderStateMask.Blend | RenderStateMask.Depth)
        {
            // blend
            blendState = BlendStateTransparent,
            // depth
            depthState = new DepthState()
            {
                compareFunction = CompareFunction.LessEqual,
                writeEnabled = enableDepthWrite
            },
            mask = RenderStateMask.Blend | RenderStateMask.Depth
        };
        return block;
    }

    public static bool inited = false;

    public static bool LayerSetCompleted()
    {
        return inited;
    }

    public static void SetLayeringConfig(IMapLayeringConfiguration layeringConfiguration)
    {
        _layeringConfig = layeringConfiguration;
    }

    public static void SetUpRenderBlocks(int viewID)
    {
        if (_layeringConfig == null)
            return;

        _forwardRendererTransparentSequence.Clear();
        // SetUpSequenceNew(LayerPassType.PrePass, ref _forwardRendererSequence, viewID);
        // SetUpSequenceNew(LayerPassType.Forward, ref _prepassRendererSequence, viewID);

        SetUpSequence(LayerPassType.PrePass, ref _forwardRendererSequence, viewID);
        SetUpSequence(LayerPassType.Forward, ref _prepassRendererSequence, viewID);
    }

    private static int[] underIndices = new[] { -1, -1, -1 };
    // Ground Rules:
    // 1. The depth test function of opaque 3D objects has to be LessEqual
    // 2. The stencil comparision function of 3D objects has to be GreaterEqual
    //
    public static void SetUpSequenceNew(LayerPassType excludePass, ref List<RendererBlock> destSequence, int viewID)
    {
        destSequence.Clear();

        if (_layeringConfig.LayeringInfos.Count == 0) return;

        // 初始化状态变量
        InitializeStateVariables(excludePass, viewID, out LayerPassInfoPair layerPassInfoPair);

        // 从后向前遍历层级信息
        for (int i = _layeringConfig.LayeringInfos.Count - 2; i >= 0; --i)
        {
            layerPassInfoPair.index = i + 1;
            layerPassInfoPair.currentLayerInfo = _layeringConfig.LayeringInfos[i];
            layerPassInfoPair.previousLayerInfo = _layeringConfig.LayeringInfos[i + 1];

            // 需要增加新的RenderBlock
            if (layerPassInfoPair.ShouldCreateNewRenderBlock())
            {
                ProcessRenderBlockCreation(ref destSequence, ref layerPassInfoPair);
            }
            else
            {
                layerPassInfoPair.UpdateQueueRange();
            }

            // 处理第一个层级的特殊情况
            if (i == 0)
            {
                layerPassInfoPair.index = 0;
                ProcessFirstLayerSpecialCase(ref destSequence, ref layerPassInfoPair);
            }
        }
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-20 14:59:19
    /// 初始化状态变量
    /// <summary>
    private static void InitializeStateVariables(LayerPassType excludePass, int viewID, out LayerPassInfoPair layerPassInfoPair)
    {
        var lastLayer = _layeringConfig.LayeringInfos.Last();
        layerPassInfoPair = new LayerPassInfoPair();
        layerPassInfoPair.excludePass = excludePass;
        layerPassInfoPair.viewID = viewID;
        layerPassInfoPair.currentMinQueue = lastLayer.RenderQueue;
        layerPassInfoPair.currentMaxQueue = lastLayer.RenderQueue;
        layerPassInfoPair.currentStencilRef = 1;
        layerPassInfoPair.IsLogBlock = true;
        RestIndices();
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-20 15:00:51
    /// 处理渲染块创建逻辑
    /// <summary>
    private static void ProcessRenderBlockCreation(ref List<RendererBlock> destSequence, ref LayerPassInfoPair layerPassInfoPair)
    {
        if (!layerPassInfoPair.IsPrevExcludePass())
        {
            AddRendererBlockNew(ref destSequence, layerPassInfoPair.previousLayerInfo, ref layerPassInfoPair);

            if (layerPassInfoPair.DetermineStencilIncrement())
            {
                layerPassInfoPair.IncreaseStencil();
            }
        }

        layerPassInfoPair.ClearRenderQueue();
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-20 15:01:15
    /// 处理第一个层级的特殊情况
    /// <summary>
    private static void ProcessFirstLayerSpecialCase(ref List<RendererBlock> destSequence, ref LayerPassInfoPair layerPassInfoPair)
    {
        if (!layerPassInfoPair.IsCurExcludePass())
        {
            AddRendererBlockNew(ref destSequence, layerPassInfoPair.currentLayerInfo, ref layerPassInfoPair);
        }
    }

    private static void AddRendererBlockNew(ref List<RendererBlock> rendererSequence, LayeringInfo info, ref LayerPassInfoPair layerPassInfoPair)
    {
        RendererBlockParam rendererBlockParam = new RendererBlockParam(info);

        //special case when rendering layer mask is k_LLNTransparent
        if ((rendererBlockParam.destRenderingLayerMask & k_LLNTransparent) != 0)
        {
            CreateLLNTransparentStateBlock(ref rendererSequence, ref layerPassInfoPair, rendererBlockParam);
        }
        else
        {
            OutputType output = info.Output;
            CreateNormalStateBlock(ref rendererSequence, output,
                                    ref layerPassInfoPair, rendererBlockParam);
        }

        GeometryType geoType = info.GeoType;
        if (rendererBlockParam.needDepthWrite && geoType != GeometryType.None)
        {
            underIndices[(int)geoType] = layerPassInfoPair.index;
        }
    }

    private static void CreateLLNTransparentStateBlock(ref List<RendererBlock> rendererSequence,
                                                        ref LayerPassInfoPair layerPassInfoPair,
                                                        RendererBlockParam rendererBlockParam)
    {
        //Draw LLN Roads who's rendering layer is k_LLNTransparent twice
        //First pass draw it with depth write disabled
        //Second pass draw it with depth write enabled
        //note rendererSequence is reverted to actual execute order
        if (layerPassInfoPair.excludePass == LayerPassType.PrePass)
        {
            for (int i = 0; i < 2; i++)
            {
                RenderStateBlock renderStateBlock = LLNTransparentStateBlock(i == 1);
                RendererBlock rendererBlock = new RendererBlock()
                {
                    minQueue = layerPassInfoPair.currentMinQueue,
                    maxQueue = layerPassInfoPair.currentMaxQueue,
                    RenderStateBlock = renderStateBlock,
                    RenderingLayerMask = (uint)rendererBlockParam.destRenderingLayerMask
                };
                _forwardRendererTransparentSequence.Add(rendererBlock);
            }
        }
        else
        {
            var renderStateBlock = new RenderStateBlock();
            var depthState = DepthOpaque;
            var stencilState = new StencilState();
            var blendState = BlendStateOpaque;
            stencilState.readMask = k_noLevelMask;
            stencilState.writeMask = k_noLevelMask;
            stencilState.compareFunctionFront = stencilState.compareFunctionBack = CompareFunction.GreaterEqual;
            stencilState.enabled = true;
            stencilState.passOperationFront = stencilState.passOperationBack = StencilOp.Replace;
            stencilState.failOperationFront = stencilState.failOperationBack = StencilOp.Keep;
            renderStateBlock.blendState = blendState;
            renderStateBlock.depthState = depthState;
            int destStencilRef = layerPassInfoPair.currentStencilRef;

            //Process stencil state for non sspr objects
            stencilState.readMask = 127;
            stencilState.writeMask = 255;
            if (rendererBlockParam.noSSPR)
                destStencilRef |= k_noSSPR_StencilMask;

            renderStateBlock.stencilState = stencilState;
            renderStateBlock.stencilReference = destStencilRef;
            renderStateBlock.mask = RenderStateMask.Blend | RenderStateMask.Stencil | RenderStateMask.Depth;

            RendererBlock rendererBlock = new RendererBlock()
            {
                minQueue = layerPassInfoPair.currentMinQueue,
                maxQueue = layerPassInfoPair.currentMaxQueue,
                RenderStateBlock = renderStateBlock,
                RenderingLayerMask = (uint)rendererBlockParam.destRenderingLayerMask
            };

            rendererSequence.Add(rendererBlock);
        }
    }

    private static void CreateNormalStateBlock(ref List<RendererBlock> rendererSequence, OutputType output,
                                                ref LayerPassInfoPair layerPassInfoPair, RendererBlockParam rendererBlockParam)
    {
        LevelInfo levelInfo = GetLevelsInfoByViewID(layerPassInfoPair.viewID);      // minimap使用levelInfo[1]，其他levelInfo[0]
        int levelCount = (rendererBlockParam.destRenderingLayerMask & levelInfo.levelAll) == 0 ? 1 : 3;

        //Once we have multi-level data, need different rendering layer mask and stencil value for each level
        for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
        {
            if (levelCount != 1)
            {
                rendererBlockParam.destRenderingLayerMask = levelInfo.levels[levelIndex];
            }

            if (rendererBlockParam.destRenderingLayerMask < 0)
            {
                continue;
            }

            RenderStateBlock stateBlock = BuildLayerRenderState(
                output,
                levelIndex,
                levelCount,
                ref layerPassInfoPair,
                rendererBlockParam
            );

            AddToRenderQueue(
                ref rendererSequence,
                output,
                rendererBlockParam,
                layerPassInfoPair,
                stateBlock
            );
        }
    }

    private static RenderStateBlock BuildLayerRenderState(OutputType outputType, int layerIndex, int totalLayers,
                                                            ref LayerPassInfoPair layerPassInfoPair, RendererBlockParam rendererBlockParam)
    {
        return new RenderStateBlock
        {
            blendState = CreateBlendState(outputType, rendererBlockParam),
            depthState = CreateDepthState(rendererBlockParam),
            stencilState = CreateStencilState(
                outputType,
                layerIndex,
                totalLayers,
                ref layerPassInfoPair,
                rendererBlockParam,
                out int stencilRef
            ),
            stencilReference = stencilRef,
            mask = DetermineStateMask(outputType)
        };
    }

    private static BlendState CreateBlendState(OutputType output, RendererBlockParam rendererBlockParam)
    {
        bool isMask = output == OutputType.DepthOnlyMask
                    || output == OutputType.StencilOnlyMask;
        
        return rendererBlockParam.isOpaque
                ? (isMask ? BlendStateOpaqueNoColor : BlendStateOpaque)
                : BlendStateTransparent;
    }

    private static DepthState CreateDepthState(RendererBlockParam rendererBlockParam)
    {
        return new DepthState
        {
            writeEnabled = rendererBlockParam.needDepthWrite,
            compareFunction = rendererBlockParam.needDepthTest
                                ? CompareFunction.LessEqual
                                : CompareFunction.Always
        };
    }

    private static StencilState CreateStencilState(OutputType output, int layerIndex, int totalLayers, ref LayerPassInfoPair layerPassInfoPair,
                                                    RendererBlockParam rendererBlockParam, out int stencilValue)
    {
        int stencilRef = Mathf.Min(layerPassInfoPair.currentStencilRef, 31);
        stencilValue = stencilRef;

        var state = new StencilState
        {
            enabled = true,
            readMask = k_noLevelMask,
            writeMask = k_noLevelMask,
        };
        state.SetCompareFunction(
            rendererBlockParam.needDepthTest
            ? rendererBlockParam.is3D ? CompareFunction.GreaterEqual : CompareFunction.Greater
            : CompareFunction.Always
        );
        state.SetPassOperation(
            rendererBlockParam.needStencilWrite ? StencilOp.Replace : StencilOp.Keep
        );
        state.SetFailOperation(
            StencilOp.Keep
        );

        if(totalLayers == 3)
        {
            ConfigureStencilForMultiLayer(output, layerIndex, totalLayers, stencilRef,
                                            ref state, ref stencilValue);
        }

        if (layerPassInfoPair.excludePass == LayerPassType.Forward)
        {
            state.readMask = 127;
            state.writeMask = 255;
            if (rendererBlockParam.noSSPR)
                stencilValue |= k_noSSPR_StencilMask;
        }

        return state;
    }

    private static void ConfigureStencilForMultiLayer(OutputType output, int layerIndex, int totalLayers, int stencilRef,
                                                        ref StencilState state, ref int stencilValue)
    {
        state.SetCompareFunction(CompareFunction.Greater);
        state.SetPassOperation(StencilOp.Replace);

        if(output == OutputType.StencilOnlyMask)
        {
            state.readMask = k_levelMaskAll;
            state.writeMask = k_levelMaskAll;
            stencilValue = k_levelMasks[layerIndex];
        }
        else
        {
            state.readMask = 255;
            state.writeMask = 255;
            stencilValue = k_levelMasks[layerIndex] | stencilRef;
        }
    }

    private static RenderStateMask DetermineStateMask(OutputType outputType)
    {
        var mask = RenderStateMask.Depth | RenderStateMask.Stencil;

        // 默认混合类型不需要特别设置混合状态
        bool isDefaultBlend = outputType == OutputType.CommonDefault
                            || outputType == OutputType.OverlayDefault;
        if(!isDefaultBlend)
        {
            mask |= RenderStateMask.Blend;
        }

        return mask;
    }

    private static void AddToRenderQueue(ref List<RendererBlock> queue, OutputType outputType, RendererBlockParam rendererBlockParam,
                                        LayerPassInfoPair layerPassInfoPair, RenderStateBlock state)
    {
        var block = new RendererBlock
        {
            minQueue = layerPassInfoPair.currentMinQueue,
            maxQueue = layerPassInfoPair.currentMaxQueue,
            RenderStateBlock = state,
            RenderingLayerMask = (uint)rendererBlockParam.destRenderingLayerMask
        };

        if(layerPassInfoPair.IsLogBlock)
        {
            LogRendererBlock(block);
        }

        if((rendererBlockParam.isOpaque && outputType != OutputType.DepthOnlyMask)
            || layerPassInfoPair.excludePass == LayerPassType.Forward)
        {
            queue.Add(block);
        }
        else
        {
            _forwardRendererTransparentSequence.Add(block);
        }
    }

    private static bool IsOpaque(LayeringInfo info)
    {
        return info.Output == OutputType.CommonOpaque || info.Output == OutputType.OverlayOpaque ||
               info.Output == OutputType.DepthOnlyMask || info.Output == OutputType.StencilOnlyMask;
    }

    private static bool Is3D(LayeringInfo info)
    {
        return info.GeoType != GeometryType.Grounded;
    }

    private static void RestIndices()
    {
        underIndices[0] = -1;
        underIndices[1] = -1;
        underIndices[2] = -1;
    }

    private static bool IsNeedDepthWrite(OutputType outputType)
    {
        return outputType == OutputType.CommonOpaque
                || outputType == OutputType.OverlayOpaque
                || outputType == OutputType.DepthOnlyMask;
    }

    private static bool IsNeedDepthTest(OutputType outputType, GeometryType geometryType)
    {
        return outputType == OutputType.CommonOpaque
                || outputType == OutputType.CommonTransparent
                || (outputType == OutputType.OverlayOpaque && geometryType > GeometryType.Grounded)  //Like if car draw in layering forward pass
                || outputType == OutputType.CommonDefault
                || (outputType == OutputType.DepthOnlyMask && geometryType != GeometryType.Grounded);   //Like water bottom
    }

    private static bool IsNeedStencilTest(bool needDepthTest, bool hasDepthConflict, bool isOpaque,
                                        OutputType outputType, GeometryType geometryType, int opaqueIndexAbove)
    {
        return (needDepthTest && hasDepthConflict)
                || outputType == OutputType.StencilOnlyMask
                || (outputType == OutputType.DepthOnlyMask && geometryType == GeometryType.Grounded)
                || (opaqueIndexAbove >= 0 && !isOpaque);
    }

    private static bool IsNeedStencilWrite(bool isOpaque, OutputType outputType)
    {
        return isOpaque && (outputType != OutputType.DepthOnlyMask);
    }

    private static bool IsNoSSPR(ref int destRenderingLayerMask)
    {
        bool noSSPR = (destRenderingLayerMask & k_NoSSPR_RenderingLayerMask) != 0;

        // Remove nosspr bit to avoid unwanted draw of element with same render queue and overlapped rendering layer mask bit
        // Example: LLN roads above zero has the same queue with LLN roads equal/under zero
        //          They both have k_NoSSPR bit in rendering layer mask
        //          LLN>0 exits in transparent queue, but LLN<=0 exits in opaque queue
        //          They both get repetitive draw because they have same render queue and both have k_NoSSPR bit
        // The perfect solution should be only one bit in the rendering layer mask.
        destRenderingLayerMask &= ~(int)k_NoSSPR_RenderingLayerMask;
        return noSSPR;
    }

    #region 测试接口
    public static void TestCreateRenderBlock(ref List<RendererBlock> rendererSequence, LayeringInfo info, ref LayerPassInfoPair layerPassInfoPair)
    {
        RendererBlockParam rendererBlockParam = new RendererBlockParam(info);

        //special case when rendering layer mask is k_LLNTransparent
        if ((rendererBlockParam.destRenderingLayerMask & k_LLNTransparent) != 0)
        {
            CreateLLNTransparentStateBlock(ref rendererSequence, ref layerPassInfoPair, rendererBlockParam);
        }
        else
        {
            OutputType output = info.Output;
            CreateNormalStateBlock(ref rendererSequence, output,
                                    ref layerPassInfoPair, rendererBlockParam);
        }
    }

    private static void LogRendererBlock(RendererBlock block)
    {
        Debug.Log($"minQueue: {block.minQueue}, maxQueue: {block.maxQueue}, RenderingLayerMask: {block.RenderingLayerMask};\n" +
                        $"blendState-       sourceColorBlendMode: {block.RenderStateBlock.blendState.blendState0.sourceColorBlendMode},\n" +
                        $"                  sourceAlphaBlendMode: {block.RenderStateBlock.blendState.blendState0.sourceAlphaBlendMode},\n" +
                        $"                  destinationColorBlendMode: {block.RenderStateBlock.blendState.blendState0.destinationColorBlendMode},\n" +
                        $"                  destinationAlphaBlendMode: {block.RenderStateBlock.blendState.blendState0.destinationAlphaBlendMode},\n" +
                        $"                  writeMask: {block.RenderStateBlock.blendState.blendState0.writeMask};\n" +
                        $"depthState-       writeEnabled: {block.RenderStateBlock.depthState.writeEnabled},\n" +
                        $"                  compareFunction: {block.RenderStateBlock.depthState.compareFunction};\n" +
                        $"stencilState-     enabled: {block.RenderStateBlock.stencilState.enabled},\n" +
                        $"                  readMask: {block.RenderStateBlock.stencilState.readMask},\n" +
                        $"                  writeMask: {block.RenderStateBlock.stencilState.writeMask},\n" +
                        $"                  compareFunctionFront: {block.RenderStateBlock.stencilState.compareFunctionFront},\n" +
                        $"                  passOperationFront: {block.RenderStateBlock.stencilState.passOperationFront},\n" +
                        $"                  failOperationFront: {block.RenderStateBlock.stencilState.failOperationFront},\n" +
                        $"stencilReference-     stencilReference: {block.RenderStateBlock.stencilReference};");
    }
    #endregion

    #region 优化前的老代码
    public static void SetUpSequence(LayerPassType excludePass, ref List<RendererBlock> destSequence, int viewID)
    {
        destSequence.Clear();

        //List<MapLayerType> historyTypes = new List<MapLayerType>();
        int layerCount = _layeringConfig.LayeringInfos.Count;

        LayeringInfo lastLayeringInfo = _layeringConfig.LayeringInfos[layerCount - 1];
        int currentMinQueue = lastLayeringInfo.RenderQueue;
        int currentMaxQueue = lastLayeringInfo.RenderQueue;
        //MapLayerType lastLayerType = lastLayeringInfo.LayerType;
        LayerPassType lastPass = lastLayeringInfo.RenderPass;
        int currentStencilRef = 1;
        RestIndices();

        for (int i = layerCount - 2; i >= 0; i--)
        {
            LayeringInfo layerInfo = _layeringConfig.LayeringInfos[i];
            LayeringInfo prevLayerInfo = _layeringConfig.LayeringInfos[i + 1];

            //MapLayerType layerType = layerInfo.LayerType;
            OutputType outputType = layerInfo.Output;

            //When depth test can not handle element layering we use stencil
            // - Elements with same depth layering each other
            // - Elements can not use depth test but need to be covered by other elements

            bool renderStateChanged = prevLayerInfo.Output != layerInfo.Output || prevLayerInfo.GeoType != layerInfo.GeoType;

            //If current layering info is different from previous one, add render block based on previous info context with block queue range
            if (renderStateChanged || layerInfo.RenderPass == excludePass || prevLayerInfo.RenderingLayerMask != layerInfo.RenderingLayerMask)
            {
                if (lastPass != excludePass)
                {
                    bool needIncrementStencil = IsOpaque(prevLayerInfo) && !IsOpaque(layerInfo);
                    needIncrementStencil |= IsOpaque(prevLayerInfo) && Is3D(prevLayerInfo) && !Is3D(layerInfo);
                    needIncrementStencil |= Is3D(layerInfo) && layerInfo.Output == OutputType.OverlayOpaque && Is3D(prevLayerInfo);
                    needIncrementStencil |= prevLayerInfo.Output == OutputType.StencilOnlyMask || layerInfo.Output == OutputType.DepthOnlyMask;

                    AddRendererBlock(ref destSequence, prevLayerInfo, i + 1, currentMinQueue, currentMaxQueue, currentStencilRef, viewID, excludePass);
                    if (needIncrementStencil)
                    {
                        currentStencilRef++;
                    }
                }

                currentMinQueue = layerInfo.RenderQueue;
                currentMaxQueue = layerInfo.RenderQueue;

                lastPass = layerInfo.RenderPass;
            }
            else
            {
                // 上一份layer数据是不处理的，直接刷新queue数据
                if (lastPass == excludePass)
                {
                    currentMinQueue = layerInfo.RenderQueue;
                    currentMaxQueue = layerInfo.RenderQueue;
                }
                // layer从后往前遍历，opaque物体进入list的时候使用renderqueue++，因此for循环是renderqueue是递减的，取最小值
                else if (IsOpaque(layerInfo) && currentMinQueue > layerInfo.RenderQueue)
                {
                    currentMinQueue = layerInfo.RenderQueue;
                }
                // layer从后往前遍历，transparent物体进入list的时候使用renderqueue--，因此for循环是renderqueue是递增的，取最大值
                else if (!IsOpaque(layerInfo) && currentMaxQueue < layerInfo.RenderQueue)
                {
                    currentMaxQueue = layerInfo.RenderQueue;
                }

                lastPass = layerInfo.RenderPass;
            }

            if (i == 0 && layerInfo.RenderPass != excludePass)
            {
                AddRendererBlock(ref destSequence, layerInfo, 0, currentMinQueue, currentMaxQueue, currentStencilRef, viewID, excludePass);
            }
        }
    }
    private static void AddRendererBlock(ref List<RendererBlock> rendererSequence, LayeringInfo info, int infoIndex, int currentMinQueue,
        int currentMaxQueue, int stencilRef, int viewID, LayerPassType excludePass)
    {
        GeometryType geoType = info.GeoType;
        OutputType output = info.Output;
        int opaqueIndexAbove = info.OpaqueIndexAbove;

        bool isOpaque = IsOpaque(info);

        bool needDepthWrite = output == OutputType.CommonOpaque || output == OutputType.OverlayOpaque ||
                              output == OutputType.DepthOnlyMask;

        bool needDepthTest = output == OutputType.CommonOpaque ||
                             output == OutputType.CommonTransparent ||
                             //Like if car draw in layering forward pass
                             (output == OutputType.OverlayOpaque && geoType > GeometryType.Grounded) ||
                             output == OutputType.CommonDefault ||
                             //Like water bottom
                             (output == OutputType.DepthOnlyMask && geoType != GeometryType.Grounded);

        //When elements can not use depth test and covered by opaque elements, they need stencil test
        bool hasDepthConflict = true;//!(geoType == GeometryType.AboveGround);

        bool needStencilTest = (needDepthTest && hasDepthConflict) || output == OutputType.StencilOnlyMask ||
                               (output == OutputType.DepthOnlyMask && geoType == GeometryType.Grounded) ||
                               (opaqueIndexAbove >= 0 && !isOpaque);
        bool needStencilWrite = isOpaque && (output != OutputType.DepthOnlyMask);
        bool is3D = geoType == GeometryType.Arbitrary || geoType == GeometryType.AboveGround;

        LevelInfo levelInfo = GetLevelsInfoByViewID(viewID);
        int destRenderingLayerMask = (int)info.RenderingLayerMask;

        bool noSSPR = (destRenderingLayerMask & k_NoSSPR_RenderingLayerMask) != 0;

        // Remove nosspr bit to avoid unwanted draw of element with same render queue and overlapped rendering layer mask bit
        // Example: LLN roads above zero has the same queue with LLN roads equal/under zero
        //          They both have k_NoSSPR bit in rendering layer mask
        //          LLN>0 exits in transparent queue, but LLN<=0 exits in opaque queue
        //          They both get repetitive draw because they have same render queue and both have k_NoSSPR bit
        // The perfect solution should be only one bit in the rendering layer mask.
        destRenderingLayerMask &= ~(int)k_NoSSPR_RenderingLayerMask;

        //special case when rendering layer mask is k_LLNTransparent
        uint k_LLNTransparent = (uint)1 << 30;
        if ((destRenderingLayerMask & k_LLNTransparent) != 0)
        {
            //Draw LLN Roads who's rendering layer is k_LLNTransparent twice
            //First pass draw it with depth write disabled
            //Second pass draw it with depth write enabled
            //note rendererSequence is reverted to actual execute order
            if (excludePass == LayerPassType.PrePass)
            {
                for (int i = 0; i < 2; i++)
                {
                    RenderStateBlock renderStateBlock = LLNTransparentStateBlock(i == 1);
                    RendererBlock rendererBlock = new RendererBlock()
                    {
                        minQueue = currentMinQueue,
                        maxQueue = currentMaxQueue,
                        RenderStateBlock = renderStateBlock,
                        RenderingLayerMask = (uint)destRenderingLayerMask
                    };
                    _forwardRendererTransparentSequence.Add(rendererBlock);
                }
            }
            else
            {
                var renderStateBlock = new RenderStateBlock();
                var depthState = DepthOpaque;
                var stencilState = new StencilState();
                var blendState = BlendStateOpaque;
                stencilState.readMask = k_noLevelMask;
                stencilState.writeMask = k_noLevelMask;
                stencilState.compareFunctionFront = stencilState.compareFunctionBack = CompareFunction.GreaterEqual;
                stencilState.enabled = true;
                stencilState.passOperationFront = stencilState.passOperationBack = StencilOp.Replace;
                stencilState.failOperationFront = stencilState.failOperationBack = StencilOp.Keep;
                renderStateBlock.blendState = blendState;
                renderStateBlock.depthState = depthState;
                int destStencilRef = stencilRef;

                //Process stencil state for non sspr objects
                stencilState.readMask = 127;
                stencilState.writeMask = 255;
                if (noSSPR)
                    destStencilRef |= k_noSSPR_StencilMask;

                renderStateBlock.stencilState = stencilState;
                renderStateBlock.stencilReference = destStencilRef;
                renderStateBlock.mask = RenderStateMask.Blend | RenderStateMask.Stencil | RenderStateMask.Depth;

                RendererBlock rendererBlock = new RendererBlock()
                {
                    minQueue = currentMinQueue,
                    maxQueue = currentMaxQueue,
                    RenderStateBlock = renderStateBlock,
                    RenderingLayerMask = (uint)destRenderingLayerMask
                };

                rendererSequence.Add(rendererBlock);
            }
        }
        else
        {
            int levelCount = (destRenderingLayerMask & levelInfo.levelAll) == 0 ? 1 : 3;

            //Once we have multi-level data, need different rendering layer mask and stencil value for each level
            for (int levelIndex = 0; levelIndex < levelCount; levelIndex++)
            {
                int id = 2 - levelIndex;
                if (levelCount == 1)
                    id = -1;
                else
                    destRenderingLayerMask = levelInfo.levels[levelIndex];

                if (destRenderingLayerMask < 0)
                    continue;

                RenderStateBlock renderStateBlock = new RenderStateBlock();
                BlendState blendState;
                DepthState depthState;
                StencilState stencilState;

                bool isMask = output == OutputType.DepthOnlyMask || output == OutputType.StencilOnlyMask;
                blendState = !isOpaque
                    ? BlendStateTransparent
                    : (isMask ? BlendStateOpaqueNoColor : BlendStateOpaque);

                depthState = new DepthState()
                {
                    writeEnabled = needDepthWrite,
                    compareFunction = needDepthTest ? CompareFunction.LessEqual : CompareFunction.Always
                };
                //-//This is workaround for car mask. Water bottom will be wrong in prepass if using this depth state.
                //depthState = output == OutputType.StencilOnlyMask ? DepthNoTestNoWrite : depthState;
                //depthState = layerType == MapLayerType.HoleMask ? DepthTestNoWrite : depthState;
                //if (layerType == MapLayerType.Clear)
                //{
                //    depthState.writeEnabled = false;
                //    depthState.compareFunction = CompareFunction.Always;

                // if (output == OutputType.StencilOnlyMask)
                // {
                //     var bs0 = blendState.blendState0;
                //     bs0.writeMask = ColorWriteMask.All;
                //     blendState.blendState0 = bs0;
                //
                //     depthState.writeEnabled = true;
                // }

                stencilRef = Mathf.Min(stencilRef, 31);
                int destStencilReference = stencilRef;
                stencilState = new StencilState();
                stencilState.readMask = k_noLevelMask;
                stencilState.writeMask = k_noLevelMask;

                var comp = is3D ? CompareFunction.GreaterEqual : CompareFunction.Greater;
                comp = needStencilTest ? comp : CompareFunction.Always;
                //comp = layerType == MapLayerType.Hole ? CompareFunction.Always : comp;
                stencilState.compareFunctionFront = stencilState.compareFunctionBack = comp;
                stencilState.enabled = true;

                var stencilPassOperation = needStencilWrite ? StencilOp.Replace : StencilOp.Keep;
                //stencilPassOperation = layerType == MapLayerType.Hole ? StencilOp.Keep : stencilPassOperation;
                stencilState.passOperationFront = stencilState.passOperationBack = stencilPassOperation;
                stencilState.failOperationFront = stencilState.failOperationBack = StencilOp.Keep;

                if (levelCount == 3)
                {
                    if (output == OutputType.StencilOnlyMask)
                    {
                        stencilState.compareFunctionFront = CompareFunction.Greater;
                        stencilState.compareFunctionBack = CompareFunction.Greater;
                        stencilState.passOperationFront = StencilOp.Replace;
                        stencilState.passOperationBack = StencilOp.Replace;
                        stencilState.readMask = k_levelMaskAll;
                        stencilState.writeMask = k_levelMaskAll;
                        destStencilReference = k_levelMasks[levelIndex];
                    }
                    else
                    {
                        stencilState.compareFunctionFront = CompareFunction.Greater;
                        stencilState.compareFunctionBack = CompareFunction.Greater;
                        stencilState.passOperationFront = StencilOp.Replace;
                        stencilState.passOperationBack = StencilOp.Replace;
                        stencilState.readMask = 255;
                        stencilState.writeMask = 255;
                        destStencilReference = k_levelMasks[levelIndex] | stencilRef;
                    }
                }
                // else if (is3D && isOpaque)
                // {
                //     stencilState.writeMask = 255;
                //     destStencilReference = k_levelMaskAll | stencilRef;
                // }
                // else if (layerType == MapLayerType.Building)
                // {
                //     stencilState.writeMask = 255;
                //     destStencilReference = buildingStencilReference;
                // }

                //Process stencil state for non sspr objects
                if (excludePass == LayerPassType.Forward)
                {
                    stencilState.readMask = 127;
                    stencilState.writeMask = 255;
                    if (noSSPR)
                        destStencilReference |= k_noSSPR_StencilMask;
                }

                renderStateBlock.blendState = blendState;
                renderStateBlock.depthState = depthState;
                renderStateBlock.stencilState = stencilState;
                renderStateBlock.stencilReference = destStencilReference; //layerType == MapLayerType.Clear ? 0 : switchCount;

                bool useDefaultBlend = output == OutputType.CommonDefault || output == OutputType.OverlayDefault;
                renderStateBlock.mask = RenderStateMask.Depth | RenderStateMask.Stencil;
                if (!useDefaultBlend)
                    renderStateBlock.mask |= RenderStateMask.Blend;

                RendererBlock rendererBlock = new RendererBlock()
                {
                    minQueue = currentMinQueue,
                    maxQueue = currentMaxQueue,
                    RenderStateBlock = renderStateBlock,
                    RenderingLayerMask = (uint)destRenderingLayerMask
                };

                if ((isOpaque && output != OutputType.DepthOnlyMask) || excludePass == LayerPassType.Forward)
                    rendererSequence.Add(rendererBlock);
                else
                    _forwardRendererTransparentSequence.Add(rendererBlock);
            }
        }

        if (needDepthWrite && geoType != GeometryType.None)
        {
            underIndices[(int)geoType] = infoIndex;
        }
    }

    private static RenderStateBlock CreateRenderStateBlock(MapLayerType layerType, int levelIndex, int switchCount)
    {
        BlendState blendState;
        DepthState depthState;

        bool is3D = false;
        bool isTransparent = false;

        switch (layerType)
        {
            case MapLayerType.Object3DOpaque:
            case MapLayerType.Hole:
            case MapLayerType.Building:
                is3D = true;
                break;

            case MapLayerType.Object3DTransparent:
            case MapLayerType.Decoration3D:
                is3D = true;
                isTransparent = true;
                break;

            case MapLayerType.Object2DTransparent:
            case MapLayerType.Decoration2D:
            case MapLayerType.Decoration2DFreeBlend:
                isTransparent = true;
                break;
        }

        bool isDecoration = layerType == MapLayerType.Decoration3D || layerType == MapLayerType.Decoration2D || layerType == MapLayerType.Decoration2DFreeBlend;
        int switchIndex = 0;

        blendState = isTransparent ? BlendStateTransparent : BlendStateOpaque;
        blendState = layerType == MapLayerType.Mask || layerType == MapLayerType.HoleMask
            ? BlendStateOpaqueNoColor
            : blendState;
        // depthState = isTransparent ? (is3D ? DepthTransparent : DepthNoTestNoWrite) : DepthOpaque;//is3D ? DepthOpaque : DepthNoTestNoWrite;

        depthState = new DepthState()
        {
            writeEnabled = (!isTransparent) || layerType == MapLayerType.Object3DTransparent,
            compareFunction = is3D ? CompareFunction.LessEqual : CompareFunction.Always
        };
        //This is workaround for car mask. Water bottom will be wrong in prepass if using this depth state.
        depthState = layerType == MapLayerType.Mask ? DepthNoTestNoWrite : depthState;
        depthState = layerType == MapLayerType.HoleMask ? DepthTestNoWrite : depthState;

        switchCount = Mathf.Min(switchCount, 31);
        int destStencilReference = switchCount;
        StencilState stencilState = new StencilState();
        stencilState.readMask = k_noLevelMask;
        stencilState.writeMask = k_noLevelMask;

        var comp = is3D ? CompareFunction.GreaterEqual : CompareFunction.Greater;
        comp = isTransparent || isDecoration ? CompareFunction.Always : comp;
        comp = layerType == MapLayerType.Hole ? CompareFunction.Always : comp;
        stencilState.compareFunctionFront = stencilState.compareFunctionBack = comp;
        stencilState.enabled = true;

        var stencilPassOperation = isTransparent ? StencilOp.Keep : StencilOp.Replace;
        stencilPassOperation = layerType == MapLayerType.Hole ? StencilOp.Keep : stencilPassOperation;
        stencilState.passOperationFront = stencilState.passOperationBack = stencilPassOperation;
        stencilState.failOperationFront = stencilState.failOperationBack = StencilOp.Keep;

        if (levelIndex >= 0)
        {
            if (layerType == MapLayerType.Clear)
            {
                depthState.writeEnabled = false;
                depthState.compareFunction = CompareFunction.Always;

                blendState = BlendStateOpaqueNoColor;

                stencilState.compareFunctionFront = CompareFunction.Greater;
                stencilState.compareFunctionBack = CompareFunction.Greater;
                stencilState.passOperationFront = StencilOp.Replace;
                stencilState.passOperationBack = StencilOp.Replace;
                stencilState.readMask = k_levelMaskAll;
                stencilState.writeMask = k_levelMasks[levelIndex];
                destStencilReference = k_levelMasks[levelIndex];
            }
            else
            {
                stencilState.compareFunctionFront = CompareFunction.Greater;
                stencilState.compareFunctionBack = CompareFunction.Greater;
                stencilState.passOperationFront = StencilOp.Replace;
                stencilState.passOperationBack = StencilOp.Replace;
                stencilState.readMask = 255;
                stencilState.writeMask = 255;
                destStencilReference = k_levelMasks[levelIndex] | switchCount;
            }
        }
        else if (layerType == MapLayerType.Object3DOpaque)
        {
            stencilState.writeMask = 255;
            destStencilReference = k_levelMaskAll | switchCount;
        }
        else if (layerType == MapLayerType.Building)
        {
            stencilState.writeMask = 255;
            destStencilReference = buildingStencilReference;
        }

        RenderStateBlock renderStateBlock = new RenderStateBlock();
        renderStateBlock.blendState = blendState;
        renderStateBlock.depthState = depthState;
        renderStateBlock.stencilState = stencilState;
        renderStateBlock.stencilReference = destStencilReference; //layerType == MapLayerType.Clear ? 0 : switchCount;

        renderStateBlock.mask = RenderStateMask.Blend | RenderStateMask.Depth | RenderStateMask.Stencil;
        if (layerType == MapLayerType.Decoration2DFreeBlend)
        {
            renderStateBlock.mask = RenderStateMask.Depth | RenderStateMask.Stencil;
        }

        return renderStateBlock;
    }
    #endregion
}