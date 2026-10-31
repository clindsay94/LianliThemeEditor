using System.IO.Compression;
using System.Text;
using ThemeEditorCSharp.Models;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class ThemePackageValidationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lltests-" + Guid.NewGuid().ToString("N"));
    private readonly ThemePackageValidationService _service = new();

    public ThemePackageValidationServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Zip(params (string Name, string Content)[] entries) =>
        Zip(entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content))).ToArray());

    private string Zip(params (string Name, byte[] Content)[] entries)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = entry.Open();
            writer.Write(content, 0, content.Length);
        }

        return path;
    }

    private static string Manifest(
        string device = "hydroshift-ii-lcd-s",
        string id = "tpl",
        string template = "template/theme.template",
        string extra = "") =>
        $$"""{"DeviceModel":"{{device}}","TemplateId":"{{id}}","TemplateFile":"{{template}}"{{extra}}}""";

    private static string[] Messages(ThemeValidationResult r, string severity) =>
        r.Issues.Where(i => i.Severity == severity).Select(i => i.Message).ToArray();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_path_is_an_error(string path)
    {
        var result = _service.Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains("was not found", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Missing_file_is_an_error()
    {
        var result = _service.Validate(Path.Combine(_dir, "nope.zip"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Valid_manifest_package_passes()
    {
        var path = Zip(("manifest.json", Manifest()), ("template/theme.template", "x"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal("hydroshift-ii-lcd-s", result.DeviceModel);
        Assert.Equal("tpl", result.TemplateId);
        Assert.Equal("template/theme.template", result.TemplateFile);
        Assert.Equal("Package checks passed.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Legacy_lowercase_manifest_keys_are_read()
    {
        var path = Zip(
            ("manifest.json", """{"deviceModel":"vm-9.2-inch","id":"legacy","template":"t/a.template"}"""),
            ("t/a.template", "x"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal("vm-9.2-inch", result.DeviceModel);
        Assert.Equal("legacy", result.TemplateId);
    }

    [Fact]
    public void Missing_device_uses_fallback_device_model()
    {
        var path = Zip(("manifest.json", """{"TemplateId":"t","TemplateFile":"a.template"}"""), ("a.template", "x"));

        Assert.False(_service.Validate(path).IsValid);
        Assert.True(_service.Validate(path, null, "vm-9.2-inch").IsValid);
    }

    [Fact]
    public void Unsupported_device_is_an_error_but_matching_is_case_insensitive()
    {
        var bad = Zip(("manifest.json", Manifest(device: "toaster")), ("template/theme.template", "x"));
        var upper = Zip(("manifest.json", Manifest(device: "VM-9.2-INCH")), ("template/theme.template", "x"));

        Assert.Contains("not supported", Assert.Single(Messages(_service.Validate(bad), "Error")));
        Assert.True(_service.Validate(upper).IsValid);
    }

    [Fact]
    public void Empty_template_id_is_an_error()
    {
        var path = Zip(("manifest.json", Manifest(id: "")), ("template/theme.template", "x"));

        Assert.Contains("Template ID is empty.", Messages(_service.Validate(path), "Error"));
    }

    [Fact]
    public void Unsupported_format_version_is_an_error()
    {
        var path = Zip(("manifest.json", Manifest(extra: ",\"FormatVersion\":2")), ("template/theme.template", "x"));

        Assert.Contains("Unsupported package version: 2.", Messages(_service.Validate(path), "Error"));
    }

    [Fact]
    public void Format_version_one_is_accepted()
    {
        var path = Zip(("manifest.json", Manifest(extra: ",\"formatVersion\":1")), ("template/theme.template", "x"));

        Assert.True(_service.Validate(path).IsValid);
    }

    [Fact]
    public void Missing_template_entry_is_an_error()
    {
        var path = Zip(("manifest.json", Manifest()));

        Assert.Contains("The template file is missing or unsafe.", Messages(_service.Validate(path), "Error"));
    }

    [Fact]
    public void Template_reference_with_backslashes_resolves_to_forward_slash_entry()
    {
        var path = Zip(("manifest.json", Manifest(template: "template\\\\theme.template")), ("template/theme.template", "x"));

        Assert.True(_service.Validate(path).IsValid);
    }

    [Fact]
    public void Template_reference_matches_entry_case_insensitively()
    {
        var path = Zip(("manifest.json", Manifest(template: "TEMPLATE/Theme.Template")), ("template/theme.template", "x"));

        Assert.True(_service.Validate(path).IsValid);
    }

    [Theory]
    [InlineData("../evil.template")]
    [InlineData("a/../../evil.template")]
    [InlineData("/abs/evil.template")]
    [InlineData("C:/evil.template")]
    public void Unsafe_template_reference_is_an_error_even_if_entry_exists(string template)
    {
        var path = Zip(("manifest.json", Manifest(template: template)), (template, "x"));

        var errors = Messages(_service.Validate(path), "Error");

        Assert.Contains("The template file is missing or unsafe.", errors);
    }

    [Fact]
    public void Inner_dotdot_that_stays_inside_root_is_not_resolved_against_entries()
    {
        var path = Zip(("manifest.json", Manifest(template: "a/../template/theme.template")), ("template/theme.template", "x"));

        // Passes the path-safety check but entry lookup is by literal name, so it is reported missing.
        Assert.Contains("The template file is missing or unsafe.", Messages(_service.Validate(path), "Error"));
    }

    [Theory]
    [InlineData("../x.txt")]
    [InlineData("/rooted.txt")]
    public void Unsafe_archive_entry_names_are_reported(string entryName)
    {
        var path = Zip(("manifest.json", Manifest()), ("template/theme.template", "x"), (entryName, "boom"));

        var errors = Messages(_service.Validate(path), "Error");

        Assert.Contains($"Unsafe package path: {entryName}", errors);
    }

    [Fact]
    public void Missing_referenced_image_is_an_error()
    {
        var path = Zip(
            ("manifest.json", Manifest(extra: ",\"ImageFiles\":[\"img/a.png\",\"img/missing.png\",\"../x.png\"]")),
            ("template/theme.template", "x"),
            ("img/a.png", "p"));

        var errors = Messages(_service.Validate(path), "Error");

        Assert.Equal(
            new[] { "Referenced image is missing: img/missing.png", "Referenced image is missing: ../x.png" },
            errors);
    }

    [Fact]
    public void Fonts_must_exist_and_be_ttf_or_otf()
    {
        var path = Zip(
            ("manifest.json", Manifest(extra: ",\"FontFiles\":[\"f/a.TTF\",\"f/b.otf\",\"f/c.woff\",\"f/gone.ttf\"]")),
            ("template/theme.template", "x"),
            ("f/a.TTF", "f"), ("f/b.otf", "f"), ("f/c.woff", "f"));

        var errors = Messages(_service.Validate(path), "Error");

        Assert.Equal(
            new[]
            {
                "Referenced font is missing or unsupported: f/c.woff",
                "Referenced font is missing or unsupported: f/gone.ttf"
            },
            errors);
    }

    [Fact]
    public void Declared_background_that_is_missing_is_a_warning_only()
    {
        var path = Zip(("manifest.json", Manifest(extra: ",\"BackgroundFile\":\"bg.mp4\"")), ("template/theme.template", "x"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "Background media is missing." }, Messages(result, "Warning"));
    }

    [Fact]
    public void Present_background_with_odd_extension_warns_about_format()
    {
        var path = Zip(
            ("manifest.json", Manifest(extra: ",\"BackgroundFile\":\"bg.bmp\"")),
            ("template/theme.template", "x"),
            ("bg.bmp", "b"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "The background format may not be supported by L-Connect." }, Messages(result, "Warning"));
    }

    [Fact]
    public void Present_background_with_supported_extension_is_clean()
    {
        var path = Zip(
            ("manifest.json", Manifest(extra: ",\"BackgroundFile\":\"bg.PNG\"")),
            ("template/theme.template", "x"),
            ("bg.PNG", "b"));

        Assert.Equal("Package checks passed.", Assert.Single(_service.Validate(path).Issues).Message);
    }

    [Fact]
    public void Undeclared_background_warns_when_template_references_external_media()
    {
        var path = Zip(("manifest.json", Manifest()), ("template/theme.template", "GraphAnimation = bg.mp4"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "Background media is missing." }, Messages(result, "Warning"));
    }

    [Fact]
    public void Undeclared_background_is_fine_when_template_embeds_a_png()
    {
        var content = Encoding.ASCII.GetBytes("GraphAnimation .png ")
            .Concat(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 }).ToArray();
        var path = Zip(("manifest.json", Encoding.UTF8.GetBytes(Manifest())), ("template/theme.template", content));

        Assert.Equal("Package checks passed.", Assert.Single(_service.Validate(path).Issues).Message);
    }

    [Fact]
    public void Already_installed_template_id_warns_case_insensitively()
    {
        var path = Zip(("manifest.json", Manifest(id: "TPL")), ("template/theme.template", "x"));

        var result = _service.Validate(path, new[] { "other", "tpl" });

        Assert.True(result.IsValid);
        Assert.Contains("already installed", Assert.Single(Messages(result, "Warning")));
    }

    [Fact]
    public void Installed_warning_suppresses_the_passed_message()
    {
        var path = Zip(("manifest.json", Manifest()), ("template/theme.template", "x"));

        var result = _service.Validate(path, new[] { "tpl" });

        Assert.DoesNotContain(result.Issues, i => i.Message == "Package checks passed.");
    }

    [Fact]
    public void Zip_without_manifest_or_template_is_an_error()
    {
        var path = Zip(("readme.txt", "hi"));

        Assert.Equal("The ZIP does not contain a template file.", Assert.Single(Messages(_service.Validate(path), "Error")));
    }

    [Fact]
    public void Lconnect_zip_without_manifest_uses_template_name_as_id()
    {
        var path = Zip(("My Theme.template", "x"), ("bg.mp4", "v"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal("My Theme", result.TemplateId);
        Assert.Equal("My Theme.template", result.TemplateFile);
        Assert.Equal("L-Connect ZIP package detected.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Lconnect_zip_without_media_warns()
    {
        var path = Zip(("sub/t.template", "x"));

        var result = _service.Validate(path);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "Background media is missing." }, Messages(result, "Warning"));
    }

    [Fact]
    public void Lconnect_zip_with_unsafe_template_path_is_an_error()
    {
        var path = Zip(("../evil.template", "x"));

        Assert.Equal("The template file path is unsafe.", Assert.Single(Messages(_service.Validate(path), "Error")));
    }
}
