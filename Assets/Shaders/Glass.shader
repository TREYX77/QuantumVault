// Window glass. Clear glass is nearly invisible face on: what gives it away is a faint green tint
// over whatever lies behind it, reflections that strengthen toward grazing angles, sharp highlights
// from the lamps, and darker green edges where the eye looks through the pane's thickness. URP's Lit
// can only lay a pale film over the background instead, which reads as plastic. So this draws in
// two passes and uses no alpha: the first multiplies what is behind by the tint, the second adds the
// reflections and highlights on top. A laser seen through it stays a laser, only slightly tinted.
//
// Wear (experimental, off by default) makes glass easy to spot the way an old window is: curved
// hairline scratches that catch the light, a film of wipe marks and dust, and a slight unevenness
// that bends what the pane reflects. All of it is procedural and measured in metres across the
// face, so a pattern never stretches with the pane and no two panes of different sizes share one.
Shader "Puzzle/Glass"
{
    Properties
    {
        // What the glass lets through. The background is multiplied by it, so white is perfectly clear.
        _Tint("Tint", Color) = (0.92, 0.97, 0.955, 1)

        // The pane seen through its full width. Box meshes only: the rim is measured from the sides
        // of the unit cube, so a mesh of another shape would get it in odd places. 0 width turns it off.
        _EdgeTint("Edge Tint", Color) = (0.45, 0.68, 0.62, 1)
        _EdgeWidth("Edge Width (m)", Range(0, 0.2)) = 0.035

        // Reflectance head on. 0.04 is real window glass; it rises toward 1 at grazing angles.
        _Reflectance("Reflectance", Range(0, 0.25)) = 0.04
        _ReflectionStrength("Reflection Strength", Range(0, 3)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.96

        // Dust and smudges catching the light, just enough that the surface is there to see.
        _Haze("Haze", Range(0, 0.3)) = 0.015

        [Header(Wear)]
        [Toggle(_GLASS_WEAR)] _Wear("Scratches And Grime", Float) = 0

        // Changes the whole pattern. Panes of different sizes already differ without it.
        _WearSeed("Seed", Float) = 0

        // How many scratches, how tightly they curl, and how strongly they catch the light.
        _ScratchAmount("Scratch Amount", Range(0, 1)) = 0.6
        _ScratchCurl("Scratch Curl", Range(0, 1)) = 0.6
        _ScratchBrightness("Scratch Brightness", Range(0, 6)) = 1.5

        // A film of wipe marks and dust. Even at 1 it holds back only a little of the light through it.
        _Grime("Grime", Range(0, 1)) = 0.4

        // How much brighter the wear glows when a lamp shines through the pane toward the eye.
        _Backlight("Backlight", Range(0, 8)) = 2

        // How uneven the glass is, bending its reflections across the pane.
        _Warp("Warp", Range(0, 0.2)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Every property in one buffer, the same in every pass, so the SRP Batcher accepts it.
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half4 _EdgeTint;
            half _EdgeWidth;
            half _Reflectance;
            half _ReflectionStrength;
            half _Smoothness;
            half _Haze;
            float _WearSeed;
            half _ScratchAmount;
            half _ScratchCurl;
            half _ScratchBrightness;
            half _Grime;
            half _Backlight;
            half _Warp;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
            float3 normalOS : TEXCOORD3;
            half fogFactor : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);

            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);

            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionOS = input.positionOS.xyz;
            output.normalOS = input.normalOS;
            output.fogFactor = ComputeFogFactor(position.positionCS.z);

            return output;
        }

        // Schlick's approximation: the share reflected rather than let through, head on to grazing.
        half Fresnel(half3 normalWS, half3 viewDirWS)
        {
            half cosine = saturate(dot(normalWS, viewDirWS));

            return _Reflectance + (1.0h - _Reflectance) * pow(1.0h - cosine, 5.0h);
        }

        float3 ObjectScale()
        {
            float4x4 toWorld = GetObjectToWorldMatrix();

            return float3(length(toWorld._m00_m10_m20), length(toWorld._m01_m11_m21), length(toWorld._m02_m12_m22));
        }

        // 1 on the rim of a box face, 0 inside it. Measured in metres along the face to its nearest
        // side, using the object's scale, so a long pane and a short one get the same rim. The thin
        // sides of a pane are all rim, which is how the edge of a sheet of glass looks.
        half EdgeMask(float3 positionOS, float3 normalOS)
        {
            float3 toSide = (0.5 - abs(positionOS)) * ObjectScale();

            // The axis a face points along is the side it sits on, so it never counts.
            float3 facing = step(0.5, abs(normalOS)) * 1e4;
            float nearest = min(min(toSide.x + facing.x, toSide.y + facing.y), toSide.z + facing.z);

            return 1.0h - smoothstep(0.0h, max(_EdgeWidth, 1e-4h), nearest);
        }

        // ---- Wear -----------------------------------------------------------------------------

        struct GlassWear
        {
            half scratch;       // 0..1, a scratch under this point
            half3 scratchDir;   // world-space direction that scratch runs
            half grime;         // 0..1, wipe marks and dust, already scaled by _Grime
            half3 tilt;         // world-space nudge to the normal from the glass's unevenness
        };

        GlassWear NoWear()
        {
            GlassWear wear;
            wear.scratch = 0.0h;
            wear.scratchDir = half3(0.0h, 0.0h, 0.0h);
            wear.grime = 0.0h;
            wear.tilt = half3(0.0h, 0.0h, 0.0h);

            return wear;
        }

        #if defined(_GLASS_WEAR)

        // Sine-free hashes (Dave Hoskins), stable on every GPU.
        float Hash12(float2 p)
        {
            float3 p3 = frac(p.xyx * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);

            return frac((p3.x + p3.y) * p3.z);
        }

        float2 Hash22(float2 p)
        {
            float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yzx + 33.33);

            return frac((p3.xx + p3.yz) * p3.zy);
        }

        // Smooth noise, 0..1, with a slope that never jumps, so it can tilt a normal.
        float ValueNoise(float2 p)
        {
            float2 corner = floor(p);
            float2 f = frac(p);
            float2 blend = f * f * (3.0 - 2.0 * f);

            float a = Hash12(corner);
            float b = Hash12(corner + float2(1.0, 0.0));
            float c = Hash12(corner + float2(0.0, 1.0));
            float d = Hash12(corner + float2(1.0, 1.0));

            return lerp(lerp(a, b, blend.x), lerp(c, d, blend.x), blend.y);
        }

        float Fbm(float2 p)
        {
            return ValueNoise(p) * 0.55 + ValueNoise(p * 2.03 + 17.1) * 0.3 + ValueNoise(p * 4.07 + 41.3) * 0.15;
        }

        // A line never drawn thinner than a pixel. A thinner one is dimmed instead, though by less
        // than its share of the pixel, since a scratch scatters far more light than its width
        // suggests: in the reference photo, hairlines read from metres away.
        half ThinLine(float offset, float width, float pixel)
        {
            float drawn = max(max(width, pixel), 1e-6);

            return (1.0 - smoothstep(0.0, drawn, offset)) * sqrt(width / drawn);
        }

        // Half a scratch: an arc from the origin along heading, bending by bend (1/m, positive turns
        // left) for arcLength metres. Returns the distance from p to it, and gives how far along it
        // the nearest point is (0 at the start, 1 at the end) and which way it runs there.
        float HalfArc(float2 p, float2 heading, float bend, float arcLength, out float along, out float2 direction)
        {
            float2 left = float2(-heading.y, heading.x);
            float radius = 1.0 / abs(bend);
            float2 centre = left / bend;
            float2 fromCentre = p - centre;
            float2 startFromCentre = -centre;
            float sweep = abs(bend) * arcLength;

            // Signed angle round the centre from the start to p, positive the way the arc turns.
            float cross2 = startFromCentre.x * fromCentre.y - startFromCentre.y * fromCentre.x;
            float angle = atan2(cross2, dot(startFromCentre, fromCentre)) * sign(bend);

            if (angle >= 0.0 && angle <= sweep)
            {
                float r = length(fromCentre);

                along = angle / sweep;
                direction = float2(-fromCentre.y, fromCentre.x) / max(r, 1e-5);

                return abs(r - radius);
            }

            // Past either end, so the nearer end.
            float turn = sweep * sign(bend);
            float2 end = centre + float2(cos(turn) * startFromCentre.x - sin(turn) * startFromCentre.y,
                                         sin(turn) * startFromCentre.x + cos(turn) * startFromCentre.y);
            float toStart = length(p);
            float toEnd = length(p - end);

            along = toStart < toEnd ? 0.0 : 1.0;
            direction = heading;

            return min(toStart, toEnd);
        }

        // One layer of scratches: at most one per cell, each free to reach into the cells round it.
        // A scratch is two half-arcs out from its middle: bending on round the same circle they make
        // a C, bending opposite ways an S. Mostly gentle curves, now and then a tight curl, and each
        // thins toward its ends and breaks up along its length the way real hairlines do. Keeps the
        // strongest one found, in strength and direction, across layers.
        void ScratchLayer(float2 uv, float cellSize, float width, half chance, half strength, float maxBend, float pixel,
                          inout half strongest, inout float2 strongestDir)
        {
            float2 cell = floor(uv / cellSize);

            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 id = cell + float2(x, y);

                    if (Hash12(id + 17.31) > chance) continue;

                    float2 random = Hash22(id);
                    float heading = Hash12(id + 3.3) * TWO_PI;
                    float2 forward = float2(cos(heading), sin(heading));

                    float curl = Hash12(id + 6.1);
                    float bend = lerp(0.4, maxBend, curl * curl) * (random.y < 0.5 ? -1.0 : 1.0);

                    // Within a cell of its middle, so the cells searched always hold all of it, and
                    // never round more than half a turn per half.
                    float halfLength = min(cellSize * (0.35 + 0.65 * Hash12(id + 5.7)), 2.8 / abs(bend));
                    float otherBend = Hash12(id + 8.8) < 0.35 ? bend : -bend;

                    float2 p = uv - (id + random) * cellSize;
                    float alongA;
                    float alongB;
                    float2 directionA;
                    float2 directionB;
                    float toA = HalfArc(p, forward, bend, halfLength, alongA, directionA);
                    float toB = HalfArc(p, -forward, otherBend, halfLength, alongB, directionB);

                    bool nearA = toA < toB;
                    float fromMiddle = nearA ? alongA : alongB;
                    float lengthwise = nearA ? 0.5 + 0.5 * alongA : 0.5 - 0.5 * alongB;

                    float taper = 1.0 - fromMiddle * fromMiddle * fromMiddle;
                    float breakUp = smoothstep(0.25, 0.65, ValueNoise(float2(lengthwise * 7.0, Hash12(id + 2.2) * 50.0)));

                    half scratch = ThinLine(nearA ? toA : toB, width * taper, pixel) * breakUp * strength * (0.4h + 0.6h * Hash12(id + 9.1));

                    if (scratch > strongest)
                    {
                        strongest = scratch;
                        strongestDir = nearA ? directionA : directionB;
                    }
                }
            }
        }

        // Scratches, grime and warp for this point. The face it is on is laid out in metres, from
        // the object's scale, and directions on it are turned into world space along that face.
        GlassWear EvaluateWear(float3 positionOS, float3 normalOS)
        {
            GlassWear wear = NoWear();

            float4x4 toWorld = GetObjectToWorldMatrix();
            float3 scale = ObjectScale();
            float3 metres = positionOS * scale;
            float3 facing = abs(normalOS);

            float2 uv;
            float3 axisU;
            float3 axisV;

            if (facing.z >= facing.x && facing.z >= facing.y)
            {
                uv = metres.xy;
                axisU = toWorld._m00_m10_m20 / scale.x;
                axisV = toWorld._m01_m11_m21 / scale.y;
            }
            else if (facing.x >= facing.y)
            {
                uv = metres.zy;
                axisU = toWorld._m02_m12_m22 / scale.z;
                axisV = toWorld._m01_m11_m21 / scale.y;
            }
            else
            {
                uv = metres.xz;
                axisU = toWorld._m00_m10_m20 / scale.x;
                axisV = toWorld._m02_m12_m22 / scale.z;
            }

            // Each pane its own pattern, from its size and the seed.
            float2 paneSeed = Hash22(scale.xy * 7.13 + scale.z * 3.71 + _WearSeed);
            uv += paneSeed * 100.0;

            float pixel = length(fwidth(uv));

            // Faint fine hairlines everywhere, then medium scratches, then a few long bright sweeps.
            half scratch = 0.0h;
            float2 scratchDir = float2(1.0, 0.0);
            ScratchLayer(uv, 0.08, 0.0002, _ScratchAmount, 0.45h, lerp(2.0, 40.0, _ScratchCurl), pixel, scratch, scratchDir);
            ScratchLayer(uv + 31.7, 0.25, 0.0003, _ScratchAmount * 0.8h, 0.75h, lerp(1.0, 14.0, _ScratchCurl), pixel, scratch, scratchDir);
            ScratchLayer(uv + 77.3, 0.7, 0.0005, _ScratchAmount * 0.5h, 1.0h, lerp(0.5, 5.0, _ScratchCurl), pixel, scratch, scratchDir);

            wear.scratch = scratch;
            wear.scratchDir = normalize(axisU * scratchDir.x + axisV * scratchDir.y);

            // Wipe marks: noise stretched along one direction per pane, the way a cloth streaks it,
            // over a finer mottle, plus the odd speck of dust.
            float wipeAngle = Hash12(paneSeed * 57.0) * PI;
            float2 wipe = float2(cos(wipeAngle), sin(wipeAngle));
            float2 streak = float2(dot(uv, wipe), dot(uv, float2(-wipe.y, wipe.x)));
            half film = saturate(Fbm(streak * float2(0.8, 2.5)) * 0.5h + Fbm(uv * 7.0 + 5.3) * 0.6h - 0.25h);

            float2 dustCell = floor(uv / 0.03);
            float2 dustRandom = Hash22(dustCell + 13.1);
            half dust = 0.0h;

            if (Hash12(dustCell + 4.4) < 0.06)
            {
                float2 speck = (dustCell + 0.2 + 0.6 * dustRandom) * 0.03;
                dust = ThinLine(length(uv - speck), 0.0006 + 0.0008 * dustRandom.x, pixel);
            }

            wear.grime = saturate(film + dust) * _Grime;

            // Old glass is never quite flat. The slope of slow noise, plus a finer ripple, tilts the
            // normal, so reflections and highlights bend as the eye moves along the pane.
            const float delta = 0.05;
            float2 broad = uv * 1.4;
            float2 fine = uv * 4.3 + 11.7;
            float broadHere = ValueNoise(broad);
            float fineHere = ValueNoise(fine);
            float2 slope = float2(ValueNoise(broad + float2(delta, 0.0)) - broadHere, ValueNoise(broad + float2(0.0, delta)) - broadHere)
                         + float2(ValueNoise(fine + float2(delta, 0.0)) - fineHere, ValueNoise(fine + float2(0.0, delta)) - fineHere) * 0.35;

            wear.tilt = (axisU * slope.x + axisV * slope.y) / delta * _Warp;

            return wear;
        }

        #endif

        GlassWear SampleWear(float3 positionOS, float3 normalOS)
        {
            #if defined(_GLASS_WEAR)
                return EvaluateWear(positionOS, normalOS);
            #else
                return NoWear();
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Tint"

            // URP draws this tag alongside UniversalForward, so one material does both passes.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend DstColor Zero
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            // 3.0 for the screen-space derivatives that keep thin scratches from shimmering.
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment TintFrag
            #pragma shader_feature_local _GLASS_WEAR
            #pragma multi_compile_instancing

            half4 TintFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                GlassWear wear = SampleWear(input.positionOS, input.normalOS);

                half3 normalWS = normalize(input.normalWS + wear.tilt);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half reflected = Fresnel(normalWS, viewDirWS);
                half3 through = lerp(_Tint.rgb, _EdgeTint.rgb, EdgeMask(input.positionOS, input.normalOS));

                // Scratches and grime scatter what passes through them, so less of the background
                // gets through there; the next pass adds the light they scatter.
                half blocked = saturate(wear.scratch * 0.15h + wear.grime * 0.15h);

                // What the glass reflects it does not let through.
                return half4(through * (1.0h - reflected) * (1.0h - blocked), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment ReflectFrag
            #pragma shader_feature_local _GLASS_WEAR

            // Highlights from every lamp, and the probes for the reflections. Forward+ keeps both
            // its probes and its lights in the cluster loop.
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // A light's highlight in the glass, the little its haze scatters back, and whatever its
            // scratches and grime scatter. Unshadowed: a pane is too thin for a shadow on it to
            // matter, and it keeps the pass cheap.
            half3 FromLight(Light light, BRDFData brdf, half3 normalWS, half3 viewDirWS, GlassWear wear)
            {
                half facing = dot(normalWS, light.direction);
                half3 lightColor = light.color * light.distanceAttenuation;
                half3 highlight = brdf.specular * DirectBRDFSpecular(brdf, normalWS, light.direction, viewDirWS);

                // A scratch is a groove, and throws light back like a thin cylinder: brightest where
                // the light and the eye make equal angles with it, so different scratches catch as
                // the player moves, while the rest stay faintly visible.
                half alongScratch = abs(dot(SafeNormalize(light.direction + viewDirWS), wear.scratchDir));
                half glint = 0.15h + pow(saturate(1.0h - alongScratch), 4.0h);

                // Wear seen against a lamp behind the pane glows, the way a dirty window lights up
                // against the sun.
                half backlit = 1.0h + _Backlight * pow(saturate(dot(-viewDirWS, light.direction)), 6.0h);

                half scatter = (wear.scratch * _ScratchBrightness * glint + wear.grime * 0.5h) * backlit;

                // Haze is lit from the front only; wear scatters light arriving from either side.
                return lightColor * ((highlight + _Haze * _Tint.rgb) * saturate(facing) + scatter * (0.25h + 0.75h * abs(facing)));
            }

            half4 ReflectFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                GlassWear wear = SampleWear(input.positionOS, input.normalOS);

                // Ambient light comes from everywhere, so scratches get only their faint share of it.
                half ambientScatter = wear.scratch * _ScratchBrightness * 0.15h + wear.grime * 0.5h;

                half3 normalWS = normalize(input.normalWS + wear.tilt);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // Specular only: glass has no diffuse colour of its own.
                half alpha = 1.0h;
                BRDFData brdf;
                InitializeBRDFDataDirect(half3(0.0h, 0.0h, 0.0h), half3(0.0h, 0.0h, 0.0h), _Reflectance.xxx, _Reflectance, 1.0h - _Reflectance, _Smoothness, alpha, brdf);

                // The room, mirrored, as strongly as the angle says.
                half3 reflectWS = reflect(-viewDirWS, normalWS);
                half3 color = GlossyEnvironmentReflection(reflectWS, input.positionWS, brdf.perceptualRoughness, 1.0h, screenUV)
                              * Fresnel(normalWS, viewDirWS) * _ReflectionStrength;

                // Ambient caught by the haze and the wear, so neither vanishes where no lamp reaches.
                color += SampleSH(normalWS) * (_Haze * _Tint.rgb + ambientScatter);

                uint meshRenderingLayers = GetMeshRenderingLayer();

                Light mainLight = GetMainLight();

                #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
                #endif
                    color += FromLight(mainLight, brdf, normalWS, viewDirWS, wear);

                #if defined(_ADDITIONAL_LIGHTS)
                // The cluster loop reads these two from a variable by this name.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;

                #if USE_CLUSTER_LIGHT_LOOP
                // Forward+ keeps directional lights out of the clusters, ahead of the rest.
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);

                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                    #endif
                        color += FromLight(light, brdf, normalWS, viewDirWS, wear);
                }
                #endif

                uint pixelLightCount = GetAdditionalLightsCount();

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);

                    #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                    #endif
                        color += FromLight(light, brdf, normalWS, viewDirWS, wear);
                LIGHT_LOOP_END
                #endif

                // Fogs toward adding nothing, since this lands on a background that is already fogged.
                color = MixFogColor(color, half3(0.0h, 0.0h, 0.0h), InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor));

                // Left unclamped, so a lamp's highlight can still bloom.
                return half4(color, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
