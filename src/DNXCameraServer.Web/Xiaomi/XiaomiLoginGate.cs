namespace DNXCameraServer.Web.Xiaomi;

/// <summary>
/// Serializa las llamadas de autenticación a go2rtc. Su módulo Xiaomi usa estado
/// global sin sincronizar (auth/tokens) y dos logins concurrentes pueden provocar
/// un "concurrent map read and map write" que tumba el proceso.
/// </summary>
public sealed class XiaomiLoginGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        return new Releaser(_semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) semaphore.Release();
        }
    }
}
