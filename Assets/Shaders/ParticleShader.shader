Shader "Custom/SPHParticle"
{
    Properties
    {
        _Size  ("Size",  Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing 

            #include "UnityCG.cginc"

            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _VelocityBuffer;
            StructuredBuffer<float4> _ColorBuffer;   // ألوان ثابتة (لكشف الدوران)

            // الأسماء متل ما عم تبعتيها من الكود 14 تماماً
            float4 _ColorSlow;
            float4 _ColorFast;
            float  _SpeedScale;
            float  _Size;
            float  _UseFixedColor;   // 0 = حسب السرعة، 1 = ألوان ثابتة

            struct appdata 
            { 
                float4 vertex : POSITION; 
                uint instanceID : SV_InstanceID; // استقبال الـ ID من الكرت
            };
            
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                
                // جلب رقم الجزيء الحالي
                uint index = v.instanceID; 
                
                // جلب موقع الجزيء وسرعته من البفرز
                float3 center = _PositionBuffer[index];
                float3 vel = _VelocityBuffer[index];
                
                // حساب موقع الجزيء في العالم بناءً على حجمه
                float3 worldPos = center + v.vertex.xyz * _Size;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                
                // اللون: إما حسب السرعة أو لون ثابت (لكشف الدوران)
                if (_UseFixedColor > 0.5)
                {
                    o.color = _ColorBuffer[index];
                }
                else
                {
                    float speed = saturate(length(vel) / max(0.001, _SpeedScale));
                    o.color = lerp(_ColorSlow, _ColorFast, speed);
                }
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target 
            { 
                return i.color; 
            }
            ENDCG
        }
    }
}