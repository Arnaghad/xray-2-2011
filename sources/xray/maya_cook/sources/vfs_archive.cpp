#include "vfs_archive.hpp"
#include "math_types.hpp"

#include <cstring>
#include <fstream>
#include <memory>
#include <stdexcept>

namespace xray::maya_cook::detail {

namespace {

struct vfs_node {
    std::string name;
    bool is_dir = true;
    std::vector<std::uint8_t> const* payload = nullptr;
    std::map<std::string, std::unique_ptr<vfs_node>> children;
    std::uint32_t node_offset = 0; // byte offset inside FAT where the 52+name block starts
    std::uint32_t payload_offset = 0;
};

std::uint32_t align8(std::uint32_t v) {
    return (v + 7u) & ~7u;
}

void write_u16(std::vector<std::uint8_t>& buf, std::size_t off, std::uint16_t v) {
    std::memcpy(buf.data() + off, &v, sizeof(v));
}

void write_u32(std::vector<std::uint8_t>& buf, std::size_t off, std::uint32_t v) {
    std::memcpy(buf.data() + off, &v, sizeof(v));
}

void write_u64(std::vector<std::uint8_t>& buf, std::size_t off, std::uint64_t v) {
    std::memcpy(buf.data() + off, &v, sizeof(v));
}

} // namespace

std::vector<std::uint8_t> build_vfs_archive(std::map<std::string, std::vector<std::uint8_t>> const& files) {
    if (files.empty()) {
        throw std::runtime_error("Cannot build empty VFS archive");
    }

    auto root = std::make_unique<vfs_node>();
    root->is_dir = true;

    std::uint32_t node_count = 1;
    std::uint32_t total_payload = 0;
    for (auto const& [path, data] : files) {
        if (path.empty() || path.front() == '/' || path.back() == '/') {
            throw std::runtime_error("Invalid virtual path for VFS archive: " + path);
        }
        vfs_node* cur = root.get();
        std::size_t start = 0;
        while (start < path.size()) {
            std::size_t slash = path.find('/', start);
            bool leaf = (slash == std::string::npos);
            std::string seg = leaf ? path.substr(start) : path.substr(start, slash - start);
            if (seg.empty() || seg == "." || seg == ".." || seg.size() >= 256) {
                throw std::runtime_error("Invalid VFS archive path segment: " + seg);
            }
            auto it = cur->children.find(seg);
            if (it == cur->children.end()) {
                auto node = std::make_unique<vfs_node>();
                node->name = seg;
                node->is_dir = !leaf;
                if (leaf) {
                    node->payload = &data;
                    total_payload += static_cast<std::uint32_t>(data.size());
                }
                cur = cur->children.emplace(seg, std::move(node)).first->second.get();
                ++node_count;
            } else {
                if (leaf || !it->second->is_dir) {
                    throw std::runtime_error("Duplicate or conflicting VFS archive path: " + path);
                }
                cur = it->second.get();
            }
            if (leaf) break;
            start = slash + 1;
        }
    }

    std::vector<vfs_node*> order;
    auto collect_dfs = [&](auto&& self, vfs_node* n) -> void {
        order.push_back(n);
        for (auto& [_, child] : n->children) {
            self(self, child.get());
        }
    };
    collect_dfs(collect_dfs, root.get());

    // Allocate FAT offsets:
    // Directory node has 16-byte header before `node_offset` (`first_child` at node-16).
    // File node has 24-byte header before `node_offset` (`length` at node-24, `payload_offset` at node-16).
    // Common body at `node_offset`: 51 bytes + strlen(name) + 1 NUL, aligned up to 8 bytes.
    std::uint32_t cursor = 0;
    for (vfs_node* n : order) {
        std::uint32_t prefix = n->is_dir ? 16u : 24u;
        cursor += prefix;
        n->node_offset = cursor;
        std::uint32_t body = 51u + static_cast<std::uint32_t>(n->name.size()) + 1u;
        cursor = align8(cursor + body);
    }
    std::uint32_t const fat_size = align8(cursor);
    std::uint32_t const data_start = 24u + fat_size;

    std::uint32_t data_cursor = data_start;
    for (vfs_node* n : order) {
        if (!n->is_dir) {
            n->payload_offset = data_cursor;
            data_cursor += static_cast<std::uint32_t>(n->payload->size());
        }
    }

    std::vector<std::uint8_t> archive(24u + fat_size + total_payload, 0);
    std::memcpy(archive.data(), "little-endian", 13);
    write_u32(archive, 16, node_count);
    write_u32(archive, 20, fat_size);

    std::uint32_t const root_base = root->node_offset - 16u;
    std::size_t const fat_base = 24u;

    for (vfs_node* n : order) {
        std::uint32_t o = n->node_offset;
        if (n == root.get()) {
            write_u16(archive, fat_base + o + 48, 13u);
            write_u64(archive, fat_base + o + 32, 0u);
            write_u64(archive, fat_base + o + 24, 0u);
            if (!n->children.empty()) {
                write_u64(archive, fat_base + o - 16, n->children.begin()->second->node_offset);
            }
        } else if (n->is_dir) {
            write_u16(archive, fat_base + o + 48, 5u);
            write_u64(archive, fat_base + o + 32, root_base);
            if (!n->children.empty()) {
                write_u64(archive, fat_base + o - 16, n->children.begin()->second->node_offset);
            }
        } else {
            write_u16(archive, fat_base + o + 48, 4u);
            write_u64(archive, fat_base + o + 32, root_base);
            write_u32(archive, fat_base + o - 24, static_cast<std::uint32_t>(n->payload->size()));
            write_u64(archive, fat_base + o - 16, n->payload_offset);
            if (!n->payload->empty()) {
                std::memcpy(archive.data() + n->payload_offset, n->payload->data(), n->payload->size());
            }
        }
        std::memcpy(archive.data() + fat_base + o + 51, n->name.c_str(), n->name.size() + 1);

        // Link child sibling chain
        if (n->is_dir && !n->children.empty()) {
            vfs_node* prev = nullptr;
            for (auto& [_, child] : n->children) {
                if (prev) {
                    write_u64(archive, fat_base + prev->node_offset + 24, child->node_offset);
                }
                prev = child.get();
            }
        }
    }

    return archive;
}

void write_file_bytes(std::filesystem::path const& path, std::vector<std::uint8_t> const& bytes) {
    if (path.has_parent_path()) {
        std::filesystem::create_directories(path.parent_path());
    }
    std::ofstream os(path, std::ios::binary | std::ios::trunc);
    if (!os) {
        throw std::runtime_error("Cannot open output file: " + path.string());
    }
    if (!bytes.empty()) {
        os.write(reinterpret_cast<char const*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    }
    os.flush();
    if (!os) {
        throw std::runtime_error("Failed writing output file: " + path.string());
    }
}

void write_file_text(std::filesystem::path const& path, std::string_view text) {
    if (path.has_parent_path()) {
        std::filesystem::create_directories(path.parent_path());
    }
    std::ofstream os(path, std::ios::binary | std::ios::trunc);
    if (!os) {
        throw std::runtime_error("Cannot open output file: " + path.string());
    }
    os.write(text.data(), static_cast<std::streamsize>(text.size()));
    os.flush();
    if (!os) {
        throw std::runtime_error("Failed writing output file: " + path.string());
    }
}

} // namespace xray::maya_cook::detail

