////////////////////////////////////////////////////////////////////////////
//	Created 	: 26.11.2026
//	Description	: POSIX / SDL3 application bootstrap & splash screen
////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "stalker2_pc_application.h"
#include <xray/engine/api.h>
#include <SDL3/SDL.h>
#include <filesystem>

using stalker2::application;

namespace {

class sdl_splash_screen final : public stalker2::i_splash_screen {
public:
	void show( pcstr ) override
	{
		if ( xray::engine::command_line_no_splash_screen() )
			return;

		std::filesystem::path const image_path = std::filesystem::path( "../../resources/sources/splash_screens" ) /
			( xray::engine::command_line_editor() ? "development_editor.bmp" : "development_engine.bmp" );
		std::error_code ec;
		if ( !std::filesystem::is_regular_file( image_path, ec ) )
			return;

		if ( SDL_WasInit( SDL_INIT_VIDEO ) == 0 && !SDL_InitSubSystem( SDL_INIT_VIDEO ) )
			return;
		m_owns_video = ( SDL_WasInit( SDL_INIT_VIDEO ) != 0 );

		SDL_Surface* const bmp = SDL_LoadBMP( image_path.c_str() );
		if ( !bmp )
			return;

		m_window = SDL_CreateWindow(
			"X-Ray Engine 2.0",
			bmp->w,
			bmp->h,
			SDL_WINDOW_BORDERLESS | SDL_WINDOW_NOT_FOCUSABLE
		);
		if ( m_window ) {
			if ( SDL_Surface* const surface = SDL_GetWindowSurface( m_window ) ) {
				SDL_BlitSurface( bmp, nullptr, surface, nullptr );
				SDL_UpdateWindowSurface( m_window );
			}
		}
		SDL_DestroySurface( bmp );
	}

	void close( ) override
	{
		if ( m_window ) {
			SDL_DestroyWindow( m_window );
			m_window = nullptr;
		}
	}

	~sdl_splash_screen( ) override
	{
		close();
	}

private:
	SDL_Window*	m_window = nullptr;
	bool		m_owns_video = false;
};

} // namespace

void application::preinitialize					( )
{
	xray::engine::preinitialize		( m_game_proxy, m_command_line.c_str(), "stalker2", __DATE__ );

	if ( !xray::engine::command_line_no_splash_screen() ) {
		m_splash_screen				= std::make_unique<sdl_splash_screen>();
		m_splash_screen->show		( m_command_line.c_str() );
	}
}

void application::postinitialize				( )
{
	if ( m_splash_screen )
		m_splash_screen->close		( );
}
