Shader "Custom/SPHParticle"
{
    Properties {
        _Size ("Particle Size", Float) = 0.05
        _ColorSlow ("Color Slow", Color) = (0.1, 0.4, 0.9, 1)
        _ColorFast ("Color Fast", Color) = (1.0, 0.3, 0.0, 1)
        _SpeedScale ("Speed Scale", Float) = 3.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _VelocityBuffer;

            float _Size;
            float4 _ColorSlow;
            float4 _ColorFast;
            float _SpeedScale;

            struct appdata {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 pos = _PositionBuffer[instanceID];
                float3 vel = _VelocityBuffer[instanceID];

                // تحديد موقع وحجم الجزيئة
                float4 worldPos = float4(pos + v.vertex.xyz * _Size, 1.0);
                o.vertex = mul(UNITY_MATRIX_VP, worldPos);

                // التلوين بناءً على السرعة
                float speed = length(vel);
                float t = saturate(speed / _SpeedScale);
                o.color = lerp(_ColorSlow, _ColorFast, t);

                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                return i.color;
            }
            ENDCG
        }
    }
}