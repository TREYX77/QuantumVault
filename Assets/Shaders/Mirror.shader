// The glass of a MirrorSurface. A silvered mirror shows no lighting of its own, only what it
// reflects, so this is unlit: the planar reflection a MirrorSurface renders for the player's view,
// or the reflection probes when there is none (edit mode, the Scene view, the minimap, and other
// mirrors' reflections, which would otherwise show a texture made for a different viewpoint).
Shader "Puzzle/Mirror"
{
    Properties
    {
        // The faint colour of the glass in front of the silver. Real mirror glass is very slightly green.
        _Tint("Tint", Color) = (0.93, 0.96, 0.94, 1)

        // How much light comes back. MirrorSurface overrides this per mirror.
        _Reflectivity("Reflectivity", Range(0, 1)) = 0.9

        // Sharpness of the probe fallback. Probes are blurry anyway, so a touch under 1 hides their seams.
        _FallbackSmoothness("Fallback Smoothness", Range(0, 1)) = 0.92

        // Set per mirror by MirrorSurface through a property block.
        [HideInInspector] _ReflectionTex("Reflection", 2D) = "black" {}
        [HideInInspector] _PlanarActive("Planar Active", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Every property in one buffer, the same in every pass, so the SRP Batcher accepts it.
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Reflectivity;
            half _FallbackSmoothness;
            half _PlanarActive;
        CBUFFER_END

        // Global, never a material property: MirrorReflection sets it to 1 only while the camera
        // the reflections were made for is drawing, so every other camera gets the probe look.
        float _MirrorPlanar;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            // Only what the probe fallback reads: Forward+ keeps its probes in the cluster loop.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionNDC : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = position.positionCS;
                output.positionNDC = position.positionNDC;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half3 color;

                if (_PlanarActive * _MirrorPlanar > 0.5)
                {
                    // positionNDC, not GetNormalizedScreenSpaceUV, because the reflection is an
                    // ordinary render texture and this keeps its bottom row at the bottom on every
                    // graphics API. The reflection camera stands the right way up behind the glass,
                    // so its picture is left-right reversed against ours: x is flipped back here.
                    float2 uv = input.positionNDC.xy / input.positionNDC.w;
                    uv.x = 1.0 - uv.x;

                    // Its camera already fogged the reflection over the whole path, eye to mirror
                    // to object, so fogging again here would count the first leg twice.
                    color = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uv).rgb;
                }
                else
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                    half3 reflectWS = reflect(-viewDirWS, normalize(input.normalWS));
                    half roughness = PerceptualSmoothnessToPerceptualRoughness(_FallbackSmoothness);

                    color = GlossyEnvironmentReflection(reflectWS, input.positionWS, roughness, 1.0h, screenUV);
                    color = MixFog(color, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));
                }

                // Left unclamped, so a reflected laser is still brighter than white and still blooms.
                return half4(color * _Tint.rgb * _Reflectivity, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment NothingFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light being drawn, the same as its own ShadowCasterPass.hlsl.
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 NothingFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);

                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        // SSAO on the PC renderer reads normals from this, so without it the glass would take
        // whatever was behind it and darken in the wrong places.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));

                return output;
            }

            // Packed the way URP's own Unlit DepthNormals pass packs it.
            void DepthNormalsFrag(Varyings input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalize(input.normalWS));
                    outNormalWS = half4(half3(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5))), 0.0);
                #else
                    outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
