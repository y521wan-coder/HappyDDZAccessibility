#ifndef RAPID_H
#define RAPID_H

#include <stddef.h>
#include <stdint.h>

#if defined(RAPID_BUILD)
#define RAPID_API __declspec(dllexport)
#else
#define RAPID_API
#endif
#define RAPID_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

/* Opaque token, never a pointer. Zero is invalid. One live engine per DLL. */
typedef uint64_t rapid_handle;
typedef enum rapid_status { // NOLINT(performance-enum-size): retain the public C ABI's int-sized enum.
    RAPID_OK = 0,
    RAPID_BUSY = 1,
    RAPID_INVALID_ARGUMENT = 2,
    RAPID_NOT_INITIALIZED = 3,
    RAPID_MODEL_ERROR = 4,
    RAPID_DECODE_ERROR = 5,
    RAPID_BACKEND_ERROR = 6,
    RAPID_OUT_OF_MEMORY = 7,
    RAPID_INTERNAL_ERROR = 8,
    RAPID_ALREADY_INITIALIZED = 9
} rapid_status;

/* JSON is UTF-8. A null options_json means defaults. out_json is mandatory.
 * Every non-null returned JSON pointer must be released with rapid_free,
 * including error responses. It remains valid after engine destruction.
 * The caller must keep this DLL loaded until all results have been freed.
 * Callers retain ownership of input memory until the synchronous call returns.
 */
RAPID_API int32_t RAPID_CALL rapid_initialize(const char* options_json, rapid_handle* out_handle, char** out_json);
RAPID_API int32_t RAPID_CALL rapid_list_models(char** out_json);
RAPID_API int32_t RAPID_CALL rapid_get_models(rapid_handle handle, char** out_json);
RAPID_API int32_t RAPID_CALL rapid_set_models(rapid_handle handle, const char* selection_json, char** out_json);
RAPID_API int32_t RAPID_CALL rapid_ocr(rapid_handle handle, const void* data, size_t byte_count,
    const char* options_json, char** out_json);
RAPID_API int32_t RAPID_CALL rapid_destroy(rapid_handle handle, char** out_json);
RAPID_API void RAPID_CALL rapid_free(char* result);

/* Function pointer types for GetProcAddress: no import library is required. */
typedef int32_t (RAPID_CALL* rapid_initialize_fn)(const char*, rapid_handle*, char**);
typedef int32_t (RAPID_CALL* rapid_list_models_fn)(char**);
typedef int32_t (RAPID_CALL* rapid_get_models_fn)(rapid_handle, char**);
typedef int32_t (RAPID_CALL* rapid_set_models_fn)(rapid_handle, const char*, char**);
typedef int32_t (RAPID_CALL* rapid_ocr_fn)(rapid_handle, const void*, size_t, const char*, char**);
typedef int32_t (RAPID_CALL* rapid_destroy_fn)(rapid_handle, char**);
typedef void (RAPID_CALL* rapid_free_fn)(char*);

#ifdef __cplusplus
}
#endif
#endif
