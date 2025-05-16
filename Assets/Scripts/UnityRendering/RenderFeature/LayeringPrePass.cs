using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityRendering.Managers;

public class MapLayeringPrePass : ScriptableRenderPass
{
    ProfilingSampler _profilingSampler = new ProfilingSampler("MapLayeringPrePass");
    public PerCameraRT perCameraRT; // 外部确保引用有效
    public int viewID;
    public PrepassMode prepassMode;

    private static readonly List<ShaderTagId> k_DepthNormals = new List<ShaderTagId>
        { new ShaderTagId("DepthNormals"), new ShaderTagId("DepthNormalsOnly") };
    private static readonly List<ShaderTagId> k_Depth = new List<ShaderTagId>
        { new ShaderTagId("DepthOnly") };

    private static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
    private static readonly int _CameraNormalsTexture = Shader.PropertyToID("_CameraNormalsTexture");
    private static readonly int _EnableDither = Shader.PropertyToID("_EnableDither");
    const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
    const int k_DepthBufferBits = 32;
    private RTHandle _depthRT;

    private RenderStateBlock _stencilDefaultState = new RenderStateBlock(RenderStateMask.Nothing);
    private RenderStateBlock _stencil1State = new RenderStateBlock(RenderStateMask.Stencil)
    {
        stencilReference = 1,
        stencilState = new StencilState()
        {
            readMask = 255,
            writeMask = 255,
            compareFunctionFront = CompareFunction.Always,
            compareFunctionBack = CompareFunction.Always,
            passOperationBack = StencilOp.Replace,
            passOperationFront = StencilOp.Replace,
            zFailOperationFront = StencilOp.Keep,
            zFailOperationBack = StencilOp.Keep,
            enabled = true
        },
    };
    
    //public const int k_NoSSPRBit = 29;

    private GraphicsFormat GetGraphicsFormat()
    {
        if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
            return GraphicsFormat.R8G8B8A8_SNorm; // Preferred format
        else if (RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render))
            return GraphicsFormat.R16G16B16A16_SFloat; // fallback
        else
            return GraphicsFormat.R32G32B32A32_SFloat; // fallback
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.msaaSamples = 1;
        desc.useMipMap = false;
        desc.autoGenerateMips = false;
        desc.bindMS = false;

        _depthRT = renderingData.cameraData.renderer.cameraDepthTargetHandle;
        if (viewID == (int)ScreenInfo.HU || viewID == (int)ScreenInfo.IC)
        {
            desc.graphicsFormat = GraphicsFormat.None; // GraphicsFormat.R32_SFloat;
            desc.depthStencilFormat = k_DepthStencilFormat;
            desc.depthBufferBits = k_DepthBufferBits;
            RenderingUtils.ReAllocateIfNeeded(ref perCameraRT.PrePassDepthRT, desc, FilterMode.Point, TextureWrapMode.Clamp, name: perCameraRT?.PrePassRTHandleName);
            _depthRT = perCameraRT.PrePassDepthRT;
        }
        
        if (prepassMode == PrepassMode.DepthNormal)
        {
            desc.graphicsFormat = GetGraphicsFormat();
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref perCameraRT.CameraNormalRT, desc, FilterMode.Point, TextureWrapMode.Clamp, name: perCameraRT.name + "_CameraNormalsTexture");
            ConfigureTarget(perCameraRT.CameraNormalRT, _depthRT);
            ConfigureClear(ClearFlag.All, Color.black);
        }
        else
        {
            ConfigureTarget(_depthRT);
            ConfigureClear(ClearFlag.DepthStencil, Color.black);
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();

        List<RendererBlock> prepassSequence = MapLayeringManager._prepassRendererSequence;
        if (prepassSequence.Count <= 0)
            return;
        using (new ProfilingScope(cmd, _profilingSampler))
        {
            cmd.SetGlobalFloat(_EnableDither, 0);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            int blockCount = prepassSequence.Count;
            for (int i = (blockCount - 1); i >= 0; i--)
            {
                RendererBlock rendererBlock = prepassSequence[i];

                RenderQueueRange renderQueueRange =
                    new RenderQueueRange(rendererBlock.minQueue, rendererBlock.maxQueue);

                DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(prepassMode == PrepassMode.DepthNormal ? k_DepthNormals : k_Depth,
                    ref renderingData, SortingCriteria.RenderQueue);

                FilteringSettings filterSettings =
                    new FilteringSettings(renderQueueRange, renderingLayerMask: rendererBlock.RenderingLayerMask);

                //RenderStateBlock renderState = (rendererBlock.RenderingLayerMask & (1 << k_NoSSPRBit)) != 0 ? _stencil1State : rendererBlock.RenderStateBlock;
                RenderStateBlock renderState = rendererBlock.RenderStateBlock;
                context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings,
                    ref filterSettings, ref renderState);
            }

            cmd.SetGlobalTexture(_CameraDepthTexture, _depthRT);
            if (prepassMode == PrepassMode.DepthNormal)
                cmd.SetGlobalTexture(_CameraNormalsTexture, perCameraRT.CameraNormalRT);
            cmd.SetGlobalFloat("_WaterBottomOffsetStrength", 0);
            cmd.SetGlobalFloat(_EnableDither, 1);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
}