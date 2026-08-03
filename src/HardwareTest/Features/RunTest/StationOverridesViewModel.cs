using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Engineer/Debug station overrides: editable step parameters, saved overrides and the debug overlay.
public partial class StationOverridesViewModel : ReactiveObject
{
    private const int MaxDebugSampleCount = 4096;
    private const int MinDebugIntervalMs = 1;

    private readonly IOpenTapPlanSession _plan;
    private readonly IOpenTapStationSession _station;
    private readonly AppSettings _settings;
    private readonly ISettingsStore? _settingsStore;
    private readonly Action<string> _setStatus;
    private readonly Func<bool> _isEngineerDebugMode;
    private readonly Func<bool> _isRunning;
    private readonly Func<HierarchyStepViewModel?> _getSelectedStep;
    private readonly Func<ProgramItemViewModel?> _getSelectedProgram;
    private readonly Action<string> _setConditionSummary;

    public StationOverridesViewModel(
        IOpenTapPlanSession plan,
        IOpenTapStationSession station,
        AppSettings settings,
        ISettingsStore? settingsStore,
        Action<string> setStatus,
        Func<bool>? isEngineerDebugMode = null,
        Func<bool>? isRunning = null,
        Func<HierarchyStepViewModel?>? getSelectedStep = null,
        Func<ProgramItemViewModel?>? getSelectedProgram = null,
        Action<string>? setConditionSummary = null)
    {
        _plan = plan;
        _station = station;
        _settings = settings;
        _settingsStore = settingsStore;
        _setStatus = setStatus;
        _isEngineerDebugMode = isEngineerDebugMode ?? (() => false);
        _isRunning = isRunning ?? (() => false);
        _getSelectedStep = getSelectedStep ?? (() => null);
        _getSelectedProgram = getSelectedProgram ?? (() => null);
        _setConditionSummary = setConditionSummary ?? (_ => { });

        ApplyDebugPatchCommand = ReactiveCommand.Create(ApplyDebugPatch);
        ApplyParametersCommand = ReactiveCommand.CreateFromTask(ApplyParametersAsync);
    }

    public ObservableCollection<InteractionFieldViewModel> ParameterFields { get; } = [];

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplyDebugPatchCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ApplyParametersCommand { get; }

    [Reactive] private bool _hasParameterFields;
    [Reactive] private string _debugResource = "MOCK::INSTR0";
    [Reactive] private int _debugSampleCount = 32;
    [Reactive] private int _debugIntervalMs = 5;
    [Reactive] private double _debugThreshold;
    [Reactive] private bool _debugStepEnabled = true;
    [Reactive] private string _stationSlotSummary = "Station: (load program)";

    public void RefreshStationSlotSummary()
        => StationSlotSummary = _station.InstrumentSlots.Count == 0
            ? "Station: (no OpenTAP instruments)"
            : "Station: " + string.Join(", ", _station.InstrumentSlots.Select(s => $"{s.Name}→{s.ResourceName}"));

    public void RefreshParameterFields()
    {
        ParameterFields.Clear();
        var step = _getSelectedStep();
        if (!_isEngineerDebugMode() || step is null)
        {
            HasParameterFields = false;
            return;
        }

        var parameters = _station.EnumerateParameters(
            OpenTapParameterScope.Step,
            step.Path,
            includeReadOnly: true,
            listing: OpenTapParameterListing.StationOverrides);
        foreach (var parameter in parameters)
        {
            ParameterFields.Add(new InteractionFieldViewModel(
                new OperatorInteractionField
                {
                    Id = parameter.MemberKey,
                    Label = string.IsNullOrWhiteSpace(parameter.Group)
                        ? parameter.DisplayName
                        : $"{parameter.Group}: {parameter.DisplayName}",
                    Kind = parameter.Kind,
                    DefaultValue = parameter.Value,
                },
                isReadOnly: parameter.IsReadOnly || _isRunning()));
        }

        HasParameterFields = ParameterFields.Count > 0;
    }

    /// Re-applies the persisted station overrides for the selected plan onto a freshly loaded plan.
    public void ApplySavedParameterOverrides()
    {
        var program = _getSelectedProgram();
        if (program is null)
        {
            return;
        }

        var planId = program.Id;
        foreach (var ov in _settings.PlanParameterOverrides.Where(o =>
                     string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(o.MemberKey)))
        {
            _station.TrySetParameter(ov.MemberKey, ov.Value ?? string.Empty);
        }
    }

    /// Resolves the role/slot → resource map for this station, falling back to legacy bindings.
    public StationProfile BuildStationProfile()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var planId = _getSelectedProgram()?.Id ?? string.Empty;
        foreach (var ov in _settings.PlanSlotOverrides.Where(o =>
                     string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
                     || string.IsNullOrWhiteSpace(planId)))
        {
            if (string.IsNullOrWhiteSpace(ov.Resource))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ov.RoleHint))
            {
                map[ov.RoleHint] = ov.Resource;
            }

            if (!string.IsNullOrWhiteSpace(ov.SlotName))
            {
                map[ov.SlotName] = ov.Resource;
            }
        }

        // Legacy fallback: station bindings → instrument registry resources.
        if (map.Count == 0)
        {
            foreach (var b in _settings.StationBindings)
            {
                var instr = _settings.Instruments.FirstOrDefault(i =>
                    string.Equals(i.Id, b.InstrumentId, StringComparison.OrdinalIgnoreCase));
                if (instr is not null && !string.IsNullOrWhiteSpace(b.Role))
                {
                    map[b.Role] = instr.Resource;
                }
            }
        }

        return new StationProfile(map);
    }

    public void ApplyDebugPatch()
    {
        var step = _getSelectedStep();
        if (!_isEngineerDebugMode() || step is null)
        {
            _setStatus("Select a step in Engineer/Debug mode.");
            return;
        }

        ClampDebugKnobs();
        _plan.TrySetStepEnabled(step.Path, DebugStepEnabled);
        step.Enabled = DebugStepEnabled;
        _station.TrySetAcquireSettings(step.Path, DebugSampleCount, DebugIntervalMs);
        _station.TrySetMeanGteThreshold(step.Path, DebugThreshold);
        _station.TryRebindDmmResource(DebugResource);
        RefreshParameterFields();
        _setStatus($"Applied debug overlay to {step.Name} (not saved to golden plan).");
    }

    private async Task ApplyParametersAsync()
    {
        if (!_isEngineerDebugMode())
        {
            _setStatus("Parameter overrides require Engineer/Debug mode.");
            return;
        }

        var program = _getSelectedProgram();
        if (program is null)
        {
            _setStatus("Select a program.");
            return;
        }

        if (ParameterFields.Count == 0)
        {
            _setStatus("Select a step with editable parameters.");
            return;
        }

        var planId = program.Id;
        var applied = 0;
        foreach (var field in ParameterFields.Where(f => !f.IsReadOnly))
        {
            var value = field.ToResponseValue();
            if (!_station.TrySetParameter(field.Id, value))
            {
                _setStatus($"Could not set {field.Label}.");
                return;
            }

            UpsertParameterOverride(planId, field.Id, value);
            applied++;
        }

        if (_settingsStore is not null)
        {
            await _settingsStore.SaveAppSettingsAsync().ConfigureAwait(false);
        }

        var step = _getSelectedStep();
        if (step is not null
            && _plan.TryGetStepConditionSummary(step.Path, out var summary)
            && !string.IsNullOrWhiteSpace(summary))
        {
            _setConditionSummary(summary!);
        }

        _setStatus(applied == 0
            ? "No writable parameters to apply."
            : $"Applied {applied} parameter(s) for {program.DisplayName} (station override; TapPlan unchanged).");
    }

    private void UpsertParameterOverride(string planId, string memberKey, string value)
    {
        var existing = _settings.PlanParameterOverrides.FirstOrDefault(o =>
            string.Equals(o.PlanId, planId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(o.MemberKey, memberKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.PlanParameterOverrides.Add(new PlanParameterOverride
            {
                PlanId = planId,
                MemberKey = memberKey,
                Value = value,
            });
            return;
        }

        existing.Value = value;
    }

    private void ClampDebugKnobs()
    {
        DebugSampleCount = Math.Clamp(DebugSampleCount, 1, MaxDebugSampleCount);
        DebugIntervalMs = Math.Max(MinDebugIntervalMs, DebugIntervalMs);
    }
}
