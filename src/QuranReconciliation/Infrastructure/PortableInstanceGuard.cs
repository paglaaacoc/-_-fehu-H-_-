using System.Text;

namespace QuranReconciliation.Infrastructure;

internal sealed class PortableInstanceGuard : IDisposable
{
    private readonly FileStream _handle;

    private PortableInstanceGuard(FileStream handle)
    {
        _handle = handle;
    }

    internal static PortableInstanceGuard Acquire(
        string? baseDirectory = null)
    {
        string root = Path.GetFullPath(
            baseDirectory ?? AppPaths.BaseDirectory);

        string dataDirectory =
            Path.Combine(root, "Data");

        Directory.CreateDirectory(dataDirectory);

        string lockPath =
            Path.Combine(
                dataDirectory,
                ".quran-reconciliation.owner-state.lock");

        try
        {
            var handle =
                new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);

            handle.SetLength(0);

            byte[] payload =
                Encoding.UTF8.GetBytes(
                    $"{BuildIdentity.WorkstationName}\n" +
                    $"PID={Environment.ProcessId}\n" +
                    $"StartedUtc={DateTimeOffset.UtcNow:O}\n" +
                    $"PortableRoot={root}\n");

            handle.Write(payload, 0, payload.Length);
            handle.Flush(flushToDisk: true);
            handle.Position = 0;

            return new PortableInstanceGuard(handle);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"This portable {BuildIdentity.WorkstationName} folder is already open in another process, or its owner-state lock cannot be acquired. Close the other copy before using this same folder. Separate portable folders may run independently.",
                ex);
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}
