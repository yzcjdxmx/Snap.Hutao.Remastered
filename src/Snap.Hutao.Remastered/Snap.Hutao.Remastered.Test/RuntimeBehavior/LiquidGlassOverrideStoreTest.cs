using Snap.Hutao.Remastered.UI.Xaml.Control.Theme;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
public sealed class LiquidGlassOverrideStoreTest
{
    private static readonly LiquidGlassParameterRange[] Ranges = [new("BlurRadius", 0, 32, 18)];

    [TestMethod]
    public void CustomValueSurvivesSerializationAndReload()
    {
        LiquidGlassOverrideStore store = new("{}", Ranges);
        Assert.IsTrue(store.TrySet("BlurRadius", 24));
        LiquidGlassOverrideStore restored = new(store.Serialize(), Ranges);
        Assert.AreEqual(24D, restored.Get("BlurRadius"));
    }

    [TestMethod]
    public void ResetIsLocalToThisControlType()
    {
        LiquidGlassOverrideStore button = new("{}", Ranges);
        LiquidGlassOverrideStore slider = new("{}", Ranges);
        button.TrySet("BlurRadius", 22);
        slider.TrySet("BlurRadius", 26);
        button.Reset();
        Assert.AreEqual(18D, button.Get("BlurRadius"));
        Assert.AreEqual(26D, slider.Get("BlurRadius"));
        Assert.AreEqual("{}", button.Serialize());
    }

    [TestMethod]
    public void NonFiniteOutOfRangeAndUnknownValuesAreRejected()
    {
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":99,\"Unknown\":1}", Ranges);
        Assert.AreEqual(18D, store.Get("BlurRadius"));
        Assert.IsFalse(store.TrySet("BlurRadius", double.NaN));
        Assert.IsFalse(store.TrySet("BlurRadius", double.PositiveInfinity));
        Assert.IsFalse(store.TrySet("BlurRadius", -1));
        Assert.IsFalse(store.TrySet("Unknown", 1));
    }

    [TestMethod]
    public void DefaultValueRemovesOnlyItsOverrideAndCorruptDataIsSafe()
    {
        LiquidGlassOverrideStore store = new("invalid json", Ranges);
        Assert.AreEqual(18D, store.Get("BlurRadius"));
        store.TrySet("BlurRadius", 22);
        store.TrySet("BlurRadius", 18);
        Assert.AreEqual("{}", store.Serialize());
    }

    [TestMethod]
    public void PendingEditsRollbackToLastCommittedValue()
    {
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", Ranges);
        store.TrySet("BlurRadius", 24);
        Assert.IsTrue(store.HasPendingChanges);
        store.Rollback();
        Assert.AreEqual(22D, store.Get("BlurRadius"));
        Assert.IsFalse(store.HasPendingChanges);
        store.TrySet("BlurRadius", 26);
        store.Commit();
        store.TrySet("BlurRadius", 28);
        store.Rollback();
        Assert.AreEqual(26D, store.Get("BlurRadius"));
    }

    [TestMethod]
    public void RejectedEditRestoresCommittedValue()
    {
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", Ranges);
        store.TrySet("BlurRadius", 24);
        Assert.IsTrue(store.RestoreRejected("BlurRadius"));
        Assert.AreEqual(22D, store.Get("BlurRadius"));
        Assert.IsFalse(store.HasPendingChanges);
    }

    [TestMethod]
    public void RejectedSavedValueIsRemovedAndCannotReturnOnRollbackOrReload()
    {
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", Ranges);
        Assert.IsTrue(store.RestoreRejected("BlurRadius"));
        Assert.AreEqual("{}", store.Serialize());
        store.TrySet("BlurRadius", 24);
        store.Rollback();
        Assert.AreEqual(18D, store.Get("BlurRadius"));
        Assert.AreEqual(18D, new LiquidGlassOverrideStore(store.Serialize(), Ranges).Get("BlurRadius"));
    }

    [TestMethod]
    public void RejectingCandidateThenSavedValueRemovesBoth()
    {
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", Ranges);
        store.TrySet("BlurRadius", 24);
        Assert.IsTrue(store.RestoreRejected("BlurRadius"));
        Assert.AreEqual(22D, store.Get("BlurRadius"));
        Assert.IsTrue(store.RestoreRejected("BlurRadius"));
        Assert.AreEqual(18D, store.Get("BlurRadius"));
        Assert.AreEqual("{}", store.Serialize());
    }

    [TestMethod]
    public void InvalidStagedValuesThrowWithoutChangingState()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1D, 99D })
        {
            LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", Ranges);
            ArgumentOutOfRangeException? captured = null;
            try
            {
                store.Stage("BlurRadius", invalid);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                captured = exception;
            }
            Assert.IsNotNull(captured);
            Assert.AreEqual(22D, store.Get("BlurRadius"));
            Assert.IsFalse(store.HasPendingChanges);
        }
    }

    [TestMethod]
    public void InvalidSavedValueProducesCapturedException()
    {
        List<Exception> captured = [];
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":99}", Ranges, (exception, key, value) =>
        {
            captured.Add(exception);
            Assert.AreEqual("BlurRadius", key);
            Assert.AreEqual(99D, value);
        });
        Assert.HasCount(1, captured);
        Assert.IsInstanceOfType<ArgumentOutOfRangeException>(captured[0]);
        Assert.AreEqual("{}", store.Serialize());
    }

    [TestMethod]
    public void RejectedPresetCannotGenerateEndlessStateChanges()
    {
        LiquidGlassOverrideStore store = new("{}", Ranges);
        long revision = store.Revision;
        Assert.IsFalse(store.RestoreRejected("BlurRadius"));
        Assert.AreEqual(revision, store.Revision);
        Assert.AreEqual("{}", store.Serialize());
    }

    [TestMethod]
    public void CommittedSnapshotDoesNotContainOtherPendingEdits()
    {
        LiquidGlassParameterRange[] ranges = [new("BlurRadius", 0, 32, 18), new("SpecularWidth", 0.25, 32, 1)];
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22}", ranges);
        store.Stage("SpecularWidth", 8);
        store.RestoreRejected("BlurRadius");
        Assert.AreEqual("{}", store.SerializeCommitted());
        Assert.AreEqual(8D, store.Get("SpecularWidth"));
        store.Rollback();
        Assert.AreEqual(1D, store.Get("SpecularWidth"));
    }

    [TestMethod]
    public void RecoveryPreservesOtherParameterOverrides()
    {
        LiquidGlassParameterRange[] ranges = [new("BlurRadius", 0, 32, 18), new("SpecularWidth", 0.25, 32, 1)];
        LiquidGlassOverrideStore store = new("{\"BlurRadius\":22,\"SpecularWidth\":4}", ranges);
        store.RestoreRejected("BlurRadius");
        Assert.AreEqual(4D, store.Get("SpecularWidth"));
    }
}
