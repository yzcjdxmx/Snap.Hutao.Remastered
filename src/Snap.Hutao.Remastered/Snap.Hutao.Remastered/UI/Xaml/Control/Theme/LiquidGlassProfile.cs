// Copyright (c) Millennium-Science-Technology-R-D-Inst. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.Mvvm.ComponentModel;
using Snap.Hutao.Remastered.Core.Setting;
using Snap.Hutao.Remastered.Service;
using System.Diagnostics;
using System.Globalization;
using WinUI.Composition.Hlsl;

namespace Snap.Hutao.Remastered.UI.Xaml.Control.Theme;

internal enum LiquidGlassApplyResult
{
    Applied,
    Recovered,
    Unsupported,
}

public sealed partial class LiquidGlassParameter : ObservableObject
{
    private readonly LiquidGlassProfile profile;
    private readonly LiquidGlassDescriptor descriptor;
    private string? errorMessage;

    internal LiquidGlassParameter(LiquidGlassProfile profile, LiquidGlassDescriptor descriptor, double defaultValue)
    {
        this.profile = profile;
        this.descriptor = descriptor;
        DefaultValue = defaultValue;
    }

    public string Key => descriptor.Key;
    public string Label => LiquidGlassOptics.Text("GlassParameter" + Key, descriptor.EnglishLabel);
    public double DefaultValue { get; }
    public double Minimum => Math.Min(descriptor.Minimum, DefaultValue);
    public double Maximum => Math.Max(descriptor.Maximum, DefaultValue);
    public double Step => descriptor.Step;
    public double Value
    {
        get => profile.ReadOverride(Key);
        set
        {
            if (value == Value) return;
            try
            {
                profile.WriteOverride(this, value);
                SetError(null);
            }
            catch (ArgumentException exception)
            {
                Reject(exception);
                LiquidGlassProfiles.ReportFailure(exception,
                    FormattableString.Invariant($"Profile={profile.Key}; Parameter={Key}; RejectedInput={value:R}"));
            }
            Refresh();
        }
    }

    public string DisplayValue => Value.ToString("0.00", CultureInfo.CurrentCulture);
    public string DefaultText => DefaultValue.ToString("0.00", CultureInfo.CurrentCulture);
    public bool IsDefault => Value == DefaultValue;
    public string? ErrorMessage => errorMessage;
    public bool HasError => errorMessage is not null;
    public void Reset()
    {
        SetError(null);
        Value = DefaultValue;
    }

    internal void Reject(Exception exception) => SetError(exception.Message);

    private void SetError(string? message)
    {
        if (errorMessage == message) return;
        errorMessage = message;
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(HasError));
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(IsDefault));
    }
}

public sealed partial class LiquidGlassProfile : ObservableObject
{
    private const double DefaultSwitchScale = 0.7;

    private readonly LiquidGlassOverrideStore overrides;
    private string persistedOverrides;
    private bool initializing = true;
    private string StorageKey => "LiquidGlass.Profiles.v1." + Key;

    internal LiquidGlassProfile(string key)
    {
        Key = key;
        LiquidGlassBrush defaults = LiquidGlassOptics.Defaults(key);
        Parameters = LiquidGlassOptics.Descriptors.Select(d => new LiquidGlassParameter(this, d, d.Read(defaults))).ToArray();
        persistedOverrides = LocalSetting.Get(StorageKey, "{}");
        overrides = new(persistedOverrides,
            Parameters.Select(static parameter => new LiquidGlassParameterRange(
                parameter.Key, parameter.Minimum, parameter.Maximum, parameter.DefaultValue)),
            (exception, parameter, value) =>
            {
                Parameters.FirstOrDefault(item => item.Key == parameter)?.Reject(exception);
                LiquidGlassProfiles.ReportFailure(exception,
                    FormattableString.Invariant($"Profile={Key}; Parameter={parameter}; RejectedSavedValue={value:R}"));
            });
        // Establish the saved baseline through the real native setters before
        // it can be used to recover a later edit. Do not commit from Apply itself.
        Apply(defaults, LiquidGlassOptics.Capture(defaults));
        initializing = false;
        CommitChanges();
    }

    public string Key { get; }
    public string Name => LiquidGlassOptics.Text("GlassProfile" + Key, Key);
    public LiquidGlassParameter[] Parameters { get; }
    public bool Enabled
    {
        get => MaterialBindingSource.Current.GetTypeEnabled(Key);
        set
        {
            if (Enabled != value) MaterialBindingSource.Current.SetTypeEnabled(Key, value);
        }
    }

    public bool IsGlassActive => MaterialBindingSource.Current.Value == 1 && Enabled;

    public bool SupportsSwitchScale => Key == "ToggleSwitch";
    public double SwitchScale
    {
        get
        {
            double value = LocalSetting.Get(StorageKey + ".SwitchScale", DefaultSwitchScale);
            return double.IsFinite(value) && value >= 0.25 && value <= 2.0 ? value : DefaultSwitchScale;
        }
        set
        {
            if (!SupportsSwitchScale || !double.IsFinite(value) || value < 0.25 || value > 2.0 || SwitchScale == value) return;
            LocalSetting.Set(StorageKey + ".SwitchScale", value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SwitchScaleDisplayValue));
            ControlMaterialState.RefreshManagedInteractions();
        }
    }

    public string SwitchScaleDisplayValue => SwitchScale.ToString("0.00", CultureInfo.CurrentCulture);
    public string SwitchScaleDefaultText => DefaultSwitchScale.ToString("0.00", CultureInfo.CurrentCulture);
    public void ResetSwitchScale() => SwitchScale = DefaultSwitchScale;

    public bool PointerLightingEnabled
    {
        get => LocalSetting.Get(StorageKey + ".PointerLightingEnabled", true);
        set
        {
            if (PointerLightingEnabled == value) return;
            LocalSetting.Set(StorageKey + ".PointerLightingEnabled", value);
            OnPropertyChanged();
            ControlMaterialState.RefreshManagedInteractions();
        }
    }

    public double PointerLightInfluence
    {
        get => LocalSetting.Get(StorageKey + ".PointerLightInfluence", 1.0);
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 1 || PointerLightInfluence == value) return;
            LocalSetting.Set(StorageKey + ".PointerLightInfluence", value);
            OnPropertyChanged();
            ControlMaterialState.RefreshManagedInteractions();
        }
    }
    internal double ReadOverride(string key) => overrides.Get(key);
    internal void WriteOverride(LiquidGlassParameter parameter, double value)
    {
        if (overrides.Stage(parameter.Key, value)) LiquidGlassProfiles.RefreshOptics();
    }

    internal long Revision => overrides.Revision;

    internal void ValidatePendingChanges()
    {
        if (!overrides.HasPendingChanges) return;
        LiquidGlassBrush brush = LiquidGlassOptics.Defaults(Key);
        if (Apply(brush, LiquidGlassOptics.Capture(brush)) == LiquidGlassApplyResult.Unsupported)
            throw new InvalidOperationException("The liquid glass library rejected its own preset.");
    }

    internal void CommitChanges()
    {
        string serialized = overrides.Serialize();
        if (serialized != persistedOverrides)
        {
            LocalSetting.Set(StorageKey, serialized);
            persistedOverrides = serialized;
        }

        overrides.Commit();
    }

    private void PersistCommittedRecovery()
    {
        if (initializing) return;
        string serialized = overrides.SerializeCommitted();
        if (serialized == persistedOverrides) return;
        LocalSetting.Set(StorageKey, serialized);
        persistedOverrides = serialized;
    }

    internal void RollbackChanges()
    {
        overrides.Rollback();
        foreach (LiquidGlassParameter parameter in Parameters) parameter.Refresh();
    }

    public void Reset()
    {
        ResetSwitchScale();
        PointerLightingEnabled = true;
        PointerLightInfluence = 1.0;
        overrides.Reset();
        LiquidGlassProfiles.RefreshOptics();
        foreach (LiquidGlassParameter parameter in Parameters) parameter.Refresh();
    }

    internal LiquidGlassApplyResult Apply(LiquidGlassBrush brush, double[] defaults)
    {
        // Restore only optical slots. The actual library control still owns
        // geometry, corner radius, DPI and interaction animation.
        LiquidGlassApplyResult result = LiquidGlassApplyResult.Applied;
        for (int index = 0; index < LiquidGlassOptics.Descriptors.Length; index++)
        {
            LiquidGlassDescriptor descriptor = LiquidGlassOptics.Descriptors[index];
            LiquidGlassParameter parameter = Parameters[index];
            long revision = overrides.Revision;
            double cachedDefault = defaults[index];
            LiquidGlassApplyResult parameterResult = ApplyParameter(brush, descriptor, parameter, cachedDefault);
            if (parameterResult > result) result = parameterResult;
            if (overrides.Revision != revision) parameter.Refresh();
        }

        // A library that rejects even its own preset must leave this brush on fallback.
        if (result == LiquidGlassApplyResult.Unsupported) brush.IsEnabled = false;
        return result;
    }

    private LiquidGlassApplyResult ApplyParameter(LiquidGlassBrush brush, LiquidGlassDescriptor descriptor,
        LiquidGlassParameter parameter, double cachedDefault)
    {
        double value = overrides.Get(descriptor.Key);
        double effective = value == parameter.DefaultValue ? cachedDefault : value;
        bool recovered = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // Keep native validation intact. A rejected setter throws before
                // changing its property; never clamp or replace the attempted value first.
                descriptor.Write(brush, effective);
                return recovered ? LiquidGlassApplyResult.Recovered : LiquidGlassApplyResult.Applied;
            }
            catch (Exception exception) when (exception.HResult == unchecked((int)0x80070057))
            {
                recovered = true;
                parameter.Reject(exception);
                LiquidGlassProfiles.ReportFailure(exception,
                    FormattableString.Invariant($"Profile={Key}; Parameter={descriptor.Key}; RejectedValue={effective:R}; Override={value:R}; Preset={parameter.DefaultValue:R}; CachedDefault={cachedDefault:R}"));
                if (value == parameter.DefaultValue && effective == parameter.DefaultValue)
                    return LiquidGlassApplyResult.Unsupported;
                overrides.RestoreRejected(descriptor.Key);
                // Persist only the corrected baseline, never other pending edits.
                PersistCommittedRecovery();
                value = overrides.Get(descriptor.Key);
                effective = value;
            }
        }

        return LiquidGlassApplyResult.Unsupported;
    }

    internal void RefreshSelection()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(IsGlassActive));
    }
}

public static class LiquidGlassProfiles
{
    private static LiquidGlassProfile[]? profiles;
    private static bool refreshPending;
    public static LiquidGlassProfile[] All => profiles ??= [
        new("Button"),
        new("ToggleButton"),
        new("HyperlinkButton"),
        new("CheckBox"),
        new("RadioButton"),
        new("Slider"),
        new("TextBox"),
        new("SearchBox"),
        new("PasswordBox"),
        new("ComboBox"),
        new("ToggleSwitch"),
        new("Card"),
        new("FloatingPanel"),
        new("TabBar"),
    ];
    public static event EventHandler? SelectionChanged;

    public static void SetMaterial(int index)
    {
        if (index is not (0 or 1)) return;
        AppOptions options = Ioc.Default.GetRequiredService<AppOptions>();
        ControlMaterial value = (ControlMaterial)index;
        if (options.ControlMaterial.Value == value) return;
        options.ControlMaterial.Value = value;
        MaterialTheme.Apply(value);
    }

    internal static LiquidGlassProfile Get(string kind) => All.Single(profile => profile.Key == kind);
    internal static void NotifySelectionChanged()
    {
        if (profiles is not null)
            foreach (LiquidGlassProfile profile in profiles) profile.RefreshSelection();
        SelectionChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void RefreshOptics()
    {
        if (refreshPending || XamlApplicationLifetime.Exiting) return;
        refreshPending = true;
        try
        {
            if (MaterialBindingSource.Current.DispatcherQueue.TryEnqueue(RefreshOpticsCallback)) return;
        }
        catch (Exception exception)
        {
            ReportFailure(exception, "Enqueue optical refresh");
        }

        refreshPending = false;
        RollbackChanges();
    }

    private static void RefreshOpticsCallback()
    {
        try
        {
            if (XamlApplicationLifetime.Exiting)
            {
                RollbackChanges();
                return;
            }

            foreach (LiquidGlassProfile profile in All) profile.ValidatePendingChanges();
            // Recovery can change a shared profile after an earlier brush was updated.
            // Reapply until all brushes agree before persisting any remaining edits.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                long[] revisions = All.Select(static profile => profile.Revision).ToArray();
                ControlMaterialState.RefreshManagedOptics();
                if (!revisions.SequenceEqual(All.Select(static profile => profile.Revision))) continue;
                foreach (LiquidGlassProfile profile in All) profile.CommitChanges();
                return;
            }

            throw new InvalidOperationException("Liquid glass parameters did not stabilize after recovery.");
        }
        catch (Exception exception)
        {
            ReportFailure(exception, "Apply optical refresh");
            RollbackChanges();
            try
            {
                if (!XamlApplicationLifetime.Exiting) ControlMaterialState.RefreshManagedOptics();
            }
            catch (Exception recoveryException)
            {
                ReportFailure(recoveryException, "Restore optical refresh");
            }
        }
        finally
        {
            refreshPending = false;
        }
    }

    private static void RollbackChanges()
    {
        if (profiles is null) return;
        foreach (LiquidGlassProfile profile in profiles)
        {
            try
            {
                profile.RollbackChanges();
            }
            catch (Exception exception)
            {
                ReportFailure(exception, "Rollback optical profile " + profile.Key);
            }
        }
    }

    internal static void ReportFailure(Exception exception, string context)
    {
        // Diagnostics are also inside the native callback boundary and must not throw.
        try
        {
            Trace.TraceError($"Liquid glass failure: {context}; HRESULT={exception.HResult:X8}; {exception}");
        }
        catch (Exception)
        {
        }

        try
        {
            SentrySdk.CaptureException(exception, scope => scope.SetExtra("LiquidGlass.Context", context));
        }
        catch (Exception)
        {
        }
    }
}
