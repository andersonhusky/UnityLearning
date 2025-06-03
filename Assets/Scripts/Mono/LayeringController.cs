using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LayeringController : MonoBehaviour
{
    public Material[] test2DOpaqueMaterials;
    public Material[] test3DOpaqueObjectMaterials;
    public Material[] test2DTransparentObjectMaterials;
    public Material[] testLLNTransparentObjectMaterials;
    public Material[] testOverlayDefaultObjectMaterials;
    public Material[] testOverlayTransparentObjectMaterials;

    public GameObject[] llnTransList;
    // Start is called before the first frame update
    void Start()
    {
        LayerRenderingConfiguration layerRenderingConfiguration = new LayerRenderingConfiguration(this);
        MapLayeringManager.SetLayeringConfig(layerRenderingConfiguration);

        for(int i = 0; i < llnTransList.Length; ++i)
        {
            llnTransList[i].GetComponent<MeshRenderer>().renderingLayerMask = LayerRenderingConfiguration.Layer.k_LLNTransparent;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
