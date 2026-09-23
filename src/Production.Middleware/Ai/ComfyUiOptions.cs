namespace Beats.Production.Middleware.Ai;

public sealed class ComfyUiOptions
{
    public const string SectionName = "ComfyUI";

    public string BaseUrl { get; set; } = "http://localhost:8188";
    public int TimeoutSeconds { get; set; } = 900;
    public int PollIntervalSeconds { get; set; } = 2;
    public string Model { get; set; } = "flux1-schnell-Q4_K_S.gguf";
    public string ClipName1 { get; set; } = "clip_l.safetensors";
    public string ClipName2 { get; set; } = "t5xxl_fp8_e4m3fn.safetensors";
    public string VaeName { get; set; } = "ae.safetensors";
    public int Width { get; set; } = 768;
    public int Height { get; set; } = 768;
    public int Steps { get; set; } = 4;
    public double Cfg { get; set; } = 1.0;
    public double Guidance { get; set; } = 3.5;
    public string SamplerName { get; set; } = "euler";
    public string Scheduler { get; set; } = "simple";
    public int MaxScenes { get; set; } = 3;
}
