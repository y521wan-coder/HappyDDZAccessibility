# 第三方来源

RapidOCR 的 DB/CTC 算法按 Apache-2.0 移植至 rapid 的 C++ 实现；不是上游官方 DLL。Clipper 6.4.2 版权所有 Angus Johnson 2010–2017，按 Boost Software License 1.0 使用。

ONNX Runtime、OpenCV、JSON、CUDA 原生包、Clipper 的固定下载来源和摘要见 resources/manifest.json；模型出处见 resources/models.json，模型许可标注来自对应发布者。静态和动态第三方源组件均未修改，构建开关改变不修改其源代码。

Eigen 为 MPL-2.0；本包使用的未修改源代码可从下列精确修订获取：
https://github.com/eigen-mirror/eigen/archive/1d8b82b0740839c0de7f1242a3585e3390ff5f33/eigen-1d8b82b0740839c0de7f1242a3585e3390ff5f33.zip

Microsoft VC 运行库的再分发清单与完整离线许可分别保存在 VisualStudio-Redist.html、VisualStudio-CRT.docx；许可来源页为 VisualStudio-CRT.html。NVDA 框架未打包，手册只提供调用其 API 的结果适配示例。

This software is based in part on the work of the Independent JPEG Group.
