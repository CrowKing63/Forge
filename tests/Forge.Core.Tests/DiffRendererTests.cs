using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class DiffRendererTests
{
    [Fact]
    public void IdenticalText_IsOneEqualSegment()
    {
        var segments = DiffRenderer.Compute("abc", "abc");

        Assert.Single(segments);
        Assert.Equal(DiffSegmentKind.Equal, segments[0].Kind);
        Assert.Equal("abc", segments[0].OriginalText);
        Assert.Equal("abc", segments[0].ResultText);
    }

    [Fact]
    public void EmptyOriginal_IsAllInserted()
    {
        var segments = DiffRenderer.Compute("", "hello world");

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.Equal(DiffSegmentKind.Inserted, s.Kind));
        Assert.Equal("hello world", string.Concat(segments.Select(s => s.ResultText)));
    }

    [Fact]
    public void EmptyResult_IsAllRemoved()
    {
        var segments = DiffRenderer.Compute("original text", "");

        Assert.NotEmpty(segments);
        Assert.All(segments, s => Assert.Equal(DiffSegmentKind.Removed, s.Kind));
        Assert.Equal("original text", string.Concat(segments.Select(s => s.OriginalText)));
    }

    [Fact]
    public void WordChange_MarksOnlyTheChangedWord()
    {
        var segments = DiffRenderer.Compute("the quick brown fox", "the quick red fox");

        var removed = segments.Where(s => s.Kind == DiffSegmentKind.Removed).ToList();
        var inserted = segments.Where(s => s.Kind == DiffSegmentKind.Inserted).ToList();
        Assert.Single(removed);
        Assert.Single(inserted);
        Assert.Contains("brown", removed[0].OriginalText);
        Assert.Contains("red", inserted[0].ResultText);
    }

    [Fact]
    public void OriginalAndResult_SpanTheWholeInput()
    {
        const string original = "one two three";
        const string result = "one four five six";
        var segments = DiffRenderer.Compute(original, result);

        Assert.Equal(original, string.Concat(segments.Select(s => s.OriginalText)));
        Assert.Equal(result, string.Concat(segments.Select(s => s.ResultText)));
    }

    [Fact]
    public void FullReplacement_IsAllChanged()
    {
        var segments = DiffRenderer.Compute("aaa", "bbb");

        Assert.All(segments, s => Assert.NotEqual(DiffSegmentKind.Equal, s.Kind));
    }
}
