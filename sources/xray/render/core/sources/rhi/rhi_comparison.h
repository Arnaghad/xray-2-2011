#ifndef XRAY_RENDER_RHI_COMPARISON_H_INCLUDED
#define XRAY_RENDER_RHI_COMPARISON_H_INCLUDED
#include <xray/type_extensions.h>

namespace xray { namespace render { namespace rhi {
enum class comparison_func : u8 {
    never=1, less, equal, less_equal, greater, not_equal, greater_equal, always
};
} } }
#endif
