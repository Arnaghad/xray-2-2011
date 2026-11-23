////////////////////////////////////////////////////////////////////////////
//	Created 	: 03.05.2026
//	Author		: Antigravity
//	Copyright (C) GSC Game World - 2026
////////////////////////////////////////////////////////////////////////////

#ifndef XRAY_CRC32_H_INCLUDED
#define XRAY_CRC32_H_INCLUDED

#include <cstdint>
#include <cstddef>

namespace xray {

class crc32_type {
private:
    uint32_t m_value;

    static uint32_t generate_table_entry(int i) {
        uint32_t crc = i;
        for (int j = 0; j < 8; ++j) {
            crc = (crc >> 1) ^ ((crc & 1) ? 0xEDB88320u : 0u);
        }
        return crc;
    }

    static const uint32_t* get_table() {
        static uint32_t table[256] = {};
        static bool initialized = false;
        if (!initialized) {
            for (int i = 0; i < 256; ++i) {
                table[i] = generate_table_entry(i);
            }
            initialized = true;
        }
        return table;
    }

public:
    crc32_type() : m_value(0xFFFFFFFFu) {}
    explicit crc32_type(uint32_t initial) : m_value(initial ^ 0xFFFFFFFFu) {}

    void process_bytes(const void* buffer, size_t byte_count) {
        const uint32_t* table = get_table();
        const uint8_t* p = static_cast<const uint8_t*>(buffer);
        for (size_t i = 0; i < byte_count; ++i) {
            m_value = (m_value >> 8) ^ table[(m_value ^ p[i]) & 0xFF];
        }
    }

    void process_block(const void* begin, const void* end) {
        const uint8_t* b = static_cast<const uint8_t*>(begin);
        const uint8_t* e = static_cast<const uint8_t*>(end);
        if (e > b) {
            process_bytes(b, static_cast<size_t>(e - b));
        }
    }

    uint32_t checksum() const {
        return ~m_value;
    }
};

} // namespace xray

#endif // #ifndef XRAY_CRC32_H_INCLUDED
