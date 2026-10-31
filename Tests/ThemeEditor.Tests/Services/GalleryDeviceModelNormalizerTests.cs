using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class GalleryDeviceModelNormalizerTests
{
    [Theory]
    [InlineData("hydroshift-c", "hydroshift-ii-lcd-c")]
    [InlineData("hydroshift-ii-c", "hydroshift-ii-lcd-c")]
    [InlineData("hydroshift-ii-lcd-c", "hydroshift-ii-lcd-c")]
    [InlineData("hydroshift-s", "hydroshift-ii-lcd-s")]
    [InlineData("hydroshift-ii-s", "hydroshift-ii-lcd-s")]
    [InlineData("hydroshift-ii-lcd-s", "hydroshift-ii-lcd-s")]
    [InlineData("universal-screen-8.8", "universal-screen-8.8-inch")]
    [InlineData("universal-8.8", "universal-screen-8.8-inch")]
    [InlineData("universal-screen-8.8-inch", "universal-screen-8.8-inch")]
    [InlineData("vm-9.2", "vm-9.2-inch")]
    [InlineData("vm-9.2-lcd", "vm-9.2-inch")]
    [InlineData("vm-9.2-inch", "vm-9.2-inch")]
    [InlineData("hydroshift-ii-oled", "hydroshift-ii-oled-curve")]
    [InlineData("hydroshift-oled-curve", "hydroshift-ii-oled-curve")]
    [InlineData("oled-curve", "hydroshift-ii-oled-curve")]
    [InlineData("hydroshift-ii-oled-curve", "hydroshift-ii-oled-curve")]
    public void Known_aliases_map_to_canonical_model(string input, string expected)
    {
        Assert.Equal(expected, GalleryDeviceModelNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("  HydroShift_C  ", "hydroshift-ii-lcd-c")]
    [InlineData("Universal Screen 8.8", "universal-screen-8.8-inch")]
    [InlineData("VM_9.2", "vm-9.2-inch")]
    [InlineData("oled__curve", "hydroshift-ii-oled-curve")]
    [InlineData("oled - curve", "hydroshift-ii-oled-curve")]
    public void Case_whitespace_underscores_and_repeated_dashes_are_folded(string input, string expected)
    {
        Assert.Equal(expected, GalleryDeviceModelNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Some_New Device", "some-new-device")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Unknown_values_are_returned_cleaned_but_unmapped(string input, string expected)
    {
        Assert.Equal(expected, GalleryDeviceModelNormalizer.Normalize(input));
    }

    [Fact]
    public void Null_input_returns_empty()
    {
        Assert.Equal("", GalleryDeviceModelNormalizer.Normalize(null!));
    }

    [Fact]
    public void Constants_match_canonical_values()
    {
        Assert.Equal("universal-screen-8.8-inch", GalleryDeviceModelNormalizer.UniversalScreenDeviceModel);
        Assert.Equal("vm-9.2-inch", GalleryDeviceModelNormalizer.Vm92DeviceModel);
        Assert.Equal("hydroshift-ii-oled-curve", GalleryDeviceModelNormalizer.OledCurveDeviceModel);
    }
}
