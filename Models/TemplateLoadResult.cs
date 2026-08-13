using System.Collections.ObjectModel;

namespace ThemeEditorCSharp.Models;

public sealed class TemplateLoadResult
{
    public string TemplateId { get; init; } = "";
    public string TemplatePath { get; init; } = "";
    public string Background { get; set; } = "";
    public string BackgroundPath { get; set; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    // Null when the template does not store an orientation flag.
    public bool? IsLandscape { get; init; }
    public ObservableCollection<LayerRow> Layers { get; } = new();
}
