////////////////////////////////////////////////////////////////////////////
//	Created		: 21.01.2010
//	Author		: Armen Abroyan
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include <xray/render/world.h>
#include <xray/render/engine/world.h>
#include <xray/memory_macros.h>
#include "update_model_vertex_buffer_command.h"
#ifdef USE_VULKAN_RENDERER
#include <xray/render/vulkan/model_resources.h>
#include <stdexcept>
#endif

namespace xray {
namespace render {

update_model_vertex_buffer_command::update_model_vertex_buffer_command(
		engine::world& world, 
		render_model_instance_ptr object, 
		vectora< buffer_fragment > const& fragments, 
		memory::base_allocator& allocator, bool indices
	) :
	m_world				( world ),
	m_object			( object ),
	m_allocator			( allocator ),
	m_fragments			( allocator ),
    m_indices(indices)
{
#ifdef USE_VULKAN_RENDERER
    if (!m_object) throw std::runtime_error("Model buffer update requires a model");
    vulkan::validate_model_buffer_fragments(fragments,64u*1024u*1024u);
    m_storage.reserve(fragments.size());
    m_fragments.reserve(fragments.size());
    for (auto const& fragment : fragments) {
        auto* begin=static_cast<char const*>(fragment.buffer);
        m_storage.emplace_back(begin,begin+fragment.size);
        m_fragments.push_back({fragment.start,fragment.size,m_storage.back().data()});
    }
#else
	ASSERT				( m_object );
	m_fragments.resize	( fragments.size() );
	Fragments::iterator			it	= m_fragments.begin();
	Fragments::const_iterator	end	= m_fragments.end();

	Fragments::const_iterator it_o	= fragments.begin();

	for( ; it != end; ++it, ++it_o)
	{
		*it = *it_o;
		it->buffer = XRAY_NEW_ARRAY_IMPL( m_allocator, char, it_o->size);
		memory::copy( it->buffer, it_o->size, it_o->buffer, it_o->size);
	}
#endif
}

update_model_vertex_buffer_command::~update_model_vertex_buffer_command	()
{
#ifndef USE_VULKAN_RENDERER
	Fragments::iterator			it	= m_fragments.begin();
	Fragments::const_iterator	end	= m_fragments.end();

	for( ; it != end; ++it)
	{
		char* temp_buffer = static_cast<char*>(it->buffer);
		XRAY_DELETE_ARRAY_IMPL( m_allocator, temp_buffer );
		it->buffer = temp_buffer;
	}
#endif
}

void update_model_vertex_buffer_command::execute()
{
	ASSERT								( m_object );
    if (m_indices) m_world.update_model_index_buffer(m_object,m_fragments);
    else m_world.update_model_vertex_buffer(m_object,m_fragments);
}

} // namespace render
} // namespace xray
