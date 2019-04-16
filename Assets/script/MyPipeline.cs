using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;
// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming

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

        private const int maxVisibleLights = 4;
        private static readonly int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        private static readonly int visibleLightDirectionsId = Shader.PropertyToID("_VisibleLightDirections");
	
        Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
        Vector4[] visibleLightDirections = new Vector4[maxVisibleLights];
        
        public MyPipeline (bool dynamicBatching,bool instancing) {
            // 使用线性空间设置 
            GraphicsSettings.lightsUseLinearIntensity = true;
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
            
            // 配置灯光信息 
            ConfigureLights();
            _cameraBuffer.BeginSample("Render Camera");
            
            // 设置 灯光 缓冲区  
            _cameraBuffer.SetGlobalVectorArray(
                visibleLightColorsId, visibleLightColors
            );
            _cameraBuffer.SetGlobalVectorArray(
                visibleLightDirectionsId, visibleLightDirections
            );
            
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

        private void ConfigureLights()
        {
            // 遍历可见灯光 
            var i = 0 ;
            for (; i < _cull.visibleLights.Count; i++) {
                if (i == maxVisibleLights) {
                    break;
                }
                
                var light = _cull.visibleLights[i];
                //finalColor ： 灯光的颜色乘以其强度
                visibleLightColors[i] = light.finalColor;   
                
                // 计算灯光方向 
                var v = light.localToWorld.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visibleLightDirections[i] = v;
            }
            for (; i < maxVisibleLights; i++) {
                visibleLightColors[i] = Color.clear;
            }
            
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
