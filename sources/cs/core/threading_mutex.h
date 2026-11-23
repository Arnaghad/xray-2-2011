////////////////////////////////////////////////////////////////////////////
//	Module 		: threading_mutex.h
//	Created 	: 26.08.2006
//  Modified 	: 26.08.2006
//	Author		: Dmitriy Iassenev
//	Description : threading mutex class
////////////////////////////////////////////////////////////////////////////

#ifndef CS_CORE_THREADING_MUTEX_H_INCLUDED
#define CS_CORE_THREADING_MUTEX_H_INCLUDED

namespace threading {

class CS_CORE_API mutex {
public:
					mutex	();
					~mutex	();

public:
			void	lock	();
			void	unlock	();
			bool	trylock	();

private:
	alignas(void*) char m_mutex[40]; // Windows x64 CRITICAL_SECTION storage.
};

}

#endif // #ifndef CS_CORE_THREADING_MUTEX_H_INCLUDED
