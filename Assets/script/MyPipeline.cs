using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;
// ReSharper disable StringLiteralTypo

// ReSharper disable BitwiseOperatorOnEnumWithoutFlags

namespace script
{
    public class MyPipeline : RenderPipeline
    {
        public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            base.Render(renderContext, cameras);
            foreach (var camera in cameras)
            {
                Render(renderContext,camera);
            }
        }

        private static CullResults _cull ;
        private static readonly CommandBuffer CameraBuffer = new CommandBuffer {
            name = "Render Camera"
        };
        private static void Render (ScriptableRenderContext context, Camera camera) {
            context.SetupCameraProperties(camera);
            
            // 剔除检查 
            if (!CullResults.GetCullingParameters(camera, out var cullparamet))    return;
            
#if UNITY_EDITOR
            if(camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif   
            // 剔除 
            CullResults.Cull(ref cullparamet, context,ref _cull);
        
            var clearFlags = camera.clearFlags;
       
            CameraBuffer.ClearRenderTarget(
                (CameraClearFlags.Depth & clearFlags )!=0 ,
                (CameraClearFlags.Color & clearFlags)!=0,
                camera.backgroundColor );
            CameraBuffer.BeginSample("Render Camera");
        
            CameraBuffer.Clear();

            var drawRendererSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
            {
                sorting = {flags = SortFlags.CommonOpaque}
            };
        
            var  filterRendererSettings = new FilterRenderersSettings(true){renderQueueRange = RenderQueueRange.opaque};
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
        
            DrawDefaultPipeline(context, camera);
            context.DrawSkybox(camera);
        
            drawRendererSettings.sorting.flags = SortFlags.CommonTransparent ;
            filterRendererSettings.renderQueueRange=RenderQueueRange.transparent;
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
        
            CameraBuffer.EndSample("Render Camera");
            context.ExecuteCommandBuffer(CameraBuffer);
            CameraBuffer.Clear();
            context.Submit();
        }

        private static Material _errorMaterial;
        
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        private static void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
        {
            if (_errorMaterial == null) {
                var errorShader = Shader.Find("Hidden/InternalErrorShader");
                _errorMaterial = new Material(errorShader) {
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            
            var drawSettings = new DrawRendererSettings(
                camera, new ShaderPassName("ForwardBase")
            );

            drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
            drawSettings.SetShaderPassName(2, new ShaderPassName("Always"));
            drawSettings.SetShaderPassName(3, new ShaderPassName("Vertex"));
            drawSettings.SetShaderPassName(4, new ShaderPassName("VertexLMRGBM"));
            drawSettings.SetShaderPassName(5, new ShaderPassName("VertexLM"));
            drawSettings.SetOverrideMaterial(_errorMaterial,0);
            var filterSettings = new FilterRenderersSettings(true);
		
            context.DrawRenderers(
                _cull.visibleRenderers, ref drawSettings, filterSettings
            );
            
        }
    }
}
