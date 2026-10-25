// Licensed under the MIT license.
#pragma once
#include <Windows.h>

namespace CraftHotkey
{
    // Low WORD: virtual key; high WORD: 0=None, 1=Ctrl, 2=Alt, 3=Shift.
    constexpr bool Matches(DWORD binding, UINT message, WPARAM key, LPARAM flags, unsigned held)
    {
        const unsigned mainKey = LOWORD(binding);
        const unsigned modifier = HIWORD(binding);
        if ((message != WM_KEYDOWN && message != WM_SYSKEYDOWN) ||
            (flags & (static_cast<LPARAM>(1) << 30)) != 0 ||
            mainKey == 0 || mainKey == 255 || mainKey != LOWORD(key) || modifier > 3)
        {
            return false;
        }
        const unsigned required = modifier == 0 ? 0 : 1u << (modifier - 1);
        return held == required;
    }

    inline bool MatchesCurrentMessage(DWORD binding, UINT message, WPARAM key, LPARAM flags)
    {
        const unsigned held =
            ((GetKeyState(VK_CONTROL) & 0x8000) ? 1u : 0u) |
            ((GetKeyState(VK_MENU) & 0x8000) ? 2u : 0u) |
            ((GetKeyState(VK_SHIFT) & 0x8000) ? 4u : 0u);
        return Matches(binding, message, key, flags, held);
    }
}
