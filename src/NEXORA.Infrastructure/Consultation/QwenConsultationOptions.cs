namespace NEXORA.Infrastructure.Consultation;

public sealed class QwenConsultationOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "qwen/qwen3-omni-flash:free";
}
