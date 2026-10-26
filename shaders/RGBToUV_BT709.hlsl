// Reference only: authoritative generation is ShaderFactory in src/DisplayEngine.cs.
// Neutral digital CbCr, BT.709 limited range, midpoint 128/255.
// The NV12 chroma target is half resolution: average the 2x2 source block.
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
float3 at(float2 coord, float2 texel)
{
    return saturate(tx.SampleLevel(sam, clamp(coord, texel*0.5, 1.0-texel*0.5), 0).xyz);
}
float2 main(PS_IN inp) : SV_Target
{
    uint width, height; tx.GetDimensions(width, height);
    float2 step = 1.0 / float2(width, height);
    float2 footprint = float2(abs(ddx(inp.uv.x)), abs(ddy(inp.uv.y))) * float2(width, height);
    float2 offset = saturate(footprint - 1.0) * 0.5 * step;
    float3 rgb = 0.25 * (at(inp.uv - offset, step) + at(inp.uv + offset, step) +
                         at(inp.uv + float2(offset.x, -offset.y), step) + at(inp.uv + float2(-offset.x, offset.y), step));
    float y = dot(rgb, float3(0.2126, 0.7152, 0.0722));
    float2 cbcr = float2((rgb.b-y)/(2.0*(1.0-0.0722)), (rgb.r-y)/(2.0*(1.0-0.2126)));
    return clamp(cbcr*(224.0/255.0)+128.0/255.0, 16.0/255.0, 240.0/255.0);
}
