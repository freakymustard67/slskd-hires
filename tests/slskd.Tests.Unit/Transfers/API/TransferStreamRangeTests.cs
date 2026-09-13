namespace slskd.Tests.Unit.Transfers.API;

using slskd.Transfers.API;
using Xunit;

public class TransferStreamRangeTests
{
    [Fact]
    public void TryParse_Returns_Full_Range_Given_No_Header()
    {
        var ok = StreamRange.TryParse(null, 1000, out var range, out var unsatisfiable);

        Assert.True(ok);
        Assert.False(unsatisfiable);
        Assert.NotNull(range);
        Assert.Equal(0, range.From);
        Assert.Equal(999, range.To);
        Assert.Equal(1000, range.Length);
        Assert.False(range.IsPartial);
    }

    [Theory]
    [InlineData("bytes=0-", 0, 999)]
    [InlineData("bytes=100-199", 100, 199)]
    [InlineData("bytes=0-999", 0, 999)]
    public void TryParse_Returns_Expected_Range(string header, long from, long to)
    {
        var ok = StreamRange.TryParse(header, 1000, out var range, out var unsatisfiable);

        Assert.True(ok);
        Assert.False(unsatisfiable);
        Assert.NotNull(range);
        Assert.Equal(from, range.From);
        Assert.Equal(to, range.To);
        Assert.Equal(to - from + 1, range.Length);
    }

    [Fact]
    public void TryParse_Clamps_End_To_Total()
    {
        var ok = StreamRange.TryParse("bytes=900-5000", 1000, out var range, out _);

        Assert.True(ok);
        Assert.Equal(900, range.From);
        Assert.Equal(999, range.To);
    }

    [Fact]
    public void TryParse_Returns_Suffix_Range()
    {
        var ok = StreamRange.TryParse("bytes=-100", 1000, out var range, out _);

        Assert.True(ok);
        Assert.Equal(900, range.From);
        Assert.Equal(999, range.To);
        Assert.True(range.IsPartial);
    }

    [Theory]
    [InlineData("bytes=1000-")]
    [InlineData("bytes=1500-2000")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=abc-def")]
    [InlineData("items=0-99")]
    [InlineData("bytes=0-99,200-299")]
    [InlineData("bytes=200-100")]
    public void TryParse_Rejects_Unsatisfiable_Headers(string header)
    {
        var ok = StreamRange.TryParse(header, 1000, out var range, out var unsatisfiable);

        Assert.False(ok);
        Assert.True(unsatisfiable);
        Assert.Null(range);
    }

    [Fact]
    public void TryParse_Rejects_NonPositive_Total()
    {
        var ok = StreamRange.TryParse(null, 0, out var range, out var unsatisfiable);

        Assert.False(ok);
        Assert.True(unsatisfiable);
        Assert.Null(range);
    }
}
