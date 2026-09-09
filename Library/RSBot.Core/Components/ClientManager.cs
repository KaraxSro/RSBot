using RSBot.Core.Event;
using RSBot.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static RSBot.Core.Extensions.NativeExtensions;

namespace RSBot.Core.Components;

public partial class ClientManager
{
    private static Process _process;
    private static string _launchId;
    private static string _launchPhase = "idle";
    private static DateTime _launchStarted;
    private static bool _intentionalExit;
    public static string CurrentLaunchId => _launchId;
    private static readonly string GitHubSignatureUrl =
        "https://raw.githubusercontent.com/myildirimofficial/rsbot/master/client-signatures.cfg";

    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Get, has client exited <c>true</c> otherwise; <c>false</c>
    /// </summary>
    public static bool IsRunning => _process?.HasExited == false;

    /// <summary>
    /// Gets whether the current client is being closed intentionally by RSBot.
    /// </summary>
    public static bool IsIntentionalExit => _intentionalExit;

    /// <summary>
    /// Loads client signatures from GitHub repository
    /// </summary>
    private static async Task<Dictionary<GameClientType, string>> LoadSignaturesFromGitHub()
    {
        var signatures = new Dictionary<GameClientType, string>();

        try
        {
            Log.Notify("Fetching client signatures from GitHub...");
            var response = await _httpClient.GetStringAsync(GitHubSignatureUrl);

            foreach (var line in response.Split('\n'))
            {
                var trimmedLine = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmedLine) ||
                    trimmedLine.StartsWith("#") ||
                    trimmedLine.StartsWith("//"))
                    continue;

                var parts = trimmedLine.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var clientTypeName = parts[0].Trim();
                var signature = parts[1].Replace('"', ' ').Trim();

                if (Enum.TryParse<GameClientType>(clientTypeName, true, out var clientType))
                {
                    signatures[clientType] = signature;
                }
                else
                {
                    Log.Warn($"Unknown client type in signatures: {clientTypeName}");
                }
            }

            Log.Notify($"Successfully loaded {signatures.Count} client signatures from GitHub");
        }
        catch (HttpRequestException ex)
        {
            Log.Error($"Failed to fetch signatures from GitHub: {ex.Message}");

            return [];
        }
        catch (Exception ex)
        {
            Log.Error($"Unexpected error loading signatures: {ex.Message}");
            return [];
        }

        return signatures;
    }

    /// <summary>
    /// Gets the signature for the specified client type
    /// </summary>
    private static async Task<string> GetSignature(GameClientType type, Dictionary<GameClientType, string> signatures)
    {
        if (signatures.TryGetValue(type, out var signature))
            return signature;

        throw new ArgumentOutOfRangeException(nameof(type),
            $"No signature found for client type: {type}");
    }

    /// <summary>
    /// Start the game client
    /// </summary>
    /// <returns>Has successfully started <c>true</c>; otherwise <c>false</c></returns>
    public static async Task<bool> Start(string reason = "manual")
    {
        _launchId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        _launchStarted = DateTime.UtcNow;
        _intentionalExit = false;
        SetLaunchPhase("validating", $"Starting client; reason={reason}; clientType={Game.ClientType}; OS={Environment.OSVersion}; rsbot64Bit={Environment.Is64BitProcess}");
        var silkroadDirectory = GlobalConfig.Get<string>("RSBot.SilkroadDirectory");
        var executable = GlobalConfig.Get<string>("RSBot.SilkroadExecutable");
        var path = Path.Combine(silkroadDirectory, executable);

        if (!File.Exists(path))
        {
            LaunchError($"Silkroad executable not found: {path}");
            return false;
        }

        var libraryDllName = "Client.Library.dll";
        var fullPath = Path.Combine(Kernel.BasePath, libraryDllName);

        if (!File.Exists(fullPath))
        {
            LaunchError($"Client library not found: {fullPath}");
            return false;
        }

        var buffer = Encoding.Unicode.GetBytes(fullPath + "\0");
        var pathLen = (uint)buffer.Length;

        var gatewayIndex = GlobalConfig.Get<byte>("RSBot.GatewayIndex");
        var divisionIndex = GlobalConfig.Get<byte>("RSBot.DivisionIndex");
        var contentId = Game.ReferenceManager.DivisionInfo.Locale;

        var args = BuildCommandLineArguments(contentId, divisionIndex, gatewayIndex);

        LogLaunchFileMetadata("client", path);
        LogLaunchFileMetadata("loader", fullPath);
        var si = new STARTUPINFO { cb = (uint)Marshal.SizeOf<STARTUPINFO>() };

        CreateMutex(0, false, "Silkroad Online Launcher");
        CreateMutex(0, false, "Ready");

        SetLaunchPhase("create-process", "Creating suspended client process (arguments redacted)");
        if (!CreateProcess(
            null,
            $"\"{path}\" {args}",
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            CREATE_SUSPENDED,
            IntPtr.Zero,
            silkroadDirectory,
            ref si,
            out var pi))
        {
            LaunchWin32Error("CreateProcess");
            return false;
        }

        using var semaphore = new Semaphore(0, 1, pi.dwProcessId.ToString());

        try
        {
            SetLaunchPhase("process-created", $"Suspended process created; pid={pi.dwProcessId}");
            if (!PrepareTempConfigFile(pi.dwProcessId, divisionIndex))
            {
                CleanupProcess(pi);
                return false;
            }

            var sroProcess = Process.GetProcessById((int)pi.dwProcessId);

            if (RequiresXigncodePatch(Game.ClientType) && !await ApplyXigncodePatch(sroProcess, pi))
            {
                CleanupProcess(pi);
                return false;
            }

            _process = sroProcess;
            _process.EnableRaisingEvents = true;
            _process.Exited += ClientProcess_Exited;
            SetLaunchPhase("monitoring", $"Client exit monitoring registered before resume; pid={pi.dwProcessId}");

            SetLaunchPhase("injecting", "Injecting Client.Library.dll");
            if (!InjectClientLibrary(pi, buffer, pathLen))
            {
                CleanupProcess(pi);
                return false;
            }

            SetLaunchPhase("resuming", "Resuming client main thread");
            if (ResumeThread(pi.hThread) == uint.MaxValue)
            {
                LaunchWin32Error("ResumeThread");
                CleanupProcess(pi);
                return false;
            }
            SetLaunchPhase("post-resume", "Client main thread resumed; refreshing process state");

            _process.Refresh();
            SetLaunchPhase("post-resume", $"Process state refreshed; hasExited={_process.HasExited}");
            if (_process.HasExited)
            {
                LaunchError($"Process exited immediately after start; exitCode=0x{_process.ExitCode:X}");
                return false;
            }

            SetLaunchPhase("running", "Client resumed and survived initial validation");
            _ = LogSurvivalCheckpoints(_process, _launchId);
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            pi.hThread = IntPtr.Zero;
            pi.hProcess = IntPtr.Zero;
            EventManager.FireEvent("OnStartClient");
            return true;
        }
        catch (Exception ex)
        {
            LaunchError($"Failed to start client: {ex}");
            CleanupProcess(pi);
            return false;
        }
    }

    /// <summary>
    /// Builds command line arguments based on client type
    /// </summary>
    private static string BuildCommandLineArguments(byte contentId, byte divisionIndex, byte gatewayIndex)
    {
        if (Game.ClientType == GameClientType.RuSro)
        {
            var login = GlobalConfig.Get<string>("RSBot.RuSro.login");
            var password = GlobalConfig.Get<string>("RSBot.RuSro.password");
            return $"-LOGIN:{login} -PASSWORD:{password}";
        }

        return $"/{contentId} {divisionIndex} {gatewayIndex} 0";
    }

    /// <summary>
    /// Checks if the client type requires XIGNCODE patching
    /// </summary>
    private static bool RequiresXigncodePatch(GameClientType clientType)
    {
        return clientType == GameClientType.VTC_Game
            || clientType == GameClientType.Turkey
            || clientType == GameClientType.Taiwan;
    }

    /// <summary>
    /// Injects the client library into the target process
    /// </summary>
    private static bool InjectClientLibrary(PROCESS_INFORMATION pi, byte[] buffer, uint pathLen)
    {
        var handle = pi.hProcess;
        if (handle == IntPtr.Zero)
        {
                LaunchError("Process handle is invalid");
            return false;
        }

        try
        {
            var kernelHandle = GetModuleHandleW("kernel32.dll");
            if (kernelHandle == IntPtr.Zero)
            {
                LaunchWin32Error("GetModuleHandleW(kernel32.dll)");
                return false;
            }

            var loadLibAddr = GetProcAddress(kernelHandle, "LoadLibraryW");
            if (loadLibAddr == IntPtr.Zero)
            {
                LaunchWin32Error("GetProcAddress(LoadLibraryW)");
                return false;
            }

            var remotePath = VirtualAllocEx(
                handle,
                IntPtr.Zero,
                pathLen,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE);

            if (remotePath == IntPtr.Zero)
            {
                LaunchWin32Error("VirtualAllocEx");
                return false;
            }

            try
            {
                if (!WriteProcessMemory(handle, remotePath, buffer, pathLen, out _))
                {
                    LaunchWin32Error("WriteProcessMemory");
                    return false;
                }

                var remoteThread = CreateRemoteThread(
                    handle,
                    IntPtr.Zero,
                    0,
                    loadLibAddr,
                    remotePath,
                    0,
                    IntPtr.Zero);

                if (remoteThread == IntPtr.Zero)
                {
                    LaunchWin32Error("CreateRemoteThread");
                    return false;
                }

                try
                {
                    Log.Debug("Waiting for LoadLibraryW to complete (10s timeout)...");
                    var waitResult = WaitForSingleObject(remoteThread, 10000);
                    if (waitResult != WAIT_OBJECT_0)
                    {
                        if (waitResult == WAIT_TIMEOUT)
                            LaunchError("LoadLibraryW timed out after 10 seconds; the loader may have deadlocked");
                        else if (waitResult == WAIT_FAILED)
                            LaunchWin32Error("WaitForSingleObject(LoadLibraryW)");
                        else
                            LaunchError($"LoadLibraryW wait returned unexpected result 0x{waitResult:X}");
                        return false;
                    }

                    if (!GetExitCodeThread(remoteThread, out var exitCode))
                    {
                        LaunchWin32Error("GetExitCodeThread");
                        return false;
                    }

                    if (exitCode == 0)
                    {
                        LaunchError("LoadLibraryW returned null; DLL could not be loaded or is incompatible");
                        return false;
                    }

                    // NTSTATUS error codes have the high two bits set (0xC0000000+)
                    if (exitCode >= 0xC0000000)
                    {
                        LaunchError($"LoadLibraryW remote thread returned NTSTATUS 0x{exitCode:X}");
                        return false;
                    }

                    Log.Notify($"Client library injected successfully (module handle: 0x{exitCode:X})");
                    return true;
                }
                finally
                {
                    CloseHandle(remoteThread);
                }
            }
            finally
            {
                VirtualFreeEx(handle, remotePath, 0, MEM_RELEASE);
            }
        }
        catch (Exception ex) {
            Log.Error($"DLL injection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies an in-memory patch to the XIGNCODE module of the specified process
    /// </summary>
    private static async Task<bool> ApplyXigncodePatch(Process process, PROCESS_INFORMATION pi)
    {
        try
        {
            var signatures = await LoadSignaturesFromGitHub();
            if (signatures.Count == 0)
            {
                Log.Error("No client signatures loaded. Cannot start client.");
                return false;
            }

            ResumeThread(pi.hThread);
            await Task.Delay(250);
            SuspendThread(pi.hThread);

            var moduleMemory = new byte[process.MainModule.ModuleMemorySize];
            if (!ReadProcessMemory(
                process.Handle,
                process.MainModule.BaseAddress,
                moduleMemory,
                process.MainModule.ModuleMemorySize,
                out _))
            {
                Log.Error("Failed to read process memory for XIGNCODE patch");
                return false;
            }

            var signature = await GetSignature(Game.ClientType, signatures);
            var baseAddress = process.MainModule.BaseAddress.ToInt32();
            var address = FindPattern(signature, moduleMemory, baseAddress);

            if (address == IntPtr.Zero)
            {
                Log.Error("XIGNCODE patching failed! Signature not found.");
                Log.Error($"Please check if the signature for {Game.ClientType} is correct in the GitHub repository.");
                return false;
            }

            // Apply patches
            var patchJmp = new byte[] { 0xEB };
            var patchNop2 = new byte[] { 0x90, 0x90, 0x90, 0x90, 0x90 };

            WriteProcessMemory(pi.hProcess, address - 0x6F, patchJmp, 1, out _);
            WriteProcessMemory(pi.hProcess, address + 0x13, patchJmp, 1, out _);
            WriteProcessMemory(pi.hProcess, address + 0xC, patchNop2, 5, out _);
            WriteProcessMemory(pi.hProcess, address + 0x95, patchJmp, 1, out _);

            Log.Notify("XIGNCODE patch applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"XIGNCODE patching exception: {ex.Message}");
            return false;
        }
        finally
        {
            GC.Collect();
        }

        return true;
    }

    /// <summary>
    /// Kill the game client process
    /// </summary>
    public static void Kill()
    {
        if (!IsRunning)
            return;

        try
        {
            _intentionalExit = true;
            SetLaunchPhase("terminating", "RSBot intentionally terminating the client");
            _process.Kill();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to kill client process: {ex.Message}");
        }
    }

    /// <summary>
    /// Change client process title
    /// </summary>
    public static void SetTitle(string title)
    {
        if (_process != null && _process.MainWindowHandle != IntPtr.Zero)
            SetWindowText(_process.MainWindowHandle, title);
    }

    /// <summary>
    /// Change client visibility
    /// </summary>
    public static void SetVisible(bool visible)
    {
        if (_process != null && _process.MainWindowHandle != IntPtr.Zero)
            ShowWindow(_process.MainWindowHandle, visible ? SW_SHOW : SW_HIDE);
    }

    /// <summary>
    /// Handles client process exit event
    /// </summary>
    private static void ClientProcess_Exited(object sender, EventArgs e)
    {
        var process = sender as Process ?? _process;
        var exitCode = "unavailable";
        var processId = 0;
        try { processId = process.Id; } catch { }
        try { exitCode = $"0x{process.ExitCode:X}"; } catch { }
        var lifetime = DateTime.UtcNow - _launchStarted;
        Log.Warn($"[ClientLaunch:{_launchId}] Client process exited; exitCode={exitCode}; lifetime={lifetime.TotalSeconds:F1}s; phase={_launchPhase}; intentional={_intentionalExit}");
        if (!_intentionalExit && lifetime < TimeSpan.FromMinutes(1))
            _ = Task.Run(() => LogRecentWindowsCrashEvidence(processId));
        EventManager.FireEvent("OnExitClient");
    }

    /// <summary>
    /// Marks the current client shutdown as intentional so disconnect handlers do not start recovery actions.
    /// </summary>
    public static void BeginIntentionalExit(string reason)
    {
        _intentionalExit = true;
        SetLaunchPhase("exiting", reason);
    }

    /// <summary>
    /// Restores normal disconnect handling when an intentional shutdown could not be completed.
    /// </summary>
    public static void CancelIntentionalExit(string reason)
    {
        _intentionalExit = false;
        SetLaunchPhase("running", reason);
    }

    /// <summary>
    /// Asks the client window to close normally.
    /// </summary>
    public static bool RequestClose()
    {
        if (!IsRunning)
            return true;

        try
        {
            BeginIntentionalExit("Requesting a graceful client window close");
            return _process.CloseMainWindow();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to request a graceful client close: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Waits asynchronously for the client process to exit.
    /// </summary>
    public static async Task<bool> WaitForExitAsync(int milliseconds)
    {
        var process = _process;
        if (process == null)
            return true;

        try
        {
            process.Refresh();
            if (process.HasExited)
                return true;

            var exitTask = process.WaitForExitAsync();
            var completedTask = await Task.WhenAny(exitTask, Task.Delay(milliseconds));
            return completedTask == exitTask;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Failed while waiting for the client to exit: {ex.Message}");
            return !IsRunning;
        }
    }

    public static void MarkProxyConnected()
    {
        SetLaunchPhase("proxy-connected", "Client connected to the RSBot proxy");
    }

    private static void SetLaunchPhase(string phase, string message)
    {
        _launchPhase = phase;
        Log.Notify($"[ClientLaunch:{_launchId}] [{phase}] {message}");
    }

    private static void LaunchError(string message) => Log.Error($"[ClientLaunch:{_launchId}] [{_launchPhase}] {message}");

    private static void LaunchWin32Error(string operation)
    {
        var code = Marshal.GetLastWin32Error();
        LaunchError($"{operation} failed; win32={code} (0x{code:X}); message={new Win32Exception(code).Message}");
    }

    private static void LogLaunchFileMetadata(string name, string path)
    {
        try
        {
            var file = new FileInfo(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown";
            SetLaunchPhase("validating", $"{name}: path={path}; version={version}; size={file.Length}; modifiedUtc={file.LastWriteTimeUtc:O}; sha256={hash}");
        }
        catch (Exception exception)
        {
            LaunchError($"Could not read {name} metadata: {exception.Message}");
        }
    }

    private static async Task LogSurvivalCheckpoints(Process process, string launchId)
    {
        foreach (var seconds in new[] { 2, 10, 30 })
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds == 2 ? 2 : seconds == 10 ? 8 : 20));
            if (launchId != _launchId)
                return;
            try
            {
                process.Refresh();
                if (process.HasExited)
                    return;
                Log.Debug($"[ClientLaunch:{launchId}] survival checkpoint +{seconds}s; workingSet={process.WorkingSet64}");
            }
            catch (Exception exception)
            {
                Log.Debug($"[ClientLaunch:{launchId}] survival checkpoint unavailable: {exception.Message}");
                return;
            }
        }
    }

    private static void LogRecentWindowsCrashEvidence(int processId)
    {
        try
        {
            var query = new EventLogQuery(
                "Application",
                PathType.LogName,
                "*[System[(EventID=1000 or EventID=1001) and TimeCreated[timediff(@SystemTime) <= 120000]]]"
            ) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var count = 0; count < 20; count++)
            {
                using var record = reader.ReadEvent();
                if (record == null)
                    break;
                var values = record.Properties.Select(property => property.Value?.ToString() ?? string.Empty).ToArray();
                if (!values.Any(value => value.Contains("sro_client", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var application = values.ElementAtOrDefault(0) ?? "sro_client.exe";
                var module = values.ElementAtOrDefault(3) ?? "unavailable";
                var exceptionCode = values.ElementAtOrDefault(6) ?? "unavailable";
                var faultOffset = values.ElementAtOrDefault(7) ?? "unavailable";
                Log.Warn($"[ClientLaunch:{_launchId}] Windows crash evidence: event={record.Id}; pid={processId}; application={application}; module={module}; exceptionCode={exceptionCode}; offset={faultOffset}");
                return;
            }
            Log.Debug($"[ClientLaunch:{_launchId}] Windows crash evidence unavailable: no matching recent Application Error/WER event for pid={processId}");
        }
        catch (Exception exception)
        {
            Log.Debug($"[ClientLaunch:{_launchId}] Windows crash evidence unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Prepare the config file for loader
    /// </summary>
    private static bool PrepareTempConfigFile(uint processId, int divisionIndex)
    {
        try
        {
            var tmpConfigFile = $"RSBot_{processId}.tmp";
            var division = Game.ReferenceManager.DivisionInfo.Divisions[divisionIndex];
            var gatewayPort = Game.ReferenceManager.GatewayInfo.Port;
            var redirectIp = "127.0.0.1";

            using var writer = new BinaryWriter(
                new FileStream(
                    Path.Combine(Path.GetTempPath(), tmpConfigFile),
                    FileMode.Create));

            writer.Write(GlobalConfig.Get<bool>("RSBot.Loader.DebugMode"));
            writer.WriteAscii(redirectIp);
            writer.Write(Kernel.Proxy.Port);
            writer.Write(division.GatewayServers.Count);

            foreach (var gatewayServer in division.GatewayServers)
                writer.WriteAscii(gatewayServer);

            writer.Write(gatewayPort);
            writer.WriteAscii(Log.SessionId);
            writer.WriteAscii(_launchId ?? string.Empty);
            writer.WriteAscii(Log.CurrentFilePath ?? string.Empty);
            SetLaunchPhase("loader-config", $"Temporary loader configuration written; pid={processId}; division={divisionIndex}; gatewayCount={division.GatewayServers.Count}; loaderDebug={GlobalConfig.Get<bool>("RSBot.Loader.DebugMode")}");
            return true;
        }
        catch (Exception ex)
        {
            LaunchError($"Failed to prepare temporary loader configuration: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Searches the specified buffer for the first occurrence of a byte pattern
    /// </summary>
    private static IntPtr FindPattern(string stringPattern, byte[] buffer, int baseAddress)
    {
        try
        {
            var pattern = stringPattern
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => byte.Parse(p, NumberStyles.AllowHexSpecifier))
                .ToArray();

            var patternLength = pattern.Length;
            var searchLength = buffer.Length - patternLength;

            for (var i = 0; i < searchLength; i++)
            {
                var found = true;
                for (var j = 0; j < patternLength; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return (IntPtr)(baseAddress + i);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Pattern search failed: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Cleanup process handles
    /// </summary>
    private static void CleanupProcess(PROCESS_INFORMATION pi)
    {
        try
        {
            if (pi.hThread != IntPtr.Zero)
                CloseHandle(pi.hThread);

            if (pi.hProcess != IntPtr.Zero)
            {
                try
                {
                    var process = Process.GetProcessById((int)pi.dwProcessId);
                    if (process?.HasExited == false)
                    {
                        _intentionalExit = true;
                        SetLaunchPhase("cleanup", $"Terminating suspended client after setup failure; pid={pi.dwProcessId}");
                        process.Kill();
                    }
                }
                catch (ArgumentException)
                {
                    // Process has already exited
                }

                CloseHandle(pi.hProcess);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to cleanup process: {ex.Message}");
        }
    }
}
