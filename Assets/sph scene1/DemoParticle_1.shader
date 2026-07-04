Shader "Custom/DemoParticle"
{
    Properties
    {
        _Size ("Size", Float) = 0.08
        _ColorSlow ("Color Slow", Color) = (0.2, 0.5, 0.95, 1)
        _ColorFast ("Color Fast", Color) = (0.9, 0.3, 0.1, 1)
        _SpeedScale ("Speed Scale", Float) = 3
        _UseFixedColor ("Use Fixed Color", Float) = 0
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

            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<float3> _VelocityBuffer;
            StructuredBuffer<float4> _ColorBuffer;

            float  _Size;
            float4 _ColorSlow;
            float4 _ColorFast;
            float  _SpeedScale;
            float  _UseFixedColor;

            struct appdata
            {
                float4 vertex : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                uint index = v.instanceID;
                float3 center = _PositionBuffer[index];
                float3 vel = _VelocityBuffer[index];

                float3 worldPos = center + v.vertex.xyz * _Size;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));

                if (_UseFixedColor > 0.5)
                    o.color = _ColorBuffer[index];
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
