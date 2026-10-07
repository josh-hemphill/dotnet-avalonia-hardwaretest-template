using HardwareTest.Core.Runs;

namespace HardwareTest.Authoring;

public static partial class BoardPreviewBuilder
{
    private static void BindPublisherGroups(string key, bool timed, IReadOnlyList<IReadOnlyList<StoredSample>> groups,
        TestRunRecord? recording, Dictionary<string, IReadOnlyList<IReadOnlyList<StoredSample>>> available)
    {
        if (timed && available.TryGetValue(key, out var preceding))
        {
            // Typed and raw scripted publishers form one grid per actual loop iteration.
            groups = preceding.Concat(groups).GroupBy(group => (group[0].LoopRunId, group[0].LoopPath, group[0].IterationIndex))
                .Select(iteration =>
                {
                    var members = iteration.SelectMany(group => group).ToHashSet();
                    // Runtime consumes published rows in order, even when their times are invalid.
                    var samples = recording is null ? iteration.SelectMany(group => group).ToArray()
                        : recording.Samples.Where(members.Contains).ToArray();
                    if (samples.GroupBy(sample => sample.ProducerStepId).Any(producer => producer.Select(sample => sample.StepRunId).Distinct().Count() > 1))
                    {
                        available[key] = [];
                        throw new AuthoringWorkspaceException("Ambiguous recording executions cannot form one input grid.");
                    }
                    return (IReadOnlyList<StoredSample>)samples;
                }).ToArray();
        }
        available[key] = groups;
    }
}
