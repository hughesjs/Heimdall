using System.Diagnostics.CodeAnalysis;

namespace Heimdall;

/// <summary>
/// Enforces a single running instance via an exclusively-held lock file. The lock is held for the
/// process lifetime and released automatically on clean exit or crash, so no stale lock survives.
/// </summary>
internal sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _stream;

    private SingleInstanceLock(FileStream stream) => _stream = stream;

    public static bool TryAcquire([NotNullWhen(true)] out SingleInstanceLock? instanceLock)
    {
        var path = Path.Combine(Path.GetTempPath(), "heimdall.lock");
        try
        {
            // FileShare.None: a second process opening the same file fails until this stream is closed.
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            instanceLock = new SingleInstanceLock(stream);
            return true;
        }
        catch (IOException)
        {
            instanceLock = null;
            return false;
        }
    }

    public void Dispose() => _stream.Dispose();
}
