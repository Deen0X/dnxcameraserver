using System.Diagnostics;
using DNXCameraServer.Web.Configuration;

namespace DNXCameraServer.Web.Go2Rtc;

/// <summary>
/// Supervisa el proceso go2rtc: genera config, arranca, reinicia con backoff y
/// lo apaga ordenadamente. Si el binario no existe, la app arranca en modo
/// degradado (útil en desarrollo sin go2rtc).
/// </summary>
public sealed class Go2RtcSupervisor(
    DnxOptions options,
    ILogger<Go2RtcSupervisor> log) : BackgroundService
{
    private Process? _process;
    private volatile bool _available;

    public bool IsAvailable => _available;
    public bool ProcessRunning => _process is { HasExited: false };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!File.Exists(options.Go2RtcBinary))
        {
            log.LogWarning(
                "Binario go2rtc no encontrado en {Path}. La app arranca en modo degradado.",
                options.Go2RtcBinary);
            return;
        }

        try
        {
            WriteConfig();
        }
        catch (Exception ex)
        {
            // Un fallo de permisos no debe tumbar la aplicación: se degrada.
            log.LogError(ex, "No se pudo escribir la config de go2rtc en {Path}.", options.EffectiveGo2RtcConfig);
            return;
        }

        var attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _process = StartProcess();
                _available = true;
                log.LogInformation("go2rtc arrancado (pid {Pid}).", _process.Id);
                await _process.WaitForExitAsync(stoppingToken);
                _available = false;
                log.LogWarning("go2rtc terminó con código {Code}.", _process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _available = false;
                log.LogError(ex, "Error gestionando go2rtc.");
            }

            attempt++;
            var delay = TimeSpan.FromSeconds(attempt switch
            {
                <= 3 => 1,
                <= 6 => 5,
                <= 10 => 10,
                _ => 30
            });

            log.LogInformation("Reintentando go2rtc en {Delay}s.", delay.TotalSeconds);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await StopAsync();
    }

    private Process StartProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = options.Go2RtcBinary,
            Arguments = $"-c \"{options.EffectiveGo2RtcConfig}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.LogDebug("[go2rtc] {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.LogWarning("[go2rtc] {Line}", e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private void WriteConfig()
    {
        var candidate = string.IsNullOrWhiteSpace(options.WebRtcCandidate) ? "" : options.WebRtcCandidate;
        var apiUri = new Uri(options.Go2RtcApi);
        var rtspHostPort = options.Go2RtcRtsp;

        var yaml = $"""
            log:
              level: info

            api:
              listen: "{apiUri.Host}:{apiUri.Port}"

            rtsp:
              listen: "{rtspHostPort}"
              default_query: video&audio

            webrtc:
              listen: ":8555"
              ice_servers: []
            {(candidate.Length > 0 ? $"  candidates:\n    - \"{candidate}\"" : "")}
            """;

        // Los tokens de Xiaomi los escribe go2rtc en este mismo fichero. Al
        // regenerar la config no deben perderse (ADR-011).
        yaml = PreserveXiaomiSection(yaml);

        var dir = Path.GetDirectoryName(options.EffectiveGo2RtcConfig);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(options.EffectiveGo2RtcConfig, yaml);
        try
        {
            File.SetUnixFileMode(options.EffectiveGo2RtcConfig,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // En Windows no aplica.
        }

        log.LogInformation("Config de go2rtc escrita en {Path}.", options.EffectiveGo2RtcConfig);
    }

    /// <summary>Conserva el bloque `xiaomi:` (tokens) de la config existente.</summary>
    private string PreserveXiaomiSection(string generated)
    {
        try
        {
            var path = options.EffectiveGo2RtcConfig;
            if (!File.Exists(path)) return generated;

            var lines = File.ReadAllLines(path);
            var start = Array.FindIndex(lines, l =>
                l.StartsWith("xiaomi:", StringComparison.OrdinalIgnoreCase));
            if (start < 0) return generated;

            var block = new System.Text.StringBuilder();
            for (var i = start; i < lines.Length; i++)
            {
                var line = lines[i];
                if (i > start && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#'))
                    break;
                block.AppendLine(line);
            }

            return generated.TrimEnd() + "\n\n" + block.ToString();
        }
        catch
        {
            return generated;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var process = _process;
        if (process is null || process.HasExited) return;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                using var kill = Process.Start(new ProcessStartInfo("kill", $"-TERM {process.Id}"));
                kill?.WaitForExit(3000);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            else
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "No se pudo detener go2rtc ordenadamente; forzando cierre.");
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        _available = false;
    }
}
