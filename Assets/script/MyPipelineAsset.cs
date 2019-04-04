using System.Collections;
using System.Collections.Generic;
using script;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/My Pipeline")]
public class MyPipelineAsset : RenderPipelineAsset
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    protected override IRenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline();
    }
}
