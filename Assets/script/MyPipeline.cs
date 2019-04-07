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
        private readonly DrawRendererFlags _drawFlags;

        public MyPipeline (bool dynamicBatching) {
            if (dynamicBatching) {
                _drawFlags = DrawRendererFlags.EnableDynamicBatching;
            }
        }
        
        public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            base.Render(renderContext, cameras);
            foreach (var camera in cameras)
            {
                Render(renderContext,camera);
            }
        }

        private  CullResults _cull ;
        private  readonly CommandBuffer _cameraBuffer = new CommandBuffer {
            name = "Render Camera"
        };
        private  void Render (ScriptableRenderContext context, Camera camera) {
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
       
            _cameraBuffer.ClearRenderTarget(
                (CameraClearFlags.Depth & clearFlags )!=0 ,
                (CameraClearFlags.Color & clearFlags)!=0,
                camera.backgroundColor );
            _cameraBuffer.BeginSample("Render Camera");
        
            _cameraBuffer.Clear();

            var drawRendererSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
            {
                sorting = {flags = SortFlags.CommonOpaque},
                // 开启动态批处理
                flags = _drawFlags
            };
            var  filterRendererSettings = new FilterRenderersSettings(true){renderQueueRange = RenderQueueRange.opaque};
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
        
            DrawDefaultPipeline(context, camera);
            context.DrawSkybox(camera);
        
            drawRendererSettings.sorting.flags = SortFlags.CommonTransparent ;
            filterRendererSettings.renderQueueRange=RenderQueueRange.transparent;
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
        
            _cameraBuffer.EndSample("Render Camera");
            context.ExecuteCommandBuffer(_cameraBuffer);
            _cameraBuffer.Clear();
            context.Submit();
        }

        private  Material _errorMaterial;
        
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        private  void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
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
