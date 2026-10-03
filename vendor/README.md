# RapidV6 运行时（本仓库只包含 CPU/tiny 子集）

本目录是 OCR 运行时 `RapidV6`（第三方交付包，交付版本 2026-10-01）中**本程序实际会用到的部分**。

本程序默认后端为 CPU，检测/识别模型为 `PP-OCRv6_det_tiny` + `PP-OCRv6_rec_tiny`，
因此仓库只保留：

- `Rapid.dll`、`rapid.h`、`集成手册.md`
- `resources/models/`：`PP-OCRv6_det_tiny.onnx`、`PP-OCRv6_rec_tiny.onnx`、
  `PP-LCNet_x1_0_textline_ori.onnx`、`PP-LCNet_x1_0_doc_ori.onnx`
- `resources/runtime/`：`onnxruntime.dll`、`onnxruntime_providers_shared.dll`、`DirectML.dll`、
  `msvcp140.dll`、`vcruntime140.dll`、`vcruntime140_1.dll`
- `resources/manifest.json`、`resources/models.json`、`resources/licenses/`

**未包含**（整体约 2.5 GiB，主要是 CUDA/cuDNN 与 medium/small/large 模型，单个文件远超
GitHub 的 100 MB 限制）：`cublasLt64_12.dll`、`cudnn_*`、`onnxruntime_providers_cuda.dll`、
`pp_doc_layoutv3.onnx`、`PP-OCRv6_*_medium/small.onnx` 等。

影响：CPU 与 DirectML 后端、tiny 模型、`ocr` 模式可正常使用；**CUDA 后端、medium/small 模型、
`layout` 模式在本仓库中不可用**。需要这些能力时，请向原交付方索取完整 RapidV6 包，把
`resources/models` 与 `resources/runtime` 补全即可，本程序代码不需要改动。

调用约定、状态码、许可与验证范围见 `集成手册.md`、`resources/manifest.json` 和 `resources/licenses/`。
`Rapid.dll` 必须与 `resources` 保持相对位置；只复制 DLL 无法推理。