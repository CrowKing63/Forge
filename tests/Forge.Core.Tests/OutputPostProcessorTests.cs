using Forge.Core;
using Xunit;

namespace Forge.Core.Tests;

public class OutputPostProcessorTests
{
    // ---- Clean: Hy-MT2 marker echo (S15) ----

    [Fact]
    public void Clean_HyMt2_StripsMarkerEcho()
    {
        // Real Q8 output from the S15 benchmark (translate-02).
        const string raw = "><|hy_User|>I'll contact you again later~<|hy_Assistant|>><|hy_User|>";
        Assert.Equal("I'll contact you again later~", OutputPostProcessor.Clean(raw, ModelFamily.HyMt2));
    }

    [Fact]
    public void Clean_HyMt2_StripsLongerEcho()
    {
        // Real Q8 output from the S15 benchmark (translate-01).
        const string raw = "><|hyBegin_of_sentence|><|hy_User|>Please confirm the email. Please reply by tomorrow.<|hy_Assistant|>><|hyBegin_of_sentence|><|hy_User|>";
        Assert.Equal("Please confirm the email. Please reply by tomorrow.",
            OutputPostProcessor.Clean(raw, ModelFamily.HyMt2));
    }

    [Fact]
    public void Clean_HyMt2_NoMarkerPair_FallsBackToStrippingAllMarkers()
    {
        const string raw = "<|hy_begin_of_sentence|>clean text here<|hy_end_of_sentence|>";
        Assert.Equal("clean text here", OutputPostProcessor.Clean(raw, ModelFamily.HyMt2));
    }

    [Fact]
    public void Clean_HyMt2_AlreadyClean_PassesThrough()
    {
        const string raw = "A perfectly clean translation.";
        Assert.Equal(raw, OutputPostProcessor.Clean(raw, ModelFamily.HyMt2));
    }

    // ---- Clean: Gemma 4 is untouched ----

    [Fact]
    public void Clean_Gemma4_PassesThrough()
    {
        const string raw = "Today the weather is nice.";
        Assert.Equal(raw, OutputPostProcessor.Clean(raw, ModelFamily.Gemma4));
    }
}
