#include "xray/maya_cook/cook.hpp"

#include "animation_cooker.hpp"
#include "exchange_schema.hpp"
#include "json_value.hpp"
#include "physics_cooker.hpp"
#include "skeleton_cooker.hpp"
#include "skinned_model_cooker.hpp"
#include "static_model_cooker.hpp"

#include <fstream>
#include <stdexcept>

namespace xray::maya_cook {

namespace {

constexpr std::size_t max_payload_bytes = 64u * 1024u * 1024u;

std::string read_text_file(std::filesystem::path const& path) {
    std::ifstream is(path, std::ios::binary);
    if (!is) throw std::runtime_error("Cannot open exchange manifest: " + path.string());
    return std::string((std::istreambuf_iterator<char>(is)), {});
}

std::vector<std::uint8_t> read_binary_file(std::filesystem::path const& path) {
    std::error_code ec;
    auto sz = std::filesystem::file_size(path, ec);
    if (ec || sz > max_payload_bytes) {
        throw std::runtime_error("Exchange payload missing or exceeds 64 MiB limit: " + path.string());
    }
    std::ifstream is(path, std::ios::binary);
    if (!is) throw std::runtime_error("Cannot open exchange payload: " + path.string());
    std::vector<std::uint8_t> buf(static_cast<std::size_t>(sz));
    if (!buf.empty()) {
        is.read(reinterpret_cast<char*>(buf.data()), static_cast<std::streamsize>(buf.size()));
    }
    if (!is) throw std::runtime_error("Failed reading exchange payload: " + path.string());
    return buf;
}

} // namespace

cook_result cook_exchange_bundle(
    std::filesystem::path const& exchange_dir,
    std::filesystem::path const& output_dir,
    std::filesystem::path const& converted_dir,
    std::filesystem::path const& archive_path,
    cook_options const& options
) {
    std::filesystem::path manifest_path = exchange_dir / "manifest.json";
    std::filesystem::path payload_path = exchange_dir / "payload.bin";

    auto manifest = detail::parse_json(read_text_file(manifest_path));
    detail::validate_exchange_header(manifest);

    auto payload = read_binary_file(payload_path);
    std::string const& kind = manifest.require("asset_kind").as_string();
    if (kind == "static_model") {
        return detail::cook_static_model(manifest, payload, output_dir, converted_dir, archive_path, options);
    }
    if (kind == "skinned_model") {
        return detail::cook_skinned_model(manifest, payload, output_dir, converted_dir, archive_path, options);
    }
    if (kind == "skeleton") {
        return detail::cook_skeleton(manifest, output_dir, converted_dir);
    }
    if (kind == "animation") {
        return detail::cook_animation(manifest, payload, output_dir, converted_dir);
    }
    if (kind == "physics") {
        return detail::cook_physics(manifest, payload, output_dir, converted_dir);
    }
    throw std::runtime_error("Unsupported or not-yet-implemented asset_kind in maya_cook: " + kind);
}

} // namespace xray::maya_cook
