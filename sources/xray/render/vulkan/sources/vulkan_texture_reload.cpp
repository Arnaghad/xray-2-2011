#include "pch.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/render/core/memory.h>
#include <xray/resources_query_result.h>
#include <xray/resources_cook_classes.h>
#include "vulkan_diagnostics.h"
#include <algorithm>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan {
namespace {
std::string source_key(std::string path) {
    for(char& c:path){if(c=='\\')c='/';else if(c>='A' && c<='Z')c+=32;}
    if(path.starts_with("resources.converted/textures/")) {
        path="resources/textures/"+path.substr(std::strlen("resources.converted/textures/"));
        if(path.ends_with(".dds"))path.replace(path.size()-4,4,".tga");
    }
    return path;
}
bool equal_texture(texture_data const& a,texture_data const& b) {
    return a.description.type==b.description.type && a.description.array_layers==b.description.array_layers
        && a.description.width==b.description.width && a.description.height==b.description.height
        && a.description.mip_levels==b.description.mip_levels && a.description.pixel_format==b.description.pixel_format
        && a.mips==b.mips;
}
bool readable_texture(std::string const& path,texture_configuration const& config) {
    WIN32_FILE_ATTRIBUTE_DATA info={};
    if (!GetFileAttributesExA(path.c_str(),GetFileExInfoStandard,&info) || (info.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)) return false;
    uint64_t size=(uint64_t(info.nFileSizeHigh)<<32)|info.nFileSizeLow;
    return size>=(config.source_tga ? 18u : 128u+unsigned(config.legacy_cooked))
        && size<=(config.source_tga ? 128u : 64u)*1024u*1024u+unsigned(config.legacy_cooked);
}
}
void scene_registry::track_texture(std::shared_ptr<texture_state> const& texture) {
    std::lock_guard lock(m_mutex);
    if (!m_accepting) throw std::runtime_error("Texture resource session is closed");
    std::erase_if(m_textures,[](auto const& item) { return item.expired(); });
    m_textures.push_back(texture);
}
texture_reload_statistics scene_registry::texture_reload_status() const { std::lock_guard lock(m_mutex); return m_texture_reload; }
std::vector<std::shared_ptr<texture_data const>> scene_registry::terrain_texture_snapshots(std::vector<std::shared_ptr<texture_state>> const& states) const {
    std::lock_guard lock(m_mutex);
    if (!m_accepting || m_texture_reload.pending) return {};
    std::vector<std::shared_ptr<texture_data const>> result;result.reserve(states.size());
    // The registry lock also serializes reload publication/start, so roles cannot tear during capture.
    for (auto const& state : states) {
        std::lock_guard state_lock(state->mutex);
        if (state->pending || state->reload_failed) return {};
        result.push_back(state->data);
    }
    return result;
}
void scene_registry::finish_texture_reload(std::weak_ptr<texture_state> const& target,std::shared_ptr<texture_data const> const& replacement) {
    std::lock_guard lock(m_mutex);
    --m_texture_reload.pending;
    auto state=target.lock();
    if (!m_accepting || !state) ++m_texture_reload.cancelled;
    else {
        std::lock_guard state_lock(state->mutex); state->pending=false;
        state->reload_failed=!replacement || replacement->description.type!=state->data->description.type
            || replacement->description.array_layers!=state->data->description.array_layers;
        if (state->reload_failed) ++m_texture_reload.failed;
        else if (equal_texture(*state->data,*replacement)) ++m_texture_reload.unchanged;
        else { state->data=replacement; ++m_texture_reload.reloaded; }
    }
    record_texture_reload(m_texture_reload);
}
void scene_registry::reload_modified_textures() {reload_textures(nullptr,nullptr);}
unsigned scene_registry::publish_texture_source(std::string const& virtual_path,std::string const& physical_path) {
    if(virtual_path.empty() || physical_path.empty())throw std::runtime_error("Missing texture source path");
    return reload_textures(&virtual_path,&physical_path);
}
std::string scene_registry::texture_source(std::string const& virtual_path) const {
    std::lock_guard lock(m_mutex);auto found=m_texture_sources.find(source_key(virtual_path));
    return found==m_texture_sources.end()?std::string{}:found->second;
}
unsigned scene_registry::reload_textures(std::string const* virtual_path,std::string const* replacement_path) {
    auto self=shared_from_this();
    std::vector<std::shared_ptr<texture_state>> work;
    auto selected=virtual_path?source_key(*virtual_path):std::string{};
    {
        std::lock_guard lock(m_mutex);
        if (!m_accepting) throw std::runtime_error("Texture reload session is closed");
        std::erase_if(m_textures,[](auto const& item) { return item.expired(); });
        work.reserve(m_textures.size());
        for (auto const& item : m_textures) {
            auto state=item.lock(); if (!state) continue;
            std::lock_guard state_lock(state->mutex);
            if(virtual_path && source_key(state->virtual_path)!=selected)continue;
            if (state->pending) {if(virtual_path)throw std::runtime_error("Texture source is already reloading");continue;}
            work.push_back(state);
        }
        // Allocate replacements before publishing any new source identity.
        std::vector<std::string> replacements;if(replacement_path)replacements.assign(work.size(),*replacement_path);
        if(replacement_path)m_texture_sources.insert_or_assign(selected,*replacement_path);
        for(size_t i=0;i<work.size();++i) {
            auto& state=work[i];std::lock_guard state_lock(state->mutex);
            if(replacement_path){state->physical_path.swap(replacements[i]);state->configuration.source_tga=true;state->configuration.legacy_cooked=false;}
            state->pending=true;
            ++m_texture_reload.requested; ++m_texture_reload.pending;
        }
        record_texture_reload(m_texture_reload);
    }
    // The physical query builds a fresh file node, avoiding stale VFS size/cache metadata.
    for (auto const& state : work) {
        std::weak_ptr<texture_state> target=state;
        try {
            if (!readable_texture(state->physical_path,state->configuration))
                throw std::runtime_error("Missing, unreadable or oversized texture reload source: "+state->physical_path);
            resources::user_data_variant user; user.set(state->configuration);
            std::string path="@"+std::string(fs_new::virtual_path_string::convert(state->physical_path.c_str()).c_str());
            resources::query_resource(path.c_str(),resources::texture_class,[self,target](resources::queries_result& result) {
                std::shared_ptr<texture_data const> replacement;
                if (result.is_successful()) {
                    auto resource=result[0].get_unmanaged_resource();
                    auto* texture=dynamic_cast<texture_resource*>(resource.c_ptr());
                    if (texture) replacement=texture->snapshot();
                }
                if (!replacement && self->accepting()) diagnostic("error","Vulkan texture reload failed; retaining previous texture");
                self->finish_texture_reload(target,replacement);
            },render::g_allocator,&user,nullptr,assert_on_fail_false);
        } catch (std::exception const& error) {
            diagnostic("error","Vulkan texture reload failed: %s",error.what());
            finish_texture_reload(target,{});
        }
    }
    return unsigned(work.size());
}
} } }
