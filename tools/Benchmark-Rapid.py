"""Local, read-only RapidV6 OCR probe for privately stored game screenshots."""
import argparse
import ctypes
import json
import statistics
import time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("image", type=Path)
    parser.add_argument("--backend", choices=["cpu", "dml"], default="cpu")
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--dll", type=Path,
                        default=Path(__file__).resolve().parents[1] / "vendor" / "RapidV6" / "Rapid.dll")
    args = parser.parse_args()
    dll = ctypes.CDLL(str(args.dll.resolve()))
    out = ctypes.POINTER(ctypes.c_void_p)
    for name, types in {
        "rapid_initialize": [ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64), out],
        "rapid_ocr": [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p, out],
        "rapid_destroy": [ctypes.c_uint64, out],
    }.items():
        fn = getattr(dll, name)
        fn.argtypes = types
        fn.restype = ctypes.c_int32
    dll.rapid_free.argtypes = [ctypes.c_void_p]
    dll.rapid_free.restype = None

    def invoke(name, *params):
        ptr = ctypes.c_void_p()
        status = getattr(dll, name)(*params, ctypes.byref(ptr))
        try:
            result = json.loads(ctypes.string_at(ptr).decode("utf-8")) if ptr.value else {}
        finally:
            dll.rapid_free(ptr)
        if status:
            raise RuntimeError(f"{name} status {status}: {result.get('error', result)}")
        return result

    handle = ctypes.c_uint64()
    options = {"backend": args.backend, "det": "PP-OCRv6_det_tiny", "rec": "PP-OCRv6_rec_tiny"}
    started = time.perf_counter()
    initial = invoke("rapid_initialize", json.dumps(options).encode(), ctypes.byref(handle))
    load_ms = round((time.perf_counter() - started) * 1000, 1)
    data = args.image.read_bytes()
    image = ctypes.create_string_buffer(data)
    times = []
    result = None
    try:
        for _ in range(args.repeats + 1):
            started = time.perf_counter()
            result = invoke("rapid_ocr", handle, image, len(data), b'{"mode":"ocr"}')
            times.append(round((time.perf_counter() - started) * 1000, 1))
    finally:
        invoke("rapid_destroy", handle)
    report = {
        "image": str(args.image), "backend_requested": args.backend,
        "backend_actual": initial.get("models", {}), "load_ms": load_ms,
        "warmup_ms": times[0], "runs_ms": times[1:],
        "median_ms": statistics.median(times[1:]),
        "text": result.get("text", ""), "lines": result.get("lines", []),
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
