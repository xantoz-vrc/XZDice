Shader "XZDice/NumberShader"
{
    Properties
    {
        _Number ("Number", Float) = 0.0
        _Digits ("Digits", Int) = 2
        [HDR]_Color ("Color", Color) = (1,1,1,1)
        [ToggleUI]_Pulse ("Pulsate every second", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _Color;
            float _Number;
            int _Digits;
            float _Pulse;

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                if (_Pulse > 0.0) {
                    o.uv = (v.uv - float2(0.5, 0.5))*(clamp(sin(frac(_Time.y)*3.1415), 0.75, 2.0)) + float2(0.5, 0.5);
                } else {
                    o.uv = v.uv;
                }
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            // --- distance to line segment with caps (From: https://shadertoyunofficial.wordpress.com/2019/01/02/programming-tricks-in-shadertoy-glsl/)
            float dist_to_line(float2 p, float2 a, float2 b)
            {
                p -= a, b -= a;
                float h = clamp(dot(p, b) / dot(b, b), 0.0, 1.0); // proj coord on line
                return length(p - b * h);                        // dist to segment
            }

            // Converts a distance to a color value. Use to plot linee by putting in the distance from UV to your line in question.
            float linefn(float a)
            {
                // return -clamp((1.0-pow(0.1/abs(a), .1)), -2, 0);
                // return -clamp((1.0-pow(0.1/abs(a), .2)), -2, 0);
                return clamp(smoothstep(a, 0.04, 2.0), 0.0, 1.0);
            }

            float number(float2 xy, float2 translation, int n)
            {
                float2 cpos = (frac(xy) - float2(0.5,0.5) - translation)*2;

                float val = 0.0;
                switch (n) {
                case 0:
                    val += linefn(dist_to_line(cpos, float2(-0.25, 0.0), float2(0.0, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.5), float2(0.25, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(0.25, 0.0), float2(0.0, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, -0.5), float2(-0.25, 0.0)));
                    break;
                case 1:
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.35), float2(0.0, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.5), float2(0.0, -0.5)));
                    break;
                case 2:
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.35), float2(0.0, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.5), float2(0.2, 0.35)));
                    val += linefn(dist_to_line(cpos, float2(0.2, 0.35), float2(-0.2, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(-0.2, -0.5), float2(0.2, -0.5)));
                    break;
                case 3:
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.5), float2(0.2, 0.2)));
                    val += linefn(dist_to_line(cpos, float2(0.2, 0.2), float2(-0.2, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.0), float2(0.2, -0.2)));
                    val += linefn(dist_to_line(cpos, float2(0.2, -0.2), float2(-0.2, -0.5)));
                    break;
                case 4:
                    val += linefn(dist_to_line(cpos, float2(0.3, 0.0), float2(-0.2, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.0), float2(0.1, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.1, 0.5), float2(0.0, -0.5)));
                    break;
                case 5:
                    val += linefn(dist_to_line(cpos, float2(0.2, 0.5), float2(-0.2, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.5), float2(-0.2, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(-0.2, 0.0), float2(0.2, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(0.2, 0.0), float2(0.2, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.2, -0.5), float2(-0.2, -0.5)));
                    break;
                case 6:
                    val += linefn(dist_to_line(cpos, float2(0.2, 0.5), float2(-0.3, -0.2)));
                    val += linefn(dist_to_line(cpos, float2(-0.3, -0.2), float2(0.1, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.1, -0.5), float2(0.25, -0.1)));
                    val += linefn(dist_to_line(cpos, float2(0.25, -0.1), float2(-0.3, -0.2)));
                    break;
                case 7:
                    val += linefn(dist_to_line(cpos, float2(-0.25, 0.5), float2(0.25, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.25, 0.5), float2(-0.05, -0.5)));
                    break;
                case 8:
                    val += linefn(dist_to_line(cpos, float2(-0.25, 0.25), float2(0.0, 0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.5), float2(0.25, 0.25)));
                    val += linefn(dist_to_line(cpos, float2(0.25, 0.25), float2(0.0, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.0), float2(-0.25, 0.25)));

                    val += linefn(dist_to_line(cpos, float2(-0.25, -0.25), float2(0.0, 0.0)));
                    val += linefn(dist_to_line(cpos, float2(0.0, 0.0), float2(0.25, -0.25)));
                    val += linefn(dist_to_line(cpos, float2(0.25, -0.25), float2(0.0, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.0, -0.5), float2(-0.25, -0.25)));
                    break;
                case 9:
                    val += linefn(dist_to_line(cpos, float2(0.1, 0.5), float2(0.05, -0.5)));
                    val += linefn(dist_to_line(cpos, float2(0.1, 0.5), float2(-0.3, 0.2)));
                    val += linefn(dist_to_line(cpos, float2(-0.3, 0.2), float2(-0.05, 0.05)));
                    val += linefn(dist_to_line(cpos, float2(-0.05, 0.05), float2(0.08, 0.14)));
                    break;
                }

                return clamp(val, 0.0, 1.0);
            }

            int get_digit(int number, int digit)
            {
                if (digit == 0) {
                    return int(_Number) % 10;
                } else {
                    return (int(_Number) / (10*digit)) % 10;
                }
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float val;
                for (int idx = 0; idx < _Digits; ++idx) {
                    // val += number(i.uv.xy, float2(0.25*(-idx) + ((_Digits)/2.0)*(0.25/2), 0.0), get_digit(_Number, idx));
                    val += number(i.uv.xy, float2(0.3*(-idx) + ((_Digits)/2.0)*(0.3/2), 0.0), get_digit(_Number, idx));
                }

                float4 col = val * _Color;

                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
