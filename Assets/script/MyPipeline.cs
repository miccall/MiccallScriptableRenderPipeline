using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;
// ReSharper disable InvertIf
// ReSharper disable PossibleLossOfFraction

// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable BitwiseOperatorOnEnumWithoutFlags
// ReSharper disable CheckNamespace

public class MyPipeline : RenderPipeline
{
    private const int maxVisibleLights = 16;
    private const string shadowsHardKeyword = "_SHADOWS_HARD";
    private const string shadowsSoftKeyword = "_SHADOWS_SOFT";
    
    private static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
    private static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    private static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
    private static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
    private static int lightIndicesOffsetAndCountID = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");
    private static int shadowMapId = Shader.PropertyToID("_ShadowMap");
    private static int worldToShadowMatricesId = Shader.PropertyToID("_WorldToShadowMatrices");
    private static int shadowBiasId = Shader.PropertyToID("_ShadowBias");
    private static int shadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
    private static int shadowDataId = Shader.PropertyToID("_ShadowData");
    
    private Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    private Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
    private Vector4[] shadowData = new Vector4[maxVisibleLights];
    private Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];
    
    private CullResults _cull;
    private Material _errorMaterial;
    
    // 阴影贴图 
    private RenderTexture shadowMap;
    
    private readonly CommandBuffer CameraBuffer = new CommandBuffer{name = "Render Camera"};
    private readonly CommandBuffer ShadowBuffer = new CommandBuffer{name = "Render Shadows"};
    
    private readonly DrawRendererFlags _drawFlags;
    private readonly int shadowMapSize;
    private int shadowTileCount;
    
    public MyPipeline(bool dynamicBatching, bool instancing,int shadowMapSize )
    {
        this.shadowMapSize = shadowMapSize;
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

    
    // ======================= render ============================== 
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

        // 配置灯光信息 
        if (_cull.visibleLights.Count > 0)
        {
            ConfigureLights();
            if (shadowTileCount > 0) {
                // 设置阴影贴图 
                RenderShadows(context);
            }
            else {
                CameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
                CameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
            }
        }
        else
        {
            CameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
            CameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
            CameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
        }

        // 更新相机信息
        context.SetupCameraProperties(camera);
        
        // ==================   clear   ========================== 

        var clearFlags = camera.clearFlags;

        CameraBuffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );

        CameraBuffer.BeginSample("Render Camera");

        // 设置 灯光 缓冲区  
        CameraBuffer.SetGlobalVectorArray(
            visibleLightColorsId, visibleLightColors
        );
        CameraBuffer.SetGlobalVectorArray(
            visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
        );
        CameraBuffer.SetGlobalVectorArray(
            visibleLightAttenuationsId, visibleLightAttenuations
        );
        CameraBuffer.SetGlobalVectorArray(
            visibleLightSpotDirectionsId, visibleLightSpotDirections
        );

        context.ExecuteCommandBuffer(CameraBuffer);
        CameraBuffer.Clear();


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

        CameraBuffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(CameraBuffer);
        CameraBuffer.Clear();
        context.Submit();
        
        // 清除 shadow map 
        if (shadowMap) {
            RenderTexture.ReleaseTemporary(shadowMap);
            shadowMap = null;
        }
        
    }

    
    // ====================   light =========================== 
    private void ConfigureLights()
    {
        shadowTileCount = 0;
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
            var shadow = Vector4.zero;

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
                    // 添加阴影 
                    var shadowLight = light.light;
                    if (shadowLight.shadows != LightShadows.None &&
                        _cull.GetShadowCasterBounds(i, out var shadowBounds )) {
                        shadowTileCount += 1;
                        shadow.x = shadowLight.shadowStrength;
                        shadow.y =
                            shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
                    }
                }
            }

            visibleLightAttenuations[i] = attenuation;
            shadowData[i] = shadow;
        }

        if (_cull.visibleLights.Count > maxVisibleLights)
        {
            var lightIndices = _cull.GetLightIndexMap();
            for (var i = maxVisibleLights; i < _cull.visibleLights.Count; i++)
            {
                lightIndices[i] = -1;
            }
            _cull.SetLightIndexMap(lightIndices);
        }
    }

    
    // error shader 
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

    private void RenderShadows (ScriptableRenderContext context) {
        
        //动态平铺参数 
        var tileIndex = 0;
        var hardShadows = false;
        var softShadows = false;
        
        int split;
        if (shadowTileCount <= 1) {
            split = 1;
        }
        else if (shadowTileCount <= 4) {
            split = 2;
        }
        else if (shadowTileCount <= 9) {
            split = 3;
        }
        else {
            split = 4;
        }
        
        // 使用 4x4的贴图来存放支持16盏灯的阴影贴图缩放
        var tileSize = shadowMapSize / split;
        var tileScale = 1f / split;
        var tileViewport = new Rect(0f, 0f, tileSize, tileSize);

        // 生成一张阴影贴图 
        shadowMap = RenderTexture.GetTemporary(
            shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap
        );
        //将纹理的滤镜模式设置为双线性,并使用clamp
        shadowMap.filterMode = FilterMode.Bilinear;
        shadowMap.wrapMode = TextureWrapMode.Clamp;
        
        // 渲染阴影之前，我们首先告诉GPU渲染到阴影贴图 
        CoreUtils.SetRenderTarget(ShadowBuffer , shadowMap,RenderBufferLoadAction.DontCare,RenderBufferStoreAction.Store,ClearFlag.Depth);
        
        // 开始渲染纹理 
        ShadowBuffer.BeginSample("Render Shadows");
        context.ExecuteCommandBuffer(ShadowBuffer);
        ShadowBuffer.Clear();
        

        // 遍历所有的灯
        for (var i = 0; i < _cull.visibleLights.Count; i++)
        {
            // 超过最大的灯 
            if (i == maxVisibleLights) break;
            // 
            if (shadowData[i].x <= 0f) continue;
            
            // V P 矩阵 viewMatrix and projectionMatrix 
            if (!_cull.ComputeSpotShadowMatricesAndCullingPrimitives(
                i, out var viewMatrix, out var projectionMatrix, out var splitData
            )) {
                //如果失败则跳过灯 将强度设置为零
                shadowData[i].x = 0f;
                continue;
            }
            
            // 视口 offset 来偏移阴影贴图的存放位置 
            float tileOffsetX = tileIndex % split ;
            float tileOffsetY = tileIndex / split ;
            tileViewport.x = tileOffsetX * tileSize;
            tileViewport.y = tileOffsetY * tileSize;
            
            
            // 如果只有1个图，就不用tile 
            if (split > 1)
            {
                //告诉GPU使用缩放的视口
                ShadowBuffer.SetViewport(tileViewport);
                // 缩放窗口，使得边缘数据不冲突
                ShadowBuffer.EnableScissorRect(new Rect(
                    tileViewport.x + 4f, tileViewport.y + 4f,
                    tileSize - 8f, tileSize - 8f
                ));
            }

            // 设置矩阵 ，执行并清除
            ShadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            ShadowBuffer.SetGlobalFloat(shadowBiasId, _cull.visibleLights[i].light.shadowBias);
            context.ExecuteCommandBuffer(ShadowBuffer);
            ShadowBuffer.Clear();
            
            //阴影设置
            var shadowSettings = new DrawShadowsSettings(_cull, i);
            context.DrawShadows(ref shadowSettings);
        
            // 是否反转矩阵 
            if (SystemInfo.usesReversedZBuffer) {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }
        
            // 从世界空间到阴影剪辑空间的转换矩阵 并且 ： 从 -1 1 到 0 1
            var scaleOffset = Matrix4x4.identity;
            scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
            scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
            // 阴影矩阵的数组
            worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

            if (split > 1)
            {
                // 更新矩阵计算再tile的图中
                var tileMatrix = Matrix4x4.identity;
                tileMatrix.m00 = tileMatrix.m11 = tileScale;
                tileMatrix.m03 = tileOffsetX * tileScale;
                tileMatrix.m13 = tileOffsetY * tileScale;
                worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
            }

            tileIndex += 1;
            if (shadowData[i].y <= 0f) {
                hardShadows = true;
            }
            else {
                softShadows = true;
            }
            
        }
        
        // 关闭裁切
        if (split > 1) ShadowBuffer.DisableScissorRect();
        
        // 设置 buffer 到 shader 
        ShadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);
        ShadowBuffer.SetGlobalMatrixArray(worldToShadowMatricesId, worldToShadowMatrices);
        ShadowBuffer.SetGlobalVectorArray(shadowDataId, shadowData);
        
        // 软阴影
        var invShadowMapSize = 1f / shadowMapSize;
        ShadowBuffer.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));
        
        // 启用软阴影 关键字 
        CoreUtils.SetKeyword(ShadowBuffer, shadowsHardKeyword, hardShadows);
        CoreUtils.SetKeyword(ShadowBuffer, shadowsSoftKeyword, softShadows);
        // 结束渲染纹理 
        ShadowBuffer.EndSample("Render Shadows");
        context.ExecuteCommandBuffer(ShadowBuffer);
        ShadowBuffer.Clear();
        
    }
}