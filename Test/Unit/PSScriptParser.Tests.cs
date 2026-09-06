using Xunit;
using WebJEA;

namespace WebJEA.Tests;

public class PSScriptParserTests
{
    private readonly string TestScriptsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts");

    private string GetScriptPath(string scriptName)
    {
        return Path.GetFullPath(Path.Combine(TestScriptsPath, scriptName));
    }

    private PSCmdParam FindParam(List<PSCmdParam> parameters, string name)
    {
        return parameters.FirstOrDefault(p => p.Name == name);
    }

    #region Scripts with no content or missing files

    [Fact]
    public void NonExistentFile_ShouldNotBeValid()
    {
        var parser = new PSScriptParser(GetScriptPath("NonExistentScript.ps1"));

        Assert.False(parser.IsValid);
    }

    #endregion

    #region validate.ps1 - comprehensive template with all param types

    [Fact]
    public void Validate_ShouldParseFullTemplateWithAllParamTypes()
    {
        var parser = new PSScriptParser(GetScriptPath("validate.ps1"));
        var parameters = parser.GetParameters();

        // Comment block
        Assert.Contains("Short 1 line description of what this script does.", parser.Synopsis);
        Assert.Contains("Detailed description", parser.Description);
        Assert.Single(parser.Examples);
        Assert.Contains("ScriptTemplate.ps1", parser.Examples[0]);

        // String params
        var p01 = FindParam(parameters, "Input01Mandatory");
        Assert.NotNull(p01);
        Assert.True(p01.IsMandatory);
        Assert.Equal("string", p01.VarType);
        Assert.Contains("computer", p01.HelpMessage.ToLower());

        var p02 = FindParam(parameters, "Input02MandatoryMinLen");
        Assert.NotNull(p02);
        Assert.True(p02.IsMandatory);
        Assert.Equal("ABC", (string)p02.DefaultValue);

        var p03 = FindParam(parameters, "Input03Str");
        Assert.NotNull(p03);
        Assert.True(p03.DirectiveMultiline);

        var p13 = FindParam(parameters, "Input13NotNullEmpty");
        Assert.NotNull(p13);
        Assert.True(p13.DirectiveMultiline);
        Assert.Equal("x", (string)p13.DefaultValue);

        // Number params
        var nInput01 = FindParam(parameters, "NInput01");
        Assert.NotNull(nInput01);
        Assert.StartsWith("int", nInput01.VarType, StringComparison.OrdinalIgnoreCase);

        var nInput02 = FindParam(parameters, "NInput02");
        Assert.NotNull(nInput02);
        Assert.True(nInput02.IsMultiValued);

        var nInput3 = FindParam(parameters, "NInput3");
        Assert.NotNull(nInput3);
        Assert.StartsWith("uint", nInput3.VarType, StringComparison.OrdinalIgnoreCase);

        var nInput4 = FindParam(parameters, "NInput4");
        Assert.NotNull(nInput4);
        Assert.Equal("double", nInput4.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSFloat, nInput4.ParamType);

        // Listbox params
        var lInput1 = FindParam(parameters, "LInput1");
        Assert.NotNull(lInput1);
        Assert.True(lInput1.IsMandatory);
        Assert.True(lInput1.IsSelect);

        var lInput2 = FindParam(parameters, "LInput2");
        Assert.NotNull(lInput2);
        Assert.True(lInput2.IsSelect);
        Assert.True(lInput2.IsMultiValued);

        // Date params
        var dInput01 = FindParam(parameters, "DInput01DT");
        Assert.NotNull(dInput01);
        Assert.Equal("datetime", dInput01.VarType);
        Assert.False(dInput01.DirectiveDateTime);

        var dInput02 = FindParam(parameters, "DInput02DT");
        Assert.NotNull(dInput02);
        Assert.Equal("datetime", dInput02.VarType);
        Assert.True(dInput02.DirectiveDateTime);

        // Switch/boolean params
        var sw = FindParam(parameters, "Input11Switch");
        Assert.NotNull(sw);
        Assert.Equal("switch", sw.VarType);
        Assert.True(sw.IsMandatory);

        var bl = FindParam(parameters, "Input12Bool");
        Assert.NotNull(bl);
        Assert.Equal("boolean", bl.VarType);
    }

    #endregion

    #region utsp-EmptyScript.ps1 - empty file

    [Fact]
    public void UtspEmptyScript_ShouldNotBeValidAndHaveNoContent()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-EmptyScript.ps1"));

        Assert.False(parser.IsValid);
        Assert.Empty(parser.Synopsis);
        Assert.Empty(parser.Description);
        Assert.Empty(parser.Examples);
        Assert.Empty(parser.GetParameters());
    }

    #endregion

    #region utsp-InvalidScript.ps1 - incomplete comment block

    [Fact]
    public void UtspInvalidScript_ShouldNotParseIncompleteCommentBlock()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-InvalidScript.ps1"));

        Assert.Empty(parser.Synopsis);
        Assert.Empty(parser.Description);
        Assert.Empty(parser.Examples);
    }

    #endregion

    #region utsp-noparamblock.ps1 - no param block

    [Fact]
    public void UtspNoParamBlock_ShouldHaveNoParameters()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-noparamblock.ps1"));
        var parameters = parser.GetParameters();

        Assert.Empty(parser.Synopsis);
        Assert.Empty(parser.Description);
        Assert.Empty(parser.Examples);
        Assert.Empty(parameters);
    }

    #endregion

    #region utsp-noparams.ps1 - empty param block

    [Fact]
    public void UtspNoParams_ShouldHaveEmptyParameterList()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-noparams.ps1"));
        var parameters = parser.GetParameters();

        Assert.Empty(parser.Synopsis);
        Assert.Empty(parser.Description);
        Assert.Empty(parser.Examples);
        Assert.Empty(parameters);
    }

    #endregion

    #region utsp-boolean.ps1 - boolean type, no default

    [Fact]
    public void UtspBoolean_ShouldParseBooleanParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-boolean.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("boolean", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSBoolean, p.ParamType);
        Assert.False(p.IsMandatory);
        Assert.Null(p.DefaultValue);
    }

    #endregion

    #region utsp-boolean-true.ps1 - boolean type with default $true

    [Fact]
    public void UtspBooleanTrue_ShouldParseBooleanParamWithDefaultTrue()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-boolean-true.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("boolean", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSBoolean, p.ParamType);
        Assert.Equal(true, p.DefaultValue);
    }

    #endregion

    #region utsp-switch.ps1 - switch type, mandatory

    [Fact]
    public void UtspSwitch_ShouldParseMandatorySwitchParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-switch.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("switch", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSBoolean, p.ParamType);
        Assert.True(p.IsMandatory);
    }

    #endregion

    #region utsp-string.ps1 - string type with multiline directive and default

    [Fact]
    public void UtspString_ShouldParseStringParamWithMultilineAndDefault()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-string.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
        Assert.True(p.DirectiveMultiline);
        Assert.Equal("A", (string)p.DefaultValue);
    }

    #endregion

    #region utsp-string-array.ps1 - string array with array default

    [Fact]
    public void UtspStringArray_ShouldParseStringArrayWithArrayDefault()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-string-array.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string[]", p.VarType);
        Assert.True(p.IsMultiValued);
        Assert.IsType<List<string>>(p.DefaultValue);
        var defaultList = (List<string>)p.DefaultValue;
        Assert.Contains("a", defaultList);
        Assert.Contains("d", defaultList);
    }

    #endregion

    #region utsp-int.ps1 - int type

    [Fact]
    public void UtspInt_ShouldParseIntParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-int.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.StartsWith("int", p.VarType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PSCmdParam.ParameterType.PSInt, p.ParamType);
    }

    #endregion

    #region utsp-int-int32.ps1 - Int32 type

    [Fact]
    public void UtspIntInt32_ShouldParseInt32Param()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-int-int32.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("int32", p.VarType, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(PSCmdParam.ParameterType.PSInt, p.ParamType);
    }

    #endregion

    #region utsp-int-uint16.ps1 - UInt16 type

    [Fact]
    public void UtspIntUInt16_ShouldParseUInt16Param()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-int-uint16.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.StartsWith("uint", p.VarType, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PSCmdParam.ParameterType.PSInt, p.ParamType);
    }

    #endregion

    #region utsp-int-array.ps1 - int array with ValidateCount and ValidateRange

    [Fact]
    public void UtspIntArray_ShouldParseIntArrayWithValidations()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-int-array.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Contains("[]", p.VarType);
        Assert.True(p.IsMultiValued);
        Assert.Equal(PSCmdParam.ParameterType.PSInt, p.ParamType);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateCount", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateRange", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-float-float.ps1 - float type

    [Fact]
    public void UtspFloatFloat_ShouldParseFloatParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-float-float.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("float", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSFloat, p.ParamType);
    }

    #endregion

    #region utsp-float-double.ps1 - double type

    [Fact]
    public void UtspFloatDouble_ShouldParseDoubleParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-float-double.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("double", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSFloat, p.ParamType);
    }

    #endregion

    #region utsp-float-decimal.ps1 - decimal type

    [Fact]
    public void UtspFloatDecimal_ShouldParseDecimalParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-float-decimal.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        // decimal is not a recognized type in the parser; VarType is left empty
        Assert.Empty(p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-dateonly.ps1 - datetime type without WEBJEA-DateTime directive

    [Fact]
    public void UtspDateOnly_ShouldParseDateTimeParamWithoutDirective()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-dateonly.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("datetime", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSDate, p.ParamType);
        Assert.False(p.DirectiveDateTime);
    }

    #endregion

    #region utsp-datetime.ps1 - datetime type with WEBJEA-DateTime directive

    [Fact]
    public void UtspDateTime_ShouldParseDateTimeParamWithDirective()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-datetime.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("datetime", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSDate, p.ParamType);
        Assert.True(p.DirectiveDateTime);
    }

    #endregion

    #region utsp-credential.ps1 - pscredential type (unrecognized, empty VarType)

    [Fact]
    public void UtspCredential_ShouldParseCredentialAsEmptyVarType()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-cred-credential.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Empty(p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-pscredential.ps1 - pscredential type (unrecognized, empty VarType)

    [Fact]
    public void UtspPsCredential_ShouldParsePsCredentialAsEmptyVarType()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-cred-pscredential.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Empty(p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-securestring.ps1 - securestring type (unrecognized, empty VarType)

    [Fact]
    public void UtspSecureString_ShouldParseSecureStringAsEmptyVarType()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-cred-securestring.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Empty(p.VarType);
    }

    #endregion

    #region utsp-validatecount.ps1 - ValidateCount on string array

    [Fact]
    public void UtspValidateCount_ShouldParseValidateCountOnStringArray()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatecount.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string[]", p.VarType);
        Assert.True(p.IsMultiValued);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateCount", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validatelength.ps1 - ValidateLength on string

    [Fact]
    public void UtspValidateLength_ShouldParseValidateLengthOnString()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatelength.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateLength", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validatenotnull.ps1 - ValidateNotNull on string

    [Fact]
    public void UtspValidateNotNull_ShouldParseValidateNotNull()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatenotnull.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateNotNull", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validatenotnullorempty.ps1 - ValidateNotNullOrEmpty on string

    [Fact]
    public void UtspValidateNotNullOrEmpty_ShouldParseValidateNotNullOrEmpty()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatenotnullorempty.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateNotNullOrEmpty", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validatepattern.ps1 - ValidatePattern on untyped param

    [Fact]
    public void UtspValidatePattern_ShouldParseValidatePattern()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatepattern.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidatePattern", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validaterange.ps1 - ValidateRange on untyped param

    [Fact]
    public void UtspValidateRange_ShouldParseValidateRange()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validaterange.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Contains(p.Validation, v => v.StartsWith("ValidateRange", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region utsp-validatescript.ps1 - ValidateScript on untyped param

    [Fact]
    public void UtspValidateScript_ShouldParseValidateScriptParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validatescript.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-validateset.ps1 - ValidateSet on string param

    [Fact]
    public void UtspValidateSet_ShouldParseValidateSetAsSelect()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-validateset.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.True(p.IsSelect);
        Assert.False(p.IsMultiValued);
        Assert.Contains("Input", p.AllowedValues);
        Assert.Contains("Output", p.AllowedValues);
        Assert.Contains("Both", p.AllowedValues);
    }

    #endregion

    #region utsp-webjeahostname.ps1 - WebJEAHostname string param

    [Fact]
    public void UtspWebJEAHostname_ShouldParseWebJEAHostnameParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-webjeahostname.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "WebJEAHostname");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-webjeausername.ps1 - WebJEAUsername string param

    [Fact]
    public void UtspWebJEAUsername_ShouldParseWebJEAUsernameParam()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-webjeausername.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "WebJEAUsername");
        Assert.NotNull(p);
        Assert.Equal("string", p.VarType);
        Assert.Equal(PSCmdParam.ParameterType.PSString, p.ParamType);
    }

    #endregion

    #region utsp-help-synopsis.ps1 - synopsis in comment block

    [Fact]
    public void UtspHelpSynopsis_ShouldParseSynopsis()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-help-synopsis.ps1"));

        Assert.Equal("Synopsis String Check", parser.Synopsis);
        Assert.Empty(parser.Description);
        Assert.Empty(parser.Examples);
    }

    #endregion

    #region utsp-help-description.ps1 - description in comment block

    [Fact]
    public void UtspHelpDescription_ShouldParseDescription()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-help-description.ps1"));

        Assert.Empty(parser.Synopsis);
        Assert.Contains("Description String Check", parser.Description);
        Assert.Empty(parser.Examples);
    }

    #endregion

    #region utsp-help-parameter.ps1 - parameter help from comment block

    [Fact]
    public void UtspHelpParameter_ShouldParseParameterHelpDetail()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-help-parameter.ps1"));
        var parameters = parser.GetParameters();

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Contains("Var description", p.HelpDetail);
    }

    #endregion

    #region utsp-help-helpmessage.ps1 - HelpMessage attribute on param

    [Fact]
    public void UtspHelpHelpMessage_ShouldParseHelpMessageAttribute()
    {
        var parser = new PSScriptParser(GetScriptPath("utsp-help-helpmessage.ps1"));
        var parameters = parser.GetParameters();

        Assert.Empty(parser.Synopsis);
        Assert.Empty(parser.Description);

        Assert.Single(parameters);
        var p = FindParam(parameters, "Var");
        Assert.NotNull(p);
        Assert.Equal("Enter Value", p.HelpMessage);
    }

    #endregion
}
