using System;
using System.Collections.Generic;

public class LayerRenderingConfigration : IMapLayeringConfiguration
{
    [AutoGenToString]
    public enum MeshPart
    {
        Frame = 1 << 0,
        Body = 1 << 1,
    }
    private ScreenInfo screenInfo = ScreenInfo.HU;
    private List<LayeringInfo> transparentLayeringInfos = new List<LayeringInfo>();
    Array meshParts = Enum.GetValues(typeof(MeshPart));
    private int opaqueRenderQueue;
    private int transparentRenderQueue;
    public LayerRenderingConfigration()
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
    }

    private void SetUpSharedInfos()
    {
        meshParts = Enum.GetValues(typeof(MeshPart));
    }
}