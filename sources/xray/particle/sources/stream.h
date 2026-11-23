////////////////////////////////////////////////////////////////////////////
//	Created		: 26.07.2010
//	Author		: Nikolay Partas
//	Copyright (C) GSC Game World - 2010
////////////////////////////////////////////////////////////////////////////

#ifndef PARTICLE_STREAM_H_INCLUDED
#define PARTICLE_STREAM_H_INCLUDED

namespace xray {
namespace particle {

class stream {
public:
					stream		( u8* buffer, bool is_reading ) 
						: m_buffer(buffer), m_is_reading(is_reading) 
					{}
	virtual			~stream		() {}

	virtual stream&	sync_data	( void* data, u32 const data_size )
	{
		if (m_is_reading)
			xray::memory::copy(data, data_size, m_buffer, data_size);
		else
			xray::memory::copy(m_buffer, data_size, data, data_size);
		
		m_buffer += data_size;
		return *this;
	}

	virtual void	operator += ( u32 size )
	{
		m_buffer += size;
	}

	u8*				c_ptr		() const { return m_buffer; }
	bool			is_reading	() const { return m_is_reading; }

protected:
	u8*		m_buffer;
	bool	m_is_reading;
}; // class stream

} // namespace particle
} // namespace xray

#endif // #ifndef PARTICLE_STREAM_H_INCLUDED
