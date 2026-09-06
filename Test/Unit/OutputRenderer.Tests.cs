using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class OutputRendererTests
{
    private readonly OutputRenderer renderer = new OutputRenderer();

    #region EncodeOutputTags - anchor tags

    [Fact]
    public void EncodeOutputTags_AnchorTag_ConvertsToHtml()
    {
        var input = "[[a|https://example.com|Click here]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal("<a href='https://example.com'>Click here</a>", result);
    }

    [Fact]
    public void EncodeOutputTags_MultipleAnchors_ConvertsAll()
    {
        var input = "[[a|https://one.com|One]] and [[a|https://two.com|Two]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Contains("<a href='https://one.com'>One</a>", result);
        Assert.Contains("<a href='https://two.com'>Two</a>", result);
    }

    [Fact]
    public void EncodeOutputTags_AnchorCaseInsensitive_Converts()
    {
        var input = "[[A|https://example.com|Link]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Contains("<a href='https://example.com'>Link</a>", result);
    }

    #endregion

    #region EncodeOutputTags - span tags

    [Fact]
    public void EncodeOutputTags_SpanTag_ConvertsToHtml()
    {
        var input = "[[span|myclass|content text]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal("<span Class='myclass'>content text</span>", result);
    }

    [Fact]
    public void EncodeOutputTags_SpanCaseInsensitive_Converts()
    {
        var input = "[[SPAN|highlight|important]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Contains("<span Class='highlight'>important</span>", result);
    }

    #endregion

    #region EncodeOutputTags - img tags

    [Fact]
    public void EncodeOutputTags_ImgTag_ConvertsToHtml()
    {
        var input = "[[img|thumbnail|https://example.com/image.png]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal("<img class='thumbnail' src='https://example.com/image.png' />", result);
    }

    [Fact]
    public void EncodeOutputTags_ImgEmptyClass_ConvertsToHtml()
    {
        var input = "[[img||https://example.com/image.png]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal("<img class='' src='https://example.com/image.png' />", result);
    }

    #endregion

    #region EncodeOutputTags - no tags

    [Fact]
    public void EncodeOutputTags_NoTags_ReturnsUnchanged()
    {
        var input = "Just plain text with no special tags";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void EncodeOutputTags_EmptyString_ReturnsEmpty()
    {
        var result = renderer.EncodeOutputTags("");

        Assert.Equal("", result);
    }

    [Fact]
    public void EncodeOutputTags_PartialTag_ReturnsUnchanged()
    {
        var input = "[[a|incomplete";
        var result = renderer.EncodeOutputTags(input);

        Assert.Equal(input, result);
    }

    #endregion

    #region EncodeOutputTags - mixed content

    [Fact]
    public void EncodeOutputTags_MixedTags_ConvertsAll()
    {
        var input = "Text [[a|https://link.com|link]] more [[span|bold|text]] end [[img|pic|https://img.com/x.png]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Contains("<a href='https://link.com'>link</a>", result);
        Assert.Contains("<span Class='bold'>text</span>", result);
        Assert.Contains("<img class='pic' src='https://img.com/x.png' />", result);
    }

    [Fact]
    public void EncodeOutputTags_TextAroundTags_PreservesSurroundingText()
    {
        var input = "Before [[a|https://example.com|link]] after";
        var result = renderer.EncodeOutputTags(input);

        Assert.StartsWith("Before ", result);
        Assert.EndsWith(" after", result);
    }

    #endregion

    #region EncodeOutputTags - XSS vectors

    [Fact]
    public void EncodeOutputTags_JavascriptInAnchorHref_StillConverts()
    {
        // This demonstrates the known XSS risk documented in plan.md Step 9
        var input = "[[a|javascript:alert(1)|click]]";
        var result = renderer.EncodeOutputTags(input);

        Assert.Contains("javascript:alert(1)", result);
    }

    #endregion
}
