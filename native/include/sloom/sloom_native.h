#ifndef SLOOM_NATIVE_H
#define SLOOM_NATIVE_H

#include <stdint.h>

#ifdef _WIN32
#define SLOOM_API __declspec(dllexport)
#else
#define SLOOM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define SLOOM_NATIVE_API_VERSION 1u

typedef struct SloomImage {
    const void* pixels;
    uint32_t width;
    uint32_t height;
    uint32_t stride;
    uint32_t format;
} SloomImage;

typedef struct SloomBuffer {
    void* data;
    uint64_t size;
} SloomBuffer;

SLOOM_API uint32_t sloom_native_api_version(void);
SLOOM_API int sloom_has_encoder(const char* name);

SLOOM_API int sloom_astc_encode(
    const SloomImage* source,
    uint32_t block_width,
    uint32_t block_height,
    int quality,
    SloomBuffer* destination);

SLOOM_API int sloom_bc_encode(
    const SloomImage* source,
    uint32_t variant,
    int quality,
    SloomBuffer* destination);

SLOOM_API int sloom_etc_encode(
    const SloomImage* source,
    uint32_t variant,
    int quality,
    SloomBuffer* destination);

SLOOM_API int sloom_basis_encode(
    const SloomImage* source,
    uint32_t mode,
    int quality,
    SloomBuffer* destination);

SLOOM_API void sloom_buffer_free(SloomBuffer* buffer);

#ifdef __cplusplus
}
#endif

#endif
