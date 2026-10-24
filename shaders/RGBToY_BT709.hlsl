// Reference only: authoritative generation is ShaderFactory in src/DisplayEngine.cs.
// Neutral BT.709 limited-range luma. The default tested profile uses BT.601.
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
float main(PS_IN inp) : SV_Target
{
    float3 rgb = saturate(tx.Sample(sam, inp.uv).xyz);
    return dot(rgb, float3(0.2126, 0.7152, 0.0722)) * (219.0 / 255.0) + 16.0 / 255.0;
}
