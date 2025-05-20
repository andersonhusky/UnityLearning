using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal.LibTessDotNet;

public partial class MapLayeringManager
{
    private static IMapLayeringConfiguration _layeringConfig;
    public static MapMode MapViewMode;

    public static List<RendererBlock> _forwardRendererSequence = new List<RendererBlock>();
    public static List<RendererBlock> _forwardRendererTransparentSequence = new List<RendererBlock>();
    public static List<RendererBlock> _prepassRendererSequence = new List<RendererBlock>();

    public class LevelInfo
    {
        public List<int> levels = new List<int>();
        public int levelAll;
    }

    private static LevelInfo[] levelInfo = new LevelInfo[2] { new LevelInfo(), new LevelInfo() };
    public static LevelInfo GetLevelsInfoByViewID(int viewID)
    {
        viewID = viewID == 3 ? 1 : 0; // Minimap == 3 else == 0
        return levelInfo[viewID];
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
        SetUpSequenceNew(LayerPassType.PrePass, ref _forwardRendererSequence, viewID);
        SetUpSequenceNew(LayerPassType.Forward, ref _prepassRendererSequence, viewID);
    }

    private static int[] underIndices = new[] { -1, -1, -1 };
    // Ground Rules:
    // 1. The depth test function of opaque 3D objects has to be LessEqual
    // 2. The stencil comparision function of 3D objects has to be GreaterEqual
    //
    public static void SetUpSequenceNew(LayerPassType excludePass, ref List<RendererBlock> destSequence, int viewID)
    {
        destSequence.Clear();

        if(_layeringConfig.LayeringInfos.Count == 0)    return;

        InitializeStateVariables(out var currentMinQueue, out var currentMaxQueue,
                                out var lastPass, out var currentStencilRef);
        
        for (int i = _layeringConfig.LayeringInfos.Count - 2; i >= 0; --i)
        {
            LayeringInfo currentLayerInfo = _layeringConfig.LayeringInfos[i];
            LayeringInfo previousLayerInfo = _layeringConfig.LayeringInfos[i + 1];

            // 需要增加新的RenderBlock
            if(ShouldCreateNewRenderBlock(currentLayerInfo, previousLayerInfo, excludePass))
            {
                ProcessRenderBlockCreation(ref destSequence, currentLayerInfo, previousLayerInfo, i + 1,
                                            ref currentMinQueue, ref currentMaxQueue, ref currentStencilRef,
                                            viewID, excludePass, lastPass);
            }
            else
            {
                UpdateQueueRange(currentLayerInfo, ref currentMinQueue, ref currentMaxQueue, excludePass, lastPass);
            }

            lastPass = currentLayerInfo.RenderPass;

            if(i == 0)
            {
                ProcessFirstLayerSpecialCase(currentLayerInfo, ref destSequence,
                                            currentMinQueue, currentMaxQueue, currentStencilRef,
                                            viewID, excludePass);
            }
        }
    }

    private static void InitializeStateVariables(out int currentMinQueue, out int currentMaxQueue,
                                                out LayerPassType lastPass, out int currentStencilRef)
    {
        var lastLayer = _layeringConfig.LayeringInfos.Last();
        currentMinQueue = lastLayer.RenderQueue;
        currentMaxQueue = lastLayer.RenderQueue;
        lastPass = lastLayer.RenderPass;
        currentStencilRef = 1;
        RestIndices();
    }

    private static bool ShouldCreateNewRenderBlock(LayeringInfo currentLayer, LayeringInfo previousLayer,
                                                LayerPassType excludePass)
    {
        bool renderStateChange = previousLayer.Output != currentLayer.Output
                                || previousLayer.GeoType != currentLayer.GeoType;
        bool renderingLayerMaskChange = previousLayer.RenderingLayerMask != currentLayer.RenderingLayerMask;
        bool isExcludePass = currentLayer.RenderPass == excludePass;
        return renderStateChange || isExcludePass || renderingLayerMaskChange;
    }

    private static void ProcessRenderBlockCreation(ref List<RendererBlock> destSequence, LayeringInfo currentLayerInfo, LayeringInfo previousLayerInfo, int layerIndex,
                                                    ref int currentMinQueue, ref int currentMaxQueue, ref int currentStencilRef, int viewID,
                                                    LayerPassType excludePass, LayerPassType lastPass)
    {
        if(lastPass != excludePass)
        {
            AddRendererBlock(ref destSequence, previousLayerInfo, layerIndex,
                            currentMinQueue, currentMaxQueue, currentStencilRef,
                            viewID, excludePass);

            if(DetermineStencilIncrement(previousLayerInfo, currentLayerInfo))
            {
                currentStencilRef++;
            }
        }

        currentMinQueue = currentLayerInfo.RenderQueue;
        currentMaxQueue = currentLayerInfo.RenderQueue;
    }

    /// <summary>
    /// Add Comment by White Hong - 2025-05-20 11:14:26
    /// 判断是否需要增加模板值
    /// <summary>
    private static bool DetermineStencilIncrement(LayeringInfo currentLayer, LayeringInfo nextLayer)
    {
        bool condition1 = IsOpaque(currentLayer) && !IsOpaque(nextLayer);       // opaque切换到tran
        bool condition2 = IsOpaque(currentLayer) && Is3D(currentLayer) && !Is3D(nextLayer);     // 3D切换到2D
        bool condition3 = Is3D(nextLayer) && nextLayer.Output == OutputType.OverlayOpaque && Is3D(currentLayer);    // 3D下碰到overlayOpaque
        bool condition4 = currentLayer.Output == OutputType.StencilOnlyMask || nextLayer.Output == OutputType.DepthOnlyMask;    // mask

        return condition1 || condition2 || condition3 || condition4;
    }

    private static void UpdateQueueRange(LayeringInfo layeringInfo, ref int currentMinQueue, ref int currentMaxQueue,
                                        LayerPassType excludePass, LayerPassType lastPass)
    {
        if (lastPass == excludePass)
        {
            currentMinQueue = layeringInfo.RenderQueue;
            currentMaxQueue = layeringInfo.RenderQueue;
        }
        else if (IsOpaque(layeringInfo))
        {
            // 对于不透明物体，取最小的RenderQueue（最先渲染）
            currentMinQueue = Mathf.Min(currentMinQueue, layeringInfo.RenderQueue);
        }
        else
        {
            // 对于透明物体，取最大的RenderQueue（最后渲染）
            currentMaxQueue = Mathf.Max(currentMaxQueue, layeringInfo.RenderQueue);
        }
        // 和上面的else if + else其实是等价的
        // else
        // {
        //     currentMinQueue = Mathf.Min(currentMinQueue, layeringInfo.RenderQueue);
        //     currentMaxQueue = Mathf.Max(currentMaxQueue, layeringInfo.RenderQueue);
        // }
    }

    private static void ProcessFirstLayerSpecialCase(LayeringInfo layeringInfo, ref List<RendererBlock> destSequence,
                                                    int currentMinQueue, int currentMaxQueue, int currentStencilRef,
                                                    int viewID, LayerPassType excludePass)
    {
        if(layeringInfo.RenderPass != excludePass)
        {
            AddRendererBlock(ref destSequence, layeringInfo, 0,
                            currentMinQueue, currentMaxQueue, currentStencilRef,
                            viewID, excludePass);
        }
    }

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

            bool renderStateChanged = prevLayerInfo.Output != layerInfo.Output || prevLayerInfo.GeoType != layerInfo.GeoType ;
            
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
                if(noSSPR)
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
                    if(noSSPR)
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

                if((isOpaque && output != OutputType.DepthOnlyMask)|| excludePass == LayerPassType.Forward)
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
}