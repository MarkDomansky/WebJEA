using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class HelpersTests
{
    #region CoalesceString

    [Fact]
    public void CoalesceString_AllNull_ReturnsNothing()
    {
        var result = Helpers.CoalesceString(null, null, null);
        Assert.Null(result);
    }

    [Fact]
    public void CoalesceString_FirstNotNull_ReturnsFirst()
    {
        var result = Helpers.CoalesceString("first", "second", "third");
        Assert.Equal("first", result);
    }

    [Fact]
    public void CoalesceString_FirstNullSecondValid_ReturnsSecond()
    {
        var result = Helpers.CoalesceString(null, "second", "third");
        Assert.Equal("second", result);
    }

    [Fact]
    public void CoalesceString_OnlyLastNotNull_ReturnsLast()
    {
        var result = Helpers.CoalesceString(null, null, "third");
        Assert.Equal("third", result);
    }

    [Fact]
    public void CoalesceString_EmptyStringIsNotNull_ReturnsEmpty()
    {
        var result = Helpers.CoalesceString("", "second");
        Assert.Equal("", result);
    }

    [Fact]
    public void CoalesceString_NoArguments_ReturnsNothing()
    {
        var result = Helpers.CoalesceString();
        Assert.Null(result);
    }

    [Fact]
    public void CoalesceString_SingleValue_ReturnsThatValue()
    {
        var result = Helpers.CoalesceString("only");
        Assert.Equal("only", result);
    }

    #endregion

    #region StringHash256

    [Fact]
    public void StringHash256_SameInput_ReturnsSameHash()
    {
        var hash1 = Helpers.StringHash256("test input");
        var hash2 = Helpers.StringHash256("test input");
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void StringHash256_DifferentInput_ReturnsDifferentHash()
    {
        var hash1 = Helpers.StringHash256("input one");
        var hash2 = Helpers.StringHash256("input two");
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void StringHash256_ReturnsHexString()
    {
        var hash = Helpers.StringHash256("test");
        Assert.Matches("^[0-9A-F]+$", hash);
    }

    [Fact]
    public void StringHash256_Returns64CharHash()
    {
        var hash = Helpers.StringHash256("test");
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void StringHash256_EmptyString_ReturnsValidHash()
    {
        var hash = Helpers.StringHash256("");
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    #endregion

    #region GetFileContent

    [Fact]
    public void GetFileContent_NonExistentFile_ReturnsNothing()
    {
        var result = Helpers.GetFileContent(@"C:\nonexistent\fake_file_12345.txt");
        Assert.Null(result);
    }

    [Fact]
    public void GetFileContent_ExistingFile_ReturnsContent()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            var result = Helpers.GetFileContent(tempFile);
            Assert.Equal("hello world", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetFileContent_EmptyFile_ReturnsEmptyString()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = Helpers.GetFileContent(tempFile);
            Assert.Equal("", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetFileContent_UnicodeContent_ReturnsCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "café résumé 日本語");
            var result = Helpers.GetFileContent(tempFile);
            Assert.Equal("café résumé 日本語", result);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
