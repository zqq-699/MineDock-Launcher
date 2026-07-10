sampler2D inputSampler : register(s0);

float inputWidth : register(c0);
float inputHeight : register(c1);
float blurLength : register(c2);
float maximumRadius : register(c3);
float directionX : register(c4);
float directionY : register(c5);

float2 ClampSampleCoordinate(float2 uv, float2 halfTexel)
{
    return clamp(uv, halfTexel, 1.0 - halfTexel);
}

float4 SampleInput(float2 uv, float2 halfTexel)
{
    float2 sampleUv = ClampSampleCoordinate(uv, halfTexel);
    return tex2Dlod(inputSampler, float4(sampleUv, 0.0, 0.0));
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 inputSize = max(float2(inputWidth, inputHeight), float2(1.0, 1.0));
    float2 halfTexel = 0.5 / inputSize;
    float4 center = SampleInput(uv, halfTexel);

    float effectiveBlurLength = max(blurLength, 0.0001);
    float progress = saturate((uv.y * inputSize.y) / effectiveBlurLength);
    [branch]
    if (progress >= 1.0 || maximumRadius <= 0.0)
        return center;

    float smoothedProgress = progress * progress * (3.0 - (2.0 * progress));
    float localRadius = maximumRadius * (1.0 - smoothedProgress);

    // Symmetric normalized 19-tap Gaussian kernel.
    const float centerWeight = 0.1478158643;
    const float weight1 = 0.1380174647;
    const float weight2 = 0.1123499907;
    const float weight3 = 0.0797329869;
    const float weight4 = 0.0493320341;
    const float weight5 = 0.0266100695;
    const float weight6 = 0.0125137938;
    const float weight7 = 0.0051304797;
    const float weight8 = 0.0018338041;
    const float weight9 = 0.0005714443;

    float2 direction = float2(directionX, directionY);
    float2 sampleStep = (direction / inputSize) * (localRadius / 9.0);

    float4 color = center * centerWeight;
    color += SampleInput(uv + (sampleStep * 1.0), halfTexel) * weight1;
    color += SampleInput(uv - (sampleStep * 1.0), halfTexel) * weight1;
    color += SampleInput(uv + (sampleStep * 2.0), halfTexel) * weight2;
    color += SampleInput(uv - (sampleStep * 2.0), halfTexel) * weight2;
    color += SampleInput(uv + (sampleStep * 3.0), halfTexel) * weight3;
    color += SampleInput(uv - (sampleStep * 3.0), halfTexel) * weight3;
    color += SampleInput(uv + (sampleStep * 4.0), halfTexel) * weight4;
    color += SampleInput(uv - (sampleStep * 4.0), halfTexel) * weight4;
    color += SampleInput(uv + (sampleStep * 5.0), halfTexel) * weight5;
    color += SampleInput(uv - (sampleStep * 5.0), halfTexel) * weight5;
    color += SampleInput(uv + (sampleStep * 6.0), halfTexel) * weight6;
    color += SampleInput(uv - (sampleStep * 6.0), halfTexel) * weight6;
    color += SampleInput(uv + (sampleStep * 7.0), halfTexel) * weight7;
    color += SampleInput(uv - (sampleStep * 7.0), halfTexel) * weight7;
    color += SampleInput(uv + (sampleStep * 8.0), halfTexel) * weight8;
    color += SampleInput(uv - (sampleStep * 8.0), halfTexel) * weight8;
    color += SampleInput(uv + (sampleStep * 9.0), halfTexel) * weight9;
    color += SampleInput(uv - (sampleStep * 9.0), halfTexel) * weight9;
    return color;
}
