using System.Threading;

namespace MG.BusinessPlatform.Infrastructure.Security;

/// <summary>
/// Async-flow-local scope used by persistence auditing to attribute writes to a
/// validated staff request. Always dispose the returned scope in a finally/using block.
/// </summary>
public static class StaffRequestContextScope
{
    private static readonly AsyncLocal<StaffRequestContext?> AmbientContext = new();

    public static StaffRequestContext? Current => AmbientContext.Value;

    public static IDisposable Push(StaffRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = AmbientContext.Value;
        AmbientContext.Value = context;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(StaffRequestContext? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            AmbientContext.Value = previous;
            _disposed = true;
        }
    }
}
