using UnityEngine.Rendering;

public partial class MapLayeringManager
{
    public static readonly BlendState BlendStateTransparent = new BlendState()
    {
        blendState0 = new RenderTargetBlendState()
        {
            sourceColorBlendMode = BlendMode.SrcAlpha,
            sourceAlphaBlendMode = BlendMode.SrcAlpha,
            destinationColorBlendMode = BlendMode.OneMinusSrcAlpha,
            destinationAlphaBlendMode = BlendMode.OneMinusSrcAlpha,
            writeMask = ColorWriteMask.All
        },
    };

    public static readonly BlendState BlendStateUnlitAlpha = new BlendState()
    {
        blendState0 = new RenderTargetBlendState()
        {
            sourceColorBlendMode = BlendMode.SrcAlpha,
            sourceAlphaBlendMode = BlendMode.SrcAlpha,
            destinationColorBlendMode = BlendMode.OneMinusSrcAlpha,
            destinationAlphaBlendMode = BlendMode.OneMinusSrcAlpha,
            writeMask = ColorWriteMask.All
        }
    };

    public static readonly BlendState BlendStateOpaque = new BlendState()
    {
        blendState0 = new RenderTargetBlendState()
        {
            sourceColorBlendMode = BlendMode.One,
            sourceAlphaBlendMode = BlendMode.One,
            destinationColorBlendMode = BlendMode.Zero,
            destinationAlphaBlendMode = BlendMode.Zero,
            writeMask = ColorWriteMask.All
        }
    };

    public static readonly BlendState BlendStateOpaqueNoColor = new BlendState()
    {
        blendState0 = new RenderTargetBlendState()
        {
            sourceColorBlendMode = BlendMode.One,
            sourceAlphaBlendMode = BlendMode.One,
            destinationColorBlendMode = BlendMode.Zero,
            destinationAlphaBlendMode = BlendMode.Zero,
            writeMask = 0
        }
    };


    public static readonly DepthState DepthOpaque = new DepthState()
    {
        writeEnabled = true,
        compareFunction = CompareFunction.LessEqual
    };

    public static readonly DepthState DepthTransparent = new DepthState()
    {
        writeEnabled = false,
        compareFunction = CompareFunction.LessEqual
    };

    public static readonly DepthState DepthNoTestWrite = new DepthState()
    {
        writeEnabled = true,
        compareFunction = CompareFunction.Always
    };

    public static readonly DepthState DepthNoTestNoWrite = new DepthState()
    {
        writeEnabled = false,
        compareFunction = CompareFunction.Always
    };

    public static readonly DepthState DepthTestNoWrite = new DepthState()
    {
        writeEnabled = false,
        compareFunction = CompareFunction.LessEqual
    };
}