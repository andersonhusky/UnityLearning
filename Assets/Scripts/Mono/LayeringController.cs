using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LayeringController : MonoBehaviour
{
    public Material[] testMaterials;
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
