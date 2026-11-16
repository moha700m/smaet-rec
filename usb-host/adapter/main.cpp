// SPDX-License-Identifier: GPL-3.0-only
// SMAET REC's isolated GPL host adapter for the iPhoneMirror C ABI.
// Modified for SMAET REC on 2026-11-16. Communicates with the MIT GUI only
// through a private named pipe; no GPL code is loaded into the GUI process.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <iPhoneMirror/CoreApi.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwctype>
#include <iostream>
#include <limits>
#include <string>
#include <string_view>
#include <vector>

namespace {
std::atomic_bool g_running{true};
HANDLE g_pipe = INVALID_HANDLE_VALUE;
constexpr std::uint32_t kMaxFrameBytes = 64u * 1024u * 1024u;

BOOL WINAPI ConsoleHandler(DWORD type) {
    if (type == CTRL_C_EVENT || type == CTRL_BREAK_EVENT || type == CTRL_CLOSE_EVENT ||
        type == CTRL_SHUTDOWN_EVENT) {
        g_running.store(false);
        return TRUE;
    }
    return FALSE;
}

std::wstring ArgValue(int argc, wchar_t** argv, const wchar_t* key) {
    for (int i = 1; i + 1 < argc; ++i)
        if (_wcsicmp(argv[i], key) == 0) return argv[i + 1];
    return {};
}

std::wstring Normalize(std::wstring value) {
    value.erase(std::remove_if(value.begin(), value.end(), [](wchar_t c) {
        return !std::iswalnum(c);
    }), value.end());
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
    return value;
}

bool WriteAll(const void* data, std::uint32_t size) {
    const auto* bytes = static_cast<const std::uint8_t*>(data);
    while (size) {
        DWORD written = 0;
        if (!WriteFile(g_pipe, bytes, size, &written, nullptr) || written == 0) return false;
        bytes += written;
        size -= written;
    }
    return true;
}

bool SendRecord(const std::string& json, const std::uint8_t* payload = nullptr,
                std::uint32_t payloadBytes = 0) {
    const auto headerBytes = static_cast<std::uint32_t>(json.size());
    const std::uint64_t recordSize64 = sizeof(std::uint32_t) + headerBytes + payloadBytes;
    if (recordSize64 > kMaxFrameBytes || headerBytes == 0) return false;
    const auto recordSize = static_cast<std::uint32_t>(recordSize64);
    if (!WriteAll(&recordSize, sizeof(recordSize)) ||
        !WriteAll(&headerBytes, sizeof(headerBytes)) ||
        !WriteAll(json.data(), headerBytes)) return false;
    return payloadBytes == 0 || WriteAll(payload, payloadBytes);
}

std::string ToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (count <= 0) return "Native USB error";
    std::string utf8(static_cast<std::size_t>(count), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
        static_cast<int>(value.size()), utf8.data(), count, nullptr, nullptr);
    return utf8;
}

std::string JsonEscape(std::string_view value) {
    std::string escaped;
    escaped.reserve(value.size() + 16);
    constexpr char hex[] = "0123456789abcdef";
    for (unsigned char c : value) {
        if (c == '"' || c == '\\') { escaped.push_back('\\'); escaped.push_back(static_cast<char>(c)); }
        else if (c < 0x20) {
            escaped += "\\u00";
            escaped.push_back(hex[c >> 4]);
            escaped.push_back(hex[c & 0x0f]);
        } else escaped.push_back(static_cast<char>(c));
    }
    return escaped;
}

bool SendStatus(const std::wstring& message) {
    return SendRecord("{\"type\":\"status\",\"message\":\"" +
        JsonEscape(ToUtf8(message)) + "\"}");
}

bool SendError(const std::wstring& message) {
    return SendRecord("{\"type\":\"error\",\"message\":\"" +
        JsonEscape(ToUtf8(message)) + "\"}");
}

bool ConnectPipe(const std::wstring& name) {
    const std::wstring path = L"\\\\.\\pipe\\" + name;
    for (int attempt = 0; attempt < 200 && g_running.load(); ++attempt) {
        g_pipe = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
        if (g_pipe != INVALID_HANDLE_VALUE) return true;
        if (GetLastError() != ERROR_PIPE_BUSY && GetLastError() != ERROR_FILE_NOT_FOUND) return false;
        WaitNamedPipeW(path.c_str(), 100);
    }
    return false;
}

bool FindDevice(const std::wstring& requested, std::wstring& udid) {
    std::uint32_t count = 0;
    auto result = im_refresh_devices_ex(nullptr, &count, 1);
    if (result != static_cast<std::int32_t>(iPhoneMirror::Result::Ok) &&
        result != static_cast<std::int32_t>(iPhoneMirror::Result::BufferTooSmall)) return false;
    if (count == 0 || count > 128) return false;
    std::vector<iPhoneMirror::DeviceInfo> devices(count);
    for (auto& d : devices) { std::memset(&d, 0, sizeof(d)); d.struct_size = sizeof(d); d.api_version = iPhoneMirror::ApiVersion; }
    result = im_refresh_devices_ex(devices.data(), &count, 1);
    if (result != static_cast<std::int32_t>(iPhoneMirror::Result::Ok)) return false;

    const auto wanted = Normalize(requested);
    std::vector<std::wstring> matches;
    for (const auto& d : devices) {
        if (!d.usb_connected || d.udid[0] == L'\0') continue;
        // iPhoneMirror's USB discovery and capture backend use the same
        // normalized physical USB serial for PnP, usbmux and libusb matching.
        // A suffix/partial match is not safe: it could select a different
        // attached device and will not pass the engine's own exact preflight.
        const std::wstring candidate(d.udid);
        if (Normalize(candidate) == wanted) matches.push_back(candidate);
    }
    if (matches.size() != 1) return false;
    udid = matches.front();
    return true;
}

int Run(const std::wstring& pipeName, const std::wstring& requestedSerial) {
    if (pipeName.empty() || requestedSerial.empty()) return 2;
    if (!ConnectPipe(pipeName)) return 3;
    if (im_api_version() != iPhoneMirror::ApiVersion) {
        SendError(L"iPhoneMirror Core API version mismatch.");
        return 4;
    }
    const auto init = im_initialize();
    if (init != static_cast<std::int32_t>(iPhoneMirror::Result::Ok)) {
        const wchar_t* detail = im_last_error();
        SendError(detail && *detail ? detail : L"Could not initialize the iPhone USB capture engine.");
        return 5;
    }

    int exitCode = 0;
    iPhoneMirror::SessionHandle session = 0;
    bool sessionCreated = false;
    std::wstring udid;
    if (!FindDevice(requestedSerial, udid)) {
        SendError(L"No exact USB serial match was found in usbmux. Unlock the iPhone, tap Trust This Computer, and reconnect the cable. Do not connect multiple Apple devices while selecting the phone.");
        exitCode = 6;
    } else {
        iPhoneMirror::CaptureOptions options{};
        options.struct_size = sizeof(options);
        options.api_version = iPhoneMirror::ApiVersion;
        options.requested_width = 0;
        options.requested_height = 0;
        options.target_fps = 60;
        options.play_audio = 0;
        options.audio_volume = 0.0f;

        const auto create = im_session_create(udid.c_str(), &options, &session);
        if (create != static_cast<std::int32_t>(iPhoneMirror::Result::Ok)) {
            const wchar_t* detail = im_last_error();
            SendError(detail && *detail ? detail : L"Could not create the iPhone QuickTime USB capture session. Check Apple device support, trust pairing and USB driver setup.");
            exitCode = 7;
        } else {
            sessionCreated = true;
            SendStatus(L"iPhone USB session created; waiting for decoded video frames.");
            std::int64_t lastTimestamp = 0;
            bool announcedStreaming = false;
            while (g_running.load()) {
                iPhoneMirror::CaptureStatus status{};
                status.struct_size = sizeof(status);
                status.api_version = iPhoneMirror::ApiVersion;
                const auto statusResult = im_session_get_status(session, &status);
                if (statusResult != static_cast<std::int32_t>(iPhoneMirror::Result::Ok)) {
                    const wchar_t* detail = im_last_error();
                    SendError(detail && *detail ? detail : L"Could not read iPhone capture status.");
                    exitCode = 8;
                    break;
                }
                if (status.state == iPhoneMirror::CaptureState::Error) {
                    SendError(status.message[0] ? status.message : L"iPhone USB capture failed.");
                    exitCode = 9;
                    break;
                }
                if (status.state == iPhoneMirror::CaptureState::Stopped) break;

                std::int64_t timestamp = 0;
                if (im_session_get_latest_video_timestamp(session, &timestamp) ==
                        static_cast<std::int32_t>(iPhoneMirror::Result::Ok) &&
                    timestamp > lastTimestamp) {
                    iPhoneMirror::VideoFrameInfo info{};
                    info.struct_size = sizeof(info);
                    info.api_version = iPhoneMirror::ApiVersion;
                    std::uint32_t capacity = 0;
                    auto copyResult = im_session_copy_latest_video_frame(
                        session, &info, nullptr, &capacity, 0, 0);
                    if (copyResult == static_cast<std::int32_t>(iPhoneMirror::Result::BufferTooSmall) &&
                        capacity > 0 && capacity <= kMaxFrameBytes) {
                        std::vector<std::uint8_t> source(capacity);
                        info = {};
                        info.struct_size = sizeof(info);
                        info.api_version = iPhoneMirror::ApiVersion;
                        copyResult = im_session_copy_latest_video_frame(
                            session, &info, source.data(), &capacity, 0, 0);
                        if (copyResult == static_cast<std::int32_t>(iPhoneMirror::Result::Ok) &&
                            info.width > 0 && info.height > 0 &&
                            info.width <= 8192 && info.height <= 8192 &&
                            info.pixel_format == 1 && info.stride >= info.width * 4u &&
                            static_cast<std::uint64_t>(info.stride) * info.height <= source.size()) {
                            const std::uint32_t tightStride = info.width * 4u;
                            const std::uint64_t outputSize64 = static_cast<std::uint64_t>(tightStride) * info.height;
                            if (outputSize64 <= kMaxFrameBytes) {
                                std::vector<std::uint8_t> packed(static_cast<std::size_t>(outputSize64));
                                for (std::uint32_t y = 0; y < info.height; ++y)
                                    std::memcpy(packed.data() + static_cast<std::size_t>(y) * tightStride,
                                                source.data() + static_cast<std::size_t>(y) * info.stride,
                                                tightStride);
                                char header[512]{};
                                const int n = std::snprintf(header, sizeof(header),
                                    "{\"type\":\"video\",\"pixelFormat\":\"bgra8\",\"width\":%u,\"height\":%u,\"stride\":%u,\"payloadBytes\":%llu,\"timestamp100ns\":%lld}",
                                    info.width, info.height, tightStride,
                                    static_cast<unsigned long long>(outputSize64),
                                    static_cast<long long>(info.timestamp_100ns));
                                if (n <= 0 || static_cast<std::size_t>(n) >= sizeof(header) ||
                                    !SendRecord(std::string(header, static_cast<std::size_t>(n)),
                                                packed.data(), static_cast<std::uint32_t>(outputSize64))) {
                                    g_running.store(false);
                                    break;
                                }
                                lastTimestamp = info.timestamp_100ns;
                                if (!announcedStreaming) {
                                    SendStatus(L"iPhone USB video is live.");
                                    announcedStreaming = true;
                                }
                            }
                        }
                    }
                }
                Sleep(5);
            }
        }
    }

    if (sessionCreated) {
        im_session_stop(session);
        im_session_destroy(session);
    }
    im_shutdown();
    SendRecord("{\"type\":\"stopped\"}");
    return exitCode;
}
} // namespace

int wmain(int argc, wchar_t** argv) {
    SetConsoleCtrlHandler(ConsoleHandler, TRUE);
    if (argc < 2 || _wcsicmp(argv[1], L"--smaet-ipc") != 0) return 1;
    const auto pipeName = ArgValue(argc, argv, L"--pipe");
    const auto serial = ArgValue(argc, argv, L"--device-serial");
    if (_wcsicmp(ArgValue(argc, argv, L"--video").c_str(), L"bgra8") != 0) return 1;
    return Run(pipeName, serial);
}
