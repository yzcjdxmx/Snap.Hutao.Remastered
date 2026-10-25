using Snap.Hutao.Remastered.Service.Game;

namespace Snap.Hutao.Remastered.Test.IncomingFeature;

[TestClass]
public sealed class CombineHotkeyBindingTest
{
    [TestMethod]
    [DataRow(0, 67)]
    [DataRow(1, 65603)]
    [DataRow(2, 131139)]
    [DataRow(3, 196675)]
    public void EncodesNativeKeyAndModifier(int modifier, int expected)
    {
        Assert.AreEqual(expected, CombineHotkeyBinding.Encode(67, modifier));
    }

    [TestMethod]
    [DataRow(255, 2)]
    [DataRow(0, 2)]
    [DataRow(67, -1)]
    [DataRow(67, 4)]
    public void DisabledOrInvalidBindingDoesNotTrigger(int key, int modifier)
    {
        Assert.AreEqual(0, CombineHotkeyBinding.Encode(key, modifier));
    }
}

