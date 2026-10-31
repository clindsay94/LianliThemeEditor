using ThemeEditorCSharp.Models;
using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class GalleryFilterServiceTests
{
    private readonly GalleryFilterService _service = new();

    private static GalleryThemeItem Theme(
        string name,
        string device = "hydroshift-ii-lcd-s",
        string orientation = "",
        bool installed = false,
        int downloads = 0,
        int votes = 0,
        double rating = 0,
        DateTime? time = null,
        int order = 0) =>
        new()
        {
            Id = name,
            Name = name,
            DeviceModel = device,
            Orientation = orientation,
            IsInstalled = installed,
            DownloadCount = downloads,
            VoteCount = votes,
            AverageRating = rating,
            GallerySortTimeUtc = time ?? DateTime.MinValue,
            GalleryOrder = order
        };

    private static GalleryFilterOptions Options(
        string sort = "",
        double minRating = 0,
        string[]? devices = null,
        string[]? orientations = null,
        bool[]? installStates = null) =>
        new(
            new HashSet<string>(devices ?? new[] { "hydroshift-ii-lcd-s", "hydroshift-ii-lcd-c", "universal-screen-8.8-inch", "vm-9.2-inch", "hydroshift-ii-oled-curve" }),
            new HashSet<string>(orientations ?? new[] { "landscape", "portrait" }),
            new HashSet<bool>(installStates ?? new[] { true, false }),
            minRating,
            sort);

    private static string[] Names(IEnumerable<GalleryThemeItem> items) => items.Select(t => t.Name).ToArray();

    [Fact]
    public void Unknown_sort_mode_keeps_input_order()
    {
        var themes = new[] { Theme("c"), Theme("a"), Theme("b") };

        Assert.Equal(new[] { "c", "a", "b" }, Names(_service.Filter(themes, Options())));
    }

    [Fact]
    public void Empty_input_gives_empty_result()
    {
        Assert.Empty(_service.Filter(Array.Empty<GalleryThemeItem>(), Options("name")));
    }

    [Fact]
    public void Sort_by_name_is_case_insensitive()
    {
        var themes = new[] { Theme("beta"), Theme("Alpha"), Theme("charlie") };

        Assert.Equal(new[] { "Alpha", "beta", "charlie" }, Names(_service.Filter(themes, Options("name"))));
    }

    [Fact]
    public void Sort_by_downloads_descending_then_name()
    {
        var themes = new[] { Theme("b", downloads: 5), Theme("a", downloads: 5), Theme("c", downloads: 9) };

        Assert.Equal(new[] { "c", "a", "b" }, Names(_service.Filter(themes, Options("downloads"))));
    }

    [Fact]
    public void Sort_by_rating_puts_voted_themes_first_then_rating_then_votes_then_name()
    {
        var themes = new[]
        {
            Theme("unvoted"),
            Theme("low", votes: 100, rating: 3),
            Theme("highFew", votes: 2, rating: 5),
            Theme("highMany", votes: 10, rating: 5),
            Theme("highManyA", votes: 10, rating: 5)
        };

        Assert.Equal(
            new[] { "highMany", "highManyA", "highFew", "low", "unvoted" },
            Names(_service.Filter(themes, Options("rating"))));
    }

    [Fact]
    public void Sort_by_votes_descending_then_rating_then_name()
    {
        var themes = new[]
        {
            Theme("a", votes: 3, rating: 2),
            Theme("b", votes: 10, rating: 1),
            Theme("c", votes: 3, rating: 4)
        };

        Assert.Equal(new[] { "b", "c", "a" }, Names(_service.Filter(themes, Options("votes"))));
    }

    [Fact]
    public void Sort_by_newest_uses_time_then_order_then_name()
    {
        var t1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var themes = new[]
        {
            Theme("old", time: t1, order: 99),
            Theme("newLowOrder", time: t2, order: 1),
            Theme("newHighOrder", time: t2, order: 5)
        };

        Assert.Equal(new[] { "newHighOrder", "newLowOrder", "old" }, Names(_service.Filter(themes, Options("newest"))));
    }

    [Fact]
    public void Install_state_filter_excludes_unselected_states()
    {
        var themes = new[] { Theme("inst", installed: true), Theme("not", installed: false) };

        Assert.Equal(new[] { "inst" }, Names(_service.Filter(themes, Options(installStates: new[] { true }))));
        Assert.Equal(new[] { "not" }, Names(_service.Filter(themes, Options(installStates: new[] { false }))));
        Assert.Empty(_service.Filter(themes, Options(installStates: Array.Empty<bool>())));
    }

    [Fact]
    public void Minimum_rating_requires_votes_and_threshold()
    {
        var themes = new[]
        {
            Theme("none"),
            Theme("low", votes: 5, rating: 3.9),
            Theme("exact", votes: 1, rating: 4),
            Theme("high", votes: 1, rating: 5)
        };

        Assert.Equal(new[] { "exact", "high" }, Names(_service.Filter(themes, Options(minRating: 4))));
    }

    [Fact]
    public void Zero_or_negative_minimum_rating_keeps_unvoted_themes()
    {
        var themes = new[] { Theme("none") };

        Assert.Single(_service.Filter(themes, Options(minRating: 0)));
        Assert.Single(_service.Filter(themes, Options(minRating: -1)));
    }

    [Fact]
    public void Device_filter_normalizes_theme_model_but_not_selected_values()
    {
        var themes = new[] { Theme("alias", device: "Hydroshift_C"), Theme("canon", device: "hydroshift-ii-lcd-s") };

        var result = _service.Filter(themes, Options(devices: new[] { "hydroshift-ii-lcd-c" }));

        Assert.Equal(new[] { "alias" }, Names(result));
        Assert.Empty(_service.Filter(themes, Options(devices: new[] { "hydroshift-c" })));
    }

    [Fact]
    public void Empty_device_or_orientation_selection_matches_nothing()
    {
        var themes = new[] { Theme("a") };

        Assert.Empty(_service.Filter(themes, Options(devices: Array.Empty<string>())));
        Assert.Empty(_service.Filter(themes, Options(orientations: Array.Empty<string>())));
    }

    [Fact]
    public void MatchesOrientationFilter_vm92_is_always_landscape()
    {
        var theme = Theme("vm", device: "VM-9.2-inch", orientation: "portrait");

        Assert.True(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "landscape" }));
        Assert.False(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
    }

    [Fact]
    public void MatchesOrientationFilter_vm92_alias_is_not_special_cased()
    {
        var theme = Theme("vm", device: "vm-9.2", orientation: "portrait");

        // Current behaviour; looks wrong: only the exact canonical id gets the landscape override, aliases fall through to Orientation.
        Assert.True(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
    }

    [Fact]
    public void MatchesOrientationFilter_uses_trimmed_orientation_when_present()
    {
        var theme = Theme("t", orientation: " portrait ");

        Assert.True(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
        Assert.False(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "landscape" }));
    }

    [Fact]
    public void MatchesOrientationFilter_selected_set_comparison_is_case_sensitive()
    {
        var theme = Theme("t", orientation: "Portrait");

        Assert.False(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
    }

    [Fact]
    public void MatchesOrientationFilter_universal_without_orientation_defaults_to_landscape()
    {
        var theme = Theme("u", device: "universal-screen-8.8-inch");

        Assert.True(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "landscape" }));
        Assert.False(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
    }

    [Fact]
    public void MatchesOrientationFilter_other_device_without_orientation_matches_any_nonempty_selection()
    {
        var theme = Theme("s");

        Assert.True(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string> { "portrait" }));
        Assert.False(GalleryFilterService.MatchesOrientationFilter(theme, new HashSet<string>()));
    }

    [Fact]
    public void MatchesDeviceFilter_empty_selection_is_false()
    {
        Assert.False(GalleryFilterService.MatchesDeviceFilter(Theme("a"), new HashSet<string>()));
    }
}
