////////////////////////////////////////////////////////////////////////////
//	Created		: 11.02.2009
//	Author		: Andrew Kolomiets
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_CONFIGS_BINARY_CONFIG_VALUE_INLINE_H_INCLUDED
#define XRAY_CONFIGS_BINARY_CONFIG_VALUE_INLINE_H_INCLUDED

#include <cstring>

namespace xray {
namespace configs {

binary_config_value const& binary_config_value::operator[]	( u32 const index ) const
{
	R_ASSERT			(type == t_table_indexed);
	R_ASSERT			(index < count);
	const_iterator it	= begin();
	std::advance		(it, index);
	return				*it;
}

binary_config_value const& binary_config_value::operator[]	( int const index ) const
{
	R_ASSERT			(index >= 0);
	return				(*this)[u32(index)];
}

bool binary_config_value::operator < (u32 const compare_crc) const
{
	return				(id_crc < compare_crc);
}

bool binary_config_value::empty() const
{
	return				(begin() == end());
}

binary_config_value::const_iterator binary_config_value::begin() const
{
	R_ASSERT			(type==t_table_named || type==t_table_indexed);
	return				pointer_cast<binary_config_value const*>(static_cast<void const*>(data));
}

binary_config_value::const_iterator binary_config_value::end() const
{
	R_ASSERT			(type==t_table_named || type==t_table_indexed);
	return				pointer_cast<binary_config_value const*>(static_cast<void const*>(data)) + count;
}

template <typename T, typename P0, typename P1>
T binary_config_value::cast_number			() const
{
	R_ASSERT			(type == t_integer);
	static_assert		( sizeof(data) >= sizeof(P1), "binary_config_value::data too small for scalar" );
	P1 raw_value		= 0;
	std::memcpy			(&raw_value, &data, sizeof(P1));
	P0 value			= raw_value;
	R_ASSERT			( value <= std::numeric_limits<T>::max() );
	R_ASSERT			( value >= std::numeric_limits<T>::min() );
	T const result		= T( value );
	return				result;
}

template <typename T>
T binary_config_value::cast_unsigned_number	() const
{
	return				cast_number<T,u64,u32>();
}

template <typename T>
T binary_config_value::cast_signed_number		() const
{
	return				cast_number<T,s64,s32>();
}

binary_config_value::operator bool				() const
{
	R_ASSERT			(type == t_boolean);
	static_assert		( sizeof(data) >= sizeof(u32), "binary_config_value::data too small for bool" );
	u32 value			= 0;
	std::memcpy			(&value, &data, sizeof(u32));
	return				!!value;
}

binary_config_value::operator s8				() const
{
	return				cast_signed_number<s8>();
}

binary_config_value::operator u8				() const
{
	return				cast_unsigned_number<u8>();
}

binary_config_value::operator s16				() const
{
	return				cast_signed_number<s16>();
}

binary_config_value::operator u16				() const
{
	return				cast_unsigned_number<u16>();
}

binary_config_value::operator s32 () const
{
	return				cast_signed_number<s32>();
}

binary_config_value::operator u32 () const
{
	return				cast_unsigned_number<u32>();
}

binary_config_value::operator float () const
{
	if ( type == t_float ) {
		static_assert	( sizeof(data) >= sizeof(float), "binary_config_value::data too small for float" );
		float value		= 0.f;
		std::memcpy		(&value, &data, sizeof(float));
		return			value;
	}
	
	R_ASSERT			( type == t_integer );
	return				float( cast_signed_number<s32>() );
}

binary_config_value::operator math::float2 const& () const
{
	R_ASSERT			(type == t_float2);
	return				*pointer_cast<math::float2 const*>(static_cast<void const*>(data));
}

binary_config_value::operator math::float3 const& () const
{
	R_ASSERT			(type == t_float3);
	return				*pointer_cast<math::float3 const*>(static_cast<void const*>(data));
}

binary_config_value::operator math::float4 const& () const
{
	R_ASSERT			(type == t_float4);
	return				*pointer_cast<math::float4 const*>(static_cast<void const*>(data));
}

binary_config_value::operator math::int2 const& () const
{
	R_ASSERT			(type == t_int2);
	return				*pointer_cast<math::int2 const*>(static_cast<void const*>(data));
}

binary_config_value::operator pcstr () const
{
	R_ASSERT			(type == t_string);
	return				pointer_cast<pcstr>(static_cast<void const*>(data));
}

} // namespace configs
} // namespace xray

#endif // #ifndef XRAY_CONFIGS_BINARY_CONFIG_VALUE_INLINE_H_INCLUDED
