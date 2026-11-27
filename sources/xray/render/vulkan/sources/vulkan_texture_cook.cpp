#include "pch.h"
#include "vulkan_texture_cook.h"
#include "vulkan_binary_config.h"
#include "vulkan_diagnostics.h"
#include <xray/render/core/memory.h>
#include <xray/resources_fs.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <filesystem>
#include <stdexcept>
#include <system_error>

namespace xray { namespace render { namespace vulkan {
namespace {
void failed(resources::query_result_for_cook& query,std::exception const& error) {
    diagnostic("error","Vulkan texture query failed: %s",error.what()); query.finish_query(resources::cook_base::result_error);
}
struct file_request {
    resources::managed_resource_ptr bytes;
    std::string physical_path,virtual_path;
    texture_configuration config;
};
bool extension(std::string const& path,pcstr suffix) {
    if (path.size()<4) return false;
    for (size_t i=0;i<4;++i) {
        char c=path[path.size()-4+i]; if (c>='A' && c<='Z') c=char(c-'A'+'a');
        if (c!=suffix[i]) return false;
    }
    return true;
}
}
texture_cook::texture_cook(std::shared_ptr<scene_registry> owner)
    : translate_query_cook(resources::texture_class,reuse_false,use_current_thread_id),m_owner(std::move(owner)),m_allocator(render::g_allocator) {}
void texture_cook::finish(resources::query_result_for_cook& query,const_buffer bytes,texture_configuration const& effective,
                         texture_configuration const& defaults,std::string const& physical_path,std::string const& virtual_path) {
    auto* texture=XRAY_NEW_IMPL(*m_allocator,texture_resource)(m_owner,bytes,effective,physical_path,virtual_path);
    // Reload starts from caller defaults, then rereads current source options.
    texture->state->configuration=defaults; texture->state->configuration.reload_read=true;
    query.set_unmanaged_resource(texture,resources::nocache_memory,sizeof(*texture)); query.finish_query(result_success);
}
void texture_cook::load_file(resources::query_result_for_cook& query,std::string const& path,texture_configuration config,std::string const& virtual_path) {
    if (path.size()>=fs_new::max_path_length) throw std::runtime_error("Texture path exceeds VFS bounds");
    config.source_tga=config.source_tga || extension(path,".tga");
    resources::query_resource(path.c_str(),resources::raw_data_class,[this,&query,path,config,virtual_path](resources::queries_result& result) {
        try {
            if (!result.is_successful()) { query.finish_query(result_error); return; }
            auto data=std::make_shared<file_request>(); data->bytes=result[0].get_managed_resource(); data->config=config;
            if (!config.reload_read && result[0].get_fat_it().is_physical())
                data->physical_path=static_cast<resources::query_result&>(result[0]).absolute_physical_path(assert_on_fail_false).c_str();
            if(!config.reload_read && !path.starts_with("@"))data->virtual_path=path;
            if(!virtual_path.empty()){data->virtual_path=virtual_path;data->physical_path=path.substr(1);}
            auto complete=[this,&query,data](texture_configuration effective) {
                resources::pinned_ptr_const<u8> bytes(data->bytes);
                finish(query,bytes,effective,data->config,data->physical_path,data->virtual_path);
            };
            if (!config.source_tga) { complete(config); return; }
            if (!extension(path,".tga")) throw std::runtime_error("Disk TGA requires a .tga path");
            std::string options=path.substr(0,path.size()-4)+".options";
            if (options.size()>=fs_new::max_path_length) throw std::runtime_error("Texture options path exceeds VFS bounds");
            auto read_options=[this,&query,data,complete,options] {
                resources::query_resource(options.c_str(),resources::raw_data_class,[&query,data,complete](resources::queries_result& source) {
                    try {
                        if (!source.is_successful()) { query.finish_query(result_error); return; }
                        resources::pinned_ptr_const<u8> bytes(source[0].get_managed_resource());
                        complete(load_texture_options(bytes,data->config));
                    } catch (std::exception const& error) { failed(query,error); }
                },m_allocator,nullptr,&query,assert_on_fail_false);
            };
            if (options.starts_with("@")) {
                std::filesystem::path const opt_path = std::filesystem::u8path(options.substr(1));
                std::error_code ec;
                auto const status = std::filesystem::status(opt_path, ec);
                if (ec == std::errc::no_such_file_or_directory || ec == std::errc::not_a_directory || status.type() == std::filesystem::file_type::not_found) {
                    complete(config); return;
                }
                if (ec || std::filesystem::is_directory(status)) throw std::runtime_error("Unreadable TGA options");
                auto const size = std::filesystem::file_size(opt_path, ec);
                if (ec || !size || size > 65536)
                    throw std::runtime_error("Invalid TGA options file");
                read_options(); return;
            }
            resources::query_vfs_iterator(fs_new::virtual_path_string(options.c_str()),[&query,config,complete,read_options](vfs::vfs_locked_iterator const& it) {
                try {
                    if (it.is_end()) { complete(config); return; }
                    if (it.is_folder() || !it.get_file_size() || it.get_file_size()>65536) throw std::runtime_error("Invalid TGA options file");
                    read_options();
                } catch (std::exception const& error) { failed(query,error); }
            },m_allocator,resources::recursive_false,&query);
        } catch (std::exception const& error) { failed(query,error); }
    },m_allocator,nullptr,&query,assert_on_fail_false);
}
void texture_cook::translate_query(resources::query_result_for_cook& query) {
    try {
        if (!m_owner->accepting()) throw std::runtime_error("Texture cook session is closed");
        texture_configuration config;
        if (query.user_data()) {
            if(auto* view=query.user_data()->get_if<texture_view_request>()) {
                auto* result=XRAY_NEW_IMPL(*m_allocator,texture_resource)(m_owner,view->data);query.set_unmanaged_resource(result,resources::nocache_memory,sizeof(*result));query.finish_query(result_success);return;
            }
            auto* value=query.user_data()->get_if<texture_configuration>();
            if (!value) throw std::runtime_error("Unknown texture configuration user data"); config=*value;
        }
        if (auto bytes=query.creation_data_from_user()) { finish(query,bytes,config,config,{}); return; }
        std::string path=query.get_requested_path();
        if (!path.starts_with("@")) binary::validate_path(path);
        // Virtual resource paths use canonical lowercase extensions.
        if (!path.starts_with("@") && (extension(path,".tga") || extension(path,".dds")))
            path.replace(path.size()-4,4,extension(path,".tga") ? ".tga" : ".dds");
        if (extension(path,".tga") || (extension(path,".dds") && !path.starts_with("resources/textures/") && !path.starts_with("resources.converted/textures/"))) {
            if(!config.reload_read)if(auto physical=m_owner->texture_source(path);!physical.empty()) {
                config.source_tga=true;config.legacy_cooked=false;load_file(query,"@"+std::string(fs_new::virtual_path_string::convert(physical.c_str()).c_str()),config,path);return;
            }
            load_file(query,path,config); return;
        }
        if (path.starts_with("resources/textures/")) path.erase(0,std::strlen("resources/textures/"));
        else if (path.starts_with("resources.converted/textures/")) path.erase(0,std::strlen("resources.converted/textures/"));
        if (extension(path,".dds")) path.resize(path.size()-4);
        binary::validate_path(path);
        std::string cooked="resources.converted/textures/"+path+".dds",source="resources/textures/"+path+".tga";
        binary::validate_path(cooked); binary::validate_path(source);
        if(auto physical=m_owner->texture_source(source);!physical.empty()) {
            config.source_tga=true;config.legacy_cooked=false;load_file(query,"@"+std::string(fs_new::virtual_path_string::convert(physical.c_str()).c_str()),config,source);return;
        }
        resources::query_vfs_iterator(fs_new::virtual_path_string(cooked.c_str()),[this,&query,cooked,source,config](vfs::vfs_locked_iterator const& it) mutable {
            try {
                if (!it.is_end()) {
                    if (it.is_folder()) throw std::runtime_error("Cooked texture path is a folder");
                    config.legacy_cooked=true; config.source_tga=false; load_file(query,cooked,config);
                } else { config.legacy_cooked=false; config.source_tga=true; load_file(query,source,config); }
            } catch (std::exception const& error) { failed(query,error); }
        },m_allocator,resources::recursive_false,&query);
    } catch (std::exception const& error) { failed(query,error); }
}
void texture_cook::delete_resource(resources::resource_base* resource) {
    auto* texture=static_cast<texture_resource*>(resource); XRAY_DELETE_IMPL(*m_allocator,texture);
}
} } }
