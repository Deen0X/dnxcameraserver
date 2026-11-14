using System.Text.Json;
using DNXCameraServer.Domain;
using DNXCameraServer.Web.Configuration;

namespace DNXCameraServer.Web.Cameras;

/// <summary>
/// Resuelve capacidades por modelo: ficha curada (data/models/*.json) →
/// heurística → override manual. Nunca inventa funciones que go2rtc no tenga.
/// </summary>
public sealed class CapabilityService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly HashSet<string> LegacyModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "lumi.camera.gwagl01",
        "chuangmi.camera.ipc019e",
        "loock.cateye.v01",
        "chuangmi.camera.xiaobai",
        "isa.camera.isc5"
    };

    private static readonly HashSet<string> HdIndex3Models = new(StringComparer.OrdinalIgnoreCase)
    {
        "chuangmi.camera.046c04",
        "chuangmi.camera.72ac1"
    };

    private readonly Dictionary<string, ModelDefinition> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<CapabilityService> _log;

    public CapabilityService(DnxOptions options, ILogger<CapabilityService> log)
    {
        _log = log;
        Load(options.EffectiveModelsDir);
    }

    private void Load(string dir)
    {
        if (!Directory.Exists(dir))
        {
            _log.LogInformation("Sin fichas de modelo en {Dir}; se usará la heurística.", dir);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var def = JsonSerializer.Deserialize<ModelDefinition>(File.ReadAllText(file), Json);
                if (def is not null && !string.IsNullOrWhiteSpace(def.Model))
                {
                    def.Capabilities.Source = "model_db";
                    _models[def.Model] = def;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Ficha de modelo inválida: {File}", file);
            }
        }

        _log.LogInformation("Cargadas {Count} fichas de modelo.", _models.Count);
    }

    public string ProtocolFor(string model) =>
        _models.TryGetValue(model, out var def) ? def.Protocol :
        LegacyModels.Contains(model) ? "legacy" : "miss";

    public CameraCapabilities Resolve(string model, string? overrideJson = null)
    {
        CameraCapabilities caps;

        if (_models.TryGetValue(model, out var def))
        {
            caps = Clone(def.Capabilities);
        }
        else
        {
            var protocol = ProtocolFor(model);
            caps = new CameraCapabilities
            {
                Video = new VideoCapabilities { Codecs = ["h264", "h265"], Lenses = 1 },
                Audio = new AudioCapabilities { Out = true, Talk = protocol == "miss" },
                Quality = new QualityCapabilities
                {
                    Profiles = [StreamProfiles.Hd, StreamProfiles.Sd, StreamProfiles.Auto],
                    Native = StreamProfiles.Hd,
                    HdIndex = HdIndex3Models.Contains(model) ? 3 : 2
                },
                Snapshot = true,
                Wakeup = model.Contains(".cateye.", StringComparison.OrdinalIgnoreCase),
                Ptz = false,
                Zoom = new ZoomCapabilities { Optical = false, DigitalInViewer = true },
                Source = "default"
            };
        }

        if (!string.IsNullOrWhiteSpace(overrideJson))
        {
            try
            {
                var patch = JsonSerializer.Deserialize<CameraCapabilities>(overrideJson, Json);
                if (patch is not null)
                {
                    caps = Merge(caps, patch);
                    caps.Source = "override";
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Override de capacidades inválido para {Model}.", model);
            }
        }

        return caps;
    }

    private static CameraCapabilities Clone(CameraCapabilities c) =>
        JsonSerializer.Deserialize<CameraCapabilities>(JsonSerializer.Serialize(c, Json), Json)
        ?? new CameraCapabilities();

    private static CameraCapabilities Merge(CameraCapabilities baseCaps, CameraCapabilities patch) =>
        new()
        {
            Video = patch.Video.Codecs.Count > 0 ? patch.Video : baseCaps.Video,
            Audio = patch.Audio.Talk || !patch.Audio.Out ? patch.Audio : baseCaps.Audio,
            Quality = patch.Quality.Profiles.Count > 0 ? patch.Quality : baseCaps.Quality,
            Snapshot = patch.Snapshot || baseCaps.Snapshot,
            Wakeup = patch.Wakeup || baseCaps.Wakeup,
            Ptz = patch.Ptz,
            Zoom = patch.Zoom.Optical ? patch.Zoom : baseCaps.Zoom,
            Source = "override"
        };
}
