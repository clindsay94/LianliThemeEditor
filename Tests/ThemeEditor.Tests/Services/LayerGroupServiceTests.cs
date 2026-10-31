using ThemeEditorCSharp.Models;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class LayerGroupServiceTests
{
    private readonly LayerGroupService _service = new();

    private static LayerGroup Group(string id, string name, string color = "#111111") =>
        new() { Id = id, Name = name, Color = color };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GetUniqueName_blank_request_defaults_to_Group(string? requested)
    {
        Assert.Equal("Group", _service.GetUniqueName(Array.Empty<LayerGroup>(), requested!));
    }

    [Fact]
    public void GetUniqueName_trims_and_returns_free_name_unchanged()
    {
        Assert.Equal("Header", _service.GetUniqueName(new[] { Group("1", "Other") }, "  Header  "));
    }

    [Fact]
    public void GetUniqueName_appends_next_free_suffix_case_insensitively()
    {
        var groups = new[] { Group("1", "Header"), Group("2", "header (2)") };

        Assert.Equal("HEADER (3)", _service.GetUniqueName(groups, "HEADER"));
    }

    [Fact]
    public void GetUniqueName_ignores_the_excluded_group()
    {
        var groups = new[] { Group("1", "Header") };

        Assert.Equal("Header", _service.GetUniqueName(groups, "Header", "1"));
        Assert.Equal("Header (2)", _service.GetUniqueName(groups, "Header", "other"));
    }

    [Fact]
    public void Assign_copies_group_fields_and_returns_distinct_previous_ids()
    {
        var group = Group("g", "New", "#ABCDEF");
        var layers = new List<LayerRow>
        {
            new() { GroupId = "a" },
            new() { GroupId = "a" },
            new() { GroupId = "b" },
            new() { GroupId = "" },
            new() { GroupId = "g" }
        };

        var previous = _service.Assign(layers, group);

        Assert.Equal(new[] { "a", "b" }, previous);
        Assert.All(layers, l =>
        {
            Assert.Equal("g", l.GroupId);
            Assert.Equal("New", l.GroupName);
            Assert.Equal("#ABCDEF", l.GroupColor);
        });
    }

    [Fact]
    public void Assign_with_no_layers_returns_empty()
    {
        Assert.Empty(_service.Assign(Array.Empty<LayerRow>(), Group("g", "N")));
    }

    [Fact]
    public void Remove_clears_group_and_resets_color_returning_previous_ids()
    {
        var layers = new[]
        {
            new LayerRow { GroupId = "a", GroupName = "A", GroupColor = "#000000" },
            new LayerRow { GroupId = "a", GroupName = "A", GroupColor = "#000000" },
            new LayerRow { GroupId = "b", GroupName = "B", GroupColor = "#000000" },
            new LayerRow()
        };

        var previous = _service.Remove(layers);

        Assert.Equal(new[] { "a", "b" }, previous);
        Assert.All(layers, l =>
        {
            Assert.Equal("", l.GroupId);
            Assert.Equal("", l.GroupName);
            Assert.Equal("#246FF2", l.GroupColor);
            Assert.Equal("Ungrouped", l.GroupDisplayName);
        });
    }

    [Fact]
    public void Remove_accepts_a_lazy_sequence()
    {
        var source = new[] { new LayerRow { GroupId = "a" } };

        var previous = _service.Remove(source.Where(_ => true));

        Assert.Equal(new[] { "a" }, previous);
        Assert.Equal("", source[0].GroupId);
    }
}
