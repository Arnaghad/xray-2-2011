#include "vulkan_smoke_materials.h"
#include "vulkan_backend.h"
#include "material_contract.h"
#include "vulkan_diagnostics.h"
#include <xray/render/core/sources/rhi/rhi_device.h>
#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <filesystem>
#include <cstdlib>
#include <cmath>
#include <stdexcept>
#include <xray/os_platform.h>
namespace xray { namespace render { namespace vulkan {
namespace {
struct vertex { float position[3], uv[2]; };
vertex const vertices[] = {{{-1,-1,.5f},{-.5f,1.5f}}, {{-1,1,.5f},{-.5f,-.5f}}, {{1,1,.5f},{1.5f,-.5f}}, {{1,-1,.5f},{1.5f,1.5f}}};
u16 const indices[] = {0,1,2,2,3,0};
void checker(std::vector<unsigned char>& pixels, unsigned char const* a, unsigned char const* b) {
    pixels.resize(8*8*4);
    for (unsigned y = 0; y < 8; ++y) for (unsigned x = 0; x < 8; ++x)
        for (unsigned c = 0; c < 4; ++c) pixels[(y*8+x)*4+c] = ((x+y)%2 ? a : b)[c];
}
rhi::sampler_desc nearest() {
    rhi::sampler_desc d; d.min_filter = d.mag_filter = d.mip_filter = rhi::sampler_filter::nearest; return d;
}
}
rhi::rhi_texture_handle smoke_materials::texture(bool left) {
    rhi::texture_desc d; d.width = d.height = 8; d.mip_levels = 2;
    d.pixel_format = left ? rhi::format::r8g8b8a8_srgb : rhi::format::r8g8b8a8_unorm;
    auto const& pixels = left ? m_left_pixels : m_right_pixels;
    return m_backend->create_texture(d, {{0,0,pixels.data(),pixels.size(),32},{1,0,m_mip.data(),m_mip.size(),16}});
}
smoke_materials::smoke_materials(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        unsigned char orange[] = {255,90,0,255}, blue[] = {0,75,255,255}, magenta[] = {255,0,255,255}, cyan[] = {0,255,255,255};
        checker(m_left_pixels,orange,blue); checker(m_right_pixels,magenta,cyan);
        m_mip.resize(4*4*4); for (size_t i = 0; i < m_mip.size(); ++i) m_mip[i] = cyan[i%4];
        m_left = texture(true); m_right = texture(false);
        m_nearest = backend.create_sampler(nearest());
        rhi::sampler_desc linear; linear.address_u = linear.address_v = rhi::sampler_address::clamp;
        m_linear = backend.create_sampler(linear);
        rhi::shader_compiler compiler;
        auto configured = std::getenv("XRAY_VULKAN_SMOKE_SHADER_DIR");
        std::filesystem::path shader_dir;
        if (configured && *configured) {
            shader_dir = std::filesystem::path(configured);
        } else {
            auto const exe_dir = xray::os::executable_directory();
            if (!exe_dir.empty()) {
                std::error_code ec;
                for (auto const& candidate : {
                         exe_dir.parent_path() / "shaders" / "basic",
                         exe_dir.parent_path() / "shaders",
                         exe_dir / "shaders" / "basic",
                         exe_dir / "shaders"}) {
                    if (std::filesystem::is_regular_file(candidate / "materials.hlsl", ec)) {
                        shader_dir = candidate;
                        break;
                    }
                }
            }
            if (shader_dir.empty()) shader_dir = std::filesystem::path(XRAY_VULKAN_SMOKE_SHADER_DIR);
        }
        auto file = (shader_dir / "materials.hlsl").string();
        rhi::shader_compile_options opt; opt.entry = "material_vs"; opt.flags = {"-fvk-invert-y"};
        auto vs = compiler.compile_file(file, rhi::shader_stage::vertex, opt);
        auto cached = compiler.compile_file(file, rhi::shader_stage::vertex, opt);
        if (!cached.cache_hit) throw std::runtime_error("Material compiler cache did not hit");
        opt.entry = "material_ps"; opt.flags.clear();
        auto ps = compiler.compile_file(file, rhi::shader_stage::pixel, opt);
        if (!vs.success || !ps.success) throw std::runtime_error(vs.error_log + ps.error_log);
        m_vs = backend.create_shader(rhi::shader_stage::vertex, vs.bytecode.data(), vs.bytecode.size()*4, "material_vs");
        m_ps = backend.create_shader(rhi::shader_stage::pixel, ps.bytecode.data(), ps.bytecode.size()*4, "material_ps");
        m_vertices = backend.create_buffer(rhi::buffer_type::vertex, sizeof(vertices), vertices);
        m_indices = backend.create_buffer(rhi::buffer_type::index, sizeof(indices), indices);
        m_frame = backend.create_buffer(rhi::buffer_type::constant, sizeof(frame_constants), nullptr);
        m_object = backend.create_buffer(rhi::buffer_type::constant, sizeof(object_constants), nullptr);
        if (!m_left || !m_right || !m_nearest || !m_linear || !m_vs || !m_ps || !m_vertices || !m_indices || !m_frame || !m_object)
            throw std::runtime_error("Material resource creation failed");
        diagnostic("info", "MATERIALS reflection ABI v1, separate texture/sampler, compiler cache: PASS");
    } catch (...) { release(); throw; }
}
smoke_materials::~smoke_materials() { release(); }
void smoke_materials::release() {
    if (rhi::rhi_device::ref().get_backend() == m_backend) {
        for (auto h : {m_vertices,m_indices,m_frame,m_object}) m_backend->destroy_buffer(h);
        for (auto h : {m_vs,m_ps}) m_backend->destroy_shader(h);
        for (auto h : {m_left,m_right}) m_backend->destroy_texture(h);
        for (auto h : {m_nearest,m_linear}) m_backend->destroy_sampler(h);
    }
    m_vertices = m_indices = m_frame = m_object = m_vs = m_ps = m_left = m_right = m_nearest = m_linear = 0;
}
void smoke_materials::draw(u32 presented, frame_constants const* camera) {
    auto& b = *m_backend;
    b.set_vertex_layout({sizeof(vertex), {{0,rhi::format::r32g32b32_float,0},{1,rhi::format::r32g32_float,12}}});
    b.bind_vertex_buffer(m_vertices,0); b.bind_index_buffer(m_indices,0);
    b.bind_shader(rhi::shader_stage::vertex,m_vs); b.bind_shader(rhi::shader_stage::pixel,m_ps);
    b.set_rasterizer_state(rhi::fill_mode::solid,rhi::cull_mode::none);
    b.set_depth_stencil_state(true,true,rhi::comparison_func::less);
    frame_constants frame; identity(frame.view); identity(frame.projection);
    frame.view[7] = .06f; frame.projection[0] = .95f; frame.projection[5] = .9f;
    if (camera) frame = *camera;
    b.update_buffer(m_frame,&frame,sizeof(frame)); b.bind_constant_buffer(0,m_frame); b.bind_constant_buffer(1,m_object);
    object_constants object = {}; identity(object.world); for (auto& v : object.tint) v = 1;
    object.world[0] = .32f; object.world[5] = .48f;
    object.world[3] = -.47f + .06f * std::sin(presented*.028f);
    b.update_buffer(m_object,&object,sizeof(object)); b.bind_texture(0,m_left); b.bind_sampler(0,m_nearest); b.draw_indexed(6,0,0);
    // Mutating the same logical buffer must not change constants of the left draw.
    object.world[3] = .47f - .06f * std::sin(presented*.028f); object.parameters[0] = presented >= 150 ? 1.f : 0.f;
    b.update_buffer(m_object,&object,sizeof(object)); b.bind_texture(0,m_right); b.bind_sampler(0,m_linear); b.draw_indexed(6,0,0);
    object = {}; identity(object.world); for (auto& v : object.tint) v = 1;
    object.world[0] = .07f; object.world[5] = .045f; object.world[7] = -.78f;
    b.update_buffer(m_object,&object,sizeof(object)); b.bind_constant_buffer(0,0); b.bind_texture(0,0); b.bind_sampler(0,0); b.draw_indexed(6,0,0);
    if (presented >= 10 && presented < 16) {
        object.world[3] = 10; b.update_buffer(m_object,&object,sizeof(object));
        for (int i = 0; i < 160; ++i) b.draw_indexed(6,0,0);
        if (presented == 11 || presented == 15) {
            auto* backend = dynamic_cast<vulkan_backend*>(&b);
            if (!backend) throw std::runtime_error("Material stress backend unavailable");
            auto capacity = backend->material_capacity();
            if (capacity[0] < 3 || capacity[2] < 3 || capacity[1] < 2 || capacity[3] < 2)
                throw std::runtime_error("Descriptor/uniform stress did not exhaust both frame arenas");
            if (presented == 11) m_stress_capacity = capacity;
            else {
                if (capacity != m_stress_capacity) throw std::runtime_error("Frame pool/arena capacity accumulated instead of reusing completed allocations");
                diagnostic("info", "MATERIALS 163 draws/frame: >=3 pools and >=2 uniform chunks in both slots, bounded growth/reuse: PASS");
            }
        }
    }
    if (!m_checked) {
        unsigned rejected = 0;
        auto invalid = [&](auto op) { try { op(); } catch (std::runtime_error const&) { ++rejected; } };
        invalid([&] { b.bind_constant_buffer(2,m_frame); }); invalid([&] { b.bind_constant_buffer(0,m_vertices); });
        invalid([&] { b.bind_constant_buffer(0,m_object); }); invalid([&] { b.update_buffer(m_vertices,&object,sizeof(object)); });
        invalid([&] { b.update_buffer(m_object,&object,1); }); invalid([&] { b.bind_texture(6,m_left); }); invalid([&] { b.bind_sampler(2,m_nearest); });
        auto surface=b.create_buffer(rhi::buffer_type::constant,sizeof(surface_object_constants),nullptr);
        if (!surface) throw std::runtime_error("ABI mismatch test buffer unavailable");
        b.bind_constant_buffer(1,surface); invalid([&] { b.draw_indexed(6,0,0); });
        b.bind_constant_buffer(1,m_object); b.destroy_buffer(surface);
        if (rejected != 8) throw std::runtime_error("Invalid material bindings accepted");
        m_checked = true; diagnostic("info", "MATERIALS constant ABI versions, update bounds and binding slots: PASS (8 rejections)");
    }
    if (presented >= 80 && !m_updated) {
        unsigned char green[] = {0,255,40,255}, white[] = {255,255,255,255}; checker(m_left_pixels,green,white);
        b.update_texture(m_left,{{0,0,m_left_pixels.data(),m_left_pixels.size(),32},{1,0,m_mip.data(),m_mip.size(),16}});
        m_updated = true; diagnostic("info", "MATERIALS texture copy-on-update after recording draw: PASS");
    }
    if (presented >= 90 && !m_recreated) {
        auto old = m_left; b.destroy_texture(old); m_left = texture(true);
        if (!m_left || m_left == old) throw std::runtime_error("Texture generation did not change");
        bool rejected = false; try { b.bind_texture(0,old); } catch (std::runtime_error const&) { rejected = true; }
        if (!rejected) throw std::runtime_error("Stale texture accepted");
        m_recreated = true; diagnostic("info", "MATERIALS in-recording texture retire/recreate/stale rejection: PASS");
    }
    if (presented >= 110 && !m_sampler_changed) {
        auto old = m_linear; b.destroy_sampler(old); m_linear = b.create_sampler(nearest());
        if (!m_linear || m_linear == old) throw std::runtime_error("Sampler generation did not change");
        bool rejected = false; try { b.bind_sampler(0,old); } catch (std::runtime_error const&) { rejected = true; }
        if (!rejected) throw std::runtime_error("Stale sampler accepted");
        m_sampler_changed = true; diagnostic("info", "MATERIALS in-recording sampler retire/recreate/change: PASS");
    }
}
} } }
