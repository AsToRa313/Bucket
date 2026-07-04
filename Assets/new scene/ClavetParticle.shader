Shader "Custom/ClavetParticle"
{
    Properties
    {
        _Size ("Size", Float) = 0.1
        _ColorSlow ("Color Slow", Color) = (0.2, 0.5, 0.95, 1)
        _ColorFast ("Color Fast", Color) = (0.9, 0.9, 1, 1)
        _SpeedScale ("Speed Scale", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            // فقط الـ buffers التي نمرّرها فعلاً (لا ColorBuffer)
            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _VelocityBuffer;

            float  _Size;
            float4 _ColorSlow;
            float4 _ColorFast;
            float  _SpeedScale;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(float4 vertex : POSITION, uint instanceID : SV_InstanceID)
            {
                v2f o;
                float3 center = _PositionBuffer[instanceID];
                float3 vel = _VelocityBuffer[instanceID];

                // موقع الرأس في العالم
                float3 worldPos = center + vertex.xyz * _Size;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));

                // اللون حسب السرعة (أزرق بطيء => فاتح سريع)
                float speed = saturate(length(vel) / max(0.001, _SpeedScale));
                o.color = lerp(_ColorSlow, _ColorFast, speed);
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
