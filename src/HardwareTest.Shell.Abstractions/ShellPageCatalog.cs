namespace HardwareTest.Shell;

/// Ordered shell pages with unique ids. Duplicate ids fail closed at construction.
public sealed class ShellPageCatalog
{
    public ShellPageCatalog(IEnumerable<ShellPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var list = pages.ToArray();
        var duplicate = list
            .GroupBy(page => page.Descriptor.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate shell page id '{duplicate.Key}'.");
        }

        Pages = list;
    }

    public IReadOnlyList<ShellPage> Pages { get; }

    /// Finds the page with <paramref name="pageId"/>, or null.
    public ShellPage? Find(string pageId)
        => Pages.FirstOrDefault(page =>
            string.Equals(page.Descriptor.Id, pageId, StringComparison.Ordinal));
}
