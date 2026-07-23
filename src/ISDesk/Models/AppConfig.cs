namespace ISDesk.Models;

public sealed class AppConfig
{
    public string BaseFolder { get; set; } = @"D:\Fences";
    public double DefaultOpacity { get; set; } = 0.75;
    public bool DefaultBlur { get; set; } = true;
    public List<FenceConfig> Fences { get; set; } = new();
}

public sealed class FenceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 260;
    public double Opacity { get; set; } = 0.75;
    public bool Blur { get; set; } = true;
    public int ActiveTab { get; set; }
    public List<TabConfig> Tabs { get; set; } = new();
}

public sealed class TabConfig
{
    public string Title { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public int IconSize { get; set; } = 32;
}
