cbuffer TransformBuffer : register(b0)
{
    float4x4 WorldViewProjection;
    float4x4 World;
    float3 LightDirection; 
    float AmbientLight;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal   : NORMAL;    // Added Normal
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position    : SV_POSITION;
    float3 WorldNormal : NORMAL;    // Added WorldNormal
    float2 TexCoord    : TEXCOORD0;
};

Texture2D DiffuseMap : register(t0);
SamplerState DiffuseSampler : register(s0);

VSOutput VSMain(VSInput input)
{
    VSOutput output;
    output.Position = mul(float4(input.Position, 1.0f), WorldViewProjection);
    
    // Rotate the normal using the World matrix so lighting updates as the cube spins.
    // Casting to float3x3 ignores translation (position), which shouldn't affect normals.
    output.WorldNormal = mul(input.Normal, (float3x3)World);
    
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PSMain(VSOutput input) : SV_Target
{
    // Sample the base texture color
    float4 texColor = DiffuseMap.Sample(DiffuseSampler, input.TexCoord);

    // Normalize vectors for accurate dot product calculation
    float3 normal = normalize(input.WorldNormal);
    float3 lightDir = normalize(LightDirection);

    // Calculate diffuse intensity using the dot product. 
    // max(..., 0) ensures faces pointing away from the light aren't drawn with negative light (which looks weird).
    float diffuseIntensity = max(dot(normal, lightDir), 0.0f);

    // Combine ambient light (base lighting so shadows aren't pitch black) with the directional light
    float lighting = AmbientLight + ((1.0f - AmbientLight) * diffuseIntensity);

    // Multiply the texture color by the lighting intensity
    return float4(texColor.rgb * lighting, texColor.a);
}