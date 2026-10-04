// NepFX: экранное освещение для Neptunia Game Maker R:Evolution (URP 12 / Unity 2021.3.39f1).
// Проходы: 0 трассировка GI+AO (половинное разрешение), 1 временное накопление, 2/3 билатеральное размытие,
//          4 композит + контактные тени (полное разрешение), 5 копирование.
Shader "Hidden/NepFX/Lighting"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #pragma target 4.5
        #pragma exclude_renderers gles gles3 glcore
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D_X_FLOAT(_NepDepth);
        TEXTURE2D_X_FLOAT(_NepCharMask);
        TEXTURE2D_X(_NepSource);
        TEXTURE2D_X(_NepTrace);
        TEXTURE2D_X(_NepHistory);
        TEXTURE2D_X(_NepBlurSrc);
        TEXTURE2D_X(_NepGI);
        TEXTURE2D_X(_MotionVectorTexture);
        SAMPLER(sampler_LinearClamp);
        SAMPLER(sampler_PointClamp);

        float4 _NepParams;    // x: радиус GI (м), y: сила GI, z: сила AO, w: номер кадра
        float4 _NepParams2;   // x: длина контактных теней (м), y: сила контактных теней, z: вес истории, w: режим отладки
        float4 _NepParams3;   // x: лучей на пиксель, y: шагов на луч, z: толщина объектов (м), w: есть векторы движения
        float4 _NepLight;     // xyz: направление НА основной свет (мир), w: 1 если свет есть
        float4 _NepSize;      // xy: размер полной картинки, zw: размер половинной
        float4 _NepParams4;   // x: сравнение до/после, y: сила эффекта на персонажах, z: есть маска персонажей, w: длина теней сверху
        float4x4 _NepPrevVP;
        float4 _NepBlobs[8];   // xyz: точка пола под персонажем, w: радиус
        float4 _NepBlobInfo;   // x: число, y: сила
        float4 _NepFade;       // x: начало затухания (м), y: конец, z: туман 0 нет 1 линейный 2 exp 3 exp2, w: плотность тумана
        float4 _NepFog2;       // x: начало линейного тумана, y: конец

        // Сколько эффекта оставить на этой глубине. Вдали картинка уже в тумане, и затенение по экрану
        // темнило сам туман вокруг стволов и веток: получались тёмные ореолы на фоне неба и скал.
        float NepFadeK(float eye)
        {
            float k = 1.0 - smoothstep(_NepFade.x, max(_NepFade.y, _NepFade.x + 0.01), eye);
            float vis = 1.0;
            if (_NepFade.z > 2.5)      { float f = _NepFade.w * eye; vis = exp(-f * f); }
            else if (_NepFade.z > 1.5) { vis = exp(-_NepFade.w * eye); }
            else if (_NepFade.z > 0.5) { vis = saturate((_NepFog2.y - eye) / max(_NepFog2.y - _NepFog2.x, 0.01)); }
            return k * vis;
        }
        float4 _NepSsr;        // x: включено, y: дальность (м), z: только пол, w: сила
        TEXTURE2D_X(_NepSSR);
        TEXTURE2D_X_FLOAT(_NepGlossMask);
        float _NepGlossVal;

        struct Attributes { uint vertexID : SV_VertexID; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            o.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return o;
        }

        // выборка глубины без производных (можно в циклах)
        float DepthLod(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_NepDepth, sampler_PointClamp, uv, 0).r;
        }

        bool IsSky(float rawDepth)
        {
        #if UNITY_REVERSED_Z
            return rawDepth <= 1e-6;
        #else
            return rawDepth >= 1.0 - 1e-6;
        #endif
        }

        float3 WorldPos(float2 uv, float rawDepth) { return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP); }
        float3 WorldPosAt(float2 uv) { return WorldPos(uv, DepthLod(uv)); }

        // Нормаль по буферу глубины: из двух соседей по каждой оси берём ближайшего (меньше ошибок на краях).
        float3 ReconstructNormal(float2 uv, float3 p, float2 texel)
        {
            float3 pr = WorldPosAt(uv + float2(texel.x, 0));
            float3 pl = WorldPosAt(uv - float2(texel.x, 0));
            float3 pu = WorldPosAt(uv + float2(0, texel.y));
            float3 pd = WorldPosAt(uv - float2(0, texel.y));
            float3 dx1 = pr - p, dx2 = p - pl;
            float3 dy1 = pu - p, dy2 = p - pd;
            float3 dx = dot(dx1, dx1) < dot(dx2, dx2) ? dx1 : dx2;
            float3 dy = dot(dy1, dy1) < dot(dy2, dy2) ? dy1 : dy2;
            float3 n = normalize(cross(dy, dx));
            if (dot(n, _WorldSpaceCameraPos - p) < 0) n = -n;
            return n;
        }

        float IGN(float2 pix, float frame)
        {
            pix += frame * 5.588238;
            return frac(52.9829189 * frac(0.06711056 * pix.x + 0.00583715 * pix.y));
        }

        float3 CosineDir(float3 n, float u1, float u2)
        {
            float r = sqrt(u1);
            float phi = 6.2831853 * u2;
            float3 t = normalize(abs(n.y) < 0.99 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0)));
            float3 b = cross(n, t);
            return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(saturate(1 - u1)));
        }

        float EyeDepthOf(float3 wpos) { return -mul(UNITY_MATRIX_V, float4(wpos, 1)).z; }

        // короткий луч по буферу глубины: 1 если путь к свету свободен, меньше 1 если его что-то закрывает
        float ContactRay(float3 p, float3 n, float3 L, float len, int steps, float jitter, float maxThick)
        {
            [loop] for (int s = 1; s <= steps; s++)
            {
                float t = len * (s - 1 + jitter) / steps + 0.015;
                float3 q = p + n * 0.01 + L * t;
                float4 ndc = ComputeClipSpacePosition(q, UNITY_MATRIX_VP);
                float2 quv = ndc.xy / ndc.w * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                quv.y = 1 - quv.y;
            #endif
                if (any(quv < 0) || any(quv > 1)) return 1.0;
                float sraw = DepthLod(quv);
                if (IsSky(sraw)) continue;
                float diff = EyeDepthOf(q) - LinearEyeDepth(sraw, _ZBufferParams);
                if (diff > 0.01 && diff < maxThick) return (float)(s - 1) / steps;
            }
            return 1.0;
        }
        ENDHLSL

        // 0: трассировка GI + AO
        Pass
        {
            Name "Trace"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                float raw = DepthLod(i.uv);
                if (IsSky(raw)) return float4(0, 0, 0, 1);
                float2 texel = 1.0 / _NepSize.xy;
                float3 p = WorldPos(i.uv, raw);
                float3 n = ReconstructNormal(i.uv, p, texel);
                float3 origin = p + n * 0.02;

                int rays = (int)_NepParams3.x;
                int steps = (int)_NepParams3.y;
                float radius = _NepParams.x;
                float thickness = _NepParams3.z;
                float2 pix = i.uv * _NepSize.zw;

                float3 gi = 0;
                float occl = 0;
                [loop] for (int r = 0; r < rays; r++)
                {
                    float u1 = IGN(pix, _NepParams.w * 2 + r);
                    float u2 = IGN(pix + 37.0, _NepParams.w * 2 + r + 13);
                    float3 dir = CosineDir(n, u1, u2);
                    float jitter = IGN(pix + 71.0, _NepParams.w + r * 3);
                    [loop] for (int s = 1; s <= steps; s++)
                    {
                        float t = radius * ((s - 1 + jitter) / steps) * ((s - 1 + jitter) / steps) + 0.03;
                        float3 q = origin + dir * t;
                        float4 ndc = ComputeClipSpacePosition(q, UNITY_MATRIX_VP);
                        float2 quv = ndc.xy / ndc.w * 0.5 + 0.5;
                    #if UNITY_UV_STARTS_AT_TOP
                        quv.y = 1 - quv.y;
                    #endif
                        if (any(quv < 0) || any(quv > 1)) break;
                        float sraw = DepthLod(quv);
                        if (IsSky(sraw)) continue;
                        float sceneEye = LinearEyeDepth(sraw, _ZBufferParams);
                        float rayEye = EyeDepthOf(q);
                        float diff = rayEye - sceneEye;
                        if (diff > 0.02 && diff < thickness + t * 0.25)
                        {
                            float3 hitCol = SAMPLE_TEXTURE2D_X_LOD(_NepSource, sampler_LinearClamp, quv, 0).rgb;
                            // яркие источники вроде светящихся сфер дают одиночные вспышки, ограничиваем яркость отсвета
                            float hl = dot(hitCol, float3(0.2126, 0.7152, 0.0722));
                            hitCol *= min(1.0, 1.5 / max(hl, 1e-4));
                            gi += hitCol;
                            occl += 1.0 - t / radius;
                            break;
                        }
                    }
                }
                gi /= max(rays, 1);
                float ao = 1.0 - saturate(occl / max(rays, 1));
                float occ = lerp(1.0, ao, saturate(_NepParams.z));

                // контактные тени считаются здесь, в половинном разрешении: дальше их сглаживают накопление кадров и размытие
                float cj = IGN(pix + 113.0, _NepParams.w);
                float csv = 1.0;
                if (_NepLight.w > 0.5 && _NepParams2.y > 0 && _NepParams2.x > 0.01)
                {
                    float3 L = normalize(_NepLight.xyz);
                    if (dot(n, L) > 0.02)
                        csv = min(csv, lerp(1.0 - _NepParams2.y, 1.0, ContactRay(p, n, L, _NepParams2.x, 12, cj, 0.25)));
                }
                if (_NepParams4.w > 0.01 && _NepParams2.y > 0)
                {
                    float3 U = normalize(float3(0.15, 1.0, 0.1));
                    // только пол и пологие поверхности рядом с камерой: на неровных стенах луч вверх упирался
                    // в соседние выступы той же стены и давал тёмные полосы под каждым уступом
                    float floorK = smoothstep(0.6, 0.85, n.y) * (1.0 - smoothstep(12.0, 20.0, EyeDepthOf(p)));
                    if (floorK > 0.01)
                        csv = min(csv, lerp(1.0 - _NepParams2.y * 0.8 * floorK, 1.0, ContactRay(p, n, U, _NepParams4.w, 14, frac(cj + 0.5), 0.15)));
                }
                return float4(gi, occ * csv);
            }
            ENDHLSL
        }

        // 1: временное накопление (перепроекция + отсечение по дисперсии соседей)
        Pass
        {
            Name "Temporal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                float4 cur = SAMPLE_TEXTURE2D_X(_NepTrace, sampler_PointClamp, i.uv);
                float raw = DepthLod(i.uv);
                if (IsSky(raw)) return cur;

                float2 prevUV;
                if (_NepParams3.w > 0.5)
                {
                    float2 mv = SAMPLE_TEXTURE2D_X(_MotionVectorTexture, sampler_PointClamp, i.uv).xy;
                    prevUV = i.uv - mv;
                }
                else
                {
                    float3 p = WorldPos(i.uv, raw);
                    float4 pc = mul(_NepPrevVP, float4(p, 1));
                    prevUV = pc.xy / pc.w * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    prevUV.y = 1 - prevUV.y;
                #endif
                }

                float2 ht = 1.0 / _NepSize.zw;
                float4 m1 = 0, m2 = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float4 c = SAMPLE_TEXTURE2D_X(_NepTrace, sampler_PointClamp, i.uv + float2(x, y) * ht);
                    m1 += c; m2 += c * c;
                }
                m1 /= 9; m2 /= 9;
                float4 sigma = sqrt(max(m2 - m1 * m1, 0));
                float4 hist = SAMPLE_TEXTURE2D_X(_NepHistory, sampler_LinearClamp, prevUV);
                hist = clamp(hist, m1 - sigma * 1.5, m1 + sigma * 1.5);

                bool valid = all(prevUV >= 0) && all(prevUV <= 1);
                float w = valid ? _NepParams2.z : 0;
                return lerp(cur, hist, w);
            }
            ENDHLSL
        }

        // 2: размытие по горизонтали, 3: по вертикали (с учётом глубины)
        Pass
        {
            Name "BlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Blur(float2 uv, float2 dir)
            {
                float centerEye = LinearEyeDepth(DepthLod(uv), _ZBufferParams);
                float4 sum = 0; float wsum = 0;
                [unroll] for (int k = -3; k <= 3; k++)
                {
                    float2 suv = uv + dir * k;
                    float eye = LinearEyeDepth(DepthLod(suv), _ZBufferParams);
                    float w = exp(-abs(eye - centerEye) / (0.03 * centerEye + 0.01)) * (1.0 - abs(k) / 4.0);
                    sum += SAMPLE_TEXTURE2D_X(_NepBlurSrc, sampler_LinearClamp, suv) * w;
                    wsum += w;
                }
                return sum / max(wsum, 1e-4);
            }
            float4 Frag(Varyings i) : SV_Target { return Blur(i.uv, float2(1.0 / _NepSize.z, 0)); }
            ENDHLSL
        }
        Pass
        {
            Name "BlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Blur(float2 uv, float2 dir)
            {
                float centerEye = LinearEyeDepth(DepthLod(uv), _ZBufferParams);
                float4 sum = 0; float wsum = 0;
                [unroll] for (int k = -3; k <= 3; k++)
                {
                    float2 suv = uv + dir * k;
                    float eye = LinearEyeDepth(DepthLod(suv), _ZBufferParams);
                    float w = exp(-abs(eye - centerEye) / (0.03 * centerEye + 0.01)) * (1.0 - abs(k) / 4.0);
                    sum += SAMPLE_TEXTURE2D_X(_NepBlurSrc, sampler_LinearClamp, suv) * w;
                    wsum += w;
                }
                return sum / max(wsum, 1e-4);
            }
            float4 Frag(Varyings i) : SV_Target { return Blur(i.uv, float2(0, 1.0 / _NepSize.w)); }
            ENDHLSL
        }

        // 4: композит + контактные тени
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D_X(_NepSource, sampler_PointClamp, i.uv);
                float raw = DepthLod(i.uv);
                int dbg = (int)_NepParams2.w;
                // 5: проверка вывода, не зависит от глубины
                if (dbg == 5) return float4(lerp(src.rgb, float3(1, 0, 0), 0.5), 1);
                // 6: буфер глубины (ближе светлее); если весь экран синий, глубины нет
                if (dbg == 6)
                {
                    if (IsSky(raw)) return float4(0, 0, 0.35, 1);
                    float e = LinearEyeDepth(raw, _ZBufferParams);
                    return float4((1.0 - saturate(e / 40.0)).xxx, 1);
                }
                if (_NepParams4.x > 0.5)
                {
                    float px = 1.5 / _NepSize.x;
                    if (abs(i.uv.x - 0.5) < px) return float4(1, 1, 1, 1);
                    if (i.uv.x < 0.5 && dbg == 0) return src;
                }
                if (IsSky(raw)) return dbg > 0 ? float4(0, 0, 0.35, 1) : src;
                float2 texel = 1.0 / _NepSize.xy;
                float3 p = WorldPos(i.uv, raw);
                float3 n = ReconstructNormal(i.uv, p, texel);

                // персонажи: маска хранит глубину их поверхности, совпадение с глубиной сцены значит, что пиксель принадлежит персонажу
                float charK = 1.0;
                if (_NepParams4.z > 0.5)
                {
                    float me = SAMPLE_TEXTURE2D_X_LOD(_NepCharMask, sampler_PointClamp, i.uv, 0).r;
                    float se = LinearEyeDepth(raw, _ZBufferParams);
                    if (me > 0 && abs(me - se) < 0.03 * se + 0.05) charK = saturate(_NepParams4.y);
                }
                float4 gi = SAMPLE_TEXTURE2D_X(_NepGI, sampler_LinearClamp, i.uv);
                gi.rgb = min(gi.rgb, 1.5);
                float fadeK = NepFadeK(LinearEyeDepth(raw, _ZBufferParams));
                gi.rgb *= fadeK;
                // в gi.a уже собраны затенение AO и контактные тени
                float ao = lerp(1.0, gi.a, charK * fadeK);
                float cs = 1.0;
                // мягкая тень на полу под персонажами, считается от их положения, а не по экрану: не мерцает и не зависит от ракурса
                if (_NepBlobInfo.x > 0.5 && charK >= 0.999 && n.y > 0.4)
                {
                    float shade = 1.0;
                    [loop] for (int b = 0; b < (int)_NepBlobInfo.x; b++)
                    {
                        float4 bl = _NepBlobs[b];
                        float h = p.y - bl.y;
                        if (h < -0.25 || h > 0.6) continue;
                        float d = length(p.xz - bl.xz) / bl.w;
                        float k = (1.0 - smoothstep(0.35, 1.0, d)) * (1.0 - saturate(abs(h) / 0.6));
                        shade = min(shade, 1.0 - _NepBlobInfo.y * k);
                    }
                    cs = min(cs, shade);
                }
                // отражения по экрану
                float4 ssr = 0;
                if (_NepSsr.x > 0.5)
                {
                    ssr = SAMPLE_TEXTURE2D_X_LOD(_NepSSR, sampler_LinearClamp, i.uv, 0);
                    float3 Vd = normalize(p - _WorldSpaceCameraPos);
                    float fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(-Vd, n)), 5.0);
                    ssr.a *= saturate(_NepSsr.w * (0.3 + 0.7 * fres)) * (charK >= 0.999 ? 1.0 : 0.0);
                }
                float lum = dot(src.rgb, float3(0.2126, 0.7152, 0.0722));
                float3 albedo = saturate(src.rgb / (lum + 0.35)) * 0.6;
                float3 outCol = src.rgb * ao * cs + gi.rgb * albedo * _NepParams.y * charK;
                if (_NepSsr.x > 0.5) outCol = lerp(outCol, ssr.rgb, saturate(ssr.a));
                if (dbg == 8) return float4(ssr.rgb * saturate(ssr.a * 3.0), 1);
                if (dbg == 7) return float4(charK < 1 ? float3(1, 0.3, 0.8) : src.rgb * 0.3, 1);

                if (dbg == 1) return float4(gi.rgb * _NepParams.y, 1);
                if (dbg == 2) return float4(ao.xxx, 1);
                if (dbg == 3) return float4(gi.a.xxx, 1);
                if (dbg == 4) return float4(n * 0.5 + 0.5, 1);
                return float4(outCol, src.a);
            }
            ENDHLSL
        }

        // 5: копирование
        Pass
        {
            Name "Copy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return SAMPLE_TEXTURE2D_X(_NepBlurSrc, sampler_PointClamp, i.uv); }
            ENDHLSL
        }
        // 6: копия глубины без MSAA
        Pass
        {
            Name "CopyDepth"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            Texture2D<float> _NepDepthSrc;
            float4 Frag(Varyings i) : SV_Target
            {
                return float4(_NepDepthSrc.Load(int3(int2(i.positionCS.xy), 0)), 0, 0, 1);
            }
            ENDHLSL
        }

        // 7: копия глубины с MSAA: берём ближайшую к камере из выборок
        Pass
        {
            Name "CopyDepthMS"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            Texture2DMS<float> _NepDepthSrcMS;
            float4 Frag(Varyings i) : SV_Target
            {
                int2 p = int2(i.positionCS.xy);
                uint w, h, n;
                _NepDepthSrcMS.GetDimensions(w, h, n);
                float d = _NepDepthSrcMS.Load(p, 0);
                for (uint k = 1; k < n; k++)
                {
                    float v = _NepDepthSrcMS.Load(p, k);
                #if UNITY_REVERSED_Z
                    d = max(d, v);
                #else
                    d = min(d, v);
                #endif
                }
                return float4(d, 0, 0, 1);
            }
            ENDHLSL
        }
            // 8: маска персонажей, в цель пишется глубина поверхности от камеры
        Pass
        {
            Name "CharMask"
            ZWrite On ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma vertex VertM
            #pragma fragment FragM
            struct AttrM { float4 positionOS : POSITION; };
            struct VaryM { float4 positionCS : SV_POSITION; float eye : TEXCOORD0; };
            VaryM VertM(AttrM v)
            {
                VaryM o;
                float3 w = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.eye = -TransformWorldToView(w).z;
                return o;
            }
            float4 FragM(VaryM i) : SV_Target { return float4(i.eye, _NepGlossVal, 0, 1); }
            ENDHLSL
        }
            // 9: отражения по экрану, половинное разрешение
        Pass
        {
            Name "SSR"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target
            {
                float raw = DepthLod(i.uv);
                if (IsSky(raw)) return 0;
                float2 texel = 1.0 / _NepSize.xy;
                float3 p = WorldPos(i.uv, raw);
                float3 n = ReconstructNormal(i.uv, p, texel);
                // блеск: 0 вода и мокрое по маске, 1 плюс весь пол, 2 все поверхности
                float gloss = 0;
                {
                    float2 gm = SAMPLE_TEXTURE2D_X_LOD(_NepGlossMask, sampler_PointClamp, i.uv, 0).rg;
                    float se = LinearEyeDepth(raw, _ZBufferParams);
                    if (gm.x > 0 && abs(gm.x - se) < 0.03 * se + 0.08) gloss = gm.y;
                }
                if (_NepSsr.z > 0.5 && n.y > 0.8) gloss = max(gloss, 0.5);
                if (_NepSsr.z > 1.5) gloss = max(gloss, 0.5);
                if (gloss <= 0.01) return 0;
                float3 V = normalize(p - _WorldSpaceCameraPos);
                float3 R = reflect(V, n);
                if (dot(R, n) <= 0.01) return 0;
                float maxD = _NepSsr.y;
                const int STEPS = 40;
                float3 o = p + n * 0.03;
                float prevT = 0.0, hitT = -1.0;
                float2 huv = 0;
                [loop] for (int s = 1; s <= STEPS; s++)
                {
                    float f = (float)s / STEPS;
                    float t = maxD * f * f + 0.05;
                    float3 q = o + R * t;
                    float4 ndc = ComputeClipSpacePosition(q, UNITY_MATRIX_VP);
                    if (ndc.w <= 0) break;
                    float2 quv = ndc.xy / ndc.w * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    quv.y = 1 - quv.y;
                #endif
                    if (any(quv < 0) || any(quv > 1)) break;
                    float sraw = DepthLod(quv);
                    if (!IsSky(sraw))
                    {
                        float diff = EyeDepthOf(q) - LinearEyeDepth(sraw, _ZBufferParams);
                        if (diff > 0 && diff < 0.25 + t * 0.08)
                        {
                            // уточнение точки попадания делением отрезка пополам
                            float a = prevT, b = t;
                            [unroll] for (int k = 0; k < 5; k++)
                            {
                                float m = (a + b) * 0.5;
                                float3 qm = o + R * m;
                                float4 nm = ComputeClipSpacePosition(qm, UNITY_MATRIX_VP);
                                float2 um = nm.xy / nm.w * 0.5 + 0.5;
                            #if UNITY_UV_STARTS_AT_TOP
                                um.y = 1 - um.y;
                            #endif
                                float dm = EyeDepthOf(qm) - LinearEyeDepth(DepthLod(um), _ZBufferParams);
                                if (dm > 0) { b = m; quv = um; } else a = m;
                            }
                            hitT = b; huv = quv;
                            break;
                        }
                    }
                    prevT = t;
                }
                if (hitT < 0) return 0;
                float3 col = SAMPLE_TEXTURE2D_X_LOD(_NepSource, sampler_LinearClamp, huv, 0).rgb;
                float2 e = min(huv, 1 - huv);
                float edge = saturate(min(e.x, e.y) * 10.0);
                float dist = 1.0 - saturate(hitT / maxD);
                float facing = saturate(dot(R, -V) * -1.0 + 1.0);
                return float4(col, edge * dist * facing * saturate(gloss * 1.5));
            }
            ENDHLSL
        }
    }
}
