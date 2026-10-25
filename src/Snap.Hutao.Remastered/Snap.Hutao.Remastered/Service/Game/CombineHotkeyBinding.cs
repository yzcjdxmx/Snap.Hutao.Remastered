// Licensed under the MIT license.
namespace Snap.Hutao.Remastered.Service.Game;

internal static class CombineHotkeyBinding
{
    // Preserve the native ABI: key in low WORD, modifier selector in high WORD.
    // VK__none_ is 255 in this project.
    public static int Encode(int key, int modifier)
    {
        return key is <= 0 or >= 255 || modifier is < 0 or > 3 ? 0 : key | (modifier << 16);
    }
}

