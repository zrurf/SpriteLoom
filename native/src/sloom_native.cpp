#include "sloom/sloom_native.h"

#include "astcenc.h"
#include "bc7enc.h"
#undef BC7ENC_BLOCK_SIZE
#include "ProcessDxtc.hpp"
#include "ProcessRGB.hpp"
#include "basisu_comp.h"
#include "basisu_enc.h"

#include <cstdlib>
#include <cstring>
#include <mutex>
#include <vector>

namespace {

constexpr int ErrorInvalid = 1;
constexpr int ErrorUnsupported = 2;
constexpr int ErrorEncode = 3;

int quality_preset(int quality)
{
    switch (quality)
    {
    case 0:
        return 0;
    case 2:
        return 2;
    default:
        return 1;
    }
}

float astc_quality(int quality)
{
    switch (quality_preset(quality))
    {
    case 0:
        return ASTCENC_PRE_FAST;
    case 2:
        return ASTCENC_PRE_THOROUGH;
    default:
        return ASTCENC_PRE_MEDIUM;
    }
}

bool valid_image(const SloomImage* image)
{
    return image && image->pixels && image->width > 0 && image->height > 0 &&
           image->format == 0 && image->stride >= image->width * 4;
}

std::vector<uint8_t> pad_rgba(const SloomImage* image, uint32_t& width, uint32_t& height)
{
    width = (image->width + 3) & ~3u;
    height = (image->height + 3) & ~3u;
    std::vector<uint8_t> padded(static_cast<size_t>(width) * height * 4);
    const auto* source = static_cast<const uint8_t*>(image->pixels);

    for (uint32_t y = 0; y < height; ++y)
    {
        uint32_t sourceY = y < image->height ? y : image->height - 1;

        for (uint32_t x = 0; x < width; ++x)
        {
            uint32_t sourceX = x < image->width ? x : image->width - 1;
            const uint8_t* from = source + sourceY * image->stride + sourceX * 4;
            uint8_t* to = padded.data() + (static_cast<size_t>(y) * width + x) * 4;
            std::memcpy(to, from, 4);
        }
    }

    return padded;
}

int encode_astc(const SloomImage* image, uint32_t blockWidth, uint32_t blockHeight,
                int quality, SloomBuffer* destination)
{
    astcenc_config config{};

    if (astcenc_config_init(ASTCENC_PRF_LDR_SRGB, blockWidth, blockHeight, 1,
                            astc_quality(quality), ASTCENC_FLG_USE_PERCEPTUAL,
                            &config) != ASTCENC_SUCCESS)
    {
        return ErrorEncode;
    }

    astcenc_context* context = nullptr;

    if (astcenc_context_alloc(&config, 1, &context, nullptr) != ASTCENC_SUCCESS)
    {
        return ErrorEncode;
    }

    uint32_t blocksX = (image->width + blockWidth - 1) / blockWidth;
    uint32_t blocksY = (image->height + blockHeight - 1) / blockHeight;
    uint64_t size = static_cast<uint64_t>(blocksX) * blocksY * 16;
    auto* output = static_cast<uint8_t*>(std::malloc(size));

    if (!output)
    {
        astcenc_context_free(context);
        return ErrorEncode;
    }

    astcenc_image input{};
    input.dim_x = image->width;
    input.dim_y = image->height;
    input.dim_z = 1;
    input.data_type = ASTCENC_TYPE_U8;
    input.row_stride = image->stride;
    void* slices[1] = {const_cast<void*>(image->pixels)};
    input.data = slices;
    astcenc_swizzle swizzle{ASTCENC_SWZ_R, ASTCENC_SWZ_G, ASTCENC_SWZ_B, ASTCENC_SWZ_A};

    astcenc_error error = astcenc_compress_image(context, &input, &swizzle,
                                                 output, size, 0);
    astcenc_context_free(context);

    if (error != ASTCENC_SUCCESS)
    {
        std::free(output);
        return ErrorEncode;
    }

    destination->data = output;
    destination->size = size;
    return 0;
}

int encode_bc(const SloomImage* image, uint32_t variant, int quality,
              SloomBuffer* destination)
{
    uint32_t width;
    uint32_t height;
    std::vector<uint8_t> padded = pad_rgba(image, width, height);
    uint32_t blocks = (width / 4) * (height / 4);
    uint32_t blockBytes = variant == 1 || variant == 4 ? 8 : 16;
    auto* output = static_cast<uint64_t*>(std::malloc(
        static_cast<size_t>(blocks) * blockBytes));

    if (!output)
    {
        return ErrorEncode;
    }

    auto* source = reinterpret_cast<const uint32_t*>(padded.data());

    switch (variant)
    {
    case 1:
        CompressBc1(source, output, blocks, width);
        break;
    case 3:
        CompressBc3(source, output, blocks, width);
        break;
    case 4:
        CompressBc4(source, output, blocks, width);
        break;
    case 5:
        CompressBc5(source, output, blocks, width);
        break;
    case 7:
    {
        bc7enc_compress_block_params params{};
        bc7enc_compress_block_params_init(&params);
        params.m_uber_level = static_cast<uint32_t>(quality_preset(quality));
        CompressBc7(source, output, blocks, width, &params);
        break;
    }
    default:
        std::free(output);
        return ErrorUnsupported;
    }

    destination->data = output;
    destination->size = static_cast<uint64_t>(blocks) * blockBytes;
    return 0;
}

int encode_etc(const SloomImage* image, uint32_t variant, int quality,
               SloomBuffer* destination)
{    uint32_t width;
    uint32_t height;
    std::vector<uint8_t> padded = pad_rgba(image, width, height);
    uint32_t blocks = (width / 4) * (height / 4);
    auto* output = static_cast<uint64_t*>(std::malloc(
        static_cast<size_t>(blocks) * 16));

    if (!output)
    {
        return ErrorEncode;
    }

    auto* source = reinterpret_cast<const uint32_t*>(padded.data());

    switch (variant)
    {
    case 1:
        CompressEtc2Rgb(source, output, blocks, width, quality_preset(quality) > 0);
        break;
    case 2:
        CompressEtc2Rgba(source, output, blocks, width, quality_preset(quality) > 0);
        break;
    default:
        std::free(output);
        return ErrorUnsupported;
    }

    destination->data = output;
    destination->size = static_cast<uint64_t>(blocks) * 16;
    return 0;
}

int encode_basis(const SloomImage* image, uint32_t mode, int quality,
                 SloomBuffer* destination)
{
    static std::once_flag initFlag;
    static bool initialized = false;
    std::call_once(initFlag, [] { initialized = basisu::basisu_encoder_init(false); });

    if (!initialized)
    {
        return ErrorEncode;
    }

    const auto format = mode == 1
        ? basist::basis_tex_format::cUASTC_LDR_4x4
        : basist::basis_tex_format::cETC1S;
    const uint32_t level = mode == 1
        ? (quality_preset(quality) == 0 ? 1u : quality_preset(quality) == 2 ? 4u : 2u)
        : (quality_preset(quality) == 0 ? 96u : quality_preset(quality) == 2 ? 180u : 128u);
    const uint32_t flags = basisu::cFlagKTX2 | basisu::cFlagSRGB | level;
    size_t size = 0;
    void* encoded = basisu::basis_compress(
        format,
        static_cast<const uint8_t*>(image->pixels),
        image->width,
        image->height,
        image->stride / 4,
        flags,
        0.0f,
        &size);

    if (!encoded || size == 0)
    {
        if (encoded)
        {
            basisu::basis_free_data(encoded);
        }

        return ErrorEncode;
    }

    auto* output = static_cast<uint8_t*>(std::malloc(size));

    if (!output)
    {
        basisu::basis_free_data(encoded);
        return ErrorEncode;
    }

    std::memcpy(output, encoded, size);
    basisu::basis_free_data(encoded);
    destination->data = output;
    destination->size = size;
    return 0;
}

} // namespace

extern "C" {

uint32_t sloom_native_api_version(void)
{
    return SLOOM_NATIVE_API_VERSION;
}

int sloom_has_encoder(const char* name)
{
    if (!name)
    {
        return 0;
    }

    return std::strcmp(name, "astc") == 0 ||
           std::strcmp(name, "bc") == 0 ||
           std::strcmp(name, "etc") == 0;
}

int sloom_astc_encode(const SloomImage* source, uint32_t blockWidth,
                      uint32_t blockHeight, int quality, SloomBuffer* destination)
{
    if (!valid_image(source) || !destination || blockWidth == 0 || blockHeight == 0)
    {
        return ErrorInvalid;
    }

    destination->data = nullptr;
    destination->size = 0;
    return encode_astc(source, blockWidth, blockHeight, quality, destination);
}

int sloom_bc_encode(const SloomImage* source, uint32_t variant, int quality,
                    SloomBuffer* destination)
{
    if (!valid_image(source) || !destination)
    {
        return ErrorInvalid;
    }

    destination->data = nullptr;
    destination->size = 0;
    return encode_bc(source, variant, quality, destination);
}

int sloom_etc_encode(const SloomImage* source, uint32_t variant, int quality,
                     SloomBuffer* destination)
{
    if (!valid_image(source) || !destination)
    {
        return ErrorInvalid;
    }

    destination->data = nullptr;
    destination->size = 0;
    return encode_etc(source, variant, quality, destination);
}

int sloom_basis_encode(const SloomImage* source, uint32_t mode, int quality,
                       SloomBuffer* destination)
{
    if (!valid_image(source) || !destination || mode > 1)
    {
        return ErrorInvalid;
    }

    destination->data = nullptr;
    destination->size = 0;
    return encode_basis(source, mode, quality, destination);
}

void sloom_buffer_free(SloomBuffer* buffer)
{
    if (buffer)
    {
        std::free(buffer->data);
        buffer->data = nullptr;
        buffer->size = 0;
    }
}

}
