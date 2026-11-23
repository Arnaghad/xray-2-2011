////////////////////////////////////////////////////////////////////////////
//	Created 	: 03.05.2026
//	Author		: Antigravity
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_NONCOPYABLE_H_INCLUDED
#define XRAY_NONCOPYABLE_H_INCLUDED

namespace xray {

class noncopyable {
protected:
	noncopyable() = default;
	~noncopyable() = default;
public:
	noncopyable(const noncopyable&) = delete;
	noncopyable& operator=(const noncopyable&) = delete;
};

} // namespace xray

#endif // #ifndef XRAY_NONCOPYABLE_H_INCLUDED
