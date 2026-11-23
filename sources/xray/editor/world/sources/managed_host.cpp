#include "pch.h"
#include "managed_host.h"
#include <algorithm>
#include <filesystem>
#include <stdexcept>
#include <mutex>
#include <vector>
#include <hostfxr.h>
#include <coreclr_delegates.h>
namespace xray::editor::hosting {
namespace {
std::filesystem::path resolve_hostfxr(std::filesystem::path& dotnet_root_out) {
    wchar_t env_root[32768]{};
    DWORD n=GetEnvironmentVariableW(L"DOTNET_ROOT",env_root,32768);
    if(n>0 && n<32768) {
        std::filesystem::path root(env_root);
        std::filesystem::path fxr_dir=root/L"host"/L"fxr";
        std::error_code ec;
        if(std::filesystem::is_directory(fxr_dir,ec)) {
            std::vector<std::filesystem::path> candidates;
            for(auto const& entry : std::filesystem::directory_iterator(fxr_dir,ec)) {
                if(entry.is_directory(ec) && std::filesystem::is_regular_file(entry.path()/L"hostfxr.dll",ec))
                    candidates.push_back(entry.path());
            }
            if(!candidates.empty()) {
                std::sort(candidates.begin(),candidates.end(),[](auto const& a,auto const& b) {
                    return a.filename().wstring()>b.filename().wstring();
                });
                dotnet_root_out=root.lexically_normal().make_preferred();
                return (candidates.front()/L"hostfxr.dll").lexically_normal().make_preferred();
            }
        }
    }
    std::filesystem::path fallback(L"" XRAY_HOSTFXR_PATH);
    dotnet_root_out=fallback.parent_path().parent_path().parent_path().parent_path().make_preferred();
    return fallback;
}
void* entrypoint(wchar_t const* method,std::filesystem::path directory) {
    // CoreCLR is process-owned. Keep hostfxr loaded for all restart cycles;
    // individual bridge contexts/delegates/windows are disposed independently.
    static std::mutex lock;std::lock_guard guard(lock);
    static HMODULE module=nullptr;
    static load_assembly_and_get_function_pointer_fn load=nullptr;
    static std::filesystem::path deployment;
    if(directory.empty()) {
        wchar_t executable[32768];auto n=GetModuleFileNameW(nullptr,executable,32768);
        if(!n || n==32768)throw std::runtime_error("Cannot locate editor deployment directory");
        directory=std::filesystem::path(executable).parent_path();
    }
    directory=std::filesystem::absolute(directory).lexically_normal();
    if(load && directory!=deployment)throw std::runtime_error("Editor runtime already owns another deployment");
    if(!load) {
    std::filesystem::path dotnet_root;
    std::filesystem::path hostfxr_path=resolve_hostfxr(dotnet_root);
    if(!module){module=GetModuleHandleW(hostfxr_path.c_str());if(!module)module=LoadLibraryW(hostfxr_path.c_str());}
    if(!module)throw std::runtime_error("Cannot load the configured system hostfxr");
    auto initialize=reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(module,"hostfxr_initialize_for_runtime_config"));
    auto get_delegate=reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(module,"hostfxr_get_runtime_delegate"));
    auto close=reinterpret_cast<hostfxr_close_fn>(GetProcAddress(module,"hostfxr_close"));
    if(!initialize || !get_delegate || !close)throw std::runtime_error("Incomplete hostfxr API");
    auto config=directory/L"xray.editor.base.runtimeconfig.json";
    hostfxr_initialize_parameters parameters{sizeof(hostfxr_initialize_parameters),nullptr,dotnet_root.c_str()};
    hostfxr_handle context=nullptr;
    auto status=initialize(config.c_str(),&parameters,&context);
    if(status<0 || !context){if(context)close(context);throw std::runtime_error("Cannot initialize editor .NET Desktop runtime");}
    void* loader=nullptr;status=get_delegate(context,hdt_load_assembly_and_get_function_pointer,&loader);close(context);
    if(status<0 || !loader)throw std::runtime_error("Cannot obtain managed assembly loader");
    deployment=directory;load=reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loader);
    }
    auto assembly=directory/L"xray.editor.base.dll";
    void* entry=nullptr;
    auto status=load(assembly.c_str(),L"xray.editor_base.Bridge, xray.editor.base",method,UNMANAGEDCALLERSONLY_METHOD,nullptr,&entry);
    if(status<0 || !entry)throw std::runtime_error("Cannot load editor managed bridge factory");
    return entry;
}
}
create_world_fn factory(std::filesystem::path directory) {return reinterpret_cast<create_world_fn>(entrypoint(L"CreateWorld",directory));}
bind_sdk_fn sdk_factory(std::filesystem::path directory) {return reinterpret_cast<bind_sdk_fn>(entrypoint(L"BindSdk",directory));}
bind_resources_fn resource_factory(std::filesystem::path directory) {return reinterpret_cast<bind_resources_fn>(entrypoint(L"BindResources",directory));}
bind_scene_fn scene_factory(std::filesystem::path directory) {return reinterpret_cast<bind_scene_fn>(entrypoint(L"BindScene",directory));}
}
