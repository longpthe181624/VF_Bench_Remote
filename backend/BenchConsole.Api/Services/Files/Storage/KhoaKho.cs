namespace BenchConsole.Api.Services.Files.Storage;

// Serialize thay đổi metadata / file trong từng kho của một instance BE.
public sealed class KhoaKho
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<IDisposable> LayAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        return new Lease(_gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
