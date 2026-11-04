#ifndef MATESOUL_REALITY_ENVIRONMENT_DEPTH_INCLUDED
#define MATESOUL_REALITY_ENVIRONMENT_DEPTH_INCLUDED

// Include the PICO EnvironmentOcclusion helper before this file.
float MateSoulEnvironmentOcclusion(float3 positionWS, float bias)
{
    if (_EnvironmentOcclusionEnabled <= .5f ||
        distance(positionWS, GetCameraPositionWS()) < .08f)
        return 1.0f;

    float4 projected = mul(
        _EnvironmentDepthReprojectionMatrices[unity_StereoEyeIndex],
        float4(positionWS, 1.0f));
    if (!(projected.w > .00001f))
        return 1.0f;

    float3 ndc = projected.xyz / projected.w;
    float2 uv = ndc.xy * .5f + .5f;
    float2 border = max(
        abs(_EnvironmentDepthTexture_TexelSize.xy) * 3.0f,
        abs(_PreprocessedEnvironmentDepthTexture_TexelSize.xy) * 1.5f);
    // Outside the depth camera's measured frustum, clamped edge texels do
    // not describe this pixel. Sampling them creates broad false masks.
    if (!(all(uv >= border) && all(uv <= 1.0f - border) &&
          ndc.z >= -1.0f && ndc.z <= 1.0f))
        return 1.0f;

    // The SDK shifts UV by half a pixel, then bilinearly samples statistics.
    // Validate all four contributing texels before interpolation; interpolating
    // an invalid marker could otherwise turn it into plausible soft depth.
    int2 pixel = int2(floor(
        uv * _PreprocessedEnvironmentDepthTexture_TexelSize.zw - 1.0f));
    float minimumDepth = 1.0f;
    float maximumDepth = 0.0f;
    [unroll]
    for (int y = 0; y < 2; ++y)
    {
        [unroll]
        for (int x = 0; x < 2; ++x)
        {
            float4 sample = _PreprocessedEnvironmentDepthTexture.Load(
                int4(pixel + int2(x, y), unity_StereoEyeIndex, 0));
            if (!(all(sample.xy > 0.0f) && all(sample.xy < 1.0f) &&
                  sample.z >= 0.0f && sample.w >= 0.0f &&
                  sample.z <= sample.w + .001f))
                return 1.0f;
            minimumDepth = min(minimumDepth, min(sample.x, sample.y));
            maximumDepth = max(maximumDepth, max(sample.x, sample.y));
        }
    }

    float raw = SampleEnvironmentDepth(uv);
    if (!(raw > 0.0f && raw < 1.0f))
        return 1.0f;
    float realDepth = _EnvironmentDepthZBufferParams.x /
        (raw * 2.0f - 1.0f + _EnvironmentDepthZBufferParams.y);
    float sceneDepth = _EnvironmentDepthZBufferParams.x /
        (ndc.z + _EnvironmentDepthZBufferParams.y);
    // Uncertain or coplanar depth must not wash out an opaque virtual surface.
    if (!(realDepth > 0.0f &&
          realDepth < sceneDepth * (1.0f - max(RELATIVE_ERROR_SCALE, abs(bias)))))
        return 1.0f;

    float visibility = CalculateEnvironmentDepthOcclusion(positionWS, bias);
    // Soft alpha belongs at measured depth edges. A uniform surface inside
    // the SDK's error band should leave the virtual object fully visible.
    if (maximumDepth - minimumDepth <= .000001f && visibility > .001f)
        return 1.0f;
    return visibility >= 0.0f && visibility <= 1.0f ? visibility : 1.0f;
}

#endif
