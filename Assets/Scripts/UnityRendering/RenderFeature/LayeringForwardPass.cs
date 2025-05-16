using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MapLayeringForwardPass : ScriptableRenderPass
{
    public int viewID = (int)ScreenInfo.HU;
    private readonly List<ShaderTagId> forwardShaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("LightweightForward") // Legacy shaders (do not have a gbuffer pass) are considered forward-only for backward compatibility
    };

    ProfilingSampler _profilingSampler = new ProfilingSampler("MapLayeringRenderPass");

    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        ConfigureClear(ClearFlag.All, Color.clear);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        List<RendererBlock> forwardSequence = MapLayeringManager._forwardRendererSequence;
        List<RendererBlock> transparentSequence = MapLayeringManager._forwardRendererTransparentSequence;
        if (forwardSequence.Count + transparentSequence.Count <= 0)
            return;

        CommandBuffer cmd = CommandBufferPool.Get();

        using (new ProfilingScope(cmd, _profilingSampler))
        {
            int blockCount = forwardSequence.Count;

            for (int i = (blockCount - 1); i >= 0; i--)
            {
                RendererBlock rendererBlock = forwardSequence[i];

                if (viewID == (int)ScreenInfo.HU_MiniMap)
                    rendererBlock.RenderStateBlock.blendState = MapLayeringManager.BlendStateTransparent;

                RenderQueueRange renderQueueRange =
                    new RenderQueueRange(rendererBlock.minQueue, rendererBlock.maxQueue);

                DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(forwardShaderTagIds,
                    ref renderingData, SortingCriteria.RenderQueue);

                FilteringSettings filterSettings =
                    new FilteringSettings(renderQueueRange, renderingLayerMask: rendererBlock.RenderingLayerMask);
                context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings,
                    ref filterSettings, ref rendererBlock.RenderStateBlock);
            }

            for (int i = 0; i < transparentSequence.Count; i++)
            {
                RendererBlock rendererBlock = transparentSequence[i];
                
                RenderQueueRange renderQueueRange =
                    new RenderQueueRange(rendererBlock.minQueue, rendererBlock.maxQueue);

                DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(forwardShaderTagIds,
                    ref renderingData, SortingCriteria.RenderQueue);

                FilteringSettings filterSettings =
                    new FilteringSettings(renderQueueRange, renderingLayerMask: rendererBlock.RenderingLayerMask);
                context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings,
                    ref filterSettings, ref rendererBlock.RenderStateBlock);
            }
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}