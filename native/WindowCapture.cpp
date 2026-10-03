#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

struct Target { DWORD pid; HWND hwnd; };

BOOL CALLBACK find_window(HWND hwnd, LPARAM value) {
    auto* target = reinterpret_cast<Target*>(value);
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid == target->pid && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == nullptr) {
        target->hwnd = hwnd;
        return FALSE;
    }
    return TRUE;
}

struct CapturedFrame {
    std::vector<std::uint8_t> bytes;
    int width = 0;
    int height = 0;
};

CapturedFrame copy_frame(Direct3D11CaptureFrame const& frame, ID3D11Device* device,
                         ID3D11DeviceContext* context) {
    auto access = frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    com_ptr<ID3D11Texture2D> source;
    check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D), source.put_void()));
    D3D11_TEXTURE2D_DESC desc{};
    source->GetDesc(&desc);
    if (desc.Width == 0 || desc.Height == 0 || desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM)
        throw hresult_error(E_FAIL, L"Unexpected capture pixel format");
    desc.BindFlags = 0;
    desc.MiscFlags = 0;
    desc.Usage = D3D11_USAGE_STAGING;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    com_ptr<ID3D11Texture2D> staging;
    check_hresult(device->CreateTexture2D(&desc, nullptr, staging.put()));
    context->CopyResource(staging.get(), source.get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    check_hresult(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
    CapturedFrame result;
    result.width = static_cast<int>(desc.Width);
    result.height = static_cast<int>(desc.Height);
    result.bytes.resize(static_cast<size_t>(desc.Width) * desc.Height * 4);
    for (UINT y = 0; y < desc.Height; ++y) {
        std::memcpy(result.bytes.data() + static_cast<size_t>(y) * desc.Width * 4,
                    static_cast<std::uint8_t const*>(mapped.pData) + static_cast<size_t>(y) * mapped.RowPitch,
                    static_cast<size_t>(desc.Width) * 4);
    }
    context->Unmap(staging.get(), 0);
    return result;
}

CapturedFrame crop_client(CapturedFrame&& input, HWND hwnd) {
    RECT window{}, client{};
    POINT origin{0, 0};
    if (!GetWindowRect(hwnd, &window) || !GetClientRect(hwnd, &client) ||
        !ClientToScreen(hwnd, &origin)) throw std::runtime_error("Cannot locate client pixels");
    int left = origin.x - window.left;
    int top = origin.y - window.top;
    int width = client.right - client.left;
    int height = client.bottom - client.top;
    if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
        left + width > input.width || top + height > input.height)
        throw std::runtime_error("Captured frame does not match client geometry");
    CapturedFrame output;
    output.width = width;
    output.height = height;
    output.bytes.resize(static_cast<size_t>(width) * height * 4);
    for (int y = 0; y < height; ++y) {
        std::memcpy(output.bytes.data() + static_cast<size_t>(y) * width * 4,
                    input.bytes.data() + (static_cast<size_t>(y + top) * input.width + left) * 4,
                    static_cast<size_t>(width) * 4);
    }
    return output;
}

#pragma pack(push, 1)
struct BmpHeader {
    std::uint16_t signature = 0x4D42;
    std::uint32_t file_size = 0;
    std::uint16_t reserved1 = 0;
    std::uint16_t reserved2 = 0;
    std::uint32_t pixel_offset = 54;
    std::uint32_t info_size = 40;
    std::int32_t width = 0;
    std::int32_t height = 0;
    std::uint16_t planes = 1;
    std::uint16_t bits_per_pixel = 32;
    std::uint32_t compression = 0;
    std::uint32_t image_size = 0;
    std::int32_t x_ppm = 0;
    std::int32_t y_ppm = 0;
    std::uint32_t colors_used = 0;
    std::uint32_t important_colors = 0;
};
#pragma pack(pop)

int wmain(int argc, wchar_t** argv) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if (argc != 3 && argc != 4) {
        std::wcerr << L"Usage: WindowCapture.exe <game-pid> [game-hwnd] <output.bmp>\n";
        return 2;
    }
    try {
        unsigned long parsed = std::stoul(argv[1]);
        if (parsed == 0 || parsed > 0xFFFFFFFFul) throw std::invalid_argument("Invalid PID");
        Target target{static_cast<DWORD>(parsed), nullptr};
        if (argc == 4) {
            auto raw = std::stoull(argv[2]);
            target.hwnd = reinterpret_cast<HWND>(static_cast<std::uintptr_t>(raw));
            DWORD actual_pid = 0;
            bool valid_window = IsWindow(target.hwnd) != FALSE;
            if (valid_window) GetWindowThreadProcessId(target.hwnd, &actual_pid);
            if (!valid_window || actual_pid != target.pid)
                throw std::runtime_error("Window handle does not belong to the game process");
        } else {
            EnumWindows(find_window, reinterpret_cast<LPARAM>(&target));
        }
        if (!target.hwnd) throw std::runtime_error("Game window not found");
        if (IsIconic(target.hwnd)) throw std::runtime_error("Game window is minimized");
        RECT rect{};
        if (!GetClientRect(target.hwnd, &rect) || rect.right <= 0 || rect.bottom <= 0)
            throw std::runtime_error("Game window has no client area");
        init_apartment(apartment_type::multi_threaded);
        com_ptr<ID3D11Device> d3d;
        com_ptr<ID3D11DeviceContext> context;
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
            d3d.put(), nullptr, context.put()));
        auto dxgi = d3d.as<IDXGIDevice>();
        com_ptr<::IInspectable> inspectable;
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
        auto winrt_device = inspectable.as<IDirect3DDevice>();
        auto factory = get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        GraphicsCaptureItem item{nullptr};
        check_hresult(factory->CreateForWindow(target.hwnd, guid_of<GraphicsCaptureItem>(), put_abi(item)));
        auto size = item.Size();
        auto pool = Direct3D11CaptureFramePool::CreateFreeThreaded(winrt_device,
            DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
        auto session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled(false);
        HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!event) throw std::runtime_error("CreateEvent failed");
        CapturedFrame captured;
        std::atomic<bool> completed{false};
        std::wstring error;
        auto token = pool.FrameArrived([&](Direct3D11CaptureFramePool const& sender, auto const&) {
            if (completed.exchange(true)) return;
            try {
                auto frame = sender.TryGetNextFrame();
                captured = copy_frame(frame, d3d.get(), context.get());
            } catch (std::exception const& ex) {
                error = std::wstring(ex.what(), ex.what() + std::strlen(ex.what()));
            } catch (...) {
                error = L"Unknown capture error";
            }
            SetEvent(event);
        });
        session.StartCapture();
        DWORD wait = WaitForSingleObject(event, 5000);
        session.Close();
        pool.FrameArrived(token);
        pool.Close();
        CloseHandle(event);
        if (wait != WAIT_OBJECT_0) throw std::runtime_error("Capture timed out");
        if (!error.empty() || captured.bytes.empty()) throw std::runtime_error("Capture returned no pixels");
        captured = crop_client(std::move(captured), target.hwnd);
        size_t bright_pixels = 0;
        for (size_t i = 0; i < captured.bytes.size(); i += 4) {
            if (captured.bytes[i] > 12 || captured.bytes[i + 1] > 12 || captured.bytes[i + 2] > 12)
                ++bright_pixels;
        }
        if (bright_pixels < static_cast<size_t>(captured.width) * captured.height / 100)
            throw std::runtime_error("Captured frame is almost black");
        BmpHeader header;
        header.width = captured.width;
        header.height = -captured.height;
        header.image_size = static_cast<std::uint32_t>(captured.bytes.size());
        header.file_size = header.pixel_offset + header.image_size;
        std::ofstream output(std::filesystem::path(argv[argc - 1]), std::ios::binary | std::ios::trunc);
        if (!output) throw std::runtime_error("Cannot write output file");
        output.write(reinterpret_cast<char const*>(&header), sizeof(header));
        output.write(reinterpret_cast<char const*>(captured.bytes.data()), captured.bytes.size());
        output.close();
        if (!output) throw std::runtime_error("Incomplete output file");
        std::wcout << L"Captured " << captured.width << L"x" << captured.height << L"\n";
        return 0;
    } catch (hresult_error const& ex) {
        std::wcerr << L"Capture error 0x" << std::hex << static_cast<unsigned>(ex.code()) << L": " << ex.message().c_str() << L"\n";
    } catch (std::exception const& ex) {
        std::cerr << "Capture error: " << ex.what() << "\n";
    }
    return 1;
}
