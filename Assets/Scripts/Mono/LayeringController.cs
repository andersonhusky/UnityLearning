using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LayeringController : MonoBehaviour
{
    public Material[] test2DOpaqueMaterials;
    public Material[] test3DOpaqueObjectMaterials;
    public Material[] test2DTransparentObjectMaterials;
    public Material[] testOverlayDefaultObjectMaterials;
    public Material[] testOverlayTransparentObjectMaterials;
    // Start is called before the first frame update
    void Start()
    {
        LayerRenderingConfiguration layerRenderingConfiguration = new LayerRenderingConfiguration(this);
        MapLayeringManager.SetLayeringConfig(layerRenderingConfiguration);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
