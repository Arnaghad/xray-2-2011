#include <fstream>
#include <sstream>
#include <filesystem>
#include <cstring>
#include <cstdlib>
#include <stdexcept>
#include <atomic>
#include <mutex>
#include <windows.h>
#include <unknwn.h>
#include <wrl/client.h>
#include <dxcapi.h>
#include "shader_compiler.h"
#include "content_hash.h"

using Microsoft::WRL::ComPtr;
namespace xray { namespace render { namespace rhi {
namespace {
std::wstring wide(std::string const& value) {
    if (value.empty()) return {};
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), int(value.size()), nullptr, 0);
    if (!count) throw std::runtime_error("Invalid UTF-8 compiler option");
    std::wstring result(count, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), int(value.size()), result.data(), count);
    return result;
}
struct include_probe {
    std::wstring path;
    bool loaded = false;
    std::vector<unsigned char> bytes;
    bool operator==(include_probe const&) const = default;
};
include_probe probe(IDxcUtils* utils, wchar_t const* name, IDxcBlob** output = nullptr) {
    include_probe p;
    p.path = std::filesystem::absolute(name).lexically_normal().wstring();
    ComPtr<IDxcBlobEncoding> blob;
    p.loaded = SUCCEEDED(utils->LoadFile(p.path.c_str(), nullptr, blob.GetAddressOf()));
    if (p.loaded) {
        auto* begin = static_cast<unsigned char const*>(blob->GetBufferPointer());
        p.bytes.assign(begin, begin + blob->GetBufferSize());
        if (output) { *output = blob.Get(); (*output)->AddRef(); }
    }
    return p;
}
class tracking_includes final : public IDxcIncludeHandler {
    std::atomic<ULONG> refs{1};
    ComPtr<IDxcUtils> utils;
public:
    std::vector<include_probe> probes;
    explicit tracking_includes(IDxcUtils* value) : utils(value) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (IsEqualIID(iid, __uuidof(IUnknown)) || IsEqualIID(iid, __uuidof(IDxcIncludeHandler))) {
            *out = static_cast<IDxcIncludeHandler*>(this); AddRef(); return S_OK;
        }
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE LoadSource(LPCWSTR name, IDxcBlob** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        try {
            auto p = probe(utils.Get(), name, out);
            bool loaded = p.loaded; probes.push_back(std::move(p));
            if (!loaded) return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
            return S_OK;
        } catch (...) { if (*out) { (*out)->Release(); *out = nullptr; } return E_OUTOFMEMORY; }
    }
};
struct cache_entry {
    std::string source;
    std::wstring cwd;
    std::vector<std::wstring> args;
    std::vector<include_probe> includes;
    compilation_result result;
};

std::string narrow_utf8(std::wstring const& value) {
    if (value.empty()) return {};
    int count = WideCharToMultiByte(CP_UTF8, 0, value.data(), int(value.size()), nullptr, 0, nullptr, nullptr);
    if (count <= 0) return {};
    std::string out(static_cast<std::size_t>(count), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value.data(), int(value.size()), out.data(), count, nullptr, nullptr);
    return out;
}

std::array<unsigned char, 32> hash_bytes(std::vector<unsigned char> const& bytes) noexcept {
    cache_util::sha256_hasher h;
    if (!bytes.empty()) h.update(bytes.data(), bytes.size());
    return h.finalize();
}

std::string compute_spirv_cache_key(std::string const& source, std::wstring const& cwd, std::vector<std::wstring> const& args) {
    cache_util::sha256_hasher hasher;
    static constexpr char tag[] = "xray_spv_v1";
    hasher.update(tag, sizeof(tag) - 1);
    hasher.update_string(source);
    hasher.update_string(narrow_utf8(cwd));
    std::uint32_t const arg_count = static_cast<std::uint32_t>(args.size());
    hasher.update_pod(arg_count);
    for (auto const& arg : args)
        hasher.update_string(narrow_utf8(arg));
    return cache_util::hex_string(hasher.finalize());
}

bool try_load_spirv_disk_cache(
    std::filesystem::path const& file_path,
    IDxcUtils* utils,
    std::vector<include_probe>& out_includes,
    compilation_result& out_result) {
    if (file_path.empty() || !utils) return false;
    std::error_code ec;
    auto const file_size = std::filesystem::file_size(file_path, ec);
    constexpr std::uint64_t kMaxDiskBytes = 32ull * 1024 * 1024;
    if (ec || file_size < 16 || file_size > kMaxDiskBytes) return false;

    std::ifstream in(file_path, std::ios::binary);
    if (!in) return false;
    std::vector<unsigned char> raw(static_cast<std::size_t>(file_size));
    if (!in.read(reinterpret_cast<char*>(raw.data()), static_cast<std::streamsize>(raw.size()))
        || static_cast<std::size_t>(in.gcount()) != raw.size())
        return false;

    std::size_t pos = 0;
    auto read_pod = [&](auto& value) -> bool {
        if (pos + sizeof(value) > raw.size()) return false;
        std::memcpy(&value, raw.data() + pos, sizeof(value));
        pos += sizeof(value);
        return true;
    };

    std::uint32_t magic = 0, version = 0, include_count = 0;
    if (!read_pod(magic) || !read_pod(version) || !read_pod(include_count)) return false;
    if (magic != 0x56505358u || version != 1u || include_count > 64u) return false;

    std::vector<include_probe> probed;
    probed.reserve(include_count);
    for (std::uint32_t i = 0; i < include_count; ++i) {
        std::uint32_t path_len = 0;
        if (!read_pod(path_len) || path_len == 0 || path_len > 16384u || path_len > raw.size() - pos)
            return false;
        std::string path_utf8(reinterpret_cast<char const*>(raw.data() + pos), path_len);
        pos += path_len;
        std::uint8_t stored_loaded = 0;
        if (!read_pod(stored_loaded) || stored_loaded > 1u) return false;
        std::array<unsigned char, 32> stored_hash = {};
        if (!read_pod(stored_hash)) return false;

        auto wide_path = wide(path_utf8);
        auto current = probe(utils, wide_path.c_str());
        if (current.loaded != (stored_loaded != 0)) return false;
        if (current.loaded && hash_bytes(current.bytes) != stored_hash) return false;
        probed.push_back(std::move(current));
    }

    std::uint32_t word_count = 0;
    if (!read_pod(word_count) || word_count < 5u || word_count > 4u * 1024u * 1024u)
        return false;
    std::size_t const byte_count = static_cast<std::size_t>(word_count) * sizeof(std::uint32_t);
    if (byte_count > raw.size() - pos) return false;

    std::vector<u32> bytecode(word_count);
    std::memcpy(bytecode.data(), raw.data() + pos, byte_count);
    pos += byte_count;
    if (bytecode[0] != 0x07230203u) return false;

    std::uint32_t footer = 0;
    if (!read_pod(footer) || footer != 0x444E4553u || pos != raw.size()) return false;

    out_includes = std::move(probed);
    out_result = {};
    out_result.bytecode = std::move(bytecode);
    out_result.success = true;
    out_result.cache_hit = false;
    return true;
}

void write_spirv_disk_cache(
    std::filesystem::path const& file_path,
    std::vector<include_probe> const& includes,
    std::vector<u32> const& bytecode) {
    if (file_path.empty() || bytecode.size() < 5) return;
    std::error_code ec;
    if (std::filesystem::is_regular_file(file_path, ec)) return;

    struct encoded_include {
        std::string path_utf8;
        std::uint8_t loaded;
        std::array<unsigned char, 32> hash;
    };
    std::vector<encoded_include> items;
    items.reserve(includes.size());
    std::size_t total_bytes = 5 * sizeof(std::uint32_t) + bytecode.size() * sizeof(u32);
    for (auto const& inc : includes) {
        std::string u8 = narrow_utf8(inc.path);
        if (u8.empty() || u8.size() > 16384u) return;
        total_bytes += sizeof(std::uint32_t) + u8.size() + 1 + 32;
        items.push_back({std::move(u8), static_cast<std::uint8_t>(inc.loaded ? 1 : 0),
            inc.loaded ? hash_bytes(inc.bytes) : std::array<unsigned char, 32>{}});
    }

    std::vector<unsigned char> blob(total_bytes);
    std::size_t pos = 0;
    auto push_pod = [&](auto const& v) {
        std::memcpy(blob.data() + pos, &v, sizeof(v));
        pos += sizeof(v);
    };
    std::uint32_t const magic = 0x56505358u, version = 1u, inc_count = static_cast<std::uint32_t>(items.size());
    push_pod(magic);
    push_pod(version);
    push_pod(inc_count);
    for (auto const& item : items) {
        std::uint32_t const len = static_cast<std::uint32_t>(item.path_utf8.size());
        push_pod(len);
        std::memcpy(blob.data() + pos, item.path_utf8.data(), len);
        pos += len;
        push_pod(item.loaded);
        push_pod(item.hash);
    }
    std::uint32_t const word_count = static_cast<std::uint32_t>(bytecode.size());
    push_pod(word_count);
    std::size_t const spv_bytes = bytecode.size() * sizeof(u32);
    std::memcpy(blob.data() + pos, bytecode.data(), spv_bytes);
    pos += spv_bytes;
    std::uint32_t const footer = 0x444E4553u;
    push_pod(footer);
    cache_util::atomic_write_file(file_path, blob.data(), blob.size());
}
}
struct shader_compiler_impl {
    HMODULE library = nullptr;
    ComPtr<IDxcUtils> utils;
    ComPtr<IDxcCompiler3> compiler;
    std::vector<cache_entry> cache;
    std::mutex mutex;
    std::string error;
    shader_compiler_impl() {
        char const* configured = std::getenv("XRAY_DXC_PATH");
        if (configured && !std::filesystem::path(configured).is_absolute()) {
            error = "XRAY_DXC_PATH must be an absolute path to dxcompiler.dll";
            return;
        }
        auto path = configured ? wide(configured) : std::wstring(L"dxcompiler.dll");
        library = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS
            | (configured ? LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR : 0));
        if (!library) {
            error = "Cannot load DXC (Win32 error " + std::to_string(GetLastError())
                + "). Deploy dxcompiler.dll beside the executable or set XRAY_DXC_PATH to its absolute path.";
            return;
        }
        auto create = reinterpret_cast<DxcCreateInstanceProc>(GetProcAddress(library, "DxcCreateInstance"));
        if (!create || FAILED(create(CLSID_DxcUtils, __uuidof(IDxcUtils), reinterpret_cast<void**>(utils.GetAddressOf())))
            || FAILED(create(CLSID_DxcCompiler, __uuidof(IDxcCompiler3), reinterpret_cast<void**>(compiler.GetAddressOf()))))
            error = "DXC COM initialization failed (DxcCreateInstance / include handler)";
    }
    ~shader_compiler_impl() {
        cache.clear(); compiler.Reset(); utils.Reset();
        if (library) FreeLibrary(library);
    }
};
shader_compiler::shader_compiler() : m_impl(std::make_unique<shader_compiler_impl>()) {}
shader_compiler::~shader_compiler() = default;

std::string shader_compiler::get_target_profile(shader_stage stage) {
    switch (stage) {
        case shader_stage::vertex: return "vs_6_0";
        case shader_stage::pixel: return "ps_6_0";
        case shader_stage::geometry: return "gs_6_0";
        case shader_stage::hull: return "hs_6_0";
        case shader_stage::domain: return "ds_6_0";
        case shader_stage::compute: return "cs_6_0";
        default: throw std::runtime_error("Unknown shader stage");
    }
}
compilation_result shader_compiler::compile(std::string const& source, shader_stage stage, shader_compile_options const& options) {
    std::lock_guard lock(m_impl->mutex);
    compilation_result result;
    if (!m_impl->error.empty()) { result.error_log = m_impl->error; return result; }
    try {
        if (options.entry.empty()) throw std::runtime_error("Shader entry must not be empty");
        std::vector<std::wstring> args = {L"-spirv", L"-fspv-target-env=vulkan1.3", L"-HV", L"2021",
            L"-T", wide(get_target_profile(stage)), L"-E", wide(options.entry), wide("-fspv-entrypoint-name=" + options.entry)};
        for (auto const& directory : options.include_directories) { args.push_back(L"-I"); args.push_back(wide(directory)); }
        for (auto const& define : options.defines) { args.push_back(L"-D"); args.push_back(wide(define)); }
        for (auto const& flag : options.flags) {
            if (flag == "-spirv" || flag.starts_with("-fspv-target-env") || flag.starts_with("-fspv-entrypoint-name")
                || flag.starts_with("-T") || flag.starts_with("/T") || flag.starts_with("-E") || flag.starts_with("/E")
                || flag.starts_with("-Fo") || flag.starts_with("/Fo") || flag == "-Vd")
                throw std::runtime_error("Reserved DXC option: " + flag);
            args.push_back(wide(flag));
        }
        auto cwd = std::filesystem::current_path().wstring();
        for (auto it = m_impl->cache.begin(); it != m_impl->cache.end(); ++it) {
            if (it->source != source || it->args != args || it->cwd != cwd) continue;
            bool current = true;
            for (auto const& dependency : it->includes)
                if (!(probe(m_impl->utils.Get(), dependency.path.c_str()) == dependency)) { current = false; break; }
            if (current) { result = it->result; result.cache_hit = true; return result; }
            m_impl->cache.erase(it); break;
        }
        std::filesystem::path disk_cache_path;
        auto const user_data = cache_util::resolve_sdk_user_data_dir();
        if (!user_data.empty()) {
            disk_cache_path = user_data / L"cache" / L"spirv" / ("spv_v1_" + compute_spirv_cache_key(source, cwd, args) + ".bin");
            std::vector<include_probe> disk_includes;
            if (try_load_spirv_disk_cache(disk_cache_path, m_impl->utils.Get(), disk_includes, result)) {
                if (m_impl->cache.size() == 64) m_impl->cache.erase(m_impl->cache.begin());
                m_impl->cache.push_back({source, cwd, args, std::move(disk_includes), result});
                return result;
            }
        }
        ComPtr<tracking_includes> includes;
        includes.Attach(new tracking_includes(m_impl->utils.Get()));
        std::vector<LPCWSTR> arguments;
        for (auto const& arg : args) arguments.push_back(arg.c_str());
        DxcBuffer buffer = {source.data(), source.size(), DXC_CP_UTF8};
        ComPtr<IDxcResult> compiled;
        HRESULT hr = m_impl->compiler->Compile(&buffer, arguments.data(), UINT32(arguments.size()),
            includes.Get(), __uuidof(IDxcResult), reinterpret_cast<void**>(compiled.GetAddressOf()));
        if (FAILED(hr) || !compiled) throw std::runtime_error("IDxcCompiler3::Compile failed (HRESULT=" + std::to_string(hr) + ")");
        ComPtr<IDxcBlobUtf8> errors;
        compiled->GetOutput(DXC_OUT_ERRORS, __uuidof(IDxcBlobUtf8), reinterpret_cast<void**>(errors.GetAddressOf()), nullptr);
        if (errors && errors->GetStringLength()) result.error_log.assign(errors->GetStringPointer(), errors->GetStringLength());
        HRESULT status = E_FAIL;
        if (FAILED(compiled->GetStatus(&status)) || FAILED(status)) {
            if (result.error_log.empty()) result.error_log = "DXC returned failed compilation status";
            return result;
        }
        ComPtr<IDxcBlob> object;
        hr = compiled->GetOutput(DXC_OUT_OBJECT, __uuidof(IDxcBlob), reinterpret_cast<void**>(object.GetAddressOf()), nullptr);
        if (FAILED(hr) || !object || object->GetBufferSize() < 20 || object->GetBufferSize() % sizeof(u32))
            throw std::runtime_error("DXC returned invalid SPIR-V object size");
        result.bytecode.resize(object->GetBufferSize() / sizeof(u32));
        std::memcpy(result.bytecode.data(), object->GetBufferPointer(), object->GetBufferSize());
        if (result.bytecode[0] != 0x07230203u) throw std::runtime_error("DXC output is not SPIR-V");
        result.success = true;
        if (!disk_cache_path.empty())
            write_spirv_disk_cache(disk_cache_path, includes->probes, result.bytecode);
        if (m_impl->cache.size() == 64) m_impl->cache.erase(m_impl->cache.begin());
        m_impl->cache.push_back({source, cwd, args, includes->probes, result});
    } catch (std::exception const& error) {
        result.error_log = error.what(); result.bytecode.clear(); result.success = result.cache_hit = false;
    }
    return result;
}
compilation_result shader_compiler::compile_file(std::string const& path, shader_stage stage, shader_compile_options const& options) {
    static std::filesystem::path const packaged_shaders = []() -> std::filesystem::path {
        wchar_t executable[32768]{};
        DWORD length = GetModuleFileNameW(nullptr, executable, 32768);
        if (length == 0 || length >= 32768) return {};
        std::error_code ec;
        auto pkg_root = std::filesystem::path(executable).parent_path().parent_path();
        auto marker = pkg_root / L"settings" / L"sdk-runtime.json";
        auto shaders = pkg_root / L"shaders";
        if (std::filesystem::is_regular_file(marker, ec) && std::filesystem::is_directory(shaders, ec))
            return shaders.lexically_normal();
        return {};
    }();
    std::filesystem::path effective_path = std::filesystem::u8path(path);
    if (!packaged_shaders.empty()) {
        std::string generic = effective_path.lexically_normal().generic_string();
        std::error_code ec;
        constexpr char marker[] = "/render/vulkan/assets/";
        if (auto pos = generic.find(marker); pos != std::string::npos) {
            std::filesystem::path candidate = packaged_shaders / std::filesystem::u8path(generic.substr(pos + sizeof(marker) - 1));
            if (std::filesystem::is_regular_file(candidate, ec))
                effective_path = candidate;
        }
    }
    std::ifstream file(effective_path, std::ios::binary);
    if (!file) return {{}, "Could not open shader file: " + path, false};
    std::stringstream contents;
    contents << file.rdbuf();
    if (file.bad()) return {{}, "Could not read shader file: " + path, false};
    auto resolved = options;
    auto parent = effective_path.parent_path();
    if (!parent.empty()) resolved.include_directories.insert(resolved.include_directories.begin(), parent.string());
    return compile(contents.str(), stage, resolved);
}
} } }
