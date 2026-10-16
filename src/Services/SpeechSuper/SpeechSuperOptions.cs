namespace Services;

public sealed class SpeechSuperOptions
{
    public const string SectionName = "SpeechSuper";

    public string AppKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://api.speechsuper.com";
    public string CoreType { get; set; } = "speak.eval.pro";
}
