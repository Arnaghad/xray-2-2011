////////////////////////////////////////////////////////////////////////////
//	Created 	: 01.02.2011
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2011
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/render/facade/ui_renderer.h>
#include <xray/render/world.h>
#include <xray/render/engine/world.h>
#include "ui_draw_vertices_command.h"
#include "functor_command.h"
#include <xray/render/vulkan/sources/vulkan_ui_renderer.h>
#include <stdexcept>

using xray::render::ui::renderer;

renderer::renderer		(
		xray::render::one_way_render_channel& channel,
		xray::memory::base_allocator& allocator,
		xray::render::engine::world& engine_world
	) :
	m_channel				( channel ),
	m_render_engine_world	( engine_world ),
	m_allocator				( allocator )
{
}

void renderer::draw_vertices	(
		xray::render::scene_view_ptr const& scene_view,
		xray::render::ui::vertex const* const begin,
		xray::render::ui::vertex const* const end,
		u32 const primitives_type,
		u32 const points_type
	)
{
    draw_vertices(scene_view,begin,end,primitives_type,points_type,{});
}

void renderer::draw_vertices(xray::render::scene_view_ptr const& view,vertex const* begin,vertex const* end,
    u32 primitives,u32 points,draw_options const& options) {
    if (!view) throw std::runtime_error("UI requires a scene view");
    auto first=reinterpret_cast<uintptr_t>(begin),last=reinterpret_cast<uintptr_t>(end);
    if ((!begin)!=(!end) || last<first || (last-first)%sizeof(vertex) || (last-first)/sizeof(vertex)>65536)
        throw std::runtime_error("Invalid UI vertex range");
    size_t count=(last-first)/sizeof(vertex);
    xray::render::vulkan::validate_ui({begin,count},int(primitives),int(points),options);
    if (!count) return;
    void* storage=XRAY_MALLOC_IMPL(m_allocator,sizeof(draw_vertices_command),"UI draw command");
    if (!storage) throw std::bad_alloc();
    draw_vertices_command* command;
    try { command=new(storage) draw_vertices_command(m_render_engine_world,view,begin,end,m_allocator,primitives,points,options); }
    catch (...) { XRAY_FREE_IMPL(m_allocator,storage); throw; }
    try { m_channel.owner_push_back(command); }
    catch (...) { XRAY_DELETE_IMPL(m_allocator,command); throw; }
}
void renderer::set_font_texture(xray::render::scene_view_ptr const& view,xray::resources::unmanaged_resource_ptr const& texture) {
    if (!view) throw std::runtime_error("UI font binding requires a scene view");
    void* storage=XRAY_MALLOC_IMPL(m_allocator,sizeof(xray::render::functor_command),"UI font binding command");
    if (!storage) throw std::bad_alloc();
    xray::render::functor_command* command;
    try {
        command=new(storage) xray::render::functor_command([world=&m_render_engine_world,view,texture] {
            world->set_ui_font_texture(view,texture);
        });
    } catch (...) { XRAY_FREE_IMPL(m_allocator,storage); throw; }
    try { m_channel.owner_push_back(command); }
    catch (...) { XRAY_DELETE_IMPL(m_allocator,command); throw; }
}

void renderer::draw_text		(
		xray::render::scene_view_ptr const& scene_view,
		pcstr const& text,
		xray::ui::font const& font,
		xray::float2 const& position,
		xray::math::color const& text_color,
		xray::math::color const& selection_color,
		u32 const max_line_size,
		bool const is_multiline,
		u32 const start_selection,
		u32 const end_selection
	)
{
    draw_text(scene_view,text,font,position,text_color,selection_color,max_line_size,is_multiline,start_selection,end_selection,{});
}
void renderer::draw_text(xray::render::scene_view_ptr const& scene_view,pcstr const& text,xray::ui::font const& font,
    xray::float2 const& position,xray::math::color const& text_color,xray::math::color const& selection_color,
    u32 max_line_size,bool is_multiline,u32 start_selection,u32 end_selection,draw_options const& options) {
    vectora<xray::render::ui::vertex> vertices(m_allocator);
	m_render_engine_world.draw_text	(
		vertices, 
		text, 
		font, 
		position, 
		text_color, 
		selection_color, 
		max_line_size, 
		is_multiline,
		start_selection, 
		end_selection
	);

    if (!vertices.empty()) draw_vertices(scene_view,&vertices.front(),&vertices.front()+vertices.size(),0,0,options);
}
