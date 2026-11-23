#ifndef XRAY_VULKAN_SPIRV_ENTRY_H_INCLUDED
#define XRAY_VULKAN_SPIRV_ENTRY_H_INCLUDED
#include <cstring>
#include <vector>
#include <string_view>
#include <xray/render/core/sources/rhi/rhi_types.h>

namespace xray { namespace render { namespace vulkan {
// Structural/stage check only. DXC performs semantic SPIR-V validation;
// arbitrary untrusted binaries need a full SPIRV-Tools validation pass.
inline bool spirv_has_entry(std::vector<u32> const& words, rhi::shader_stage stage, char const* entry, bool descriptors = false, bool demote = false, bool multisample = false, bool bindless = false) {
    if (!entry || !*entry || words.size() < 5 || words[0] != 0x07230203u
        || words[1] < 0x00010000u || words[1] > 0x00010600u || words[3] == 0 || words[4] != 0)
        return false;
    if (stage != rhi::shader_stage::vertex && stage != rhi::shader_stage::pixel && stage != rhi::shader_stage::compute) return false;
    bool found = false;
    for (size_t i = 5; i < words.size();) {
        u32 const count = words[i] >> 16, opcode = words[i] & 0xffff;
        if (!count || count > words.size() - i) return false;
        // DemoteToHelperInvocation (5379), multisample (35), MinLod (50), and bindless capabilities are opt-in and fragment-only.
        if (opcode == 17 && (count != 2 || (words[i + 1] != 0 && words[i + 1] != 1
            && !(words[i + 1] == 5379 && demote && stage == rhi::shader_stage::pixel)
            && !(words[i + 1] == 35 && multisample && stage == rhi::shader_stage::pixel)
            && !(words[i + 1] == 50 && descriptors && stage == rhi::shader_stage::pixel)
            && !((words[i + 1] == 5301 || words[i + 1] == 5302 || words[i + 1] == 5307) && bindless && stage == rhi::shader_stage::pixel)))) return false;
        if (!descriptors && opcode == 71 && count >= 3 && (words[i + 2] == 33 || words[i + 2] == 34)) return false;
        if (opcode == 59 && count >= 4 && words[i + 3] == 9) return false;
        if (opcode == 15) { // OpEntryPoint
            if (count < 4 || !words[i + 2] || words[i + 2] >= words[3]) return false;
            auto* name = reinterpret_cast<char const*>(&words[i + 3]);
            auto* end = static_cast<char const*>(std::memchr(name, 0, (count - 3) * sizeof(u32)));
            if (!end) return false;
            u32 const model = stage == rhi::shader_stage::vertex ? 0 : stage == rhi::shader_stage::compute ? 5 : 4;
            found = found || (words[i + 1] == model && std::string_view(name, end - name) == entry);
        }
        i += count;
    }
    return found;
}
} } }
#endif
