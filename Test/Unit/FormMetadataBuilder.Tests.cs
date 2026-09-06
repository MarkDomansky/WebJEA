using Xunit;
using WebJEA;
using WebJEA.Api.Dto;

namespace WebJEA.Tests;

public class FormMetadataBuilderTests
{
    private static ParameterDto BuildSingle(PSCmdParam param)
    {
        var cmd = new ConfigCmd { ID = "test" };
        var pscmd = new PSCmd { Parameters = new List<PSCmdParam> { param } };
        var dto = new FormMetadataBuilder().Build(cmd, pscmd, null, "Title", false);
        return Assert.Single(dto.Parameters);
    }

    [Fact]
    public void Build_WebJeaParams_AreOmitted()
    {
        var cmd = new ConfigCmd { ID = "test" };
        var pscmd = new PSCmd
        {
            Parameters = new List<PSCmdParam>
            {
                new PSCmdParam { Name = "WEBJEAUSERNAME", VarType = "string" },
                new PSCmdParam { Name = "WebJEAHostname", VarType = "string" },
                new PSCmdParam { Name = "Visible", VarType = "string" }
            }
        };

        var dto = new FormMetadataBuilder().Build(cmd, pscmd, null, "Title", false);

        var p = Assert.Single(dto.Parameters);
        Assert.Equal("Visible", p.Name);
    }

    [Fact]
    public void Build_NoScript_HasScriptFalseAndNoParameters()
    {
        var cmd = new ConfigCmd { ID = "test" };

        var dto = new FormMetadataBuilder().Build(cmd, null, null, "Title", true);

        Assert.False(dto.HasScript);
        Assert.False(dto.HasOnload);
        Assert.True(dto.ShowVerbose);
        Assert.Empty(dto.Parameters);
    }

    [Fact]
    public void Build_DisplayNameFallsBackToId()
    {
        var cmd = new ConfigCmd { ID = "fallback" };

        var dto = new FormMetadataBuilder().Build(cmd, null, null, "Title", false);

        Assert.Equal("fallback", dto.DisplayName);
    }

    [Fact]
    public void TextControl_HelpMessageBecomesLabelOverride()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string", HelpMessage = "Enter the computer name" };

        var dto = BuildSingle(param);

        Assert.Equal("text", dto.Control);
        Assert.Equal("Enter the computer name", dto.LabelOverride);
    }

    [Fact]
    public void DateControl_HelpMessageStaysHelpMessage()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "datetime", HelpMessage = "Pick a date" };

        var dto = BuildSingle(param);

        Assert.Equal("date", dto.Control);
        Assert.Null(dto.LabelOverride);
        Assert.Equal("Pick a date", dto.HelpMessage);
    }

    [Fact]
    public void DateTimeDirective_ProducesDateTimeControl()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "datetime", DirectiveDateTime = true };

        var dto = BuildSingle(param);

        Assert.Equal("datetime", dto.Control);
        Assert.Equal("date", dto.Type);
    }

    [Fact]
    public void Boolean_ProducesCheckbox()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "switch" };

        var dto = BuildSingle(param);

        Assert.Equal("checkbox", dto.Control);
        Assert.Equal("boolean", dto.Type);
    }

    [Fact]
    public void MandatoryCheckbox_GetsMandatoryCheckboxRule()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "switch" };
        param.AddValidation("Mandatory");

        var dto = BuildSingle(param);

        var rule = Assert.Single(dto.Validation);
        Assert.Equal("mandatoryCheckbox", rule.Type);
    }

    [Fact]
    public void MandatoryText_GetsRequiredRule()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string" };
        param.AddValidation("Mandatory");

        var dto = BuildSingle(param);

        var rule = Assert.Single(dto.Validation);
        Assert.Equal("required", rule.Type);
        Assert.True(dto.IsMandatory);
    }

    [Fact]
    public void ValidateSet_ProducesSelectWithAllowedValues()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string" };
        param.AddValidation("ValidateSet('Input','Output','Both')");

        var dto = BuildSingle(param);

        Assert.Equal("select", dto.Control);
        Assert.Equal(new List<string> { "Input", "Output", "Both" }, dto.AllowedValues);
        // SetCol is expressed by the control type, not a validation rule
        Assert.Empty(dto.Validation);
    }

    [Fact]
    public void ValidateSetOnArray_ProducesMultiselectWithCappedRows()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string[]" };
        param.AddValidation("ValidateSet('A','B','C','D','E','F','G')");

        var dto = BuildSingle(param);

        Assert.Equal("multiselect", dto.Control);
        Assert.Equal(5, dto.Rows);
    }

    [Fact]
    public void ValidateSetOnArray_FewOptions_RowsMatchOptionCount()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string[]" };
        param.AddValidation("ValidateSet('A','B','C')");

        var dto = BuildSingle(param);

        Assert.Equal(3, dto.Rows);
    }

    [Fact]
    public void MultilineDirective_ProducesTextareaWithFiveRows()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string", DirectiveMultiline = true };

        var dto = BuildSingle(param);

        Assert.Equal("textarea", dto.Control);
        Assert.Equal(5, dto.Rows);
    }

    [Fact]
    public void ArrayType_ProducesTextarea()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "int[]" };

        var dto = BuildSingle(param);

        Assert.Equal("textarea", dto.Control);
        Assert.True(dto.IsMultiValued);
        Assert.Equal("int", dto.Type);
    }

    [Fact]
    public void RangeOnFloat_GetsFloatValueType()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "double" };
        param.AddValidation("ValidateRange(0,100)");

        var dto = BuildSingle(param);

        var rule = Assert.Single(dto.Validation);
        Assert.Equal("range", rule.Type);
        Assert.Equal("float", rule.ValueType);
        Assert.Equal(0, rule.Min);
        Assert.Equal(100, rule.Max);
    }

    [Fact]
    public void RangeOnInt_GetsIntegerValueType()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "int" };
        param.AddValidation("ValidateRange(1,5)");

        var dto = BuildSingle(param);

        Assert.Equal("integer", Assert.Single(dto.Validation).ValueType);
    }

    [Fact]
    public void LengthAndPattern_MapToRules()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string" };
        param.AddValidation("ValidateLength(1,10)");
        param.AddValidation("ValidatePattern('^[a-z]+$')");

        var dto = BuildSingle(param);

        Assert.Equal(2, dto.Validation.Count);
        Assert.Contains(dto.Validation, r => r.Type == "length" && r.Min == 1 && r.Max == 10);
        Assert.Contains(dto.Validation, r => r.Type == "pattern" && r.Pattern == "^[a-z]+$");
    }

    [Fact]
    public void ValidateCount_MapsToCountRule()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string[]" };
        param.AddValidation("ValidateCount(1,5)");

        var dto = BuildSingle(param);

        Assert.Contains(dto.Validation, r => r.Type == "count" && r.Min == 1 && r.Max == 5);
    }

    [Fact]
    public void NotNullValidations_ProduceNoClientRule()
    {
        var param = new PSCmdParam { Name = "Var", VarType = "string" };
        param.AddValidation("ValidateNotNullOrEmpty");

        var dto = BuildSingle(param);

        Assert.Empty(dto.Validation);
    }
}
