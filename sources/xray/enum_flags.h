#pragma once

namespace xray {
    template <typename EnumType>
    class enum_flags {
    private:
        unsigned int m_value;

    public:
        inline enum_flags() : m_value(0) {}
        inline enum_flags(EnumType e) : m_value(static_cast<unsigned int>(e)) {}
        inline enum_flags(unsigned int v) : m_value(v) {}

        inline bool is(EnumType e) const { return (m_value & static_cast<unsigned int>(e)) == static_cast<unsigned int>(e); }
        inline bool is_any(EnumType e) const { return (m_value & static_cast<unsigned int>(e)) != 0; }

        inline void set(EnumType e) { m_value |= static_cast<unsigned int>(e); }
        inline void unset(EnumType e) { m_value &= ~static_cast<unsigned int>(e); }

        inline enum_flags operator|(EnumType e) const { return enum_flags(m_value | static_cast<unsigned int>(e)); }
        inline enum_flags operator&(EnumType e) const { return enum_flags(m_value & static_cast<unsigned int>(e)); }
        inline enum_flags operator~() const { return enum_flags(~m_value); }

        inline operator unsigned int() const { return m_value; }
    };
} // namespace xray
