cbuffer TransformBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float3 LightDirection; 
    float AmbientLight;
    uint UseNormalMap; // The toggle from C#
    float3 Padding;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal   : NORMAL;
    float3 Tangent  : TANGENT;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position     : SV_POSITION;
    float3 WorldNormal  : NORMAL;
    float3 WorldTangent : TANGENT;
    float2 TexCoord     : TEXCOORD0;
};

Texture2D AtlasMap : register(t0); // Single texture holding all 4 images
SamplerState DiffuseSampler : register(s0);

VSOutput VSMain(VSInput input)
{
    VSOutput output;
    output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
    
    // Rotate both Normal and Tangent into world space
    output.WorldNormal  = mul(input.Normal,  (float3x3)World);
    output.WorldTangent = mul(input.Tangent, (float3x3)World);
    
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target
{
    float3 lightDir = normalize(LightDirection);

    // ==========================================
    // FLOOR PATH (Uses Atlas and Normal Maps)
    // ==========================================
    if (UseNormalMap == 1)
    {
        float2 wrappedUV = frac(input.TexCoord);
        float2 diffuseUV = wrappedUV * 0.5f; 
        float2 normalUV  = (wrappedUV * 0.5f) + float2(0.5f, 0.0f);

        float4 texColor = AtlasMap.Sample(DiffuseSampler, diffuseUV);
        float3 normalMapSample = AtlasMap.Sample(DiffuseSampler, normalUV).rgb;
        float3 localNormal = normalMapSample * 2.0f - 1.0f;

        float3 N = normalize(input.WorldNormal);
        float3 T = normalize(input.WorldTangent);
        T = normalize(T - dot(T, N) * N);
        float3 B = cross(N, T);

        float3 finalNormal = normalize((T * localNormal.x) + (B * localNormal.y) + (N * localNormal.z));
        
        float diffuseIntensity = max(dot(finalNormal, lightDir), 0.0f);
        float lighting = AmbientLight + ((1.0f - AmbientLight) * diffuseIntensity);

        return float4(texColor.rgb * lighting, texColor.a);
    }
    // ==========================================
    // CUBE PATH (Standard Texture and flat Normals)
    // ==========================================
    else 
    {
        // Standard sample without atlas math
        float4 texColor = AtlasMap.Sample(DiffuseSampler, input.TexCoord);
        
        // Standard flat lighting without normal map bumps
        float3 N = normalize(input.WorldNormal);
        float diffuseIntensity = max(dot(N, lightDir), 0.0f);
        float lighting = AmbientLight + ((1.0f - AmbientLight) * diffuseIntensity);
        
        return float4(texColor.rgb * lighting, texColor.a);
    }
}