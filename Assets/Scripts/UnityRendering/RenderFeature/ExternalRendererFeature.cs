using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ExternalPass : ScriptableRenderPass
{
    private readonly List<ShaderTagId> _forwardShaderTagIds = new List<ShaderTagId>
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("LightweightForward")
    };

    private ProfilingSampler _profilingSampler = new ProfilingSampler(nameof(ExternalPass));

    public static Vector2Int CommonQueue = new Vector2Int(2000, 3000);
    private RenderStateBlock defaultRenderState = new RenderStateBlock();

    public void Setup(RTHandle colorRT, RTHandle depthRT)
    {
        ConfigureTarget(colorRT, depthRT);
        ConfigureClear(ClearFlag.DepthStencil, Color.clear);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get();
        using (new ProfilingScope(cmd, _profilingSampler))
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            FilteringSettings filteringSettings = FilteringSettings.defaultValue;
            filteringSettings.renderingLayerMask = CommonParameters.k_ExternalPassMask_One | CommonParameters.k_ExternalPassMask_Two;

            var drawingSettings = RenderingUtils.CreateDrawingSettings(_forwardShaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
            filteringSettings.renderQueueRange = new RenderQueueRange()
            {
                lowerBound = CommonQueue.x,
                upperBound = CommonQueue.y
            };
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref defaultRenderState);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }
}

public class ExternalRendererFeature : ScriptableRendererFeature
{
    public RenderPassEvent evt = RenderPassEvent.AfterRendering;

    private ExternalPass externalPass;

    public override void Create()
    {
        externalPass = new ExternalPass()
        {
            renderPassEvent = evt
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        externalPass.Setup(
            renderer.cameraColorTargetHandle,
            renderer.cameraDepthTargetHandle
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(externalPass);
    }
}