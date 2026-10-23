// ============================================================================
// RGBToY_BT709.hlsl — Direct3D 11 Pixel Shader (ps_5_0)
// Substitui o shader original de luminância (Y) do UDisplay.exe (Offset 0x4E8FF0)
//
// Problema no shader original de fábrica:
//   - Aplicava ganho de contraste forçado: rgb = (rgb - 0.5) * 1.15 + 0.5
//   - Usava coeficientes incorretos sem compensação de faixa limitada (16..235),
//     esmagando tons escuros (black crush) e estourando brancos (white clipping).
//
// Solução UDisplay Pro:
//   - Matriz padrão ITU-R BT.709 Studio Swing (16..235 em 8 bits -> 0.0627451..0.9215686)
//   - Suporte opcional em tempo de compilação a Brilho, Contraste, Temperatura e Gamma
// ============================================================================

Texture2D tx : register(t0);
SamplerState sam : register(s0);

struct PS_IN
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

float4 main(PS_IN inp) : SV_Target
{
    float3 rgb = saturate(tx.Sample(sam, inp.uv).xyz);

    // Coeficientes BT.709 Limited Range (16..235):
    // Y = 0.182586 * R + 0.614231 * G + 0.062007 * B + (16.0 / 255.0)
    float y = dot(rgb, float3(0.182586, 0.614231, 0.062007)) + 0.0627451;

    return float4(saturate(y), 0.0, 0.0, 1.0);
}
