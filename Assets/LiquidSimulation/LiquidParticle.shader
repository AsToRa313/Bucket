// =============================================================
// LiquidParticle.shader
// Shader لتصيير جسيمات السائل مع URP 17.x في Unity 6
// يقرأ مواقع الجسيمات مباشرة من الـ GraphicsBuffer على الـ GPU
// =============================================================

Shader "Custom/LiquidParticle"
{
    Properties
    {
        _Color        ("لون السائل",  Color)       = (0.15, 0.45, 0.95, 0.85)
        _ParticleSize ("حجم الجسيم",  Float)       = 0.06
        _Smoothness   ("النعومة",     Range(0, 1)) = 0.75
        _FresnelPower ("قوة Fresnel", Range(0, 5)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent-10"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // إعدادات المزج للشفافية
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM

            // نستهدف Shader Model 4.5 لدعم StructuredBuffer
            #pragma target 4.5

            #pragma vertex   vert
            #pragma fragment frag

            // دعم إضاءة URP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            // تضمين مكتبات URP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // =========================================================
            // هيكل الجسيم - يجب أن يطابق C# و .compute بالضبط
            // =========================================================
            struct Particle
            {
                float3 position; // 12 byte
                float3 velocity; // 12 byte
                float  density;  //  4 byte
                float  pressure; //  4 byte
            };

            // مخزن الجسيمات (للقراءة فقط في الـ Shader)
            // يُمرر من C# عبر MaterialPropertyBlock.SetBuffer
            StructuredBuffer<Particle> _Particles;

            // =========================================================
            // المتغيرات الموحدة (Properties)
            // يجب وضعها داخل CBUFFER_START/END في URP
            // =========================================================
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _ParticleSize;
                float  _Smoothness;
                float  _FresnelPower;
            CBUFFER_END

            // =========================================================
            // هياكل الـ Vertex و Fragment
            // =========================================================
            struct Attributes
            {
                float4 positionOS : POSITION;  // موقع الـ vertex في local space
                float3 normalOS   : NORMAL;    // الوجه الطبيعي
                uint   instanceID : SV_InstanceID; // رقم الجسيم (0، 1، 2...)
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION; // موقع في Clip Space
                float3 normalWS    : TEXCOORD0;   // الوجه الطبيعي في World Space
                float3 positionWS  : TEXCOORD1;   // الموقع في World Space
                float3 viewDirWS   : TEXCOORD2;   // اتجاه الرؤية
                float  speed       : TEXCOORD3;   // السرعة (لتلوين ديناميكي)
                float  fogFactor   : TEXCOORD4;   // عامل الضباب
            };

            // =========================================================
            // Vertex Shader
            // يُشغَّل مرة لكل vertex لكل جسيم
            // =========================================================
            Varyings vert(Attributes input)
            {
                Varyings output;

                // قراءة بيانات الجسيم المقابل من المخزن
                // instanceID يأتي تلقائياً من DrawMeshInstancedProcedural
                Particle p = _Particles[input.instanceID];

                // الموقع النهائي = موقع الجسيم + شكل الـ Mesh مكبَّر بحجم الجسيم
                float3 worldPos = p.position + input.positionOS.xyz * _ParticleSize;

                // حساب السرعة للتلوين التفاعلي
                float spd = length(p.velocity);

                // تحويل إلى Clip Space
                output.positionCS = TransformWorldToHClip(worldPos);

                // تحويل الوجه الطبيعي إلى World Space
                output.normalWS   = normalize(TransformObjectToWorldNormal(input.normalOS));

                output.positionWS = worldPos;
                output.viewDirWS  = GetWorldSpaceViewDir(worldPos);
                output.speed      = spd;

                // حساب عامل الضباب
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            // =========================================================
            // Fragment Shader
            // يُشغَّل مرة لكل pixel في كل جسيم
            // =========================================================
            half4 frag(Varyings input) : SV_Target
            {
                float3 normalWS   = normalize(input.normalWS);
                float3 viewDirWS  = normalize(input.viewDirWS);

                // ─── الضوء الرئيسي ───
                Light mainLight = GetMainLight();
                float NdotL     = saturate(dot(normalWS, mainLight.direction));

                // ─── الإضاءة المحيطة (Ambient) ───
                float3 ambient = SampleSH(normalWS) * 0.35;

                // ─── الإضاءة الانتشارية (Diffuse) ───
                float3 diffuse = mainLight.color * NdotL * 0.8;

                // ─── الإضاءة الانعكاسية (Specular) - Blinn-Phong ───
                float3 halfDir  = normalize(mainLight.direction + viewDirWS);
                float  NdotH    = saturate(dot(normalWS, halfDir));
                float  specPow  = exp2(_Smoothness * 9.0 + 2.0);
                float3 specular = mainLight.color * pow(NdotH, specPow) * _Smoothness * 0.6;

                // ─── تأثير Fresnel ───
                // يجعل حواف الجسيمات أكثر إضاءة (مثل الماء الحقيقي)
                float  fresnel     = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                float3 fresnelColor = mainLight.color * fresnel * 0.3;

                // ─── تلوين ديناميكي بناءً على السرعة ───
                // الجسيمات السريعة أفتح قليلاً (أكثر حيوية)
                float  speedFactor = saturate(input.speed * 0.15);
                float3 baseColor   = lerp(_Color.rgb, _Color.rgb * 1.4 + float3(0.1, 0.1, 0.2), speedFactor);

                // ─── إجمالي اللون ───
                float3 finalColor = baseColor * (ambient + diffuse)
                                  + specular
                                  + fresnelColor;

                // تطبيق الضباب
                finalColor = MixFog(finalColor, input.fogFactor);

                // الشفافية: ثابتة من الـ Color مع زيادة خفيفة في الحواف
                float alpha = _Color.a + fresnel * 0.1;

                return half4(finalColor, saturate(alpha));
            }

            ENDHLSL
        }

        // ─── Shadow Pass ───
        // نُبقيه فارغاً لأن الجسيمات الصغيرة لا تحتاج ظلالاً
        // إذا أردت تفعيل الظلال، أضف Pass هنا بـ LightMode = ShadowCaster
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
