"""Read a local sample image with the project's RapidV6 runtime (development only)."""

import ctypes
import json
import sys
from pathlib import Path


root = Path(__file__).resolve().parents[1]
source = Path(sys.argv[1]) if len(sys.argv) > 1 else root / "samples" / "private" / "replay-step0.bmp"
lib = ctypes.CDLL(str(root / "vendor" / "RapidV6" / "Rapid.dll"))
out = ctypes.POINTER(ctypes.c_void_p)
for name, arguments in {
    "rapid_initialize": [ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64), out],
    "rapid_ocr": [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p, out],
    "rapid_destroy": [ctypes.c_uint64, out],
}.items():
    function = getattr(lib, name)
    function.argtypes = arguments
    function.restype = ctypes.c_int32
lib.rapid_free.argtypes = [ctypes.c_void_p]


def invoke(name, *args):
    pointer = ctypes.c_void_p()
    code = getattr(lib, name)(*args, ctypes.byref(pointer))
    try:
        result = json.loads(ctypes.string_at(pointer).decode("utf-8")) if pointer.value else {}
    finally:
        lib.rapid_free(pointer)
    if code:
        raise RuntimeError(f"{name} {code}: {result}")
    return result


handle = ctypes.c_uint64()
invoke("rapid_initialize", b'{"backend":"cpu","det":"PP-OCRv6_det_tiny","rec":"PP-OCRv6_rec_tiny"}',
       ctypes.byref(handle))
try:
    data = source.read_bytes()
    buffer = ctypes.create_string_buffer(data)
    result = invoke("rapid_ocr", handle, buffer, len(data), b'{"mode":"ocr"}')
finally:
    invoke("rapid_destroy", handle)
print(json.dumps({"text": result.get("text", ""), "lines": result.get("lines", [])},
                 ensure_ascii=False, indent=2))
