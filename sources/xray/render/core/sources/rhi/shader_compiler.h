////////////////////////////////////////////////////////////////////////////
//	Shader Compiler
//	Wraps DXC for HLSL -> SPIR-V compilation.
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_RENDER_RHI_SHADER_COMPILER_H_INCLUDED
#define XRAY_RENDER_RHI_SHADER_COMPILER_H_INCLUDED

#include <xray/type_extensions.h>
#include <xray/render/core/sources/rhi/rhi_types.h>
#include <string>
#include <vector>
#include <optional>
#include <memory>
#include <xray/render/vulkan/library_linkage.h>

namespace xray {
namespace render {
namespace rhi {

struct compilation_result {
	std::vector<u32> bytecode;
	std::string error_log;
	bool success = false;
    bool cache_hit = false;
};

struct shader_compile_options {
	std::string entry = "main";
	std::vector<std::string> include_directories;
	std::vector<std::string> defines;
	std::vector<std::string> flags;
};
struct shader_compiler_impl;
class XRAY_RENDER_VULKAN_API shader_compiler {
public:
	shader_compiler();
	~shader_compiler();
	shader_compiler(shader_compiler const&) = delete;
	shader_compiler& operator=(shader_compiler const&) = delete;

	// Compiles HLSL source to SPIR-V bytecode.
	compilation_result compile(
		const std::string& source, 
		shader_stage stage, 
		shader_compile_options const& options = {}
	);

	// Compiles HLSL file to SPIR-V bytecode.
	compilation_result compile_file(
		const std::string& path, 
		shader_stage stage, 
		shader_compile_options const& options = {}
	);

private:
	// Internal helper to map RHI shader stage to DXC target profile
	std::string get_target_profile(shader_stage stage);
	std::unique_ptr<shader_compiler_impl> m_impl;
};

} // namespace rhi
} // namespace render
} // namespace xray

#endif // XRAY_RENDER_RHI_SHADER_COMPILER_H_INCLUDED
