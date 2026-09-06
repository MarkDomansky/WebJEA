using WebJEA.Api.Dto;

namespace WebJEA;

/// <summary>
/// Maps parsed PowerShell parameters to the JSON metadata contract consumed by
/// wwwroot/resources/form-renderer.js. This replaces ControlBuilder's server-side
/// control generation; the mapping rules mirror ControlBuilder.vb exactly.
/// </summary>
public class FormMetadataBuilder
{
    public CommandMetadataDto Build(ConfigCmd cmd, PSCmd scriptCmd, PSCmd onloadCmd, string title, bool isGlobalUser)
    {
        var dto = new CommandMetadataDto
        {
            Id = cmd.ID,
            Title = title,
            DisplayName = cmd.DisplayName ?? cmd.ID,
            Synopsis = cmd.Synopsis,
            Description = cmd.Description,
            HasScript = scriptCmd != null,
            HasOnload = onloadCmd != null,
            ShowVerbose = isGlobalUser,
            RenderMode = cmd.RenderMode
        };

        var parameters = scriptCmd != null ? scriptCmd.Parameters : new List<PSCmdParam>();
        foreach (PSCmdParam param in parameters)
        {
            // WEBJEA* parameters are injected server-side at execution and never rendered
            if (param.Name.ToUpper().StartsWith("WEBJEA")) continue;

            dto.Parameters.Add(BuildParameter(param));
        }

        return dto;
    }

    private static ParameterDto BuildParameter(PSCmdParam param)
    {
        string control = ResolveControl(param);

        var dto = new ParameterDto
        {
            Name = param.Name,
            Type = ResolveType(param),
            Control = control,
            HelpMessage = param.HelpMessage,
            HelpDetail = param.HelpDetail,
            IsMandatory = param.IsMandatory,
            IsMultiValued = param.IsMultiValued,
            AllowedValues = param.AllowedValues,
            DefaultValue = param.DefaultValue
        };

        // ControlBuilder.NewControlString replaces the label with HelpMessage for
        // plain string controls; every other control keeps the name as label and
        // renders HelpMessage as a separate help-message span.
        if ((control == "text" || control == "textarea") && !string.IsNullOrWhiteSpace(param.HelpMessage))
        {
            dto.LabelOverride = param.HelpMessage;
        }

        if (control == "textarea")
        {
            dto.Rows = 5;
        }
        else if (control == "multiselect")
        {
            dto.Rows = param.AllowedValues.Count < 5 ? param.AllowedValues.Count : 5;
        }

        foreach (PSCmdParamVal valobj in param.ValidationObjects)
        {
            var rule = BuildValidationRule(param, valobj);
            if (rule != null)
            {
                dto.Validation.Add(rule);
            }
        }

        return dto;
    }

    private static string ResolveControl(PSCmdParam param)
    {
        if (param.IsSelect)
        {
            return param.IsMultiValued ? "multiselect" : "select";
        }

        switch (param.ParamType)
        {
            case PSCmdParam.ParameterType.PSBoolean:
                return "checkbox";
            case PSCmdParam.ParameterType.PSDate:
                return param.DirectiveDateTime ? "datetime" : "date";
            default:
                return (param.IsMultiValued || param.DirectiveMultiline) ? "textarea" : "text";
        }
    }

    private static string ResolveType(PSCmdParam param)
    {
        switch (param.ParamType)
        {
            case PSCmdParam.ParameterType.PSInt: return "int";
            case PSCmdParam.ParameterType.PSFloat: return "float";
            case PSCmdParam.ParameterType.PSDate: return "date";
            case PSCmdParam.ParameterType.PSBoolean: return "boolean";
            default: return "string";
        }
    }

    private static ValidationRuleDto BuildValidationRule(PSCmdParam param, PSCmdParamVal valobj)
    {
        switch (valobj.Type)
        {
            case PSCmdParamVal.ValType.Mandatory:
                if (param.ParamType == PSCmdParam.ParameterType.PSBoolean)
                {
                    return new ValidationRuleDto { Type = "mandatoryCheckbox" };
                }

                return new ValidationRuleDto { Type = "required" };

            case PSCmdParamVal.ValType.Length:
                return new ValidationRuleDto { Type = "length", Min = valobj.LowerLimit, Max = valobj.UpperLimit };

            case PSCmdParamVal.ValType.Pattern:
                return new ValidationRuleDto { Type = "pattern", Pattern = valobj.Pattern };

            case PSCmdParamVal.ValType.Range:
                string valueType = "integer";
                if (param.ParamType == PSCmdParam.ParameterType.PSFloat) valueType = "float";
                else if (param.ParamType == PSCmdParam.ParameterType.PSDate) valueType = "date";
                return new ValidationRuleDto { Type = "range", Min = valobj.LowerLimit, Max = valobj.UpperLimit, ValueType = valueType };

            case PSCmdParamVal.ValType.Count:
                return new ValidationRuleDto { Type = "count", Min = valobj.LowerLimit, Max = valobj.UpperLimit };

            default:
                // SetCol is expressed as select/multiselect; NotNull/NotNullOrEmpty had no
                // client-side validator in ControlBuilder (server validation still applies)
                return null;
        }
    }
}
