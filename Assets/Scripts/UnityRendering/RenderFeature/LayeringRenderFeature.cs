using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityRendering.Managers;

public enum PrepassMode
{
    Depth,
    DepthNormal
}

public class MapLayeringRendererFeature : ScriptableRendererFeature
{
    public bool enablePrepass = false;
    public PrepassMode prepassMode;
    private MapLayeringForwardPass _forwardPass;
    private MapLayeringPrePass _prePass;

    public override void Create()
    {
        if (_forwardPass == null)
            _forwardPass = new MapLayeringForwardPass();
        _forwardPass.renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
        
        if (_prePass == null)
            _prePass = new MapLayeringPrePass();
        _prePass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var perCameraRT = AdditionalCameraDataManager.GetPerCameraRT(renderingData.cameraData.camera);
        if (perCameraRT == null)
            return;

        int viewID = AdditionalCameraDataManager.GetViewIdByCamera(renderingData.cameraData.camera);
        MapLayeringManager.SetUpRenderBlocks(viewID);

        _forwardPass.viewID = viewID;
        renderer.EnqueuePass(_forwardPass);
        
        if (enablePrepass)
        {
            _prePass.viewID = viewID;
            _prePass.perCameraRT = perCameraRT;
            _prePass.prepassMode = prepassMode;
            renderer.EnqueuePass(_prePass);
        }
    }
}