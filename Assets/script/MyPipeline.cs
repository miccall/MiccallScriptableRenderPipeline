using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;

// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable BitwiseOperatorOnEnumWithoutFlags
// ReSharper disable CheckNamespace

public class MyPipeline : RenderPipeline
{
    private const int maxVisibleLights = 16;
    private static  int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
    private static  int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    private static  int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
    private static  int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
    private static  int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
    private Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
    private CullResults _cull;
    private Material _errorMaterial;
    private readonly CommandBuffer _cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };
    private readonly DrawRendererFlags _drawFlags;
    
    public MyPipeline(bool dynamicBatching, bool instancing)
    {
        // 使用线性空间设置 
        GraphicsSettings.lightsUseLinearIntensity = true;
        // 开启 动态合批处理 
        if (dynamicBatching)
        {
            _drawFlags = DrawRendererFlags.EnableDynamicBatching;
        }

        // 开启 GPU 实例化 
        if (instancing)
        {
            _drawFlags |= DrawRendererFlags.EnableInstancing;
        }
    }

    public override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);
        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    private void Render(ScriptableRenderContext context, Camera camera)
    {
        // =================    剔除检查 ========================= 
        if (!CullResults.GetCullingParameters(camera, out var cullparamet)) return;

#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

        // 剔除 
        CullResults.Cull(ref cullparamet, context, ref _cull);
        context.SetupCameraProperties(camera);


        // ==================   clear   ========================== 

        var clearFlags = camera.clearFlags;

        _cameraBuffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );

        // 配置灯光信息 
        if (_cull.visibleLights.Count > 0) ConfigureLights();
        else _cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);

        _cameraBuffer.BeginSample("Render Camera");

        // 设置 灯光 缓冲区  
        _cameraBuffer.SetGlobalVectorArray(
            visibleLightColorsId, visibleLightColors
        );
        _cameraBuffer.SetGlobalVectorArray(
            visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
        );
        _cameraBuffer.SetGlobalVectorArray(
            visibleLightAttenuationsId, visibleLightAttenuations
        );
        _cameraBuffer.SetGlobalVectorArray(
            visibleLightSpotDirectionsId, visibleLightSpotDirections
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

        if (_cull.visibleLights.Count > 0)
        {
            drawRendererSettings.rendererConfiguration =
                RendererConfiguration.PerObjectLightIndices8;
        }


        var filterRendererSettings = new FilterRenderersSettings(true)
        {
            renderQueueRange = RenderQueueRange.opaque
        };

        context.DrawRenderers(_cull.visibleRenderers, ref drawRendererSettings, filterRendererSettings);

        // =========== Sky box ==================== 
        context.DrawSkybox(camera);


        // =========== Transparent ==================== 
        drawRendererSettings.sorting.flags = SortFlags.CommonTransparent;
        filterRendererSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(_cull.visibleRenderers, ref drawRendererSettings, filterRendererSettings);

        // =========== Default ==================== 
        DrawDefaultPipeline(context, camera);

        _cameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(_cameraBuffer);
        _cameraBuffer.Clear();
        context.Submit();
    }

    private void ConfigureLights()
    {
        for (var i = 0; i < _cull.visibleLights.Count; i++)
        {
            // 灯光数量限制
            if (i == maxVisibleLights) break;

            // 可用灯光 
            var light = _cull.visibleLights[i];
            //finalColor ： 灯光的颜色乘以其强度
            visibleLightColors[i] = light.finalColor;
            // 衰减值 
            var attenuation = Vector4.zero;
            attenuation.w = 1f;

            // 平行光 ： 计算灯光方向 ，位置无关 
            if (light.lightType == LightType.Directional)
            {
                var v = light.localToWorld.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visibleLightDirectionsOrPositions[i] = v;
            }
            else
            {
                // point light

                visibleLightDirectionsOrPositions[i] = light.localToWorld.GetColumn(3);
                attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
                if (light.lightType == LightType.Spot)
                {
                    var v = light.localToWorld.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightSpotDirections[i] = v;

                    var outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    var outerCos = Mathf.Cos(outerRad);
                    var outerTan = Mathf.Tan(outerRad);
                    var innerCos =
                        Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));
                    var angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;
                }
            }

            visibleLightAttenuations[i] = attenuation;
        }

        if (_cull.visibleLights.Count <= maxVisibleLights) return;
        {
            var lightIndices = _cull.GetLightIndexMap();
            for (var i = maxVisibleLights; i < _cull.visibleLights.Count; i++)
            {
                lightIndices[i] = -1;
            }

            _cull.SetLightIndexMap(lightIndices);
        }
    }

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    private void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (_errorMaterial == null)
        {
            var errorShader = Shader.Find("Hidden/InternalErrorShader");
            _errorMaterial = new Material(errorShader)
            {
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
        drawSettings.SetOverrideMaterial(_errorMaterial, 0);
        var filterSettings = new FilterRenderersSettings(true);

        context.DrawRenderers(
            _cull.visibleRenderers, ref drawSettings, filterSettings
        );
    }
}