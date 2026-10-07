using System.Reflection;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Tests.OpenTap;

/// Loads genuine current library metadata without opening any instrument in the host.
internal static class PublishedLibraryMetadataFixture
{
    internal static Assembly Assembly
    {
        get
        {
            var stage = ExecutionInstrumentLibrary.EnsureLoaded([]);
            OpenTapPluginSearch.SearchSerialized([stage]);
            return AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == PublishedInstrumentComponents.PackageName);
        }
    }
}
