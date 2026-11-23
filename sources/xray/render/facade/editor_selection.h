#pragma once
#include <cstdint>
namespace xray::render {
struct editor_selection_settings {
    std::uint32_t version=1,size=32,enabled=0,color=0xff4080ff;
    float rate=1,time=0,width=2,reserved=0;
};
static_assert(sizeof(editor_selection_settings)==32);
}
