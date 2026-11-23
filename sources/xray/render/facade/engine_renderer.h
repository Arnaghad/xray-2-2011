////////////////////////////////////////////////////////////////////////////
//	Created 	: 17.11.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_FACADE_ENGINE_RENDERER_H_INCLUDED
#define XRAY_RENDER_FACADE_ENGINE_RENDERER_H_INCLUDED

#include <xray/render/api.h>
#include <xray/os_include.h>

namespace xray {
namespace render {
namespace engine {

class world;

class XRAY_RENDER_API renderer : private core::noncopyable {
private:
	friend class render::world;
			renderer	( engine::world& engine_world );

public:
#if defined(USE_VULKAN_RENDERER)
	bool initialize(HWND window_handle);
	// Production scene startup includes the external particle cooks on the render owner.
	bool initialize_game(HWND window_handle);
	// Retry after producer references and resource callbacks have drained.
	bool finalize_game_resources();
	bool initialize_geometry_smoke();
    bool initialize_material_smoke();
	bool initialized() const;
	u32 presented_frame_id() const;
#else
	void initialize();
#endif
	u32		frame_id	( );
	
private:
	engine::world&	m_render_engine_world;
}; // class renderer

} // namespace engine
} // namespace render
} // namespace xray

#endif // #ifndef XRAY_RENDER_FACADE_ENGINE_RENDERER_H_INCLUDED
