using ThemeEditorCSharp.Services;

namespace ThemeEditorCSharp.Tests.Services;

public sealed class GalleryPaginationServiceTests
{
    private readonly GalleryPaginationService _service = new();

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(-5, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(5, 1, 5)]
    public void GetTotalPages_rounds_up_with_minimum_of_one(int count, int size, int expected)
    {
        Assert.Equal(expected, _service.GetTotalPages(count, size));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetTotalPages_rejects_non_positive_page_size(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetTotalPages(5, size));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GetPage_rejects_non_positive_page_size(int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetPage(new[] { 1 }, 1, size));
    }

    [Fact]
    public void First_page_of_several()
    {
        var page = _service.GetPage(Enumerable.Range(1, 25).ToList(), 1, 10);

        Assert.Equal(Enumerable.Range(1, 10), page.Items);
        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(1, page.FirstItem);
        Assert.Equal(10, page.LastItem);
    }

    [Fact]
    public void Last_partial_page()
    {
        var page = _service.GetPage(Enumerable.Range(1, 25).ToList(), 3, 10);

        Assert.Equal(new[] { 21, 22, 23, 24, 25 }, page.Items);
        Assert.Equal(3, page.CurrentPage);
        Assert.Equal(21, page.FirstItem);
        Assert.Equal(25, page.LastItem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Page_below_one_clamps_to_first(int requested)
    {
        var page = _service.GetPage(Enumerable.Range(1, 25).ToList(), requested, 10);

        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(1, page.Items[0]);
    }

    [Fact]
    public void Page_past_the_end_clamps_to_last()
    {
        var page = _service.GetPage(Enumerable.Range(1, 25).ToList(), 99, 10);

        Assert.Equal(3, page.CurrentPage);
        Assert.Equal(5, page.Items.Count);
    }

    [Fact]
    public void Empty_input_gives_single_empty_page_with_zero_range()
    {
        var page = _service.GetPage(new List<int>(), 5, 10);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(0, page.FirstItem);
        Assert.Equal(0, page.LastItem);
    }

    [Fact]
    public void Page_size_one_returns_single_item_pages()
    {
        var page = _service.GetPage(new[] { "a", "b", "c" }, 2, 1);

        Assert.Equal(new[] { "b" }, page.Items);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.FirstItem);
        Assert.Equal(2, page.LastItem);
    }

    [Fact]
    public void Exact_multiple_has_no_trailing_empty_page()
    {
        var page = _service.GetPage(Enumerable.Range(1, 20).ToList(), 2, 10);

        Assert.Equal(2, page.TotalPages);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal(20, page.LastItem);
    }

    [Fact]
    public void Page_size_larger_than_item_count_returns_everything()
    {
        var page = _service.GetPage(new[] { 1, 2, 3 }, 1, 100);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(3, page.LastItem);
    }
}
