namespace WebJEA.Api.Dto;

public class CommandMetadataDto
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string DisplayName { get; set; }
    public string Synopsis { get; set; }
    public string Description { get; set; }
    public bool HasScript { get; set; }
    public bool HasOnload { get; set; }
    public bool ShowVerbose { get; set; }
    public string RenderMode { get; set; }
    public List<ParameterDto> Parameters { get; set; } = new List<ParameterDto>();
}

public class ParameterDto
{
    public string Name { get; set; }

    /// <summary>string | int | float | date | boolean (from PSCmdParam.ParameterType).</summary>
    public string Type { get; set; }

    /// <summary>text | textarea | checkbox | select | multiselect | date | datetime.</summary>
    public string Control { get; set; }

    /// <summary>
    /// ControlBuilder quirk carried forward: for text/textarea controls a non-empty
    /// HelpMessage replaces the label; other controls render it as a help span.
    /// </summary>
    public string LabelOverride { get; set; }

    public string HelpMessage { get; set; }
    public string HelpDetail { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsMultiValued { get; set; }

    /// <summary>Row count for textarea (5) and multiselect (min(options,5)) controls.</summary>
    public int? Rows { get; set; }

    public List<string> AllowedValues { get; set; }

    /// <summary>string | string[] | bool, mirroring PSScriptParser.ParseDefaultValue output.</summary>
    public object DefaultValue { get; set; }

    public List<ValidationRuleDto> Validation { get; set; } = new List<ValidationRuleDto>();
}

public class ValidationRuleDto
{
    /// <summary>required | mandatoryCheckbox | length | pattern | range | count.</summary>
    public string Type { get; set; }

    public int? Min { get; set; }
    public int? Max { get; set; }
    public string Pattern { get; set; }

    /// <summary>For range rules: integer | float | date (drives client-side parsing).</summary>
    public string ValueType { get; set; }
}
