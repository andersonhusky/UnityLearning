using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;


namespace UnityRendering.Managers
{
    public class PerCameraRT
    {
        public string name;
        
        public RTHandle CameraColorAttachmentRT;
        private string _CameraColorAttachmentRTHandleName;
        public string cameraColorAttachmentRTHandleName
        {
            get
            {
                if (_CameraColorAttachmentRTHandleName == null)
                    _CameraColorAttachmentRTHandleName = name + "_CameraColorAttachment";
                return _CameraColorAttachmentRTHandleName;
            }
        }
        
        public RTHandle CameraDepthAttachmentRT;
        private string _CameraDepthAttachmentRTHandleName;
        public string cameraDepthAttachmentRTHandleName
        {
            get
            {
                if (_CameraDepthAttachmentRTHandleName == null)
                    _CameraDepthAttachmentRTHandleName = name + "_CameraDepthAttachment";
                return _CameraDepthAttachmentRTHandleName;
            }
        }
        
        public RTHandle CameraNormalRT;
        private string _cameraNormalRTHandleName;
        public string cameraNormalRTHandleName
        {
            get
            {
                if (_cameraNormalRTHandleName == null)
                    _cameraNormalRTHandleName = name + "_CameraNormalsTexture";
                return _cameraNormalRTHandleName;
            }
        }
        
        public RTHandle[] SSAORT = new RTHandle[4];
        private string[] SSAOHandleName = new string[4];

        public RTHandle[] BloomMipDown = new RTHandle[16];
        private string[] _BloomMipDownHandleName = new string[16];
        
        public RTHandle[] BloomMipUp = new RTHandle[16];
        private string[] _BloomMipUpHandleName = new string[16];
        
        public RTHandle PostProcessColorRT;
        private string _postProcessColorRTHandleName;
        public string postProcessColorRTHandleName
        {
            get
            {
                if (_postProcessColorRTHandleName == null)
                    _postProcessColorRTHandleName = name + "_TempTarget";
                return _postProcessColorRTHandleName;
            }
        }
        
        public RTHandle[] TaaHistoryRT = new RTHandle[2];
        private string[] _TaaHistoryRTName = new string[2];
        
        public RTHandle motionVectorColorRT;
        private string _motionVectorColorRTHandleName;
        public string motionVectorColorRTHandleName
        {
            get
            {
                if (_motionVectorColorRTHandleName == null)
                    _motionVectorColorRTHandleName = name + "_MotionVectorTexture";
                return _motionVectorColorRTHandleName;
            }
        }
        
        public RTHandle motionVectorDepthRT;
        private string _motionVectorDepthRTHandleName;
        public string motionVectorDepthRTHandleName
        {
            get
            {
                if (_motionVectorDepthRTHandleName == null)
                    _motionVectorDepthRTHandleName = name + "_MotionVectorDepthTexture";
                return _motionVectorDepthRTHandleName;
            }
        }
        
        public int prevIndex;

        public RTHandle ScreenSpaceReflectionRT;
        public ComputeBuffer SSPRUVHash;
        public RTHandle SsprMaskRT;
        private string _ssprMaskRTRTHandleName;
        public string ssprMaskRTRTHandleName
        {
            get
            {
                if (_ssprMaskRTRTHandleName == null)
                    _ssprMaskRTRTHandleName = name + "_SSPR_MASK";
                return _ssprMaskRTRTHandleName;
            }
        }

        public RTHandle PrePassDepthRT; // Hu and IC need extra depth rt for cover-transparent effect.
        private string _prePassRTHandleName;
        public string PrePassRTHandleName
        {
            get
            {
                if (_prePassRTHandleName == null)
                    _prePassRTHandleName = name + "_PrePassDepthRT";
                return _prePassRTHandleName;
            }
        }

        public RTHandle FxaaRT;
        private string _fxaaRTHandleName;
        public string fxaaRTHandleName
        {
            get
            {
                if (_fxaaRTHandleName == null)
                    _fxaaRTHandleName = name + "_FXAA_RT";
                return _fxaaRTHandleName;
            }
        }

        public void Dispose()
        {
            ReleaseRT(ref CameraDepthAttachmentRT);
            ReleaseRT(ref CameraNormalRT);
            for (int i = 0; i < SSAORT.Length; i++)
            {
                ReleaseRT(ref SSAORT[i]);
            }

            for (int i = 0; i < BloomMipUp.Length; i++)
            {
                ReleaseRT(ref BloomMipUp[i]);
                ReleaseRT(ref BloomMipDown[i]);
            }

            ReleaseRT(ref CameraColorAttachmentRT);
            ReleaseRT(ref PostProcessColorRT);

            ReleaseRT(ref motionVectorColorRT);
            ReleaseRT(ref motionVectorDepthRT);
            ReleaseRT(ref TaaHistoryRT[0]);
            ReleaseRT(ref TaaHistoryRT[1]);
            
            ReleaseRT(ref PrePassDepthRT);
            
            ReleaseRT(ref FxaaRT);
            
            ReleaseRT(ref SsprMaskRT);
        }
		
        public string GetSSAOHandleName(int index)
        {
            if (SSAOHandleName[index] == null)
                SSAOHandleName[index] = index != 3 ? name + "_SSAO_OcclusionTexture" + index : name + "_SSAO_OcclusionTexture";
            return SSAOHandleName[index];
        }
        
        public string GetTaaHistoryRTName(int index)
        {
            if (_TaaHistoryRTName[index] == null)
                _TaaHistoryRTName[index] = name + "TAA_History" + index;
            return _TaaHistoryRTName[index];
        }

        public string GetBloomMipHandleName(bool isBloomMipDown, int index)
        {
            string[] retHandleNames = isBloomMipDown ? _BloomMipDownHandleName : _BloomMipUpHandleName;
            if (retHandleNames[index] == null)
                retHandleNames[index] = isBloomMipDown ? name + "_BloomMipDown" + index : name + "_BloomMipUp" + index;
            return retHandleNames[index];
        }

        private static void ReleaseRT(ref RTHandle rtHandle)
        {
            rtHandle?.Release();
            rtHandle = null;
        }

        public RTHandle GetHistoryRT()
        {
            return TaaHistoryRT[prevIndex];
        }
    }

    public class AdditionalCameraData
    {
        public int viewID;
        public PerCameraRT perCameraRT;
        public MotionVectorSettings mvSettings;
        public Settings settings;
        public bool needYFlip;
        public bool needLinearToSrgb;
    }

    public static class AdditionalCameraDataManager
    {
        private static Camera[] _cameras = new Camera[7];
        private static Dictionary<Camera, AdditionalCameraData> _cameraDataDic = new Dictionary<Camera, AdditionalCameraData>();

        public static void Collect(int index, Camera camera, bool yFlip = false, bool linearToSrgb = false, bool needMotionVector = false)
        {
            if (index >= 0)
                _cameras[index] = camera;

            _cameraDataDic[camera] = new AdditionalCameraData()
            {
                viewID = index,
                perCameraRT = new PerCameraRT() { name = camera.name },
                mvSettings = needMotionVector ? new MotionVectorSettings() : null,
                needYFlip = yFlip,
                needLinearToSrgb = linearToSrgb
            };
        }

        public static int GetViewIdByCamera(Camera camera)
        {
            var acd = GetCameraData(camera);
            int viewID = acd != null ? acd.viewID : -1;
            return viewID;
        }

        public static RenderTexture GetHistoryColor(Camera camera)
        {
            Settings temporalSettings = GetCameraData(camera).settings;
            if (temporalSettings != null)
            {
#if TIME_DOMAIN_FILTER
                Fsr2Settings settings = temporalSettings as Fsr2Settings;
                if (settings != null)
                    return settings.HistoryColor;
                else
#endif
                {
                    return GetPerCameraRT(camera).GetHistoryRT();
                }
            }

            return null;
        }

        public static RTHandle GetVelocityBuffer(Camera camera)
        {
            return GetPerCameraRT(camera).motionVectorColorRT;
        }

        public static AdditionalCameraData GetCameraData(Camera camera) => _cameraDataDic.ContainsKey(camera) ? _cameraDataDic[camera] : null;

        // public static TAASetting GetTaaSetting(Camera camera) => GetCameraData(camera)?.TaaSetting;

        public static PerCameraRT GetPerCameraRT(Camera camera) => GetCameraData(camera)?.perCameraRT;

        public static void Dispose()
        {
            foreach (var cameraData in _cameraDataDic)
            {
                cameraData.Value?.perCameraRT?.Dispose();
                cameraData.Value?.settings?.OnDisable();
            }
        }
        
        public static void UpdateCCPInfo(int index, in Vector3 ccpPosition, in Quaternion ccpRotation)
        {
            var mvSettings = GetCameraData(_cameras[index]).mvSettings;
            if (mvSettings != null)
            {
                mvSettings.ccpPosition = ccpPosition;
                mvSettings.ccpRotation = ccpRotation;
            }
        }

        public static void UpdateMapCenterInfo(int index, in Vector3 mapCenterPosition, in Vector3 mapCenterScale)
        {
            var mvSettings = GetCameraData(_cameras[index]).mvSettings;
            if (mvSettings != null)
            {
                mvSettings.mapCenterPosition = mapCenterPosition;
                mvSettings.mapCenterScale = mapCenterScale;
            }
        }
    }
}