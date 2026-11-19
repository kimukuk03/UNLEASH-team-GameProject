// World-space procedural facade for placeholder buildings (Built-in Render Pipeline).
// Windows, floors and lit windows are computed from world position, so any box of any size
// gets correctly scaled windows with a single shared material. Keep buildings axis-aligned.
Shader "CowMeat/BuildingFacade"
{
    Properties
    {
        _WallColor ("Wall Color", Color) = (0.55, 0.57, 0.60, 1)
        _RoofColor ("Roof Color", Color) = (0.20, 0.21, 0.23, 1)
        _WindowColor ("Window Color", Color) = (0.06, 0.10, 0.16, 1)
        _WindowLitColor ("Lit Window Color", Color) = (1.0, 0.82, 0.55, 1)
        _BayWidth ("Bay Width (m)", Float) = 3
        _FloorHeight ("Floor Height (m)", Float) = 4
        _WindowSize ("Window Size Ratio (x=width, y=height)", Vector) = (0.7, 0.6, 0, 0)
        _LobbyHeight ("Lobby Height (m)", Float) = 5
        _LitRatio ("Lit Window Ratio", Range(0, 1)) = 0.2
        _LitIntensity ("Lit Intensity", Range(0, 5)) = 1.2
        _WallSmoothness ("Wall Smoothness", Range(0, 1)) = 0.3
        _WindowSmoothness ("Window Smoothness", Range(0, 1)) = 0.9
        _Metallic ("Window Metallic", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _WallColor;
        fixed4 _RoofColor;
        fixed4 _WindowColor;
        fixed4 _WindowLitColor;
        float _BayWidth;
        float _FloorHeight;
        float4 _WindowSize;
        float _LobbyHeight;
        float _LitRatio;
        float _LitIntensity;
        float _WallSmoothness;
        float _WindowSmoothness;
        float _Metallic;

        // Optional (set per renderer via MaterialPropertyBlock, default off): evaluate the facade at the
        // object's ORIGINAL world placement so windows stick to moving debris instead of sliding.
        // Per-instance so moving debris keeps GPU instancing (one draw call for many pieces).
        UNITY_INSTANCING_BUFFER_START(FacadeProps)
            UNITY_DEFINE_INSTANCED_PROP(float4x4, _OrigObjectToWorld)
            UNITY_DEFINE_INSTANCED_PROP(float, _UseOrigSpace)
        UNITY_INSTANCING_BUFFER_END(FacadeProps)

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(IN.worldNormal);
            float3 wp = IN.worldPos;
            if (UNITY_ACCESS_INSTANCED_PROP(FacadeProps, _UseOrigSpace) > 0.5)
            {
                float3 op = mul(unity_WorldToObject, float4(wp, 1.0)).xyz;
                float4x4 orig = UNITY_ACCESS_INSTANCED_PROP(FacadeProps, _OrigObjectToWorld);
                wp = mul(orig, float4(op, 1.0)).xyz;
                n = normalize(mul((float3x3)orig, mul((float3x3)unity_WorldToObject, n)));
            }

            // Roofs / floors: flat roof material.
            if (abs(n.y) > 0.5)
            {
                o.Albedo = _RoofColor.rgb;
                o.Smoothness = 0.15;
                o.Metallic = 0;
                return;
            }

            bool xFacing = abs(n.x) > abs(n.z);
            float u = xFacing ? wp.z : wp.x;
            float w = xFacing ? wp.x : wp.z;
            float faceId = (xFacing ? sign(n.x) * 3.0 : sign(n.z) * 7.0) + floor(w / 1.37 + 0.31) * 0.173;
            float y = wp.y;

            float2 cell = float2(u / max(_BayWidth, 0.1), y / max(_FloorHeight, 0.1));
            float2 id = floor(cell);
            float2 f = frac(cell) - 0.5;
            float2 aa = max(fwidth(cell), 1e-4);
            float2 halfSize = _WindowSize.xy * 0.5;
            float2 m = 1.0 - smoothstep(halfSize - aa, halfSize + aa, abs(f));
            float mask = m.x * m.y;

            float h = Hash21(id + faceId * 17.13);
            float lit = step(h, _LitRatio);

            // Ground-floor lobby: tall glass band, mostly lit.
            if (y < _LobbyHeight)
            {
                mask = m.x * smoothstep(0.5, 0.8, y) * (1.0 - smoothstep(_LobbyHeight - 0.8, _LobbyHeight - 0.5, y));
                lit = 0.6;
            }

            // Fade to average at distance to reduce aliasing / moire.
            float fade = saturate(max(aa.x, aa.y) * 1.5);
            mask = lerp(mask, _WindowSize.x * _WindowSize.y, fade);
            lit = lerp(lit, _LitRatio, fade);

            float h2 = Hash21(id.yx + 3.7 + faceId);
            float3 litCol = _WindowLitColor.rgb * lerp(0.55, 1.0, h2);
            float3 windowAlbedo = _WindowColor.rgb * lerp(0.8, 1.25, h2);

            o.Albedo = lerp(_WallColor.rgb, windowAlbedo, mask);
            o.Emission = litCol * lit * mask * _LitIntensity;
            o.Smoothness = lerp(_WallSmoothness, _WindowSmoothness, mask);
            o.Metallic = lerp(0, _Metallic, mask);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
