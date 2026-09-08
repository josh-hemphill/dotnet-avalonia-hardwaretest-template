using System.Reflection;
using HardwareTest.Core.Hardware;

namespace HardwareTest.OpenTap.Host;

/// Binds HardwareTest <see cref="IVisaBroker"/> onto InstrumentComponents.OpenTap without a compile-time package reference.
/// When the library pack is loaded, <c>OpenTapScpiIo.Provider</c> opens sessions through the process broker.
internal static class InstrumentComponentsScpiIo
{
    internal const string OpenTapScpiIoTypeName = "InstrumentComponents.OpenTap.OpenTapScpiIo";

    public static bool TryRegisterProvider(IVisaBroker broker)
    {
        ArgumentNullException.ThrowIfNull(broker);
        var openTapScpiIo = FindType(OpenTapScpiIoTypeName);
        if (openTapScpiIo is null)
        {
            return false;
        }

        var providerProperty = openTapScpiIo.GetProperty("Provider", BindingFlags.Public | BindingFlags.Static);
        if (providerProperty is null || !providerProperty.CanWrite)
        {
            return false;
        }

        var providerType = Nullable.GetUnderlyingType(providerProperty.PropertyType) ?? providerProperty.PropertyType;
        var provider = ScpiIoProviderProxy.Create(providerType, broker);
        providerProperty.SetValue(null, provider);
        return true;
    }

    internal static object CreateIo(Type scpiIoType, IVisaSession session, TimeSpan timeout)
        => VisaBrokerScpiIoProxy.Create(scpiIoType, session, timeout);

    internal static object CreateProvider(Type providerType, IVisaBroker broker)
        => ScpiIoProviderProxy.Create(providerType, broker);

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static int ClampTimeout(TimeSpan timeout)
        => Math.Clamp(
            (int)Math.Round(timeout.TotalMilliseconds),
            IviVisaSessionFactory.MinIoTimeoutMilliseconds,
            IviVisaSessionFactory.MaxIoTimeoutMilliseconds);

    private class ScpiIoProviderProxy : DispatchProxy
    {
        private IVisaBroker _broker = null!;

        public static object Create(Type providerType, IVisaBroker broker)
        {
            var proxy = (ScpiIoProviderProxy)CreateProxy(providerType, typeof(ScpiIoProviderProxy));
            proxy._broker = broker;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            if (targetMethod.Name == "Open" && args is { Length: 2 })
            {
                var address = args[0] as string
                              ?? throw new ArgumentException("VisaAddress is required.", targetMethod.Name);
                var timeout = args[1] is TimeSpan span ? span : TimeSpan.FromMilliseconds(5000);
                using var cts = new CancellationTokenSource(ClampTimeout(timeout));
                var session = _broker.OpenAsync(address, cts.Token).GetAwaiter().GetResult();
                session.IoTimeoutMilliseconds = ClampTimeout(timeout);
                return VisaBrokerScpiIoProxy.Create(targetMethod.ReturnType, session, timeout);
            }

            throw new NotImplementedException(
                $"InstrumentComponents SCPI provider proxy does not implement '{targetMethod.DeclaringType?.FullName}.{targetMethod.Name}'.");
        }
    }

    private class VisaBrokerScpiIoProxy : DispatchProxy
    {
        private IVisaSession _session = null!;
        private TimeSpan _timeout;

        public static object Create(Type scpiIoType, IVisaSession session, TimeSpan timeout)
        {
            var proxy = (VisaBrokerScpiIoProxy)CreateProxy(scpiIoType, typeof(VisaBrokerScpiIoProxy));
            proxy._session = session;
            proxy._timeout = timeout;
            session.IoTimeoutMilliseconds = ClampTimeout(timeout);
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                return null;
            }

            switch (targetMethod.Name)
            {
                case "get_IoTimeout":
                    return _timeout;
                case "set_IoTimeout":
                    _timeout = args is { Length: > 0 } && args[0] is TimeSpan span
                        ? span
                        : _timeout;
                    _session.IoTimeoutMilliseconds = ClampTimeout(_timeout);
                    return null;
                case "Write":
                    Write((string)args![0]!);
                    return null;
                case "Query":
                    return Query((string)args![0]!);
                case nameof(IDisposable.Dispose):
                    // Library IScpiIo is IDisposable only. Session close is IVisaSession.DisposeAsync;
                    // GetAwaiter().GetResult() is the host-side sync adapter (same as IviVisaSession).
                    _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    return null;
                default:
                    throw new NotImplementedException(
                        $"InstrumentComponents SCPI proxy does not implement '{targetMethod.DeclaringType?.FullName}.{targetMethod.Name}'.");
            }
        }

        private void Write(string command)
        {
            using var cts = new CancellationTokenSource(ClampTimeout(_timeout));
            _session.WriteAsync(command, cts.Token).GetAwaiter().GetResult();
        }

        private string Query(string command)
        {
            using var cts = new CancellationTokenSource(ClampTimeout(_timeout));
            return _session.QueryAsync(command, cts.Token).GetAwaiter().GetResult();
        }
    }

    private static object CreateProxy(Type interfaceType, Type proxyType)
    {
        var create = typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create)
                         && m.IsGenericMethodDefinition
                         && m.GetGenericArguments().Length == 2
                         && m.GetParameters().Length == 0);
        return create.MakeGenericMethod(interfaceType, proxyType).Invoke(null, null)
               ?? throw new InvalidOperationException($"Could not create proxy for '{interfaceType.FullName}'.");
    }
}
