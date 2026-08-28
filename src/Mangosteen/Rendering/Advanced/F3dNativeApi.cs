using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Mangosteen.Rendering.Advanced;

internal sealed partial class F3dNativeApi : IDisposable
{
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
    private nint _module;

    private readonly EngineCreateExternalWgl _engineCreateExternalWgl;
    private readonly EngineDelete _engineDelete;
    private readonly EngineAutoloadPlugins _engineAutoloadPlugins;
    private readonly EngineGetHandle _engineGetOptions;
    private readonly EngineGetHandle _engineGetWindow;
    private readonly EngineGetHandle _engineGetScene;
    private readonly OptionsSetBool _optionsSetBool;
    private readonly OptionsSetDoubleVector _optionsSetDoubleVector;
    private readonly SceneAdd _sceneAdd;
    private readonly SceneClear _sceneClear;
    private readonly SceneSupports _sceneSupports;
    private readonly SceneAnimationTimeRange _sceneAnimationTimeRange;
    private readonly SceneAvailableAnimations _sceneAvailableAnimations;
    private readonly SceneLoadAnimationTime _sceneLoadAnimationTime;
    private readonly WindowGetCamera _windowGetCamera;
    private readonly WindowRender _windowRender;
    private readonly WindowSetSize _windowSetSize;
    private readonly CameraUnaryDouble _cameraDolly;
    private readonly CameraPan _cameraPan;
    private readonly CameraUnaryDouble _cameraAzimuth;
    private readonly CameraUnaryDouble _cameraElevation;
    private readonly CameraUnaryDouble _cameraResetToBounds;

    public F3dNativeApi(string componentDirectory)
    {
        var libraryPath = FindLibrary(componentDirectory);
        _module = LoadLibraryEx(
            libraryPath,
            nint.Zero,
            LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
        if (_module == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load the optional F3D runtime from '{libraryPath}'.");
        }

        try
        {
            _engineCreateExternalWgl = GetExport<EngineCreateExternalWgl>("f3d_engine_create_external_wgl");
            _engineDelete = GetExport<EngineDelete>("f3d_engine_delete");
            _engineAutoloadPlugins = GetExport<EngineAutoloadPlugins>("f3d_engine_autoload_plugins");
            _engineGetOptions = GetExport<EngineGetHandle>("f3d_engine_get_options");
            _engineGetWindow = GetExport<EngineGetHandle>("f3d_engine_get_window");
            _engineGetScene = GetExport<EngineGetHandle>("f3d_engine_get_scene");
            _optionsSetBool = GetExport<OptionsSetBool>("f3d_options_set_as_bool");
            _optionsSetDoubleVector = GetExport<OptionsSetDoubleVector>("f3d_options_set_as_double_vector");
            _sceneAdd = GetExport<SceneAdd>("f3d_scene_add");
            _sceneClear = GetExport<SceneClear>("f3d_scene_clear");
            _sceneSupports = GetExport<SceneSupports>("f3d_scene_supports");
            _sceneAnimationTimeRange = GetExport<SceneAnimationTimeRange>("f3d_scene_animation_time_range");
            _sceneAvailableAnimations = GetExport<SceneAvailableAnimations>("f3d_scene_available_animations");
            _sceneLoadAnimationTime = GetExport<SceneLoadAnimationTime>("f3d_scene_load_animation_time");
            _windowGetCamera = GetExport<WindowGetCamera>("f3d_window_get_camera");
            _windowRender = GetExport<WindowRender>("f3d_window_render");
            _windowSetSize = GetExport<WindowSetSize>("f3d_window_set_size");
            _cameraDolly = GetExport<CameraUnaryDouble>("f3d_camera_dolly");
            _cameraPan = GetExport<CameraPan>("f3d_camera_pan");
            _cameraAzimuth = GetExport<CameraUnaryDouble>("f3d_camera_azimuth");
            _cameraElevation = GetExport<CameraUnaryDouble>("f3d_camera_elevation");
            _cameraResetToBounds = GetExport<CameraUnaryDouble>("f3d_camera_reset_to_bounds");
        }
        catch
        {
            FreeLibrary(_module);
            _module = nint.Zero;
            throw;
        }
    }

    public nint CreateEngine()
    {
        _engineAutoloadPlugins();
        return _engineCreateExternalWgl();
    }

    public void DeleteEngine(nint engine) => _engineDelete(engine);
    public nint GetOptions(nint engine) => _engineGetOptions(engine);
    public nint GetWindow(nint engine) => _engineGetWindow(engine);
    public nint GetScene(nint engine) => _engineGetScene(engine);
    public nint GetCamera(nint window) => _windowGetCamera(window);
    public void ClearScene(nint scene) => _sceneClear(scene);
    public uint AvailableAnimations(nint scene) => _sceneAvailableAnimations(scene);
    public void LoadAnimationTime(nint scene, double value) => _sceneLoadAnimationTime(scene, value);
    public void AnimationTimeRange(nint scene, out double minimum, out double maximum) => _sceneAnimationTimeRange(scene, out minimum, out maximum);
    public bool Render(nint window) => _windowRender(window) != 0;
    public void SetWindowSize(nint window, int width, int height) => _windowSetSize(window, width, height);
    public void Dolly(nint camera, double value) => _cameraDolly(camera, value);
    public void Pan(nint camera, double right, double up, double forward) => _cameraPan(camera, right, up, forward);
    public void Azimuth(nint camera, double value) => _cameraAzimuth(camera, value);
    public void Elevation(nint camera, double value) => _cameraElevation(camera, value);
    public void ResetToBounds(nint camera, double value) => _cameraResetToBounds(camera, value);

    public bool AddSceneFile(nint scene, string path)
    {
        return WithUtf8(path, pointer => _sceneAdd(scene, pointer)) != 0;
    }

    public bool Supports(nint scene, string path)
    {
        return WithUtf8(path, pointer => _sceneSupports(scene, pointer)) != 0;
    }

    public void SetBool(nint options, string name, bool value)
    {
        WithUtf8(name, pointer =>
        {
            _optionsSetBool(options, pointer, value ? 1 : 0);
            return 0;
        });
    }

    public void SetDoubleVector(nint options, string name, ReadOnlySpan<double> values)
    {
        var namePointer = Marshal.StringToCoTaskMemUTF8(name);
        var valuePointer = Marshal.AllocHGlobal(checked(values.Length * sizeof(double)));
        try
        {
            var copy = values.ToArray();
            Marshal.Copy(copy, 0, valuePointer, copy.Length);
            _optionsSetDoubleVector(options, namePointer, valuePointer, (nuint)copy.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(valuePointer);
            Marshal.FreeCoTaskMem(namePointer);
        }
    }

    public void Dispose()
    {
        if (_module == nint.Zero) return;
        FreeLibrary(_module);
        _module = nint.Zero;
    }

    private T GetExport<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(_module, name);
        if (address == nint.Zero)
        {
            throw new EntryPointNotFoundException($"The F3D runtime does not export '{name}'.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static string FindLibrary(string componentDirectory)
    {
        foreach (var path in new[]
        {
            Path.Combine(componentDirectory, "bin", "f3d_c_api.dll"),
            Path.Combine(componentDirectory, "f3d_c_api.dll")
        })
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
        }

        throw new FileNotFoundException("The optional 3D component is installed, but f3d_c_api.dll is missing.");
    }

    private static int WithUtf8(string value, Func<nint, int> action)
    {
        var pointer = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return action(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint EngineCreateExternalWgl();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void EngineDelete(nint engine);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void EngineAutoloadPlugins();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint EngineGetHandle(nint engine);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OptionsSetBool(nint options, nint name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OptionsSetDoubleVector(nint options, nint name, nint values, nuint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SceneAdd(nint scene, nint path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SceneClear(nint scene);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SceneSupports(nint scene, nint path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SceneAnimationTimeRange(nint scene, out double minimum, out double maximum);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint SceneAvailableAnimations(nint scene);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SceneLoadAnimationTime(nint scene, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint WindowGetCamera(nint window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WindowRender(nint window);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void WindowSetSize(nint window, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CameraUnaryDouble(nint camera, double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CameraPan(nint camera, double right, double up, double forward);

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryEx(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcAddress(nint module, string procedureName);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);
}
