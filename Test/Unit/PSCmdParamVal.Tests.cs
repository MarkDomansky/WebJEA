using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class PSCmdParamValTests
{
    #region Mandatory

    [Fact]
    public void Mandatory_ParsesCorrectly()
    {
        var val = new PSCmdParamVal("Mandatory");

        Assert.Equal(PSCmdParamVal.ValType.Mandatory, val.Type);
        Assert.True(val.IsValid);
    }

    #endregion

    #region ValidateLength

    [Fact]
    public void ValidateLength_ParsesLimits()
    {
        var val = new PSCmdParamVal("ValidateLength(1,50)");

        Assert.Equal(PSCmdParamVal.ValType.Length, val.Type);
        Assert.Equal(1, val.LowerLimit);
        Assert.Equal(50, val.UpperLimit);
        Assert.True(val.IsValid);
    }

    [Fact]
    public void ValidateLength_WithSpaces_ParsesCorrectly()
    {
        var val = new PSCmdParamVal("ValidateLength( 5 , 100 )");

        Assert.Equal(PSCmdParamVal.ValType.Length, val.Type);
        Assert.Equal(5, val.LowerLimit);
        Assert.Equal(100, val.UpperLimit);
    }

    #endregion

    #region ValidateRange

    [Fact]
    public void ValidateRange_ParsesLimits()
    {
        var val = new PSCmdParamVal("ValidateRange(0,999)");

        Assert.Equal(PSCmdParamVal.ValType.Range, val.Type);
        Assert.Equal(0, val.LowerLimit);
        Assert.Equal(999, val.UpperLimit);
        Assert.True(val.IsValid);
    }

    #endregion

    #region ValidatePattern

    [Fact]
    public void ValidatePattern_ParsesPattern()
    {
        var val = new PSCmdParamVal("ValidatePattern('^[a-z]+$')");

        Assert.Equal(PSCmdParamVal.ValType.Pattern, val.Type);
        Assert.Equal("^[a-z]+$", val.Pattern);
        Assert.True(val.IsValid);
    }

    [Fact]
    public void ValidatePattern_DoubleQuotes_ParsesPattern()
    {
        var val = new PSCmdParamVal("ValidatePattern(\"^\\d+$\")");

        Assert.Equal(PSCmdParamVal.ValType.Pattern, val.Type);
        Assert.Equal("^\\d+$", val.Pattern);
    }

    #endregion

    #region ValidateCount

    [Fact]
    public void ValidateCount_ParsesLimits()
    {
        var val = new PSCmdParamVal("ValidateCount(1,5)");

        Assert.Equal(PSCmdParamVal.ValType.Count, val.Type);
        Assert.Equal(1, val.LowerLimit);
        Assert.Equal(5, val.UpperLimit);
        Assert.True(val.IsValid);
    }

    #endregion

    #region ValidateSet

    [Fact]
    public void ValidateSet_ParsesOptions()
    {
        var val = new PSCmdParamVal("ValidateSet('A','B','C')");

        Assert.Equal(PSCmdParamVal.ValType.SetCol, val.Type);
        Assert.True(val.IsValid);
        Assert.Equal(3, val.Options.Count);
        Assert.Contains("A", val.Options);
        Assert.Contains("B", val.Options);
        Assert.Contains("C", val.Options);
    }

    [Fact]
    public void ValidateSet_DoubleQuotes_ParsesOptions()
    {
        var val = new PSCmdParamVal("ValidateSet(\"Input\",\"Output\",\"Both\")");

        Assert.Equal(PSCmdParamVal.ValType.SetCol, val.Type);
        Assert.Equal(3, val.Options.Count);
        Assert.Contains("Input", val.Options);
        Assert.Contains("Output", val.Options);
        Assert.Contains("Both", val.Options);
    }

    [Fact]
    public void ValidateSet_SingleOption_ParsesCorrectly()
    {
        var val = new PSCmdParamVal("ValidateSet('Only')");

        Assert.Equal(PSCmdParamVal.ValType.SetCol, val.Type);
        Assert.Single(val.Options);
        Assert.Contains("Only", val.Options);
    }

    #endregion

    #region ValidateNotNull / ValidateNotNullOrEmpty

    [Fact]
    public void ValidateNotNull_IsValid()
    {
        var val = new PSCmdParamVal("ValidateNotNull");
        Assert.Equal(PSCmdParamVal.ValType.NotNull, val.Type);
        Assert.True(val.IsValid);
    }

    [Fact]
    public void ValidateNotNullOrEmpty_IsValid()
    {
        var val = new PSCmdParamVal("ValidateNotNullOrEmpty");
        Assert.Equal(PSCmdParamVal.ValType.NotNullOrEmpty, val.Type);
        Assert.True(val.IsValid);
    }

    #endregion

    #region Unsupported/ignored rules

    [Fact]
    public void ValidateScript_IsNotValid()
    {
        var val = new PSCmdParamVal("ValidateScript({$_ -gt 0})");
        Assert.Equal(PSCmdParamVal.ValType.Err, val.Type);
        Assert.False(val.IsValid);
    }

    [Fact]
    public void AllowNull_IsNotValid()
    {
        var val = new PSCmdParamVal("AllowNull");
        Assert.Equal(PSCmdParamVal.ValType.Err, val.Type);
        Assert.False(val.IsValid);
    }

    [Fact]
    public void AllowEmptyString_IsNotValid()
    {
        var val = new PSCmdParamVal("AllowEmptyString");
        Assert.Equal(PSCmdParamVal.ValType.Err, val.Type);
        Assert.False(val.IsValid);
    }

    [Fact]
    public void AllowEmptyCollection_IsNotValid()
    {
        var val = new PSCmdParamVal("AllowEmptyCollection");
        Assert.Equal(PSCmdParamVal.ValType.Err, val.Type);
        Assert.False(val.IsValid);
    }

    #endregion

    #region Empty parentheses removal

    [Fact]
    public void EmptyParentheses_RemovedBeforeParsing()
    {
        var val = new PSCmdParamVal("Mandatory()");

        Assert.Equal(PSCmdParamVal.ValType.Mandatory, val.Type);
        Assert.True(val.IsValid);
    }

    #endregion

    #region Rule property

    [Fact]
    public void Rule_ReturnsOriginalRule()
    {
        var val = new PSCmdParamVal("ValidateRange(1,100)");
        Assert.Equal("ValidateRange(1,100)", val.Rule);
    }

    [Fact]
    public void Rule_EmptyParensRemoved_ReturnsCleanedRule()
    {
        var val = new PSCmdParamVal("Mandatory()");
        Assert.Equal("Mandatory", val.Rule);
    }

    #endregion
}
