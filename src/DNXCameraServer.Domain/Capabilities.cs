using System.Text.Json.Serialization;

namespace DNXCameraServer.Domain;

public sealed class CameraCapabilities
{
    public VideoCapabilities Video { get; set; } = new();
    public AudioCapabilities Audio { get; set; } = new();
    public QualityCapabilities Quality { get; set; } = new();
    public bool Snapshot { get; set; } = true;
    public bool Wakeup { get; set; }
    public bool Ptz { get; set; }
    public ZoomCapabilities Zoom { get; set; } = new();

    /// <summary>Origen de la ficha: model_db | default | probe | override.</summary>
    public string Source { get; set; } = "default";
}

public sealed class VideoCapabilities
{
    public List<string> Codecs { get; set; } = new() { "h264" };
    public int Lenses { get; set; } = 1;
}

public sealed class AudioCapabilities
{
    [JsonPropertyName("out")] public bool Out { get; set; } = true;
    public bool Talk { get; set; }
}

public sealed class QualityCapabilities
{
    public List<string> Profiles { get; set; } = new() { StreamProfiles.Hd, StreamProfiles.Sd, StreamProfiles.Auto };
    public string Native { get; set; } = StreamProfiles.Hd;
    public int HdIndex { get; set; } = 2;
}

public sealed class ZoomCapabilities
{
    public bool Optical { get; set; }
    public bool DigitalInViewer { get; set; } = true;
}

public sealed class ModelDefinition
{
    public string Model { get; set; } = "";
    public string Protocol { get; set; } = "miss";
    public CameraCapabilities Capabilities { get; set; } = new();
}
