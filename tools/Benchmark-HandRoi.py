"""Compare warmed RapidV6 CPU OCR on a replay hand ROI and full frame.

The replay screenshot stays in samples/private and is never written by this tool.
"""

import ctypes
import io
import json
import statistics
import time
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "samples" / "private" / "replay-step0.bmp"
DLL = ctypes.CDLL(str(ROOT / "vendor" / "RapidV6" / "Rapid.dll"))
POINTER = ctypes.POINTER(ctypes.c_void_p)
for name, args in {
    "rapid_initialize": [ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64), POINTER],
    "rapid_ocr": [ctypes.c_uint64, ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p, POINTER],
    "rapid_destroy": [ctypes.c_uint64, POINTER],
}.items():
    function = getattr(DLL, name)
    function.argtypes = args
    function.restype = ctypes.c_int32
DLL.rapid_free.argtypes = [ctypes.c_void_p]


def invoke(name, *args):
    pointer = ctypes.c_void_p()
    code = getattr(DLL, name)(*args, ctypes.byref(pointer))
    try:
        result = json.loads(ctypes.string_at(pointer).decode("utf-8")) if pointer.value else {}
    finally:
        DLL.rapid_free(pointer)
    if code:
        raise RuntimeError(f"{name}: {code} {result}")
    return result


def recognize(handle, image):
    buffer = ctypes.create_string_buffer(image)
    start = time.perf_counter()
    result = invoke("rapid_ocr", handle, buffer, len(image), b'{"mode":"ocr"}')
    return round((time.perf_counter() - start) * 1000, 1), result.get("text", "")


with Image.open(SOURCE) as source:
    full = io.BytesIO()
    source.save(full, format="BMP")
    hand = io.BytesIO()
    source.crop((240, 525, 1020, 615)).save(hand, format="BMP")

for detector, recognizer in (("tiny", "tiny"), ("tiny", "small"), ("small", "small")):
    handle = ctypes.c_uint64()
    options = json.dumps({"backend": "cpu", "det": f"PP-OCRv6_det_{detector}",
                          "rec": f"PP-OCRv6_rec_{recognizer}"}).encode()
    invoke("rapid_initialize", options, ctypes.byref(handle))
    try:
        recognize(handle, hand.getvalue())  # model warm-up
        hand_trials = [recognize(handle, hand.getvalue()) for _ in range(3)]
        full_trials = [recognize(handle, full.getvalue()) for _ in range(3)]
        print(json.dumps({"models": f"{detector}+{recognizer}",
                          "hand_ms_median": statistics.median(t for t, _ in hand_trials),
                          "full_ms_median": statistics.median(t for t, _ in full_trials),
                          "hand_text": hand_trials[-1][1]}, ensure_ascii=False))
    finally:
        invoke("rapid_destroy", handle)
