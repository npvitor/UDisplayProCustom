// Reference only: authoritative generation is ShaderFactory in src/DisplayEngine.cs.
// Neutral digital CbCr, BT.709 limited range, midpoint 128/255.
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
float2 main(PS_IN inp) : SV_Target
{
    float3 rgb = saturate(tx.Sample(sam, inp.uv).xyz);
    float y = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float2 cbcr = float2((rgb.b-y)/(2.0*(1.0-0.0722)), (rgb.r-y)/(2.0*(1.0-0.2126)));
    return clamp(cbcr*(224.0/255.0)+128.0/255.0, 16.0/255.0, 240.0/255.0);
}
