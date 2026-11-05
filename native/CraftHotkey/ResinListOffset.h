#pragma once
#include <cstddef>
#include <cstdint>

namespace ResinListOffset
{
    // Game 7.2 delivery module: mov rcx,[rbx+disp32]; test rcx,rcx.
    // The 7.1 module uses a different instruction location and is left unchanged.
    constexpr std::uint32_t Read(const unsigned char* code, std::size_t size)
    {
        if (!code || size < 0x2E ||
            code[0x24] != 0x48 || code[0x25] != 0x8B || code[0x26] != 0x8B ||
            code[0x2B] != 0x48 || code[0x2C] != 0x85 || code[0x2D] != 0xC9)
            return 0;

        return std::uint32_t(code[0x27]) |
            (std::uint32_t(code[0x28]) << 8) |
            (std::uint32_t(code[0x29]) << 16) |
            (std::uint32_t(code[0x2A]) << 24);
    }
}
