using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WhisperMyAss.Services;

/// <summary>
/// Locations and native-library loading for the optional local Parakeet engine.
/// The native ONNX runtime + sherpa libs and the model are downloaded (Phase 3)
/// into %LOCALAPPDATA%\WhisperMyAss, keeping the base installer tiny. We point
/// the sherpa-onnx managed bindings at those downloaded natives via a
/// DllImport resolver.
/// </summary>
public static class LocalEngine
{
    public const string ModelName = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8";

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhisperMyAss");

    public static string EngineDir => Path.Combine(Root, "engine");
    public static string ModelDir => Path.Combine(Root, "models", ModelName);

    public static string Encoder => Path.Combine(ModelDir, "encoder.int8.onnx");
    public static string Decoder => Path.Combine(ModelDir, "decoder.int8.onnx");
    public static string Joiner => Path.Combine(ModelDir, "joiner.int8.onnx");
    public static string Tokens => Path.Combine(ModelDir, "tokens.txt");

    public static bool NativesInstalled =>
        File.Exists(Path.Combine(EngineDir, "sherpa-onnx-c-api.dll")) &&
        File.Exists(Path.Combine(EngineDir, "onnxruntime.dll"));

    public static bool ModelInstalled =>
        File.Exists(Encoder) && File.Exists(Decoder) && File.Exists(Joiner) && File.Exists(Tokens);

    public static bool Installed => NativesInstalled && ModelInstalled;

    private static bool _resolverSet;
    private static IntPtr _ortHandle;

    /// <summary>Wire the sherpa-onnx bindings to load natives from EngineDir.
    /// Idempotent; call before constructing a recognizer.</summary>
    public static void EnsureNativeLoading()
    {
        if (_resolverSet) return;

        // Pre-load onnxruntime so sherpa-onnx-c-api's implicit dependency
        // resolves to our copy rather than the default DLL search path.
        var ort = Path.Combine(EngineDir, "onnxruntime.dll");
        if (File.Exists(ort))
            NativeLibrary.TryLoad(ort, out _ortHandle);

        NativeLibrary.SetDllImportResolver(typeof(SherpaOnnx.OfflineRecognizer).Assembly, Resolve);
        _resolverSet = true;
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var name = libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? libraryName
            : libraryName + ".dll";
        var candidate = Path.Combine(EngineDir, name);
        if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var h))
            return h;
        return IntPtr.Zero; // let the default resolver try
    }
}
