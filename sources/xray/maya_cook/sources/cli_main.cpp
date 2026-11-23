#include "xray/maya_cook/cook.hpp"

#include <cstdlib>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string_view>

namespace {

void print_usage() {
    std::cerr <<
        "Usage: xray_asset_cook_cli --input <bundle_dir> --output <out_path>\n"
        "  (legacy entry point: xray_maya_cook_cli)\n"
        "  [--converted-output <dir>] [--pack-archive <file.model>]\n"
        "  [--fit-collision] [--clip-samples <file.clip_samples>]\n"
        "  [--overwrite-settings] [--no-optimize]\n"
        "  [--fast-overdraw] [--preserve-tangents] [--max-verts <N>]\n";
}

} // namespace

int main(int argc, char** argv) {
    std::filesystem::path input_dir;
    std::filesystem::path output_dir;
    std::filesystem::path converted_dir;
    std::filesystem::path archive_path;
    std::filesystem::path clip_samples_path;
    bool fit_collision = false;
    xray::maya_cook::cook_options opts;

    try {
        for (int i = 1; i < argc; ++i) {
            std::string_view arg = argv[i];
            auto next_arg = [&](std::string_view flag) -> std::string_view {
                if (i + 1 >= argc) {
                    throw std::runtime_error(std::string("Missing value for ") + std::string(flag));
                }
                return argv[++i];
            };

            if (arg == "--help" || arg == "-h") {
                print_usage();
                return 0;
            } else if (arg == "--input" || arg == "-i") {
                input_dir = next_arg(arg);
            } else if (arg == "--output" || arg == "-o") {
                output_dir = next_arg(arg);
            } else if (arg == "--converted-output" || arg == "-c") {
                converted_dir = next_arg(arg);
            } else if (arg == "--pack-archive" || arg == "-a") {
                archive_path = next_arg(arg);
            } else if (arg == "--fit-collision") {
                fit_collision = true;
            } else if (arg == "--clip-samples") {
                clip_samples_path = next_arg(arg);
            } else if (arg == "--overwrite-settings") {
                opts.preserve_existing_settings = false;
            } else if (arg == "--no-optimize") {
                opts.optimize_mesh = false;
            } else if (arg == "--no-optimize-uv") {
                opts.optimize_uv = false;
            } else if (arg == "--fast-overdraw") {
                opts.high_quality_overdraw = false;
            } else if (arg == "--preserve-tangents") {
                opts.preserve_input_tangents = true;
            } else if (arg == "--max-verts") {
                opts.max_verts_per_surface = static_cast<std::uint32_t>(std::stoul(std::string(next_arg(arg))));
            } else {
                throw std::runtime_error(std::string("Unknown CLI flag: ") + std::string(arg));
            }
        }

        if (!clip_samples_path.empty()) {
            xray::maya_cook::verify_clip_samples(clip_samples_path);
            return 0;
        }

        if (fit_collision) {
            if (input_dir.empty()) {
                print_usage();
                return 2;
            }
            std::string const json_out = xray::maya_cook::fit_collision_bundle_to_json(input_dir);
            if (!output_dir.empty()) {
                if (output_dir.has_parent_path()) {
                    std::filesystem::create_directories(output_dir.parent_path());
                }
                std::ofstream os(output_dir, std::ios::binary | std::ios::trunc);
                if (!os) {
                    throw std::runtime_error("Cannot open fit output JSON file: " + output_dir.string());
                }
                os.write(json_out.data(), static_cast<std::streamsize>(json_out.size()));
            } else {
                std::cout << json_out;
            }
            return 0;
        }

        if (input_dir.empty() || output_dir.empty()) {
            print_usage();
            return 2;
        }

        auto res = xray::maya_cook::cook_exchange_bundle(
            input_dir, output_dir, converted_dir, archive_path, opts
        );

        std::cout << "Cooked " << res.asset_kind << " (" << res.logical_name << "): "
                  << res.surfaces.size() << " surface(s), bones=" << res.bone_count
                  << ", frames=" << res.frame_count << ", locators=" << res.locator_count
                  << ", bodies=" << res.body_count << ", joints=" << res.joint_count
                  << ", collision=" << (res.has_collision ? "yes" : "no") << '\n';
        for (auto const& s : res.surfaces) {
            std::cout << "  surface '" << s.name << "': vertices=" << s.vertex_count
                      << " indices=" << s.index_count << " material='" << s.material_name << "'\n";
        }
        return 0;
    } catch (std::exception const& ex) {
        std::cerr << "xray_maya_cook_cli error: " << ex.what() << '\n';
        return 1;
    }
}
