"""Run tiny OCR over each anonymous card corner using one RapidV6 session."""
import ctypes
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
lib = ctypes.CDLL(str(root / "vendor" / "RapidV6" / "Rapid.dll"))
out = ctypes.POINTER(ctypes.c_void_p)
for name, types in {
    "rapid_initialize": [ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64), out],
    "rapid_ocr": [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p, out],
    "rapid_destroy": [ctypes.c_uint64, out],
}.items():
    fn = getattr(lib, name)
    fn.argtypes = types
    fn.restype = ctypes.c_int32
lib.rapid_free.argtypes = [ctypes.c_void_p]


def invoke(name, *args):
    ptr = ctypes.c_void_p()
    code = getattr(lib, name)(*args, ctypes.byref(ptr))
    try:
        result = json.loads(ctypes.string_at(ptr).decode("utf-8")) if ptr.value else {}
    finally:
        lib.rapid_free(ptr)
    if code:
        raise RuntimeError(f"{name} {code}: {result}")
    return result


handle = ctypes.c_uint64()
invoke("rapid_initialize", b'{"backend":"cpu","det":"PP-OCRv6_det_tiny","rec":"PP-OCRv6_rec_tiny"}',
       ctypes.byref(handle))
records = []
try:
    for path in sorted((root / "samples" / "anonymous").glob("corner-*.png")):
        data = path.read_bytes()
        buffer = ctypes.create_string_buffer(data)
        result = invoke("rapid_ocr", handle, buffer, len(data), b'{"mode":"ocr"}')
        records.append({"image": path.name, "text": result.get("text", ""), "lines": result.get("lines", [])})
finally:
    invoke("rapid_destroy", handle)
print(json.dumps(records, ensure_ascii=False, indent=2))
