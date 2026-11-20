using Xunit;

// Redact keeps its rules in static state that Log.Write also reads, so the tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SfSaveHealth.Tests;

/// <summary>A new empty folder under the system temp folder, deleted again at the end of the test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sf-save-health-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name, byte[]? bytes = null)
    {
        var p = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(p, bytes ?? []);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
