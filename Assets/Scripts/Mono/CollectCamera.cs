using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Linq;
using UnityRendering.Managers;

public class CollectCamera : MonoBehaviour
{
    public GameObject[] cameras;
    public enum ScreenInfo
    {
        HU = 0,
        IC = 1,
        HU_JV = 2,
        HU_MiniMap = 3,
        IC_JV = 4,
        HUD = 5,
        HUD_JV = 6,

        INVALID
        //CDD = 7
    }

    private int _tdfMask = 0;
    private void Awake()
    {
        Collect();
    }

    public void Collect()
    {
        for (int index = 0; index < cameras.Length; index++)
        {
            var cameraObj = cameras[index];
            if (cameraObj == null)
                continue;

            var camera = cameraObj.GetComponent<Camera>();
// #if TIME_DOMAIN_FILTER
//                 TimeDomainFilter.CameraCollector.Collect(index, camera);
// #endif
            bool needFlip = false;
            bool needSrgbEncoding = false;
#if PLATFORM_EMBEDDED_LINUX && !UNITY_EDITOR
            ScreenInfo screenInfo = GetScreenInfo(camera.cullingMask);
            needFlip = screenInfo != ScreenInfo.HU && screenInfo != ScreenInfo.IC && screenInfo != ScreenInfo.HUD;
            needSrgbEncoding = (screenInfo == ScreenInfo.HU && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan) || 
                                screenInfo == ScreenInfo.IC || 
                                screenInfo == ScreenInfo.HU_MiniMap ||
                                screenInfo == ScreenInfo.HU_JV || 
                                screenInfo == ScreenInfo.HUD;
#endif
            bool needMotionVector = (_tdfMask & (1 << index)) != 0 ? true : false;
            AdditionalCameraDataManager.Collect(index, camera, needFlip, needSrgbEncoding, needMotionVector);
        }

#if UNITY_EDITOR
        foreach (var cam in SceneView.GetAllSceneCameras())
        {
            if (cam.cameraType == CameraType.SceneView)
                AdditionalCameraDataManager.Collect(-1, cam);
        }
#endif
    }
}