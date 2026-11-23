/*
 * This is a part of the BugTrap package.
 * Copyright (c) 2001-2002 David Yuheng Zhao.
 *
 * Distribute and change freely, except: don't remove my name from the source
 *
 * No warrantee of any kind, express or implied, is included with this
 * software; use at your own risk, responsibility for damages (if any) to
 * anyone resulting from the use of this software rests entirely with the
 * user.
 *
 * Based on the _ThemeHelper struct in MFC7.0 source code (winctrl3.cpp),
 * and the difference is that this implementation wraps the full set of
 * visual style APIs from the platform SDK August 2001
 *
 * If you have any questions, I can be reached as follows:
 *  yuheng_zhao@yahoo.com
 *
 * Description: XP themes support.
 * Updated by: Maksim Pyatkovskiy.
 * Note: Based on code developed by David Y. Zhao.
 * Downloaded from: http://www.codeproject.com/w2k/xpvisualstyle.asp
 *
 * This source code is only intended as a supplement to the
 * BugTrap package reference and related electronic documentation
 * provided with the product. See these sources for detailed
 * information regarding the BugTrap package.
 */

#include "stdafx.h"
#include "ThemeXP.h"

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

UINT CThemeXP::m_uCount = 0;
HMODULE CThemeXP::m_hThemeDll = nullptr;
CThemeXP::FUNCTIONPOINTERS CThemeXP::m_fnPtrs;

CThemeXP::CThemeXP(HTHEME hTheme)
{
	if (m_uCount++ == 0)
		LoadThemeDll();
	m_hTheme = m_hThemeDll != nullptr ? hTheme : nullptr;
}

CThemeXP::CThemeXP(HWND hwnd, LPCWSTR pszClassList)
{
	if (m_uCount++ == 0)
		LoadThemeDll();
	m_hTheme = m_hThemeDll != nullptr ? OpenThemeData(hwnd, pszClassList) : nullptr;
}

CThemeXP::~CThemeXP()
{
	if (m_hTheme != nullptr)
		CloseThemeData(m_hTheme);
	if (--m_uCount == 0)
		UnloadThemeDll();
}

BOOL CThemeXP::Attach(HTHEME hTheme)
{
	_ASSERTE(hTheme != nullptr);
	_ASSERTE(m_hTheme == nullptr);
	if (hTheme != nullptr &&
		m_hTheme == nullptr &&
		m_hThemeDll != nullptr)
	{
		m_hTheme = hTheme;
		return TRUE;
	}
	return FALSE;
}

HTHEME CThemeXP::Detach()
{
	HTHEME hTheme = m_hTheme;
	m_hTheme = nullptr;
	return hTheme;
}

void CThemeXP::UnloadThemeDll()
{
	if (m_hThemeDll != nullptr)
	{
		FreeLibrary(m_hThemeDll);
		ZeroMemory(&m_fnPtrs, sizeof(m_fnPtrs));
		m_hThemeDll = nullptr;
	}
}

BOOL CThemeXP::LoadThemeDll()
{
	if (m_hThemeDll == nullptr)
		m_hThemeDll = LoadLibrary(_T("UxTheme.dll"));
	return (m_hThemeDll != nullptr);
}

void CThemeXP::GetThemeProcAddress(LPCSTR szProc, FARPROC& pfnProc, FARPROC pfnFailProc)
{
	if (pfnProc == nullptr)
	{
		if (m_hThemeDll != nullptr)
		{
			pfnProc = GetProcAddress(m_hThemeDll, szProc);
			if (pfnProc == nullptr)
				pfnProc = pfnFailProc;
		}
		else
			pfnProc = pfnFailProc;
	}
}
