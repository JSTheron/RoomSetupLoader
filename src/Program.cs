using Microsoft.Win32;
using System.Drawing;
using System.Reflection;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;

// Blackmagic Switchers SDK BMDSwitcherAPI.idl; no atemlib required.
// Windows x64, .NET 8. The Blackmagic ATEM Switchers SDK/runtime must be installed.
internal static class Program
{
    static int Width = 1920, Height = 1080;
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp"];
    static StreamWriter? log;
    static string? currentLogPath;
    static string? logRoot;
    static string? logBaseName;
    static int logPart;
    static Settings loggingSettings = new();
    static readonly object logLock = new();
    static void Report(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        lock (logLock)
        {
            try
            {
                if (log == null) return;
                if (loggingSettings.MaxIndividualLogSizeMB > 0 && log.BaseStream.Length + System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine) > (long)loggingSettings.MaxIndividualLogSizeMB * 1024 * 1024)
                    RotateLog();
                log?.WriteLine(line);
            }
            catch (Exception ex) { Console.Error.WriteLine("Logging warning: " + ex.Message); }
        }
    }
    static void RotateLog()
    {
        if (logRoot == null || logBaseName == null) return;
        var next = Path.Combine(logRoot, $"{logBaseName}_part{++logPart:D3}.log");
        var writer = new StreamWriter(next, append: false) { AutoFlush = true };
        var old = log;
        var oldPath = currentLogPath;
        log = writer;
        currentLogPath = next;
        old?.Dispose();
        var notice = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log rotation: previous={Path.GetFileName(oldPath)}; current={Path.GetFileName(next)}; part={logPart}";
        Console.WriteLine(notice);
        log.WriteLine(notice);
    }
    // Called under logLock, including at startup. Diagnostics are written directly to
    // the application log to avoid recursive Report() -> CleanupLogs() calls.
    static void CleanupDiagnostic(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log cleanup: {message}";
        Console.WriteLine(line);
        try
        {
            if (log == null) return;
            if (loggingSettings.MaxIndividualLogSizeMB > 0 && log.BaseStream.Length + System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine)
                > (long)loggingSettings.MaxIndividualLogSizeMB * 1024 * 1024)
                RotateLog();
            log?.WriteLine(line);
        }
        catch (Exception ex) { Console.Error.WriteLine("Log cleanup diagnostic warning: " + ex); }
    }
    // Windows installed-software metadata, not the firmware running on the ATEM hardware.
    // Registry inspection is best-effort and must never prevent an upload.
    static void ReportInstalledAtemSoftwareVersion()
    {
        try
        {
            var matches = new List<string>();
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall == null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var item = uninstall.OpenSubKey(name);
                        var product = item?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(product) ||
                            !product.Contains("ATEM", StringComparison.OrdinalIgnoreCase) ||
                            !(product.Contains("Switchers", StringComparison.OrdinalIgnoreCase) ||
                              product.Contains("Software Control", StringComparison.OrdinalIgnoreCase))) continue;
                        var version = item?.GetValue("DisplayVersion") as string;
                        matches.Add($"{product.Trim()}: {(string.IsNullOrWhiteSpace(version) ? "version not published" : version.Trim())}");
                    }
                    catch (Exception ex) { Report($"ATEM software version detection: warning reading installed-product entry: {ex.Message}"); }
                }
            }
            var found = matches.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            Report(found.Length == 0
                ? "ATEM Switchers software installed on Windows: Not detected (installer registry metadata unavailable). This does not indicate whether the SDK COM component is registered."
                : "ATEM Switchers software installed on Windows (not switcher firmware): " + string.Join("; ", found));
        }
        catch (Exception ex)
        {
            Report($"ATEM Switchers software installed on Windows: Not detected (registry inspection failed: {ex.Message}).");
        }
    }

    static void ReportLoggingConfiguration(Settings config)
    {
        Report($"Logging configuration: LogRetentionDays={config.LogRetentionDays} ({(config.LogRetentionDays == 0 ? "DISABLED: no age-based deletion" : "ENABLED: age-based deletion")}); " +
            $"MaxLogFolderSizeMB={config.MaxLogFolderSizeMB} ({(config.MaxLogFolderSizeMB == 0 ? "DISABLED: no folder size limit" : "ENABLED: folder size limit")}); " +
            $"LogCleanupIntervalHours={config.LogCleanupIntervalHours} ({(config.LogCleanupIntervalHours == 0 ? "DISABLED: no automatic cleanup" : "ENABLED: cleanup at startup when due")}); " +
            $"MaxIndividualLogSizeMB={config.MaxIndividualLogSizeMB} ({(config.MaxIndividualLogSizeMB == 0 ? "DISABLED: no rotation" : "ENABLED: rotate when size exceeded")}).");
    }
    static bool CleanupLogs()
    {
        lock (logLock)
        {
            if (logRoot == null) return false;
            var cutoff = loggingSettings.LogRetentionDays > 0
                ? DateTime.UtcNow.AddDays(-loggingSettings.LogRetentionDays) : DateTime.MinValue;
            var limitBytes = (long)loggingSettings.MaxLogFolderSizeMB * 1024 * 1024;
            int discovered = 0, eligible = 0, deleted = 0, failed = 0;
            long before = 0, remaining = 0;
            try
            {
                var files = new DirectoryInfo(logRoot).EnumerateFiles("RoomSetupLoader_*.log")
                    .OrderBy(f => f.LastWriteTimeUtc).ToList();
                discovered = files.Count;
                before = files.Sum(f => f.Length);
                remaining = before;
                CleanupDiagnostic($"started; retention={(loggingSettings.LogRetentionDays == 0 ? "DISABLED" : $"{loggingSettings.LogRetentionDays} days")}; cutoff UTC={(loggingSettings.LogRetentionDays == 0 ? "N/A" : cutoff.ToString("O"))}; " +
                    $"folder limit={(loggingSettings.MaxLogFolderSizeMB == 0 ? "DISABLED" : $"{loggingSettings.MaxLogFolderSizeMB} MB")}; discovered={discovered}; size before={before} bytes; " +
                    $"directory={logRoot}");
                foreach (var file in files)
                {
                    if (string.Equals(file.FullName, currentLogPath, StringComparison.OrdinalIgnoreCase)) continue;
                    bool expired = loggingSettings.LogRetentionDays > 0 && file.LastWriteTimeUtc < cutoff;
                    bool overLimit = loggingSettings.MaxLogFolderSizeMB > 0 && remaining > limitBytes;
                    if (!expired && !overLimit) continue;
                    eligible++;
                    try
                    {
                        var size = file.Length;
                        var lastModifiedUtc = file.LastWriteTimeUtc;
                        file.Delete();
                        remaining -= size;
                        deleted++;
                        CleanupDiagnostic($"deleted {file.Name}; reason={(expired ? "expired" : "folder size")}; " +
                            $"last modified UTC={lastModifiedUtc:O}; bytes={size}");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        CleanupDiagnostic($"FAILED deleting {file.Name}; {ex.GetType().Name}: {ex.Message}");
                    }
                }
                CleanupDiagnostic($"finished; discovered={discovered}; eligible={eligible}; deleted={deleted}; " +
                    $"failed={failed}; size before={before} bytes; size after (tracked)={remaining} bytes");
                return failed == 0;
            }
            catch (Exception ex)
            {
                CleanupDiagnostic($"FAILED enumerating or processing logs; {ex.GetType().Name}: {ex.Message}; " +
                    $"discovered={discovered}; eligible={eligible}; deleted={deleted}; failed={failed}");
                return false;
            }
        }
    }
    // One cleanup opportunity per process startup, gated by a persisted UTC timestamp.
    // The marker lives outside Logs so size/retention cleanup cannot remove it.
    static void CleanupAtStartup(string baseDir)
    {
        if (loggingSettings.LogCleanupIntervalHours == 0)
        {
            Report("Log cleanup: disabled (LogCleanupIntervalHours=0).");
            return;
        }
        var marker = Path.Combine(baseDir, "log-cleanup-last-success.txt");
        var now = DateTime.UtcNow;
        try
        {
            if (File.Exists(marker))
            {
                var saved = File.ReadAllText(marker).Trim();
                if (DateTime.TryParse(saved, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lastUtc)
                    && lastUtc <= now)
                {
                    var nextUtc = lastUtc.AddHours(loggingSettings.LogCleanupIntervalHours);
                    if (now < nextUtc)
                    {
                        Report($"Log cleanup: skipped; last successful UTC={lastUtc:O}; next due UTC={nextUtc:O}; interval={loggingSettings.LogCleanupIntervalHours} hours");
                        return;
                    }
                }
                else
                    Report("Log cleanup: saved timestamp invalid or in future; running cleanup.");
            }
        }
        catch (Exception ex)
        {
            Report($"Log cleanup: unable to read schedule state; running cleanup; {ex.GetType().Name}: {ex.Message}");
        }
        if (!CleanupLogs())
        {
            Report("Log cleanup: unsuccessful; next startup will retry.");
            return;
        }
        try
        {
            // Write to a temporary file, then replace the marker to avoid partial timestamps.
            var temp = marker + "." + Environment.ProcessId + ".tmp";
            try
            {
                File.WriteAllText(temp, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                File.Move(temp, marker, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            Report($"Log cleanup: schedule updated; interval={loggingSettings.LogCleanupIntervalHours} hours.");
        }
        catch (Exception ex)
        {
            Report($"Log cleanup: unable to save schedule state; next startup will retry; {ex.GetType().Name}: {ex.Message}");
        }
    }
    // Explicit test only: no switcher connection or media upload.
    // A zero limit verifies that a >2 MB log stays in one file; 1-2 MB
    // limits retain the existing rotation diagnostic behaviour.
    static bool RunLogRotationDiagnostic()
    {
        const int maxTestLimitMB = 2;
        var configuredLimit = loggingSettings.MaxIndividualLogSizeMB;
        if (configuredLimit > maxTestLimitMB)
        {
            Report($"Log rotation test: NOT RUN; set MaxIndividualLogSizeMB to 0, 1 or 2 for this diagnostic (currently {configuredLimit}).");
            return false;
        }
        var startingPart = logPart;
        var startingPath = currentLogPath;
        var payload = new string('X', 1800);
        var bytesToWrite = (long)Math.Max(configuredLimit, 1) * 1024 * 1024 * 2 + 32768;
        var entries = (int)(bytesToWrite / payload.Length) + 20;
        Report($"Log rotation test: START; max individual size={configuredLimit} MB; entries planned={entries}; ATEM connection disabled.");
        for (int i = 0; i < entries; i++)
            Report($"Log rotation test: entry {i + 1}/{entries}; {payload}");
        Report($"Log rotation test: END; rotations={logPart - startingPart}; current={Path.GetFileName(currentLogPath)}");
        if (configuredLimit == 0)
        {
            // Flush before checking the physical file size, and verify no part was created.
            long actualBytes;
            try
            {
                lock (logLock)
                {
                    log?.Flush();
                    actualBytes = startingPath == null ? 0 : new FileInfo(startingPath).Length;
                }
            }
            catch (Exception ex)
            {
                Report($"Log rotation test: FAIL; unable to verify log size; {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            var success = logPart == startingPart &&
                          string.Equals(currentLogPath, startingPath, StringComparison.OrdinalIgnoreCase) &&
                          actualBytes > 2L * 1024 * 1024;
            Report($"Log rotation test: {(success ? "PASS" : "FAIL")}; rotation disabled; rotations={logPart - startingPart}; log bytes={actualBytes}; expected one log file larger than 2097152 bytes.");
            return success;
        }
        else
        {
            var success = logPart - startingPart >= 2;
            Report($"Log rotation test: {(success ? "PASS" : "FAIL")}; expected at least 2 rotations.");
            return success;
        }
    }

    sealed class Settings
    {
        public string AtemIpAddress { get; set; } = "10.79.30.200";
        public string ImageFolder { get; set; } = @"D:\Assets\ATEM";
        public string ImagePrefix { get; set; } = "";
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int LogRetentionDays { get; set; } = 365;
        public int MaxLogFolderSizeMB { get; set; } = 1024;
        public int LogCleanupIntervalHours { get; set; } = 24;
        public int MaxIndividualLogSizeMB { get; set; } = 10;
        public int AtemConnectionAttempts { get; set; } = 3;
        public int AtemConnectionRetryDelaySeconds { get; set; } = 5;
    }
    const int TimeoutMs = 120_000;
    const int ARGB = 0x61726762;
    static readonly Guid MediaPoolId = new("33D14916-8E2C-46CC-A63B-2C9213A572C1");

    [MTAThread]
    static int Main(string[] args)
    {
        // Store configuration and logs beside the EXE, not in the current working directory.
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Room Setup Loader");
        var logsDir = Path.Combine(baseDir, "Logs");
        var rotationTest = args.Length == 1 && string.Equals(args[0], "--test-log-rotation", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 0 && !rotationTest)
        {
            Console.Error.WriteLine("Unknown arguments. Supported diagnostic: --test-log-rotation");
            return 2;
        }
        var connectionAttempted = false;
        var connected = false;
        var matched = 0;
        var uploaded = 0;
        var failed = 0;
        var status = "FAILED";
        IDiscovery? discovery = null;
        IntPtr switcher = IntPtr.Zero;
        IMediaPool? pool = null;
        IStills? stills = null;
        bool locked = false;
        LockCallback? lockCallback = null;
        StillsCallback? transferCallback = null;
        try
        {
            Directory.CreateDirectory(logsDir);
            var logPath = Path.Combine(logsDir, $"RoomSetupLoader_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}.log");
            log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            logRoot = logsDir;
            currentLogPath = logPath;
            logBaseName = Path.GetFileNameWithoutExtension(logPath);
            Report("Run started. Log: " + logPath);
            Report("Application: Room Setup Loader");
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(Program).Assembly.GetName().Version?.ToString()
                ?? "Unknown";
            Report("Version: " + version);
            ReportInstalledAtemSoftwareVersion();
            var configPath = Path.Combine(baseDir, "config.json");
            if (!File.Exists(configPath)) throw new FileNotFoundException("Configuration not found", configPath);
            var config = JsonSerializer.Deserialize<Settings>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });
            if (config == null) throw new InvalidOperationException("Configuration is empty.");
            if (string.IsNullOrWhiteSpace(config.AtemIpAddress)) throw new InvalidOperationException("AtemIpAddress is empty.");
            if (string.IsNullOrWhiteSpace(config.ImageFolder)) throw new InvalidOperationException("ImageFolder is empty.");
            if (string.IsNullOrWhiteSpace(config.ImagePrefix)) throw new InvalidOperationException("ImagePrefix is empty.");
            if (config.Width <= 0 || config.Height <= 0) throw new InvalidOperationException("Width and Height must be positive.");
            if (config.LogRetentionDays < 0 || config.MaxLogFolderSizeMB < 0 || config.LogCleanupIntervalHours < 0 || config.MaxIndividualLogSizeMB < 0)
                throw new InvalidOperationException("Logging limits must be non-negative integers (0 disables the corresponding setting).");
            if (config.AtemConnectionAttempts < 1 || config.AtemConnectionAttempts > 20)
                throw new InvalidOperationException("AtemConnectionAttempts must be between 1 and 20 (includes the initial attempt).");
            if (config.AtemConnectionRetryDelaySeconds < 0 || config.AtemConnectionRetryDelaySeconds > 300)
                throw new InvalidOperationException("AtemConnectionRetryDelaySeconds must be between 0 and 300.");
            loggingSettings = config;
            ReportLoggingConfiguration(config);
            CleanupAtStartup(baseDir);
            if (rotationTest)
            {
                var passed = RunLogRotationDiagnostic();
                status = passed ? "SUCCESS (LOG ROTATION TEST)" : "FAILED (LOG ROTATION TEST)";
                return passed ? 0 : 1;
            }
            Width = config.Width; Height = config.Height;
            var folder = Environment.ExpandEnvironmentVariables(config.ImageFolder);
            Report($"Configuration: ATEM={config.AtemIpAddress}; Folder={folder}; Prefix={config.ImagePrefix}; Frame={Width}x{Height}; ConnectionAttempts={config.AtemConnectionAttempts}; RetryDelaySeconds={config.AtemConnectionRetryDelaySeconds}");
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Image folder not found: {folder}");

            // A matching name is Prefix + one or more digits + a supported extension.
            // E.g. prefix 'Still_' matches Still_01.png and Still_12.jpg, not Still_logo.png.
            var pattern = new Regex("^" + Regex.Escape(config.ImagePrefix) + "(?<number>[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var files = Directory.EnumerateFiles(folder)
                .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => new { File = f, Match = pattern.Match(Path.GetFileNameWithoutExtension(f)) })
                .Where(x => x.Match.Success)
                .Select(x => new { x.File, Number = long.Parse(x.Match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture) })
                .OrderBy(x => x.Number).ThenBy(x => Path.GetFileName(x.File), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            // A filename's numeric suffix is its one-based still-slot number.
            // Out-of-range slots are handled individually after reading the ATEM slot count.
            // Reject duplicate slot numbers before changing the Media Pool.
            var duplicates = files.GroupBy(x => x.Number).Where(g => g.Count() > 1).ToArray();
            if (duplicates.Length > 0)
                throw new InvalidOperationException("Multiple files target the same still slot: " +
                    string.Join("; ", duplicates.Select(g => $"slot {g.Key}: {string.Join(", ", g.Select(x => Path.GetFileName(x.File)))}")));
            matched = files.Length;
            Report($"File discovery: {(matched > 0 ? "SUCCESS" : "NO FILES")}; {matched} matching file(s).");
            foreach (var file in files) Report($"  Matched: {Path.GetFileName(file.File)} -> slot {file.Number}");
            if (matched == 0)
            {
                Report("INFO: No images matched the configured prefix + number naming scheme. Nothing to upload.");
                status = "NO FILES";
                return 0; // The finally block still writes the summary and closes the log.
            }

            connectionAttempted = true;
            Report($"Connecting to ATEM {config.AtemIpAddress}; attempts={config.AtemConnectionAttempts}; retry delay={config.AtemConnectionRetryDelaySeconds}s.");
            var clsid = new Guid("A9CDC765-3787-409D-A1E5-29F4F034A599");
            for (int attempt = 1; attempt <= config.AtemConnectionAttempts; attempt++)
            {
                IntPtr candidate = IntPtr.Zero;
                try
                {
                    Report($"Connection: attempt {attempt}/{config.AtemConnectionAttempts}; ATEM={config.AtemIpAddress}.");
                    // Recreate the discovery object for each attempt so failed SDK state is not reused.
                    var comType = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
                    discovery = (IDiscovery)Activator.CreateInstance(comType)!;
                    var hr = discovery.ConnectTo(config.AtemIpAddress, out candidate, out var reason);
                    if (hr < 0 || candidate == IntPtr.Zero)
                        throw new COMException($"ConnectTo failed; {DescribeConnectionFailure(reason)}; HRESULT=0x{hr:X8}", hr < 0 ? hr : unchecked((int)0x80004005));
                    switcher = candidate;
                    candidate = IntPtr.Zero;
                    connected = true;
                    Report($"Connection: SUCCESS; ATEM={config.AtemIpAddress}; attempt={attempt}/{config.AtemConnectionAttempts}.");
                    break;
                }
                catch (COMException ex)
                {
                    if (ex.ErrorCode == unchecked((int)0x80040154))
                    {
                        Report($"Connection: FAILED; ATEM={config.AtemIpAddress}; attempt={attempt}/{config.AtemConnectionAttempts}; required ATEM Switchers SDK COM component is not registered; HRESULT=0x80040154 (REGDB_E_CLASSNOTREG).");
                        Report("ERROR: ATEM Switchers SDK is missing or incorrectly registered. Install or repair the Blackmagic ATEM Switchers SDK/runtime. ATEM Switchers software versions tested with Room Setup Loader: 10.4.1 and 11.0.");
                        Report("ERROR: Download the required ATEM Switchers SDK files from Blackmagic Design support: https://www.blackmagicdesign.com/support/");
                        Report("ERROR: ATEM connection aborted: required SDK COM component unavailable; retries will not resolve this registration error. No images uploaded.");
                        throw;
                    }
                    Report($"Connection: FAILED; ATEM={config.AtemIpAddress}; attempt={attempt}/{config.AtemConnectionAttempts}; {ex.Message}");
                    if (attempt == config.AtemConnectionAttempts)
                    {
                        Report($"ERROR: ATEM connection failed after {attempt} attempt(s); no images uploaded.");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    // Unexpected SDK/COM initialization failures are logged and propagated, not retried.
                    Report($"Connection: FAILED; ATEM={config.AtemIpAddress}; attempt={attempt}/{config.AtemConnectionAttempts}; unexpected error: {ex}");
                    throw;
                }
                finally
                {
                    if (candidate != IntPtr.Zero) Marshal.Release(candidate);
                    if (!connected && discovery != null)
                    {
                        Marshal.ReleaseComObject(discovery);
                        discovery = null;
                    }
                }
                if (config.AtemConnectionRetryDelaySeconds > 0)
                {
                    Report($"Connection: retrying in {config.AtemConnectionRetryDelaySeconds} second(s).");
                    Thread.Sleep(TimeSpan.FromSeconds(config.AtemConnectionRetryDelaySeconds));
                }
            }
            var iid = MediaPoolId;
            Check(Marshal.QueryInterface(switcher, ref iid, out var poolPtr), "QueryInterface(IBMDSwitcherMediaPool)");
            try { pool = (IMediaPool)Marshal.GetTypedObjectForIUnknown(poolPtr, typeof(IMediaPool)); }
            finally { Marshal.Release(poolPtr); }
            Check(pool.GetStills(out var stillsPtr), "GetStills");
            try { stills = (IStills)Marshal.GetTypedObjectForIUnknown(stillsPtr, typeof(IStills)); }
            finally { Marshal.Release(stillsPtr); }
            Check(stills.GetCount(out var count), "GetCount");
            Report($"ATEM still slots available: {count}");
            // Invalid target slots are per-file failures, not a reason to abort valid uploads.
            var uploadFiles = files.Where(x => x.Number >= 1 && x.Number <= count).ToArray();
            foreach (var invalid in files.Where(x => x.Number < 1 || x.Number > count))
            {
                failed++;
                Report($"ERROR: Skipping {Path.GetFileName(invalid.File)}: requested still slot {invalid.Number} " +
                    $"is outside the ATEM's available range 1–{count}. Continuing with remaining images.");
            }
            if (uploadFiles.Length == 0)
            {
                Report("ERROR: No images target an available ATEM still slot. No uploads attempted.");
                status = "FAILED";
                return 1;
            }

            lockCallback = new LockCallback();
            transferCallback = new StillsCallback();
            Check(stills.AddCallback(transferCallback), "AddCallback");
            Check(stills.Lock(lockCallback), "Lock");
            if (!lockCallback.Ready.Wait(TimeoutMs)) throw new TimeoutException("Timed out waiting for Media Pool lock.");
            locked = true;
            Report("Media Pool lock obtained.");
            for (int i = 0; i < uploadFiles.Length; i++)
            {
                var filename = uploadFiles[i].File;
                var slot = (uint)uploadFiles[i].Number;
                var sdkIndex = slot - 1; // SDK indexes slots from zero.
                Report($"Uploading {Path.GetFileName(filename)} -> slot {slot} ({i + 1}/{uploadFiles.Length})");
                IntPtr framePtr = IntPtr.Zero;
                IFrame? frame = null;
                try
                {
                    Check(pool.CreateFrame(ARGB, (uint)Width, (uint)Height, out framePtr), "CreateFrame");
                    frame = (IFrame)Marshal.GetTypedObjectForIUnknown(framePtr, typeof(IFrame));
                    WriteImageToFrame(filename, frame);
                    transferCallback.Prepare();
                    Check(stills.Upload(sdkIndex, Path.GetFileNameWithoutExtension(filename), frame), "Upload");
                    if (!transferCallback.Done.Wait(TimeoutMs))
                    {
                        stills.CancelTransfer();
                        throw new TimeoutException($"Timed out uploading {filename}");
                    }
                    if (transferCallback.Result != 0x636D7074)
                        throw new Exception($"Transfer failed (event 0x{transferCallback.Result:X8}).");
                    Check(stills.IsValid(sdkIndex, out var valid), "IsValid");
                    if (valid == 0) throw new Exception("Still slot was not marked valid.");
                    uploaded++;
                    Report($"Slot {slot}: SUCCESS");
                }
                catch (Exception ex)
                {
                    failed++;
                    Report($"Slot {slot}: FAILED - {ex}");
                    // A failed transfer may leave an in-flight SDK operation;
                    // stop rather than risk overlapping transfers.
                    break;
                }
                finally
                {
                    if (frame != null) Marshal.ReleaseComObject(frame);
                    if (framePtr != IntPtr.Zero) Marshal.Release(framePtr);
                }
            }
            status = uploaded == matched && failed == 0 ? "SUCCESS" : uploaded > 0 ? "PARTIAL FAILURE" : "FAILED";
        }
        catch (Exception ex)
        {
            Report("ERROR: " + ex);
        }
        finally
        {
            if (stills != null)
            {
                if (locked && lockCallback != null)
                {
                    try { Check(stills.Unlock(lockCallback), "Unlock"); Report("Media Pool unlocked."); }
                    catch (Exception ex) { Report("Unlock warning: " + ex.Message); status = "FAILED"; }
                }
                if (transferCallback != null)
                    try { stills.RemoveCallback(transferCallback); } catch (Exception ex) { Report("RemoveCallback warning: " + ex.Message); }
                Marshal.ReleaseComObject(stills);
            }
            if (pool != null) Marshal.ReleaseComObject(pool);
            if (switcher != IntPtr.Zero) Marshal.Release(switcher);
            if (discovery != null) Marshal.ReleaseComObject(discovery);
            GC.KeepAlive(lockCallback);
            GC.KeepAlive(transferCallback);
            Report($"SUMMARY: {status}; Connection={(connected ? "SUCCESS" : connectionAttempted ? "FAILED" : "NOT ATTEMPTED")}; FilesFound={matched}; Uploaded={uploaded}; Failed={failed}; NotUploaded={matched - uploaded}");
            log?.Dispose();
            log = null;
        }
        return status is "SUCCESS" or "NO FILES" ? 0 : 1;
    }

    static string DescribeConnectionFailure(int reason)
    {
        // ATEM SDK failure reasons are four-character codes packed into an integer.
        // Preserve the raw code for diagnosis, even when the SDK adds new reasons.
        var chars = new[] { (char)((reason >> 24) & 0xff), (char)((reason >> 16) & 0xff),
                            (char)((reason >> 8) & 0xff), (char)(reason & 0xff) };
        var code = chars.All(c => c >= 32 && c <= 126) ? new string(chars) : "unknown";
        var explanation = code switch
        {
            "cfnr" => "connection failed: no response from ATEM (check address, power and network)",
            "cfip" => "connection failed: incompatible product or firmware",
            "cfrb" => "connection failed: ATEM is busy or rejected the connection",
            "cfsr" => "connection failed: state synchronization failed",
            "cfna" => "connection failed: no ATEM available at the address",
            _ => "connection failed: SDK reason not recognised"
        };
        return $"{explanation}; reason=0x{reason:X8} ('{code}')";
    }

    static void WriteImageToFrame(string filename, IFrame frame)
    {
        if (frame.GetWidth() != Width || frame.GetHeight() != Height)
            throw new InvalidOperationException("Unexpected frame dimensions.");
        Check(frame.GetBytes(out var destination), "Frame.GetBytes");
        var destStride = frame.GetRowBytes();
        if (destStride < Width * 4) throw new Exception("Unexpected frame row stride.");
        using var source = new Bitmap(filename);
        using var resized = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, new Rectangle(0, 0, Width, Height));
        }
        var data = resized.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Width * 4];
            for (var y = 0; y < Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                // Both GDI+ Format32bppArgb and the ATEM ARGB frame use
                // BGRA byte order in little-endian memory. Copy unchanged
                // to preserve R, G, B and the original alpha channel.
                Marshal.Copy(row, 0, IntPtr.Add(destination, y * destStride), row.Length);
            }
        }
        finally { resized.UnlockBits(data); }
    }

    static void Check(int hr, string operation)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr, new IntPtr(-1));
    }
}

[ComImport, Guid("28449053-AC7A-49EB-ACD2-D1E0C57DC627"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDiscovery
{
    [PreserveSig] int ConnectTo([MarshalAs(UnmanagedType.BStr)] string address, out IntPtr switcher, out int reason);
}

[ComImport, Guid("33D14916-8E2C-46CC-A63B-2C9213A572C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMediaPool
{
    [PreserveSig] int GetStills(out IntPtr stills);
    [PreserveSig] int GetClip(uint index, out IntPtr clip);
    [PreserveSig] int GetClipCount(out uint count);
    [PreserveSig] int CreateFrame(int pixelFormat, uint width, uint height, out IntPtr frame);
}

[ComImport, Guid("35A1F6A6-D317-4F89-A565-0F0BD414CF77"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IFrame
{
    [PreserveSig] int GetWidth();
    [PreserveSig] int GetHeight();
    [PreserveSig] int GetRowBytes();
    [PreserveSig] int GetPixelFormat();
    [PreserveSig] int GetBytes(out IntPtr buffer);
}

[ComImport, Guid("235C654C-7389-4D3E-AB46-D0078D065940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IStills
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int IsValid(uint index, out int valid);
    [PreserveSig] int GetName(uint index, [MarshalAs(UnmanagedType.BStr)] out string name);
    [PreserveSig] int SetName(uint index, [MarshalAs(UnmanagedType.BStr)] string name);
    [PreserveSig] int GetHash(uint index, IntPtr hash);
    [PreserveSig] int SetInvalid(uint index);
    [PreserveSig] int Lock(ILockCallback callback);
    [PreserveSig] int Unlock(ILockCallback callback);
    [PreserveSig] int Upload(uint index, [MarshalAs(UnmanagedType.BStr)] string name, IFrame frame);
    [PreserveSig] int Download(uint index);
    [PreserveSig] int CancelTransfer();
    [PreserveSig] int GetProgress(out double progress);
    [PreserveSig] int AddCallback(IStillsCallback callback);
    [PreserveSig] int RemoveCallback(IStillsCallback callback);
}

[Guid("56663D7A-85A8-4DA0-9B13-2A52D3C7740C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ILockCallback { [PreserveSig] int Obtained(); }

[Guid("7AF82DC6-9A43-4CD2-9712-585E6BA159BA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IStillsCallback { [PreserveSig] int Notify(int eventType, IntPtr frame, int index); }

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
sealed class LockCallback : ILockCallback
{
    public ManualResetEventSlim Ready { get; } = new(false);
    public int Obtained() { Ready.Set(); return 0; }
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
sealed class StillsCallback : IStillsCallback
{
    public ManualResetEventSlim Done { get; } = new(false);
    public int Result { get; private set; }
    public void Prepare() { Result = 0; Done.Reset(); }
    public int Notify(int eventType, IntPtr frame, int index)
    {
        if (eventType is 0x636D7074 or 0x636E636C or 0x6661696C)
        {
            Result = eventType;
            Done.Set();
        }
        return 0;
    }
}
