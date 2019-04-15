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
        private  CullResults _cull ;
        private  Material _errorMaterial;
        private  readonly CommandBuffer _cameraBuffer = new CommandBuffer {
            name = "Render Camera"
        };
        
        public MyPipeline (bool dynamicBatching,bool instancing) {
            // 开启 动态合批处理 
            if (dynamicBatching) {
                _drawFlags = DrawRendererFlags.EnableDynamicBatching;
            }
            // 开启 GPU 实例化 
            if (instancing) {
                _drawFlags |= DrawRendererFlags.EnableInstancing;
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

        private  void Render (ScriptableRenderContext context, Camera camera) 
        {
            // =================    剔除检查 ========================= 
            if (!CullResults.GetCullingParameters(camera, out var cullparamet)) return;
            
        #if UNITY_EDITOR
            if(camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        #endif 
            
            // 剔除 
            CullResults.Cull(ref cullparamet, context , ref _cull);
            context.SetupCameraProperties(camera);
            
            
            // ==================   clear   ========================== 
            
            var clearFlags = camera.clearFlags;
       
            _cameraBuffer.ClearRenderTarget(
                (clearFlags & CameraClearFlags.Depth) != 0,
                (clearFlags & CameraClearFlags.Color) != 0,
                camera.backgroundColor
            );
            
            _cameraBuffer.BeginSample("Render Camera");
            context.ExecuteCommandBuffer(_cameraBuffer);
            _cameraBuffer.Clear();
            
            
            // ================== Draw  =============================
            
            // =========== Opaque ==================== 
            var drawRendererSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"))
            {
                sorting = {flags = SortFlags.CommonOpaque},
                // 开启动态批处理
                flags = _drawFlags
            };
            
            var filterRendererSettings = new FilterRenderersSettings(true)
            {
                renderQueueRange = RenderQueueRange.opaque
            };
            
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
            
            // =========== Sky box ==================== 
            context.DrawSkybox(camera);
            
            // =========== Transparent ==================== 
            drawRendererSettings.sorting.flags = SortFlags.CommonTransparent ;
            filterRendererSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(_cull.visibleRenderers,ref drawRendererSettings,filterRendererSettings);
        
            // =========== Default ==================== 
            DrawDefaultPipeline(context, camera);
            
            _cameraBuffer.EndSample("Render Camera");
            context.ExecuteCommandBuffer(_cameraBuffer);
            _cameraBuffer.Clear();
            context.Submit();
        }

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
