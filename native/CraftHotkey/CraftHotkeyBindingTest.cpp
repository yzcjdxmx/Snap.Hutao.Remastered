// Compile the production matcher: static_assert remains active in Release builds.
#include "CraftHotkeyBinding.h"
#include "ResinListOffset.h"
#include <array>

constexpr DWORD Key(unsigned modifier) { return 67 | (modifier << 16); }
static_assert(CraftHotkey::Matches(Key(0), WM_KEYDOWN, 'C', 0, 0));
static_assert(CraftHotkey::Matches(Key(1), WM_KEYDOWN, 'C', 0, 1));
static_assert(CraftHotkey::Matches(Key(2), WM_SYSKEYDOWN, 'C', 0, 2));
static_assert(CraftHotkey::Matches(Key(3), WM_KEYDOWN, 'C', 0, 4));
static_assert(!CraftHotkey::Matches(Key(2), WM_KEYDOWN, 'C', 0, 0));
static_assert(!CraftHotkey::Matches(Key(1), WM_KEYDOWN, 'C', 0, 3));
static_assert(!CraftHotkey::Matches(Key(0), WM_KEYDOWN, 'C', 0, 2));
static_assert(!CraftHotkey::Matches(Key(2), WM_SYSKEYDOWN, 'D', 0, 2));
static_assert(!CraftHotkey::Matches(Key(2), WM_SYSKEYDOWN, 'C', 1LL << 30, 2));
static_assert(!CraftHotkey::Matches(Key(2), WM_KEYUP, 'C', 0, 2));
static_assert(!CraftHotkey::Matches(0, WM_KEYDOWN, 0, 0, 0));
static_assert(!CraftHotkey::Matches(255, WM_KEYDOWN, 255, 0, 0));
static_assert(!CraftHotkey::Matches(Key(4), WM_KEYDOWN, 'C', 0, 0));
// Reproduce 7.2's reward-list instructions; 0x1A is deliberately unrelated data.
constexpr auto RewardCode()
{
    std::array<unsigned char, 0x2E> code{};
    code[0x1A] = 0x11;
    code[0x24] = 0x48; code[0x25] = 0x8B; code[0x26] = 0x8B;
    code[0x27] = 0x58;
    code[0x2B] = 0x48; code[0x2C] = 0x85; code[0x2D] = 0xC9;
    return code;
}
constexpr auto rewardCode = RewardCode();
static_assert(ResinListOffset::Read(rewardCode.data(), rewardCode.size()) == 0x58);
static_assert(ResinListOffset::Read(nullptr, rewardCode.size()) == 0);
static_assert(ResinListOffset::Read(rewardCode.data(), 0x2D) == 0);
constexpr bool RejectInvalidRewardCode()
{
    for (auto position : {0x24, 0x25, 0x26, 0x2B, 0x2C, 0x2D})
    {
        auto code = RewardCode();
        code[position] = 0;
        if (ResinListOffset::Read(code.data(), code.size()) != 0)
            return false;
    }
    auto code = RewardCode();
    code[0x27] = 0;
    return ResinListOffset::Read(code.data(), code.size()) == 0;
}
static_assert(RejectInvalidRewardCode());
int main() { return 0; }
