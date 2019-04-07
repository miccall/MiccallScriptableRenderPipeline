using System.Collections;
using System.Collections.Generic;
using script;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/My Pipeline")]
public class MyPipelineAsset : RenderPipelineAsset
{
    [SerializeField]
    bool dynamicBatching;
   protected override IRenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline(dynamicBatching);
    }
}
