////////////////////////////////////////////////////////////////////////////
//	Created		: 26.11.2026
//	Description	: Self-contained portable DXC COM ABI and UTF-8 helpers (PAL-5)
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_DXC_ABI_H_INCLUDED
#define XRAY_RENDER_RHI_DXC_ABI_H_INCLUDED

#include <xray/macro_compiler.h>
#include <xray/macro_platform.h>
#include <xray/render/vulkan/library_linkage.h>

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <string>
#include <utility>

#if XRAY_PLATFORM_WINDOWS
#	define XRAY_DXC_CALL __stdcall
#else
#	define XRAY_DXC_CALL
#endif

namespace xray {
namespace render {
namespace rhi {
namespace dxc {

using dxc_hresult = std::int32_t;
using dxc_ulong = unsigned long;
using dxc_uint32 = std::uint32_t;
#if XRAY_PLATFORM_WINDOWS
using dxc_bool = int;
#else
using dxc_bool = bool;
#endif

inline constexpr dxc_uint32 k_dxc_cp_utf8 = 65001u;

inline constexpr dxc_hresult k_dxc_s_ok = 0;
inline constexpr dxc_hresult k_dxc_e_fail = static_cast<dxc_hresult>( 0x80004005u );
inline constexpr dxc_hresult k_dxc_e_nointerface = static_cast<dxc_hresult>( 0x80004002u );
inline constexpr dxc_hresult k_dxc_e_pointer = static_cast<dxc_hresult>( 0x80004003u );
inline constexpr dxc_hresult k_dxc_e_outofmemory = static_cast<dxc_hresult>( 0x8007000Eu );
inline constexpr dxc_hresult k_dxc_e_file_not_found = static_cast<dxc_hresult>( 0x80070002u );

inline constexpr bool dxc_succeeded( dxc_hresult const hr ) noexcept
{
	return hr >= 0;
}

inline constexpr bool dxc_failed( dxc_hresult const hr ) noexcept
{
	return hr < 0;
}

struct dxc_guid
{
	std::uint32_t data1;
	std::uint16_t data2;
	std::uint16_t data3;
	std::uint8_t  data4[8];

	constexpr bool operator==( dxc_guid const& other ) const noexcept
	{
		if ( data1 != other.data1 || data2 != other.data2 || data3 != other.data3 )
			return false;
		for ( std::size_t i = 0; i < 8; ++i ) {
			if ( data4[i] != other.data4[i] )
				return false;
		}
		return true;
	}
};

inline constexpr dxc_guid k_iid_iunknown = {
	0x00000000u, 0x0000u, 0x0000u, { 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46 }
};
inline constexpr dxc_guid k_iid_dxc_blob = {
	0x8BA5FB08u, 0x5195u, 0x40E2u, { 0xAC, 0x58, 0x0D, 0x98, 0x9C, 0x3A, 0x01, 0x02 }
};
inline constexpr dxc_guid k_iid_dxc_blob_encoding = {
	0x7241D424u, 0x2646u, 0x4191u, { 0x97, 0xC0, 0x98, 0xE9, 0x6E, 0x42, 0xFC, 0x68 }
};
inline constexpr dxc_guid k_iid_dxc_blob_wide = {
	0xA3F84EABu, 0x0FAAu, 0x497Eu, { 0xA3, 0x9C, 0xEE, 0x6E, 0xD6, 0x0B, 0x2D, 0x84 }
};
inline constexpr dxc_guid k_iid_dxc_blob_utf8 = {
	0x3DA636C9u, 0xBA71u, 0x4024u, { 0xA3, 0x01, 0x30, 0xCB, 0xF1, 0x25, 0x30, 0x5B }
};
inline constexpr dxc_guid k_iid_dxc_include_handler = {
	0x7F61FC7Du, 0x950Du, 0x467Fu, { 0xB3, 0xE3, 0x3C, 0x02, 0xFB, 0x49, 0x18, 0x7C }
};
inline constexpr dxc_guid k_iid_dxc_operation_result = {
	0xCEDB484Au, 0xD4E9u, 0x445Au, { 0xB9, 0x91, 0xCA, 0x21, 0xCA, 0x15, 0x7D, 0xC2 }
};
inline constexpr dxc_guid k_iid_dxc_result = {
	0x58346CDAu, 0xDDE7u, 0x4497u, { 0x94, 0x61, 0x6F, 0x87, 0xAF, 0x5E, 0x06, 0x59 }
};
inline constexpr dxc_guid k_iid_dxc_utils = {
	0x4605C4CBu, 0x2019u, 0x492Au, { 0xAD, 0xA4, 0x65, 0xF2, 0x0B, 0xB7, 0xD6, 0x7F }
};
inline constexpr dxc_guid k_iid_dxc_compiler3 = {
	0x228B4687u, 0x5A6Au, 0x4730u, { 0x90, 0x0C, 0x97, 0x02, 0xB2, 0x20, 0x3F, 0x54 }
};

inline constexpr dxc_guid k_clsid_dxc_compiler = {
	0x73E22D93u, 0xE6CEu, 0x47F3u, { 0xB5, 0xBF, 0xF0, 0x66, 0x4F, 0x39, 0xC1, 0xB0 }
};
inline constexpr dxc_guid k_clsid_dxc_utils = {
	0x6245D6AFu, 0x66E0u, 0x48FDu, { 0x80, 0xB4, 0x4D, 0x27, 0x17, 0x96, 0x74, 0x8C }
};

enum dxc_out_kind : std::uint32_t
{
	dxc_out_none = 0,
	dxc_out_object = 1,
	dxc_out_errors = 2,
	dxc_out_pdb = 3,
	dxc_out_shader_hash = 4,
	dxc_out_disassembly = 5,
	dxc_out_hlsl = 6,
	dxc_out_text = 7,
	dxc_out_reflection = 8,
	dxc_out_root_signature = 9,
	dxc_out_extra_outputs = 10,
	dxc_out_remarks = 11,
	dxc_out_time_report = 12,
	dxc_out_time_trace = 13,
	dxc_out_force_dword = 0xFFFFFFFFu
};

struct dxc_buffer
{
	void const*  ptr;
	std::size_t  size;
	unsigned int encoding;
};

struct i_dxc_unknown
{
	virtual dxc_hresult XRAY_DXC_CALL QueryInterface( dxc_guid const& riid, void** ppv_object ) = 0;
	virtual dxc_ulong XRAY_DXC_CALL AddRef() = 0;
	virtual dxc_ulong XRAY_DXC_CALL Release() = 0;
};

struct i_dxc_blob : public i_dxc_unknown
{
	virtual void* XRAY_DXC_CALL GetBufferPointer() = 0;
	virtual std::size_t XRAY_DXC_CALL GetBufferSize() = 0;
};

struct i_dxc_blob_encoding : public i_dxc_blob
{
	virtual dxc_hresult XRAY_DXC_CALL GetEncoding( dxc_bool* known, dxc_uint32* code_page ) = 0;
};

struct i_dxc_blob_wide : public i_dxc_blob_encoding
{
	virtual wchar_t const* XRAY_DXC_CALL GetStringPointer() = 0;
	virtual std::size_t XRAY_DXC_CALL GetStringLength() = 0;
};

struct i_dxc_blob_utf8 : public i_dxc_blob_encoding
{
	virtual char const* XRAY_DXC_CALL GetStringPointer() = 0;
	virtual std::size_t XRAY_DXC_CALL GetStringLength() = 0;
};

struct i_dxc_include_handler : public i_dxc_unknown
{
	virtual dxc_hresult XRAY_DXC_CALL LoadSource( wchar_t const* filename, i_dxc_blob** include_source ) = 0;
};

struct i_dxc_operation_result : public i_dxc_unknown
{
	virtual dxc_hresult XRAY_DXC_CALL GetStatus( dxc_hresult* status ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetResult( i_dxc_blob** result ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetErrorBuffer( i_dxc_blob_encoding** errors ) = 0;
};

struct i_dxc_result : public i_dxc_operation_result
{
	virtual dxc_bool XRAY_DXC_CALL HasOutput( dxc_out_kind kind ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetOutput( dxc_out_kind kind, dxc_guid const& iid, void** object, i_dxc_blob_wide** output_name ) = 0;
	virtual dxc_uint32 GetNumOutputs() = 0;
	virtual dxc_out_kind GetOutputByIndex( dxc_uint32 index ) = 0;
	virtual dxc_out_kind PrimaryOutput() = 0;
};

struct i_dxc_utils : public i_dxc_unknown
{
	virtual dxc_hresult XRAY_DXC_CALL CreateBlobFromBlob( i_dxc_blob* blob, dxc_uint32 offset, dxc_uint32 length, i_dxc_blob** result ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL CreateBlobFromPinned( void const* data, dxc_uint32 size, dxc_uint32 code_page, i_dxc_blob_encoding** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL MoveToBlob( void const* data, void* malloc_ptr, dxc_uint32 size, dxc_uint32 code_page, i_dxc_blob_encoding** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL CreateBlob( void const* data, dxc_uint32 size, dxc_uint32 code_page, i_dxc_blob_encoding** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL LoadFile( wchar_t const* file_name, dxc_uint32* code_page, i_dxc_blob_encoding** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL CreateReadOnlyStreamFromBlob( i_dxc_blob* blob, void** stream ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL CreateDefaultIncludeHandler( i_dxc_include_handler** result ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetBlobAsUtf8( i_dxc_blob* blob, i_dxc_blob_utf8** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetBlobAsWide( i_dxc_blob* blob, i_dxc_blob_wide** blob_encoding ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetDxilContainerPart( dxc_buffer const* shader, dxc_uint32 dxc_part, void** part_data, dxc_uint32* part_size_in_bytes ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL CreateReflection( dxc_buffer const* data, dxc_guid const& iid, void** reflection ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL BuildArguments(
		wchar_t const* source_name,
		wchar_t const* entry_point,
		wchar_t const* target_profile,
		wchar_t const** arguments,
		dxc_uint32 arg_count,
		void const* defines,
		dxc_uint32 define_count,
		void** args_out ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL GetPDBContents( i_dxc_blob* pdb_blob, i_dxc_blob** hash, i_dxc_blob** container ) = 0;
};

struct i_dxc_compiler3 : public i_dxc_unknown
{
	virtual dxc_hresult XRAY_DXC_CALL Compile(
		dxc_buffer const* source,
		wchar_t const* const* arguments,
		dxc_uint32 arg_count,
		i_dxc_include_handler* include_handler,
		dxc_guid const& riid,
		void** result ) = 0;
	virtual dxc_hresult XRAY_DXC_CALL Disassemble(
		dxc_buffer const* object,
		dxc_guid const& riid,
		void** result ) = 0;
};

using dxc_create_instance_fn = dxc_hresult ( XRAY_DXC_CALL* )(
	dxc_guid const& rclsid,
	dxc_guid const& riid,
	void** ppv );

template <typename T>
class dxc_com_ptr
{
public:
	dxc_com_ptr() noexcept = default;
	dxc_com_ptr( std::nullptr_t ) noexcept {}

	explicit dxc_com_ptr( T* ptr ) noexcept : m_ptr( ptr )
	{
		if ( m_ptr )
			m_ptr->AddRef();
	}

	dxc_com_ptr( dxc_com_ptr const& other ) noexcept : m_ptr( other.m_ptr )
	{
		if ( m_ptr )
			m_ptr->AddRef();
	}

	dxc_com_ptr( dxc_com_ptr&& other ) noexcept : m_ptr( std::exchange( other.m_ptr, nullptr ) ) {}

	~dxc_com_ptr() noexcept
	{
		reset();
	}

	dxc_com_ptr& operator=( dxc_com_ptr const& other ) noexcept
	{
		if ( this != &other ) {
			dxc_com_ptr tmp( other );
			std::swap( m_ptr, tmp.m_ptr );
		}
		return *this;
	}

	dxc_com_ptr& operator=( dxc_com_ptr&& other ) noexcept
	{
		if ( this != &other ) {
			reset();
			m_ptr = std::exchange( other.m_ptr, nullptr );
		}
		return *this;
	}

	dxc_com_ptr& operator=( std::nullptr_t ) noexcept
	{
		reset();
		return *this;
	}

	void attach( T* ptr ) noexcept
	{
		reset();
		m_ptr = ptr;
	}

	T* detach() noexcept
	{
		return std::exchange( m_ptr, nullptr );
	}

	void reset() noexcept
	{
		if ( m_ptr ) {
			m_ptr->Release();
			m_ptr = nullptr;
		}
	}

	T* get() const noexcept { return m_ptr; }
	T** put() noexcept
	{
		reset();
		return &m_ptr;
	}

	T* operator->() const noexcept { return m_ptr; }
	T& operator*() const noexcept { return *m_ptr; }
	explicit operator bool() const noexcept { return m_ptr != nullptr; }

private:
	T* m_ptr = nullptr;
};

XRAY_RENDER_VULKAN_API std::wstring utf8_to_dxc_wide( std::string const& utf8 );
XRAY_RENDER_VULKAN_API std::string dxc_wide_to_utf8( wchar_t const* data, std::size_t length );
XRAY_RENDER_VULKAN_API std::string dxc_wide_to_utf8( std::wstring const& wide );

} // namespace dxc
} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_DXC_ABI_H_INCLUDED
