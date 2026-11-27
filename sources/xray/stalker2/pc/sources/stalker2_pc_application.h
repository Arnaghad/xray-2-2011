////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.08.2008
//	Author		: Dmitriy Iassenev
//	Copyright (C) GSC Game World - 2009
////////////////////////////////////////////////////////////////////////////

#ifndef STARTER2_PC_APPLICATION_H_INCLUDED
#define STARTER2_PC_APPLICATION_H_INCLUDED

#include <xray/stalker2/game_module_proxy.h>
#include <memory>
#include <string>

namespace stalker2 {

struct i_splash_screen {
	virtual			~i_splash_screen( ) = default;
	virtual void	show			( pcstr command_line ) = 0;
	virtual void	close			( ) = 0;
};

class application {
public:
			void		initialize		( );
			void		initialize		( pcstr command_line );
			void		execute			( );
			void		finalize		( );
	inline	u32			get_exit_code	( ) const { return m_exit_code; }

private:
			void		preinitialize	( );
			void		postinitialize	( );

private:
	std::unique_ptr<i_splash_screen>	m_splash_screen;
	std::string							m_command_line;

private:
	game_module_proxy	m_game_proxy;
	u32					m_exit_code;
}; // class application

} // namespace stalker2

#endif // #ifndef STARTER2_PC_APPLICATION_H_INCLUDED
