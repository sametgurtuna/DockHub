using CustomDock.Core;

namespace CustomDock.Tests;

public class LauncherTests
{
    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("3,5*2", 7)]
    [InlineData("3.5*2", 7)]
    [InlineData("2^3^2", 512)]
    [InlineData("200*15%", 30)]
    [InlineData("-4+10", 6)]
    [InlineData("sqrt(16)+1", 5)]
    [InlineData("= 10/4", 2.5)]
    [InlineData("6x7", 42)]
    public void Math_expressions_are_evaluated(string input, double expected)
    {
        Assert.True(LauncherMath.TryEvaluate(input, out double result));
        Assert.Equal(expected, result, 6);
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("42")]
    [InlineData("Windows 11")]
    [InlineData("1/0")]
    [InlineData("(2+3")]
    [InlineData("")]
    public void Non_calculations_are_ignored(string input)
        => Assert.False(LauncherMath.TryEvaluate(input, out _));

    [Fact]
    public void Prefix_beats_word_start_beats_contains()
    {
        int prefix = LauncherMatch.Score("Visual Studio Code", null, "vis");
        int word = LauncherMatch.Score("Visual Studio Code", null, "stu");
        int inside = LauncherMatch.Score("Visual Studio Code", null, "tud");
        int acronym = LauncherMatch.Score("Visual Studio Code", null, "vsc");
        Assert.True(prefix > word && word > inside && inside > acronym && acronym > 0);
    }

    [Fact]
    public void Keywords_and_misses()
    {
        Assert.Equal(45, LauncherMatch.Score("Sound", "audio volume", "volume"));
        Assert.Equal(0, LauncherMatch.Score("Sound", "audio volume", "zzz"));
    }
}
