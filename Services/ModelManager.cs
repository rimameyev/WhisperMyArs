using System.IO;
using System.IO.Compression;
using System.Net.Http;
using SharpCompress.Readers;

namespace WhisperMyArs.Services;

public readonly record struct InstallProgress(string Stage, double Fraction);

/// <summary>
/// Downloads and installs the optional local engine: the native ONNX/sherpa
/// libraries (from the version-matched NuGet runtime package, a zip) and the
/// Parakeet model (a tar.bz2 from the sherpa-onnx model releases). Both land in
/// %LOCALAPPDATA%\WhisperMyArs. Progress is reported with a fraction in [0,1],
/// or -1 for an indeterminate stage (extraction).
/// </summary>
public sealed class ModelManager
{
    // Native libs must match the managed binding version referenced in the csproj.
    private const string NativeNupkgUrl =
        "https://api.nuget.org/v3-flatcontainer/org.k2fsa.sherpa.onnx.runtime.win-x64/1.13.3/org.k2fsa.sherpa.onnx.runtime.win-x64.1.13.3.nupkg";

    private const string ModelUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2";

    private static readonly string[] NeededModelFiles =
        { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" };

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public bool IsInstalled => LocalEngine.Installed;

    public async Task InstallAsync(IProgress<InstallProgress> progress, CancellationToken ct)
    {
        if (!LocalEngine.NativesInstalled)
            await InstallNativesAsync(progress, ct);

        if (!LocalEngine.ModelInstalled)
            await InstallModelAsync(progress, ct);

        progress.Report(new InstallProgress("Done", 1));
    }

    public void Remove()
    {
        if (Directory.Exists(LocalEngine.ModelDir)) Directory.Delete(LocalEngine.ModelDir, recursive: true);
        if (Directory.Exists(LocalEngine.EngineDir)) Directory.Delete(LocalEngine.EngineDir, recursive: true);
    }

    private async Task InstallNativesAsync(IProgress<InstallProgress> progress, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wma-engine-{Guid.NewGuid():N}.nupkg");
        try
        {
            await DownloadAsync(NativeNupkgUrl, tmp,
                f => progress.Report(new InstallProgress("Downloading engine…", f)), ct);

            Directory.CreateDirectory(LocalEngine.EngineDir);
            using var zip = ZipFile.OpenRead(tmp);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.Contains("runtimes/win-x64/native/", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ExtractToFile(Path.Combine(LocalEngine.EngineDir, entry.Name), overwrite: true);
                }
            }
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private async Task InstallModelAsync(IProgress<InstallProgress> progress, CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"wma-model-{Guid.NewGuid():N}.tar.bz2");
        try
        {
            await DownloadAsync(ModelUrl, tmp,
                f => progress.Report(new InstallProgress("Downloading model…", f)), ct);

            progress.Report(new InstallProgress("Extracting model…", -1));
            Directory.CreateDirectory(LocalEngine.ModelDir);

            using var fileStream = File.OpenRead(tmp);
            using var reader = ReaderFactory.OpenReader(fileStream, new ReaderOptions()); // auto-detects tar.bz2
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory) continue;

                var name = Path.GetFileName(reader.Entry.Key) ?? "";
                if (!NeededModelFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                using var outFile = File.Create(Path.Combine(LocalEngine.ModelDir, name));
                reader.WriteEntryTo(outFile);
            }
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static async Task DownloadAsync(string url, string destPath, Action<double> onProgress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;

        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destPath);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0) onProgress(readTotal / (double)total);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
