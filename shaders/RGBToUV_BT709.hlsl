// ============================================================================
// RGBToUV_BT709.hlsl — Direct3D 11 Pixel Shader (ps_5_0)
// Substitui o shader original de crominância (UV / NV12) do UDisplay.exe (Offset 0x4E8CE0)
//
// Problema no shader original de fábrica:
//   - Aplicava saturação artificial excessiva (1.28x) combinada com matriz BT.601 incorreta,
//     deixando vermelhos/azuis neon e borrando bordas de texto colorido.
//
// Solução UDisplay Pro:
//   - Matriz de crominância ITU-R BT.709 (16..240) precisa para plano UV intercalado (NV12)
//   - Offset neutro exato em 128/255 (0.5019608)
// ============================================================================

Texture2D tx : register(t0);
SamplerState sam : register(s0);

struct PS_IN
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

float2 main(PS_IN inp) : SV_Target
{
    float3 rgb = saturate(tx.Sample(sam, inp.uv).xyz);

    // Coeficientes Cb (U) e Cr (V) BT.709 Limited Range (16..240):
    float u = dot(rgb, float3(-0.100644, -0.338572,  0.439216)) + 0.5019608;
    float v = dot(rgb, float3( 0.439216, -0.398942, -0.040274)) + 0.5019608;

    return saturate(float2(u, v));
}
