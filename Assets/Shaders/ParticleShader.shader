Shader "Custom/SPHParticle"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.2, 0.2, 1)
        _Size  ("Size",  Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct ParticleData
            {
                float3 position;
                float3 velocity;
                float2 density;
                float  _pad0;
                float  _pad1;
            };

            StructuredBuffer<ParticleData> _ParticleBuffer;
            float4 _Color;
            float  _Size;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v, uint instanceID : SV_InstanceID)
            {
                v2f o;
                ParticleData p = _ParticleBuffer[instanceID];

                // حرّك كل vertex حسب موقع الجزيء
                float3 worldPos = p.position + v.vertex.xyz * _Size;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1));

                // لوّن حسب السرعة
                float speed = saturate(length(p.velocity) / 3.0);
                o.color = lerp(_Color, float4(1,0.8,0,1), speed);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}