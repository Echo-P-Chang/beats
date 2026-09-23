namespace Beats.Production.Middleware.Ai;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.1:8b";
    public int TimeoutSeconds { get; set; } = 120;
    public double Temperature { get; set; } = 0.7;
    public int NumPredict { get; set; } = 2048;
}
