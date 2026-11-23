////////////////////////////////////////////////////////////////////////////
//	Created		: 04.02.2010
//	Author		: Konstantin Slipchenko
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_ANIMATION_BONE_NAMES_H_INCLUDED
#define XRAY_ANIMATION_BONE_NAMES_H_INCLUDED

#include <xray/shared_string.h>
#include <xray/animation/api.h>
#include <xray/configs_lua_config_value.h>
#include <xray/animation/type_definitions.h>
#include <xray/animation/i_bone_names.h>
#include <xray/animation/animation_containers.h>
#include <xray/noncopyable.h>
#include <algorithm>

namespace xray {

namespace animation {

class skeleton;
typedef string64	bone_name_string_type;

struct bone_name_index {

	bone_name_index( u32 idx, pcstr name );
	bone_name_index( );

	bone_name_string_type name;
	u32	crc;
	u32 index;
};

class XRAY_ANIMATION_API bone_names:
	public i_bone_names,
	private noncopyable
{
public:
	
				bone_names( );

static		u32	count_internal_memory_size	( u32 m_bone_count );

public:
			void			create_internals_in_place	( u32 bones_count, void* memory );
			void			create_internals_in_place	( const bone_names &names, void* memory );
			
			u32				bones_number	( ) const { return m_bone_count; }
			void			set_bones_number( bone_index_type size ) override { m_bone_count = size; }

			void			set_name		( bone_index_type bone_index, pcstr name ) override;
            void sort_by_crc() {
                auto* first = bone_names_idx();
                std::sort(first, first + m_bone_count, [](bone_name_index const& a, bone_name_index const& b) { return a.crc < b.crc; });
            }

public:	
			template <typename ConfigValue>
			void			read			( ConfigValue const &cfg )
			{
				const bone_index_type size = cfg.size();
				R_ASSERT( size == m_bone_count );

				bone_name_index * my_idx = bone_names_idx();

				for ( bone_index_type i = 0; i < size; ++i )
					my_idx[i] = bone_name_index( i, pcstr( static_cast<pcstr>( cfg[i] ) ) );

				std::sort( my_idx, my_idx + m_bone_count, []( bone_name_index const& l, bone_name_index const& r ) { return l.crc < r.crc; } );
			}
			void			read			( xray::configs::lua_config_value const &cfg ) override
			{
				const bone_index_type size = cfg.size();
				R_ASSERT( size == m_bone_count );
				bone_name_index * my_idx = bone_names_idx();
				for ( bone_index_type i = 0; i < size; ++i )
					my_idx[i] = bone_name_index( i, pcstr( static_cast<pcstr>( cfg[i] ) ) );
				std::sort( my_idx, my_idx + m_bone_count, []( bone_name_index const& l, bone_name_index const& r ) { return l.crc < r.crc; } );
			}

			void			write			( xray::configs::lua_config_value		&cfg )const override;

			void			write			( stream &file )const;

public:
			void			create_index	( const skeleton &skel, vector< bone_index_type > &index )const;

public:
			bone_index_type	bone_index		( pcstr name )const;
			pcstr			bone_name		( bone_index_type index )const;

private:
	bone_name_index			*bone_names_idx() { return static_cast<bone_name_index*> (  pvoid(  pbyte( this ) + m_internal_memory_position ) ); }

public:
	bone_name_index	const	*bone_names_idx() const  {  return  static_cast<bone_name_index*> ( pvoid(pbyte( this ) + m_internal_memory_position) ); }
	
private:
	size_t					m_internal_memory_position;
	u32						m_bone_count;
}; // class bone_names

} // namespace animation
} // namespace xray

#endif // #ifndef XRAY_ANIMATION_BONE_NAMES_H_INCLUDED
