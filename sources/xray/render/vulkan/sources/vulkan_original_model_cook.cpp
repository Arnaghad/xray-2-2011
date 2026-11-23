#include "pch.h"
#include "vulkan_original_static_assets.h"
#include "vulkan_binary_config.h"
#include "vulkan_diagnostics.h"
#include <xray/render/vulkan/material_resources.h>
#include <xray/resources_fs.h>
#include <xray/resources_query_result.h>
#include <xray/resources_pinned_ptr.h>
#include <filesystem>
#include <fstream>
#include <cwctype>
#include <cmath>
#include <set>
#include <stdexcept>

namespace xray { namespace render { namespace vulkan { namespace original {
std::string validate_cache_directory(std::string const& directory,std::vector<std::string> const& sources) {
    if(directory.empty() || sources.empty())throw std::runtime_error("Original runtime cache and source roots are required");
    auto canonical=[](std::string const& path){return std::filesystem::weakly_canonical(std::filesystem::absolute(path));};
    auto cache=canonical(directory);
    auto contains=[](auto const& parent,auto const& child){auto p=parent.begin(),c=child.begin();
        for(;p!=parent.end();++p,++c) {
            if(c==child.end())return false;
            auto left=p->wstring(),right=c->wstring();
            for(auto& ch:left)ch=std::towlower(ch);
            for(auto& ch:right)ch=std::towlower(ch);
            if(left!=right)return false;
        }return true;};
    for(auto const& source:sources) {
        auto root=canonical(source);
        if(contains(root,cache) || contains(cache,root))throw std::runtime_error("Original runtime cache overlaps a read-only source mount");
    }
    return cache.string();
}
namespace {
struct cooking:std::enable_shared_from_this<cooking> {
    resources::query_result_for_cook& query;
    std::shared_ptr<scene_registry> owner;
    memory::base_allocator& allocator;
    std::string model,settings;
    std::filesystem::path cache;
    std::function<void(std::shared_ptr<static_model_data const>)> complete;
    files archive,sources;
    std::vector<std::string> materials,material_sources;
    std::vector<bytes> configs;
    std::shared_ptr<static_model_data> data=std::make_shared<static_model_data>();
    bool finished=false;
    cooking(resources::query_result_for_cook& q,std::shared_ptr<scene_registry> o,memory::base_allocator& a,
        std::string m,std::string const& c,std::function<void(std::shared_ptr<static_model_data const>)> done)
        :query(q),owner(std::move(o)),allocator(a),model(std::move(m)),cache(c),complete(std::move(done)) {
        binary::validate_path(model);
        if(!model.starts_with("resources/models/") || !model.ends_with(".model/render") || model.size()>=240 || c.empty())
            throw std::runtime_error("Original static cook requires resources/models/<name>.model/render and a separate cache");
        model=model.substr(std::strlen("resources/models/"));model.resize(model.size()-7);
        settings="models/"+model+"/settings";
        std::vector<std::string> roots;
        for(pcstr alias:{"resources","original.exported"}) {
            fs_new::native_path_string physical;
            if(!resources::convert_virtual_to_physical_path(&physical,fs_new::virtual_path_string(alias),nullptr))
                throw std::runtime_error("Original runtime cook requires physical source mounts");
            roots.push_back(physical.c_str());
        }
        cache=validate_cache_directory(c,roots);
    }
    template<class F>void guard(F action) {
        if(finished)return;
        try {
            if(!owner->accepting())throw std::runtime_error("Original static cook session is closed");
            action();
        } catch(std::exception const& e) {
            finished=true;diagnostic("error","Original static query failed [%s]: %s",query.get_requested_path(),e.what());
            query.finish_query(resources::cook_base::result_error);
        }
    }
    void store(std::string const& relative,bytes const& payload) {
        binary::validate_path(relative);auto path=cache/relative;
        if(std::filesystem::exists(path)) {
            std::ifstream input(path,std::ios::binary);bytes existing((std::istreambuf_iterator<char>(input)),{});
            if(existing!=payload)throw std::runtime_error("Original runtime cache differs from deterministic recook: "+relative);
            return;
        }
        std::filesystem::create_directories(path.parent_path());std::ofstream output(path,std::ios::binary);
        output.write(reinterpret_cast<pcstr>(payload.data()),payload.size());output.close();
        if(!output)throw std::runtime_error("Cannot write original runtime cache: "+relative);
    }
    static bytes copy(resources::queries_result& result,u32 index) {
        resources::pinned_ptr_const<u8> raw(result[index].get_managed_resource());
        if(raw.size()>64u*1024u*1024u)throw std::runtime_error("Original raw dependency exceeds bounds");
        return bytes(raw.c_ptr(),raw.c_ptr()+raw.size());
    }
    void start() {
        auto self=shared_from_this();std::string path="original.exported/models/"+model,config="resources/"+settings;
        resources::request requests[]={{path.c_str(),resources::raw_data_class},{config.c_str(),resources::raw_data_class}};
        resources::query_resources(requests,2,[self](resources::queries_result& result){self->guard([&]{
            if(!result.is_successful())throw std::runtime_error("Missing original export archive or source model settings");
            auto raw=copy(result,0);self->archive=read_export_archive(const_buffer(raw.data(),raw.size()));
            self->sources.emplace(self->settings,copy(result,1));
            self->closure([self]{self->select_materials();});
        });},&allocator,nullptr,&query,assert_on_fail_false);
    }
    void fetch(std::vector<std::string> paths,std::function<void()> next) {
        if(paths.empty()){next();return;}
        if(sources.size()+paths.size()>128)throw std::runtime_error("Original config module count exceeds bounds");
        std::vector<std::string> logical;std::vector<resources::request> requests;
        for(auto const& path:paths){binary::validate_path(path);logical.push_back("resources/"+path);}
        for(auto const& path:logical)requests.push_back({path.c_str(),resources::raw_data_class});
        auto self=shared_from_this();
        resources::query_resources(requests.data(),u32(requests.size()),[self,paths=std::move(paths),next=std::move(next)](resources::queries_result& result){self->guard([&]{
            if(!result.is_successful())throw std::runtime_error("Missing original source material/module dependency");
            size_t total=0;for(auto const& entry:self->sources)total+=entry.second.size();
            for(u32 i=0;i<paths.size();++i) {
                auto raw=copy(result,i);total+=raw.size();
                if(raw.size()>4u*1024u*1024u || total>8u*1024u*1024u)throw std::runtime_error("Original module bytes exceed bounds");
                self->sources.emplace(paths[i],std::move(raw));
            }
            next();
        });},&allocator,nullptr,&query,assert_on_fail_false);
    }
    void closure(std::function<void()> next) {
        std::set<std::string> missing;
        for(auto const& [name,raw]:sources)for(auto const& ref:source_references(const_buffer(raw.data(),raw.size())))
            if(!sources.contains(ref))missing.insert(ref);
        if(missing.empty()){next();return;}
        auto self=shared_from_this();fetch({missing.begin(),missing.end()},[self,next=std::move(next)]{self->closure(next);});
    }
    void select_materials() {
        auto config=read_source_config(sources,settings);auto const& table=config.field("material_settings");
        for(auto const& entry:table.children) {
            auto const& material=entry.field("material_name");
            if(material.type!=5 || material.text.empty())throw std::runtime_error("Original model requires named textured material surfaces");
            binary::validate_path(material.text);
            if(std::find(materials.begin(),materials.end(),material.text)==materials.end())materials.push_back(material.text);
        }
        if(materials.empty() || materials.size()>128)throw std::runtime_error("Original static material count exceeds bounds");
        locate_material(0);
    }
    void locate_material(size_t index) {
        if(index==materials.size()) {
            auto self=shared_from_this();fetch(material_sources,[self]{self->closure([self]{self->compile();});});return;
        }
        auto self=shared_from_this();std::string instance="resources/material_instances/"+materials[index]+".material";
        resources::query_vfs_iterator(fs_new::virtual_path_string(instance.c_str()),[self,index](vfs::vfs_locked_iterator const& it){self->guard([&]{
            self->material_sources.push_back(std::string(it.is_end()?"materials/":"material_instances/")+self->materials[index]+".material");
            self->locate_material(index+1);
        });},&allocator,resources::recursive_false,&query);
    }
    bytes properties(std::string const& name) {
        auto it=archive.find(name);if(it==archive.end())throw std::runtime_error("Missing original export properties: "+name);
        files source{{name,it->second}};return binary_config(read_source_config(source,name));
    }
    void compile() {
        auto prefix="models/"+model+"/";auto root=properties("render/export_properties");
        data->properties=load_model_properties(const_buffer(root.data(),root.size()),allocator);
        if(data->properties.type!=1)throw std::runtime_error("Original runtime profile requires static mesh root type");
        store(prefix+"render/export_properties",root);
        auto config=read_source_config(sources,settings);store(prefix+"settings",binary_config(config));
        auto const& table=config.field("material_settings");
        for(auto const& [path,vertices]:archive)if(path.starts_with("render/") && path.ends_with("/vertices")) {
            auto name=path.substr(7,path.size()-7-9);binary::validate_path(name);
            if(name.find('/')!=std::string::npos || data->surfaces.size()>=128)throw std::runtime_error("Invalid original static surface name/count");
            auto index=archive.find("render/"+name+"/indices");if(index==archive.end())throw std::runtime_error("Missing original static indices");
            auto mesh=convert_static_surface(const_buffer(vertices.data(),vertices.size()),const_buffer(index->second.data(),index->second.size()));
            auto prop=properties("render/"+name+"/export_properties");static_surface_data surface;surface.name=name;
            surface.mesh=load_static_mesh(const_buffer(mesh.data(),mesh.size()));surface.properties=load_model_properties(const_buffer(prop.data(),prop.size()),allocator);
            if(surface.properties.type!=0)throw std::runtime_error("Original runtime profile requires static surface type");
            for(unsigned axis=0;axis<3;++axis) {
                float epsilon=.001f*std::max({1.f,std::abs(data->properties.minimum[axis]),std::abs(data->properties.maximum[axis])});
                if(surface.mesh->minimum[axis]<surface.properties.minimum[axis]-epsilon || surface.mesh->maximum[axis]>surface.properties.maximum[axis]+epsilon
                    || surface.properties.minimum[axis]<data->properties.minimum[axis]-epsilon || surface.properties.maximum[axis]>data->properties.maximum[axis]+epsilon)
                    throw std::runtime_error("Original exported bounds do not contain static geometry");
            }
            table.field(name.c_str());store(prefix+"render/"+name+"/converted_model",mesh);store(prefix+"render/"+name+"/export_properties",prop);
            data->surfaces.push_back(std::move(surface));
        }
        if(data->surfaces.empty() || table.children.size()!=data->surfaces.size())throw std::runtime_error("Original settings must match every exported surface");
        for(size_t i=0;i<materials.size();++i) {
            auto resolved=read_source_config(sources,material_sources[i]);
            store("resolved/"+material_sources[i],binary_config(resolved));
            auto cooked=binary_config(static_material(std::move(resolved)));
            // Same strict material parser as cooked models; unsupported active features fail before dependency queries.
            load_material_asset(const_buffer(cooked.data(),cooked.size()),allocator);
            store("material_instances/"+materials[i]+".material",cooked);configs.push_back(std::move(cooked));
        }
        std::vector<resources::creation_request> requests;
        for(size_t i=0;i<materials.size();++i)requests.push_back({materials[i].c_str(),const_buffer(configs[i].data(),configs[i].size()),resources::material_class});
        auto self=shared_from_this();
        resources::query_create_resources(requests.data(),u32(requests.size()),[self](resources::queries_result& result){self->guard([&]{
            if(!result.is_successful())throw std::runtime_error("Original runtime material/texture dependency failed");
            auto config=read_source_config(self->sources,self->settings);auto const& table=config.field("material_settings");
            for(auto& surface:self->data->surfaces) {
                auto const& name=table.field(surface.name.c_str()).field("material_name").text;
                auto index=std::find(self->materials.begin(),self->materials.end(),name)-self->materials.begin();
                surface.material=result[u32(index)].get_unmanaged_resource();
            }
            diagnostic("info","Original static cook ready: model=%s surfaces=%u source_configs=%u cache=%s",self->model.c_str(),
                unsigned(self->data->surfaces.size()),unsigned(self->sources.size()),self->cache.string().c_str());
            self->complete(self->data);self->finished=true;
        });},&allocator,nullptr,&query,assert_on_fail_false);
    }
};
}
void query_static_model(resources::query_result_for_cook& query,std::shared_ptr<scene_registry> owner,memory::base_allocator& allocator,
    std::string const& model,std::string const& cache,std::function<void(std::shared_ptr<static_model_data const>)> complete) {
    auto state=std::make_shared<cooking>(query,std::move(owner),allocator,model,cache,std::move(complete));
    state->guard([&]{state->start();});
}
} } } }
