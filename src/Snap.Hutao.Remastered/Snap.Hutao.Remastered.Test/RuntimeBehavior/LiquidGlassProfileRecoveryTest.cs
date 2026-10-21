using Snap.Hutao.Remastered.Core.Setting;
using Snap.Hutao.Remastered.UI.Xaml.Control.Theme;
using System.Reflection;
using WinUI.Composition.Hlsl;

namespace Snap.Hutao.Remastered.Test.RuntimeBehavior;

[TestClass]
[DoNotParallelize]
public sealed class LiquidGlassProfileRecoveryTest
{
    private const string StorageKey = "LiquidGlass.Profiles.v1.Button";

    [TestInitialize]
    public void Initialize()
    {
        typeof(LiquidGlassProfiles).GetField("profiles", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
        typeof(LiquidGlassProfiles).GetField("refreshPending", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
        LocalSetting.Values.Clear();
        LocalSetting.BeforeWrite = null;
        LiquidGlassOptics.BeforeWrite = null;
        LiquidGlassOptics.Descriptors[0] = LiquidGlassOptics.Descriptors[0] with { Minimum = 0.25 };
        ControlMaterialState.Refresh = null;
        XamlApplicationLifetime.Exiting = false;
        MaterialBindingSource.Current = new();
        LiquidGlassTestTelemetry.Exceptions.Clear();
        LiquidGlassTestTelemetry.ThrowOnCapture = false;
    }

    [TestMethod]
    public void ValueIsSavedOnlyAfterQueuedApplicationSucceeds()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush brush = new();
        ControlMaterialState.Refresh = () => profile.Apply(brush, [1]);
        LocalSetting.BeforeWrite = (key, _) =>
        {
            if (key == StorageKey) Assert.AreEqual(8D, brush.SpecularWidth);
        };
        profile.Parameters[0].Value = 8;
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(8D, JsonSerializer.Deserialize<Dictionary<string, double>>((string)LocalSetting.Values[StorageKey])!["SpecularWidth"]);
    }

    [TestMethod]
    public void StartupRepairsRejectedPersistedParameterBeforeNextLaunch()
    {
        LocalSetting.Values[StorageKey] = "{\"SpecularWidth\":4}";
        LiquidGlassOptics.BeforeWrite = (_, value) =>
        {
            if (value == 4) throw new ArgumentException("Native rejection");
        };
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush brush = new();
        Assert.AreEqual(LiquidGlassApplyResult.Applied, profile.Apply(brush, [1]));
        Assert.AreEqual(1D, brush.SpecularWidth);
        Assert.AreEqual("{}", LocalSetting.Values[StorageKey]);
        Assert.AreEqual(1D, new LiquidGlassProfile("Button").Parameters[0].Value);
    }

    [TestMethod]
    public void LaterBrushRejectionRestoresEarlierBrushBeforeCommit()
    {
        LocalSetting.Values[StorageKey] = "{\"SpecularWidth\":4}";
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush first = new();
        LiquidGlassBrush second = new();
        LiquidGlassOptics.BeforeWrite = (brush, value) =>
        {
            if (ReferenceEquals(brush, second) && value == 8) throw new ArgumentException("Second brush rejected edit");
        };
        ControlMaterialState.Refresh = () =>
        {
            profile.Apply(first, [1]);
            profile.Apply(second, [1]);
        };
        profile.Parameters[0].Value = 8;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(4D, profile.Parameters[0].Value);
        Assert.AreEqual(4D, first.SpecularWidth);
        Assert.AreEqual(4D, second.SpecularWidth);
        Assert.AreEqual(4D, new LiquidGlassProfile("Button").Parameters[0].Value);
    }

    [TestMethod]
    public void UnexpectedCallbackFailureDoesNotEscapeAndNextEditStillRuns()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        ControlMaterialState.Refresh = () => throw new InvalidOperationException("Refresh failed");
        profile.Parameters[0].Value = 8;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(1D, profile.Parameters[0].Value);
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
        LiquidGlassBrush brush = new();
        ControlMaterialState.Refresh = () => profile.Apply(brush, [1]);
        profile.Parameters[0].Value = 6;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(6D, brush.SpecularWidth);
        Assert.AreEqual(6D, new LiquidGlassProfile("Button").Parameters[0].Value);
    }

    [TestMethod]
    public void EnqueueFailureRollsBackUnappliedEdit()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        MaterialBindingSource.Current.DispatcherQueue.AcceptWork = false;
        profile.Parameters[0].Value = 8;
        Assert.AreEqual(1D, profile.Parameters[0].Value);
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
        MaterialBindingSource.Current.DispatcherQueue.AcceptWork = true;
        profile.Parameters[0].Value = 6;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(6D, new LiquidGlassProfile("Button").Parameters[0].Value);
    }

    [TestMethod]
    public void TelemetryFailureCannotEscapeNativeCallbackBoundary()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassTestTelemetry.ThrowOnCapture = true;
        ControlMaterialState.Refresh = () => throw new InvalidOperationException("Refresh failed");
        profile.Parameters[0].Value = 8;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(1D, profile.Parameters[0].Value);
    }

    [TestMethod]
    public void PersistenceFailureRestoresPreviouslyAppliedValue()
    {
        LocalSetting.Values[StorageKey] = "{\"SpecularWidth\":4}";
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush brush = new();
        ControlMaterialState.Refresh = () => profile.Apply(brush, [1]);
        LocalSetting.BeforeWrite = (key, _) =>
        {
            if (key == StorageKey) throw new InvalidOperationException("Disk write failed");
        };
        profile.Parameters[0].Value = 8;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(4D, brush.SpecularWidth);
        Assert.AreEqual(4D, profile.Parameters[0].Value);
        Assert.AreEqual("{\"SpecularWidth\":4}", LocalSetting.Values[StorageKey]);
    }

    [TestMethod]
    public void UnsupportedPresetLeavesBrushOnFallback()
    {
        LiquidGlassOptics.BeforeWrite = (_, _) => throw new ArgumentException("Preset rejected");
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush brush = new();
        Assert.AreEqual(LiquidGlassApplyResult.Unsupported, profile.Apply(brush, [1]));
        Assert.IsFalse(brush.IsEnabled);
    }

    [TestMethod]
    public void ShutdownDoesNotCommitQueuedEdit()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        profile.Parameters[0].Value = 8;
        XamlApplicationLifetime.Exiting = true;
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(1D, profile.Parameters[0].Value);
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
    }

    [TestMethod]
    public void NativeRejectionIsActuallyThrownCaughtAndShownWithoutSavingCandidate()
    {
        LiquidGlassOptics.Descriptors[0] = LiquidGlassOptics.Descriptors[0] with { Minimum = 0.1 };
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        LiquidGlassBrush activeBrush = new();
        ArgumentException nativeFailure = new("SpecularWidth must be between 0.25 and 32 DIPs.");
        int nativeThrows = 0;
        LiquidGlassOptics.BeforeWrite = (_, value) =>
        {
            if (value >= 0.25) return;
            nativeThrows++;
            throw nativeFailure;
        };
        ControlMaterialState.Refresh = () => profile.Apply(activeBrush, [1]);
        profile.Parameters[0].Value = 0.1;
        Assert.AreEqual(0, nativeThrows);
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(1, nativeThrows);
        Assert.IsTrue(LiquidGlassTestTelemetry.Exceptions.Any(exception => ReferenceEquals(exception, nativeFailure)));
        Assert.AreEqual(nativeFailure.Message, profile.Parameters[0].ErrorMessage);
        Assert.IsTrue(profile.Parameters[0].HasError);
        Assert.AreEqual(1D, profile.Parameters[0].Value);
        Assert.AreEqual(1D, activeBrush.SpecularWidth);
        Assert.IsFalse(((string?)LocalSetting.Values.GetValueOrDefault(StorageKey))?.Contains("0.1", StringComparison.Ordinal) ?? false);
    }

    [TestMethod]
    public void InvalidInputThrowsAndIsCapturedInsteadOfSilentlyReturning()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        profile.Parameters[0].Value = 99;
        Assert.AreEqual(1D, profile.Parameters[0].Value);
        Assert.IsTrue(profile.Parameters[0].HasError);
        Assert.IsTrue(LiquidGlassTestTelemetry.Exceptions.Any(static exception => exception is ArgumentOutOfRangeException));
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
    }

    [TestMethod]
    public void InvalidCachedDefaultIsRejectedByNativeSetterBeforeRecovery()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        ArgumentException nativeFailure = new("Invalid cached native value");
        int nativeThrows = 0;
        LiquidGlassOptics.BeforeWrite = (_, value) =>
        {
            if (value >= 0.25) return;
            nativeThrows++;
            throw nativeFailure;
        };
        LiquidGlassBrush brush = new();
        Assert.AreEqual(LiquidGlassApplyResult.Recovered, profile.Apply(brush, [0.1]));
        Assert.AreEqual(1, nativeThrows);
        Assert.IsTrue(LiquidGlassTestTelemetry.Exceptions.Any(exception => ReferenceEquals(exception, nativeFailure)));
        Assert.AreEqual(1D, brush.SpecularWidth);
        Assert.IsTrue(profile.Parameters[0].HasError);
    }

    [TestMethod]
    public void DirectApplyDoesNotCommitAnotherPendingEdit()
    {
        LiquidGlassProfile profile = LiquidGlassProfiles.All[0];
        profile.Parameters[0].Value = 8;
        Assert.AreEqual(LiquidGlassApplyResult.Applied, profile.Apply(new(), [1]));
        Assert.IsFalse(LocalSetting.Values.ContainsKey(StorageKey));
        MaterialBindingSource.Current.DispatcherQueue.Drain();
        Assert.AreEqual(8D, new LiquidGlassProfile("Button").Parameters[0].Value);
    }
}
