// Compile the production matcher: static_assert remains active in Release builds.
#include "CraftHotkeyBinding.h"

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
int main() { return 0; }
