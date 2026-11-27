#include <filesystem>
#include <cstdlib>
#include <stdexcept>
#include <xray/os_platform.h>
#include "vulkan_smoke_geometry.h"
#include "vulkan_diagnostics.h"
#include <xray/render/core/sources/rhi/rhi_device.h>

namespace xray { namespace render { namespace vulkan {
namespace {
struct vertex { float position[3]; float color[3]; };
vertex const vertices[] = {
    {{9,9,9}, {0,0,0}}, // Deliberate prefix: vertex binding uses a nonzero offset.
    {{-.65f,-.65f,.6f}, {0,.8f,1}}, {{-.65f,.65f,.6f}, {1,.1f,.1f}},
    {{.65f,.65f,.6f}, {.1f,1,.1f}}, {{.65f,-.65f,.6f}, {.1f,.1f,1}},
    {{-.25f,-.25f,.2f}, {1,.3f,.03f}}, {{0,.3f,.2f}, {1,.3f,.03f}}, {{.25f,-.25f,.2f}, {1,.3f,.03f}},
    {{-.25f,-.25f,.8f}, {0,0,0}}, {{0,.3f,.8f}, {0,0,0}}, {{.25f,-.25f,.8f}, {0,0,0}}
};
u16 const indices16[] = {99, 0,1,2, 2,3,0};
u32 const indices32[] = {99, 0,1,2, 2,3,0};
}
smoke_geometry::smoke_geometry(rhi::rhi_backend& backend) : m_backend(&backend) {
    try {
        rhi::shader_compiler compiler;
        char const* configured = std::getenv("XRAY_VULKAN_SMOKE_SHADER_DIR");
        std::filesystem::path directory;
        if (configured && *configured) {
            directory = std::filesystem::path(configured);
        } else {
            auto const exe_dir = xray::os::executable_directory();
            if (!exe_dir.empty()) {
                std::error_code ec;
                for (auto const& candidate : {
                         exe_dir.parent_path() / "shaders" / "basic",
                         exe_dir.parent_path() / "shaders",
                         exe_dir / "shaders" / "basic",
                         exe_dir / "shaders"}) {
                    if (std::filesystem::is_regular_file(candidate / "geometry.hlsl", ec)) {
                        directory = candidate;
                        break;
                    }
                }
            }
            if (directory.empty()) directory = std::filesystem::path(XRAY_VULKAN_SMOKE_SHADER_DIR);
        }
        auto compile = [&](rhi::shader_stage stage, char const* entry) {
            rhi::shader_compile_options options;
            options.entry = entry;
            options.defines = {"SMOKE_DEPTH=0.25"};
            if (stage == rhi::shader_stage::vertex) options.flags = {"-fvk-invert-y"};
            auto result = compiler.compile_file((directory / "geometry.hlsl").string(), stage, options);
            if (!result.success) throw std::runtime_error(result.error_log);
            return result.bytecode;
        };
        m_triangle_code = compile(rhi::shader_stage::vertex, "triangle_vs");
        m_mesh_code = compile(rhi::shader_stage::vertex, "mesh_vs");
        m_pixel_code = compile(rhi::shader_stage::pixel, "color_ps");
        create();
        diagnostic("info", "GEOMETRY HLSL/DXC: three named entries, relative include, macro, Vulkan 1.3 SPIR-V");
    } catch (...) { release(); throw; }
}
smoke_geometry::~smoke_geometry() { release(); }
void smoke_geometry::release() {
    // RHI may have shut down after a fatal frame error; never dereference it then.
    if (rhi::rhi_device::ref().get_backend() == m_backend) {
        for (auto handle : {m_vertices, m_indices16, m_indices32}) m_backend->destroy_buffer(handle);
        for (auto handle : {m_triangle, m_mesh, m_pixel}) m_backend->destroy_shader(handle);
    }
    m_vertices = m_indices16 = m_indices32 = m_triangle = m_mesh = m_pixel = 0;
}
void smoke_geometry::create() {
    m_triangle = m_backend->create_shader(rhi::shader_stage::vertex, m_triangle_code.data(), m_triangle_code.size()*4, "triangle_vs");
    m_mesh = m_backend->create_shader(rhi::shader_stage::vertex, m_mesh_code.data(), m_mesh_code.size()*4, "mesh_vs");
    m_pixel = m_backend->create_shader(rhi::shader_stage::pixel, m_pixel_code.data(), m_pixel_code.size()*4, "color_ps");
    m_vertices = m_backend->create_buffer(rhi::buffer_type::vertex, sizeof(vertices), vertices);
    m_indices16 = m_backend->create_buffer(rhi::buffer_type::index, sizeof(indices16), indices16);
    m_indices32 = m_backend->create_buffer(rhi::buffer_type::index, sizeof(indices32), indices32);
    if (!m_triangle || !m_mesh || !m_pixel || !m_vertices || !m_indices16 || !m_indices32)
        throw std::runtime_error("Cannot create geometry smoke resources");
}
void smoke_geometry::draw(u32 presented) {
    m_backend->bind_shader(rhi::shader_stage::pixel, m_pixel);
    m_backend->set_topology(rhi::topology::triangle_list);
    m_backend->set_depth_stencil_state(true, true, rhi::comparison_func::less);
    if (presented < 20) {
        m_backend->set_vertex_layout({});
        m_backend->set_rasterizer_state(rhi::fill_mode::solid, rhi::cull_mode::none);
        m_backend->bind_shader(rhi::shader_stage::vertex, m_triangle);
        m_backend->draw(3, 0);
    } else {
        m_backend->set_vertex_layout({sizeof(vertex), {{0,rhi::format::r32g32b32_float,0}, {1,rhi::format::r32g32b32_float,12}}});
        m_backend->set_rasterizer_state(rhi::fill_mode::solid, rhi::cull_mode::back);
        m_backend->bind_shader(rhi::shader_stage::vertex, m_mesh);
        m_backend->bind_vertex_buffer(m_vertices, sizeof(vertex));
        bool const wide = presented % 2 != 0;
        m_backend->bind_index_buffer(wide ? m_indices32 : m_indices16, wide ? 4 : 2,
            wide ? rhi::index_format::uint32 : rhi::index_format::uint16);
        m_backend->draw_indexed(6, 0, 0);
        if (!m_contracts_checked) {
            unsigned rejected = 0;
            auto invalid = [&](auto operation) { try { operation(); } catch (std::runtime_error const&) { ++rejected; } };
            invalid([&] { m_backend->bind_vertex_buffer(m_indices16, 0); });
            invalid([&] { m_backend->bind_index_buffer(m_vertices, 0, rhi::index_format::uint16); });
            invalid([&] { m_backend->bind_index_buffer(m_indices16, 1, rhi::index_format::uint16); });
            invalid([&] { m_backend->bind_shader(rhi::shader_stage::pixel, m_mesh); });
            invalid([&] { m_backend->draw_indexed(1000, 0, 0); });
            invalid([&] { m_backend->draw_indexed(6, 0, 1000); });
            invalid([&] { m_backend->draw(1000, 0); });
            if (rejected != 7) throw std::runtime_error("Invalid binding/draw range was accepted");
            m_contracts_checked = true;
            diagnostic("info", "GEOMETRY buffer types, index alignment, shader stage and draw bounds: PASS (7 rejections)");
        }
        m_backend->draw(3, 4); // Near orange triangle.
        m_backend->draw(3, 7); // Far black triangle must fail depth, despite being drawn last.
    }
    if (presented >= 90 && !m_recreated) {
        auto const stale_shader = m_mesh;
        auto const stale_buffer = m_vertices;
        auto const stale_index = m_indices16;
        release();
        create(); // Old GPU objects remain alive until this recording is submitted/completed.
        bool rejected_shader = false, rejected_buffer = false, rejected_index = false;
        try { m_backend->bind_shader(rhi::shader_stage::vertex, stale_shader); }
        catch (std::runtime_error const&) { rejected_shader = true; }
        try { m_backend->bind_vertex_buffer(stale_buffer, 0); }
        catch (std::runtime_error const&) { rejected_buffer = true; }
        try { m_backend->bind_index_buffer(stale_index, 0, rhi::index_format::uint16); }
        catch (std::runtime_error const&) { rejected_index = true; }
        if (!rejected_shader || !rejected_buffer || !rejected_index) throw std::runtime_error("Stale resource handle accepted");
        m_recreated = true;
        diagnostic("info", "GEOMETRY in-recording destroy/recreate; stale shader/buffer rejected: PASS");
    }
}
} } }
