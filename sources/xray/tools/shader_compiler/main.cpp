#include <xray/render/core/sources/rhi/shader_compiler.h>
#include <iostream>
#include <vector>
#include <string>
#include <filesystem>
#include <fstream>

namespace fs = std::filesystem;
using namespace xray::render::rhi;

void print_help() {
    std::cout << "Usage: shader_compiler <input_file> <output_file> <stage> [flags...]\n";
    std::cout << "Stages:\n";
    std::cout << "  vertex\n  pixel\n  geometry\n  hull\n  domain\n  compute\n";
    std::cout << "Example:\n";
    std::cout << "Options: --entry=name --include=directory --define=NAME=value [DXC flags]\n";
    std::cout << "  shader_compiler main.hlsl main.spv vertex -O3\n";
}

shader_stage parse_stage(const std::string& stage_str) {
    if (stage_str == "vertex") return shader_stage::vertex;
    if (stage_str == "pixel") return shader_stage::pixel;
    if (stage_str == "geometry") return shader_stage::geometry;
    if (stage_str == "hull") return shader_stage::hull;
    if (stage_str == "domain") return shader_stage::domain;
    if (stage_str == "compute") return shader_stage::compute;
    throw std::runtime_error("Invalid shader stage: " + stage_str);
}

int main(int argc, char* argv[]) {
    if (argc < 4) {
        print_help();
        return 1;
    }

    std::string input_path = argv[1];
    std::string output_path = argv[2];
    std::string stage_str = argv[3];
    
    shader_compile_options options;
    for (int i = 4; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg.starts_with("--entry=")) options.entry = arg.substr(8);
        else if (arg.starts_with("--include=")) options.include_directories.push_back(arg.substr(10));
        else if (arg.starts_with("--define=")) options.defines.push_back(arg.substr(9));
        else options.flags.push_back(arg);
    }

    try {
        shader_stage stage = parse_stage(stage_str);
        shader_compiler compiler;
        
        std::cout << "Compiling " << input_path << " (" << stage_str << ")...\n";
        
        auto result = compiler.compile_file(input_path, stage, options);
        
        if (!result.success) {
            std::cerr << "Compilation failed:\n" << result.error_log << "\n";
            return 1;
        }

        std::ofstream output(output_path, std::ios::binary);
        if (!output) {
            std::cerr << "Failed to open output file: " << output_path << "\n";
            return 1;
        }

        output.write(reinterpret_cast<const char*>(result.bytecode.data()), result.bytecode.size() * sizeof(uint32_t));
        output.close();
        if (!output) throw std::runtime_error("Failed to write shader output: " + output_path);
        
        std::cout << "Successfully compiled to " << output_path << "\n";
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }

    return 0;
}
