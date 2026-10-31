using ThemeEditorCSharp.Models;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class ThemePackageManifestNormalizerTests
{
    [Fact]
    public void Empty_manifest_takes_fallback_device_and_template_id()
    {
        var manifest = new ThemePackageManifest();

        var changed = ThemePackageManifestNormalizer.Normalize(manifest, "vm-9.2-inch", "tpl-1");

        Assert.True(changed);
        Assert.Equal("vm-9.2-inch", manifest.DeviceModel);
        Assert.Equal("tpl-1", manifest.TemplateId);
    }

    [Fact]
    public void Legacy_fields_win_over_fallbacks()
    {
        var manifest = new ThemePackageManifest { deviceModel = "legacy-dev", id = "legacy-id" };

        var changed = ThemePackageManifestNormalizer.Normalize(manifest, "fallback-dev", "fallback-id");

        Assert.True(changed);
        Assert.Equal("legacy-dev", manifest.DeviceModel);
        Assert.Equal("legacy-id", manifest.TemplateId);
    }

    [Fact]
    public void Existing_values_are_kept_and_report_unchanged()
    {
        var manifest = new ThemePackageManifest
        {
            DeviceModel = "dev",
            TemplateId = "id",
            TemplateFile = "custom/file.template",
            BackgroundFile = "bg.png",
            PreviewFile = "pv.png",
            UniversalOrientation = "portrait",
            deviceModel = "other",
            id = "other",
            template = "other.template",
            background = "other.png",
            preview = "other.png",
            universalOrientation = "landscape"
        };

        var changed = ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.False(changed);
        Assert.Equal("dev", manifest.DeviceModel);
        Assert.Equal("id", manifest.TemplateId);
        Assert.Equal("custom/file.template", manifest.TemplateFile);
        Assert.Equal("bg.png", manifest.BackgroundFile);
        Assert.Equal("pv.png", manifest.PreviewFile);
        Assert.Equal("portrait", manifest.UniversalOrientation);
    }

    [Fact]
    public void Whitespace_values_count_as_empty()
    {
        var manifest = new ThemePackageManifest { DeviceModel = "  ", TemplateId = "\t", deviceModel = "d", id = "i" };

        ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.Equal("d", manifest.DeviceModel);
        Assert.Equal("i", manifest.TemplateId);
    }

    [Theory]
    [InlineData("template/theme.template")]
    [InlineData("TEMPLATE/THEME.TEMPLATE")]
    [InlineData("")]
    public void Default_or_empty_template_file_is_replaced_by_legacy_template(string current)
    {
        var manifest = new ThemePackageManifest { TemplateFile = current, template = "legacy/x.template" };

        var changed = ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.True(changed);
        Assert.Equal("legacy/x.template", manifest.TemplateFile);
    }

    [Fact]
    public void Default_template_file_stays_when_no_legacy_template()
    {
        var manifest = new ThemePackageManifest();

        var changed = ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.False(changed);
        Assert.Equal("template/theme.template", manifest.TemplateFile);
    }

    [Fact]
    public void Legacy_background_preview_and_orientation_are_copied()
    {
        var manifest = new ThemePackageManifest
        {
            background = "bg.mp4",
            preview = "p.png",
            universalOrientation = "Portrait"
        };

        var changed = ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.True(changed);
        Assert.Equal("bg.mp4", manifest.BackgroundFile);
        Assert.Equal("p.png", manifest.PreviewFile);
        Assert.Equal("portrait", manifest.UniversalOrientation);
    }

    [Fact]
    public void Orientation_is_normalized_even_when_nothing_else_changes()
    {
        var manifest = new ThemePackageManifest { UniversalOrientation = "LANDSCAPE" };

        var changed = ThemePackageManifestNormalizer.Normalize(manifest);

        Assert.False(changed);
        Assert.Equal("landscape", manifest.UniversalOrientation);
    }

    [Fact]
    public void Changed_flag_is_reset_when_template_id_ends_up_empty()
    {
        var manifest = new ThemePackageManifest();

        var changed = ThemePackageManifestNormalizer.Normalize(manifest, "dev", "");

        Assert.Equal("dev", manifest.DeviceModel);
        // Current behaviour; looks wrong: the TemplateId step overwrites `changed` set by the DeviceModel step.
        Assert.False(changed);
    }

    [Theory]
    [InlineData("portrait", "portrait")]
    [InlineData("Portrait", "portrait")]
    [InlineData("LANDSCAPE", "landscape")]
    [InlineData("landscape", "landscape")]
    [InlineData(" portrait ", "")]
    [InlineData("square", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeUniversalOrientation_maps_known_values(string? input, string expected)
    {
        Assert.Equal(expected, ThemePackageManifestNormalizer.NormalizeUniversalOrientation(input));
    }
}
