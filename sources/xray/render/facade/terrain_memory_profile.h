#ifndef XRAY_RENDER_TERRAIN_MEMORY_PROFILE_H_INCLUDED
#define XRAY_RENDER_TERRAIN_MEMORY_PROFILE_H_INCLUDED
#include <cstddef>
#include <stdexcept>
namespace xray { namespace render {
// Explicit per-scene and per-cell opt-in. Existing callers retain the 256 MiB profile.
enum class terrain_memory_profile { standard_256_mib, extended_384_mib };
struct terrain_memory_limits { std::size_t cell_bytes, queued_view_bytes; };
inline terrain_memory_limits terrain_limits(terrain_memory_profile profile) {
    switch (profile) {
    case terrain_memory_profile::standard_256_mib: return {256u*1024u*1024u,256u*1024u*1024u};
    case terrain_memory_profile::extended_384_mib: return {384u*1024u*1024u,576u*1024u*1024u};
    }
    throw std::runtime_error("Unsupported terrain memory profile");
}
} }
#endif
