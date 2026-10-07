using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    private static void BindChannelProducers(TestPlan plan) => BindSequence(plan.ChildTestSteps);

    // Capture requires a preceding Sample publisher in the same actual execution scope.
    private static void BindSequence(IEnumerable<ITestStep> steps, bool bindProducer = true, string? onlyOutput = null, bool safeScope = true,
        IReadOnlyDictionary<string, string>? incompleteFields = null, IReadOnlyList<Guid>? scopeNodes = null,
        Action<double>? scalarExample = null, Guid? onlyNodeId = null)
    {
        var producers = new Dictionary<string, List<ITestStep>>(StringComparer.OrdinalIgnoreCase);
        var validations = new Dictionary<ITestStep, Action>(ReferenceEqualityComparer.Instance);
        var completed = new HashSet<ITestStep>(ReferenceEqualityComparer.Instance);
        foreach (var step in steps)
        {
            // Disabled imported scopes and their descendants never participate in execution.
            if (!step.Enabled) continue;
            Action? validate = null;
            string? output = null;
            if (step is ChannelAverageStep average)
            {
                output = average.Channel;
                var preceding = Snapshot(producers);
                validate = () =>
                {
                    if (completed.Contains(step)) return;
                    RequireCompleteInput(step.Id, incompleteFields);
                    foreach (var scope in scopeNodes ?? []) RequireCompleteInput(scope, incompleteFields);
                    RequireKnownScope(safeScope);
                    if (string.IsNullOrWhiteSpace(average.InputChannel))
                        throw new AuthoringWorkspaceException(AuthoringFunctionCatalog.InputChannelIssue(AuthoringFunctionIds.BasicChannelAverage, [])!);
                    if (scalarExample is not null && (onlyNodeId is { } scalarTarget ? step.Id == scalarTarget : string.Equals(output, onlyOutput, StringComparison.OrdinalIgnoreCase))
                        && preceding.TryGetValue(average.InputChannel, out var scalarMatches)
                        && scalarMatches is [PublishBandScalarStep scalar])
                    {
                        RequireCompleteInput(scalar.Id, incompleteFields);
                        if (!double.IsFinite(scalar.Value)) throw new AuthoringWorkspaceException("FORMULA_EVAL: Scalar preview example requires a finite value.");
                        scalarExample(scalar.Value);
                        completed.Add(step);
                        return;
                    }
                    RequireSampleInput(preceding, average.InputChannel, filter: false, out var matches);
                    ValidateDependencies(matches, validations, incompleteFields);
                    if (bindProducer)
                    {
                        average.ProducerStepId = matches[0].Id;
                        average.Unit = OpenTapPresentation.TryReadMixin(step)?.YUnit ?? string.Empty;
                        average.Channel = OpenTapPresentation.TryReadMixin(step)?.ChannelKey ?? average.Channel;
                    }
                    completed.Add(step);
                };
            }
            else if (step is ApplyTransferFunctionStep filter)
            {
                output = filter.Channel;
                var preceding = Snapshot(producers);
                validate = () =>
                {
                    if (completed.Contains(step)) return;
                    RequireCompleteInput(step.Id, incompleteFields);
                    foreach (var scope in scopeNodes ?? []) RequireCompleteInput(scope, incompleteFields);
                    RequireKnownScope(safeScope);
                    RequireSampleInput(preceding, filter.InputChannel, filter: true, out var matches);
                    ValidateDependencies(matches, validations, incompleteFields);
                    RequireFilterGrid(matches, filter);
                    completed.Add(step);
                };
            }
            if (validate is not null)
            {
                validations[step] = validate;
                if (onlyNodeId is { } validationTarget ? step.Id == validationTarget : onlyOutput is null || string.Equals(output, onlyOutput, StringComparison.OrdinalIgnoreCase)) validate();
            }
            if (step.ChildTestSteps.Count > 0) BindSequence(step.ChildTestSteps, bindProducer, onlyOutput,
                safeScope && (step is TestGroupStep or RepeatLoopStep), incompleteFields, [.. (scopeNodes ?? []), step.Id], scalarExample, onlyNodeId);
            var channel = DeclaredChannel(step);
            if (!string.IsNullOrWhiteSpace(channel))
            {
                if (!producers.TryGetValue(channel, out var list)) producers[channel] = list = [];
                list.Add(step);
            }
        }
    }

    private static Dictionary<string, List<ITestStep>> Snapshot(Dictionary<string, List<ITestStep>> producers)
        => producers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.OrdinalIgnoreCase);

    private static void ValidateDependencies(IReadOnlyList<ITestStep> producers, Dictionary<ITestStep, Action> validations,
        IReadOnlyDictionary<string, string>? incompleteFields)
    {
        foreach (var producer in producers)
        {
            RequireCompleteInput(producer.Id, incompleteFields);
            if (validations.TryGetValue(producer, out var validate)) validate();
        }
    }

    internal static void RequireCompleteInput(Guid nodeId, IReadOnlyDictionary<string, string>? incompleteFields)
    {
        var key = incompleteFields?.Keys.Order(StringComparer.Ordinal).FirstOrDefault(key => key.StartsWith($"{nodeId:D}/", StringComparison.Ordinal));
        if (key is not null)
            throw new AuthoringWorkspaceException($"BUILD_INCOMPLETE: Deployment field '{key[(key.IndexOf('/') + 1)..]}' on step '{nodeId:D}' contains incomplete numeric text. Repair this field before deploying.");
    }

    private static void RequireKnownScope(bool safeScope)
    {
        if (!safeScope) throw new AuthoringWorkspaceException("SAMPLE_SCOPE: unsupported execution boundary; place the producer and check in a Basic Test Group or Repeat Loop.");
    }

}
