using System.Collections.Generic;
using UnityEngine.Rendering;

public enum MapLayerType
{
    Object2DOpaque,
    Object3DOpaque,
    Hole, // base: 3DOpaque, no stencil writing, stencil compare always
    HoleMask, // base: 2DOpaque, no color writing, no depth writing
    Object2DTransparent,
    Object3DTransparent,
    Decoration2D,
    Decoration2DFreeBlend,
    Decoration3D, // base: 3DTransparent, stencil compare always
    Mask, // base: 2DOpaque, no color writing
    Building,
    GroundMask,
    Clear,
    GroundDepthOverwrite
}

public enum GeometryType
{
    Grounded = 0,
    AboveGround = 1,
    Arbitrary = 2,
    None = -1
}

public enum OutputType
{
    CommonDefault, //Go with shader, do not override
    OverlayDefault,
    CommonTransparent,
    OverlayTransparent,
    CommonOpaque,
    OverlayOpaque,
    StencilOnlyMask,
    DepthOnlyMask,
    Custom
    //OneSideTransparent,
}

public enum LayerPassType
{
    Forward = 1,
    PrePass = 2,
    All = 4
}

public enum MapMode
{
    Map2D = 1,
    Map3D = 2,
    MapAll = 4
}

public enum ShadingMode
{
    Lit,
    Unlit
}

public abstract class IMapLayeringConfiguration
{
    public List<LayeringInfo> LayeringInfos;
    //public List<LayeringInfo> OpaqueLayeringInfos;
    //public List<LayeringInfo> TransparentLayeringInfos;

    protected void PreprocessLayeringInfos()
    {
        int layeringCount = LayeringInfos.Count;
        for (int i = 0; i < layeringCount; i++)
        {
            
        }
    }
}

public struct LayeringInfo
{
    //public MapLayerType LayerType;
    public GeometryType GeoType;
    public OutputType Output;
    public LayerPassType RenderPass;
    public MapMode Mode;
    public ShadingMode ShadingMode;
    public uint RenderingLayerMask;
    
    public int OpaqueIndexAbove;
    public int RenderQueue;
}

public struct RendererBlock
{
    public int minQueue;
    public int maxQueue;
    public RenderStateBlock RenderStateBlock;

    public uint RenderingLayerMask;
    //public ParamOverride ColorOverride;
}


// ------------------------------------
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