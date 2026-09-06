using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class PSCmdParamTests
{
    #region ParamType classification

    [Theory]
    [InlineData("string", PSCmdParam.ParameterType.PSString)]
    [InlineData("string[]", PSCmdParam.ParameterType.PSString)]
    [InlineData("String", PSCmdParam.ParameterType.PSString)]
    [InlineData("datetime", PSCmdParam.ParameterType.PSDate)]
    [InlineData("int", PSCmdParam.ParameterType.PSInt)]
    [InlineData("int32", PSCmdParam.ParameterType.PSInt)]
    [InlineData("int64", PSCmdParam.ParameterType.PSInt)]
    [InlineData("int[]", PSCmdParam.ParameterType.PSInt)]
    [InlineData("uint32", PSCmdParam.ParameterType.PSInt)]
    [InlineData("byte", PSCmdParam.ParameterType.PSInt)]
    [InlineData("long", PSCmdParam.ParameterType.PSInt)]
    [InlineData("single", PSCmdParam.ParameterType.PSFloat)]
    [InlineData("double", PSCmdParam.ParameterType.PSFloat)]
    [InlineData("float", PSCmdParam.ParameterType.PSFloat)]
    [InlineData("boolean", PSCmdParam.ParameterType.PSBoolean)]
    [InlineData("bool", PSCmdParam.ParameterType.PSBoolean)]
    [InlineData("switch", PSCmdParam.ParameterType.PSBoolean)]
    [InlineData("", PSCmdParam.ParameterType.PSString)]
    public void ParamType_ClassifiesCorrectly(string varType, PSCmdParam.ParameterType expected)
    {
        var param = new PSCmdParam();
        param.VarType = varType;

        Assert.Equal(expected, param.ParamType);
    }

    [Fact]
    public void ParamType_UnknownType_DefaultsToString()
    {
        var param = new PSCmdParam();
        param.VarType = "pscredential";

        Assert.Equal(PSCmdParam.ParameterType.PSString, param.ParamType);
    }

    #endregion

    #region IsMandatory

    [Fact]
    public void IsMandatory_NoValidation_ReturnsFalse()
    {
        var param = new PSCmdParam();
        Assert.False(param.IsMandatory);
    }

    [Fact]
    public void IsMandatory_HasMandatoryValidation_ReturnsTrue()
    {
        var param = new PSCmdParam();
        param.AddValidation("Mandatory");

        Assert.True(param.IsMandatory);
    }

    [Fact]
    public void IsMandatory_CaseInsensitive()
    {
        var param = new PSCmdParam();
        param.AddValidation("MANDATORY");

        Assert.True(param.IsMandatory);
    }

    [Fact]
    public void IsMandatory_OtherValidation_ReturnsFalse()
    {
        var param = new PSCmdParam();
        param.AddValidation("ValidateLength(1,50)");

        Assert.False(param.IsMandatory);
    }

    #endregion

    #region IsMultiValued

    [Fact]
    public void IsMultiValued_ArrayType_ReturnsTrue()
    {
        var param = new PSCmdParam();
        param.VarType = "string[]";

        Assert.True(param.IsMultiValued);
    }

    [Fact]
    public void IsMultiValued_ScalarType_ReturnsFalse()
    {
        var param = new PSCmdParam();
        param.VarType = "string";

        Assert.False(param.IsMultiValued);
    }

    [Fact]
    public void IsMultiValued_MultilineDirective_ReturnsFalse()
    {
        var param = new PSCmdParam();
        param.DirectiveMultiline = true;

        Assert.False(param.IsMultiValued);
    }

    [Fact]
    public void IsMultiValued_IntArray_ReturnsTrue()
    {
        var param = new PSCmdParam();
        param.VarType = "int[]";

        Assert.True(param.IsMultiValued);
    }

    #endregion

    #region IsSelect and AllowedValues

    [Fact]
    public void IsSelect_NoValidateSet_ReturnsFalse()
    {
        var param = new PSCmdParam();
        Assert.False(param.IsSelect);
    }

    [Fact]
    public void IsSelect_WithValidateSet_ReturnsTrue()
    {
        var param = new PSCmdParam();
        param.AddValidation("ValidateSet('A','B','C')");

        Assert.True(param.IsSelect);
    }

    [Fact]
    public void AllowedValues_WithValidateSet_ReturnsOptions()
    {
        var param = new PSCmdParam();
        param.AddValidation("ValidateSet('Input','Output','Both')");

        var allowed = param.AllowedValues;
        Assert.NotNull(allowed);
        Assert.Contains("Input", allowed);
        Assert.Contains("Output", allowed);
        Assert.Contains("Both", allowed);
    }

    [Fact]
    public void AllowedValues_NoValidateSet_ReturnsNothing()
    {
        var param = new PSCmdParam();
        Assert.Null(param.AllowedValues);
    }

    #endregion

    #region AddValidation

    [Fact]
    public void AddValidation_ValidateLength_Added()
    {
        var param = new PSCmdParam();
        param.AddValidation("ValidateLength(1,50)");

        Assert.Single(param.Validation);
        Assert.Contains("ValidateLength(1,50)", param.Validation);
    }

    [Fact]
    public void AddValidation_Mandatory_Added()
    {
        var param = new PSCmdParam();
        param.AddValidation("Mandatory");

        Assert.Single(param.Validation);
    }

    [Fact]
    public void AddValidation_DuplicatesIgnored()
    {
        var param = new PSCmdParam();
        param.AddValidation("Mandatory");
        param.AddValidation("Mandatory");

        Assert.Single(param.Validation);
    }

    [Fact]
    public void AddValidation_AliasIsIgnored()
    {
        var param = new PSCmdParam();
        param.AddValidation("Alias('myalias')");

        Assert.Empty(param.Validation);
    }

    [Fact]
    public void AddValidation_AllowNull_Added()
    {
        var param = new PSCmdParam();
        param.AddValidation("AllowNull");

        Assert.Single(param.Validation);
    }

    #endregion

    #region Clone

    [Fact]
    public void Clone_CopiesAllProperties()
    {
        var original = new PSCmdParam();
        original.Name = "TestParam";
        original.HelpMessage = "Help";
        original.HelpDetail = "Detail";
        original.VarType = "string";
        original.DirectiveMultiline = true;
        original.DirectiveDateTime = true;
        original.DefaultValue = "default";
        original.AddValidation("Mandatory");
        original.AddValidation("ValidateLength(1,50)");

        var clone = original.Clone();

        Assert.Equal("TestParam", clone.Name);
        Assert.Equal("Help", clone.HelpMessage);
        Assert.Equal("Detail", clone.HelpDetail);
        Assert.Equal("string", clone.VarType);
        Assert.True(clone.DirectiveMultiline);
        Assert.True(clone.DirectiveDateTime);
        Assert.Equal("default", (string)clone.DefaultValue);
        Assert.True(clone.IsMandatory);
        Assert.Equal(2, clone.Validation.Count);
    }

    [Fact]
    public void Clone_IsIndependentCopy()
    {
        var original = new PSCmdParam();
        original.Name = "Original";
        original.AddValidation("Mandatory");

        var clone = original.Clone();
        clone.Name = "Cloned";
        clone.AddValidation("ValidateLength(1,50)");

        Assert.Equal("Original", original.Name);
        Assert.Single(original.Validation);
    }

    #endregion

    #region MergeUnder

    [Fact]
    public void MergeUnder_DoesNotOverwriteExistingValues()
    {
        var target = new PSCmdParam();
        target.Name = "Target";
        target.HelpMessage = "Existing Help";
        target.VarType = "int";

        var source = new PSCmdParam();
        source.HelpMessage = "Source Help";
        source.HelpDetail = "Source Detail";
        source.VarType = "string";

        target.MergeUnder(source);

        Assert.Equal("Existing Help", target.HelpMessage);
        Assert.Equal("Source Detail", target.HelpDetail);
        Assert.Equal("int", target.VarType);
    }

    [Fact]
    public void MergeUnder_FillsEmptyValues()
    {
        var target = new PSCmdParam();
        target.Name = "Target";

        var source = new PSCmdParam();
        source.HelpMessage = "Source Help";
        source.HelpDetail = "Source Detail";
        source.VarType = "string";
        source.DefaultValue = "default";

        target.MergeUnder(source);

        Assert.Equal("Source Help", target.HelpMessage);
        Assert.Equal("Source Detail", target.HelpDetail);
        Assert.Equal("string", target.VarType);
        Assert.Equal("default", (string)target.DefaultValue);
    }

    [Fact]
    public void MergeUnder_MergesValidation()
    {
        var target = new PSCmdParam();
        target.AddValidation("Mandatory");

        var source = new PSCmdParam();
        source.AddValidation("ValidateLength(1,50)");

        target.MergeUnder(source);

        Assert.Equal(2, target.Validation.Count);
    }

    #endregion

    #region MergeOver

    [Fact]
    public void MergeOver_OverwritesWithSourceValues()
    {
        var target = new PSCmdParam();
        target.Name = "Target";
        target.HelpMessage = "Original";

        var source = new PSCmdParam();
        source.HelpMessage = "Override";

        target.MergeOver(source);

        Assert.Equal("Override", target.HelpMessage);
    }

    [Fact]
    public void MergeOver_DoesNotOverwriteWithEmptySource()
    {
        var target = new PSCmdParam();
        target.HelpMessage = "Keep This";

        var source = new PSCmdParam();

        target.MergeOver(source);

        Assert.Equal("Keep This", target.HelpMessage);
    }

    [Fact]
    public void MergeOver_MergesValidation()
    {
        var target = new PSCmdParam();
        target.AddValidation("Mandatory");

        var source = new PSCmdParam();
        source.AddValidation("ValidateRange(1,100)");

        target.MergeOver(source);

        Assert.Equal(2, target.Validation.Count);
    }

    #endregion

    #region FieldName

    [Fact]
    public void FieldName_ReturnsName()
    {
        var param = new PSCmdParam();
        param.Name = "MyParam";

        Assert.Equal("MyParam", param.FieldName);
    }

    #endregion

    #region ValidationObjects

    [Fact]
    public void ValidationObjects_ReturnsValidParsedObjects()
    {
        var param = new PSCmdParam();
        param.AddValidation("Mandatory");
        param.AddValidation("ValidateLength(1,50)");
        param.AddValidation("ValidateRange(0,100)");

        var valObjs = param.ValidationObjects;
        Assert.Equal(3, valObjs.Count);
        Assert.All(valObjs, v => Assert.True(v.IsValid));
    }

    [Fact]
    public void ValidationObjects_EmptyValidation_ReturnsEmptyList()
    {
        var param = new PSCmdParam();

        var valObjs = param.ValidationObjects;
        Assert.Empty(valObjs);
    }

    #endregion
}
