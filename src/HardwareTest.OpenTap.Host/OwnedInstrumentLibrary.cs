using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace HardwareTest.OpenTap.Host;

/// Captures provenance from the exact stream supplied to the CLR, retaining a separate
/// owned disk payload for OpenTAP's lazy metadata discovery.
internal static class OwnedInstrumentLibrary
{
    private sealed record Payload(byte[] Hash, string Origin);
    private static readonly object Gate = new();
    private static readonly Dictionary<Assembly, Payload> Loaded = [];

    internal static Assembly Load(byte[] approvedBytes, string path)
    {
        lock (Gate)
        {
            // Retain one private immutable copy from the caller's approved capture.
            // Origin bytes authorize provenance, never supply bytes to the CLR loader.
            var bytes = approvedBytes.ToArray();
            var hash = SHA256.HashData(bytes);
            var origin = Path.GetFullPath(path);
            if (!SHA256.HashData(File.ReadAllBytes(origin)).SequenceEqual(hash))
                throw new InvalidOperationException("The approved Instrument Components library no longer matches its source payload. Repair the source before loading.");
            using var identityStream = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(identityStream);
            var metadata = pe.GetMetadataReader();
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name);
            if (existing is not null)
            {
                if (!Fingerprint(existing).SequenceEqual(hash))
                    throw new InvalidOperationException("The selected Instrument Components payload differs from the loaded execution library. Restart the executing process to use this home.");
                return existing;
            }
            using var stream = new MemoryStream(bytes, writable: false);
            var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
            if (!string.IsNullOrEmpty(assembly.Location))
                throw new InvalidOperationException("The loaded execution library has no verified load provenance. Restart the process before loading the selected library.");
            Loaded.Add(assembly, new(hash, origin));
            return assembly;
        }
    }

    internal static byte[] Fingerprint(Assembly assembly)
    {
        lock (Gate)
        {
            if (!Loaded.TryGetValue(assembly, out var payload))
                throw new InvalidOperationException("The loaded execution library has no verified load provenance. Restart the process before loading the selected library.");
            if (!SHA256.HashData(File.ReadAllBytes(payload.Origin)).SequenceEqual(payload.Hash))
                throw new InvalidOperationException("The loaded execution library no longer matches its source payload. Restart the executing process.");
            return payload.Hash.ToArray();
        }
    }

    internal static string Directory(Assembly assembly)
    {
        lock (Gate)
        {
            _ = Fingerprint(assembly);
            return Path.GetDirectoryName(Loaded[assembly].Origin)!;
        }
    }
}
