using System.Text.RegularExpressions;

namespace WebJEA;

public class PSCmdParamVal
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public enum ValType
    {
        Mandatory,
        Length,
        Range,
        Pattern,
        Count,
        SetCol,
        NotNull,
        NotNullOrEmpty,
        Err
    }

    private string prvRule;

    public ValType Type { get; set; } = ValType.Err;
    public int UpperLimit { get; set; }
    public int LowerLimit { get; set; }
    public string Pattern { get; set; }
    public List<string> Options { get; set; } = new List<string>();

    public PSCmdParamVal(string rule)
    {
        if (rule.Contains("()")) // unnecessary
        {
            rule = rule.Replace("()", "");
        }

        prvRule = rule;

        string upper = rule.ToUpper();
        if (upper == "VALIDATENOTNULL")
        {
            Type = ValType.NotNull;
        }
        else if (upper == "VALIDATENOTNULLOREMPTY")
        {
            Type = ValType.NotNullOrEmpty;
        }
        else if (upper == "ALLOWNULL" || upper == "ALLOWEMPTYSTRING" || upper == "ALLOWEMPTYCOLLECTION")
        {
            // we don't support these at this time.
        }
        else if (upper.StartsWith("VALIDATESCRIPT(") && upper.EndsWith(")"))
        {
            // can't validate
        }
        else if (upper.StartsWith("VALIDATELENGTH(") && upper.EndsWith(")"))
        {
            Type = ValType.Length;
            ParseRange(prvRule);
        }
        else if (upper.StartsWith("VALIDATERANGE(") && upper.EndsWith(")"))
        {
            Type = ValType.Range;
            ParseRange(prvRule);
        }
        else if (upper.StartsWith("VALIDATEPATTERN(") && upper.EndsWith(")"))
        {
            Type = ValType.Pattern;
            SetPattern(prvRule);
        }
        else if (upper.StartsWith("VALIDATECOUNT(") && upper.EndsWith(")"))
        {
            Type = ValType.Count;
            ParseRange(prvRule);
        }
        else if (upper.StartsWith("VALIDATESET(") && upper.EndsWith(")"))
        {
            Type = ValType.SetCol;
            ParseColSet(prvRule);
        }
        else if (upper == "MANDATORY")
        {
            Type = ValType.Mandatory;
        }
        else
        {
            dlog.Error("Don't know how to parse validation rule: " + rule);
        }
    }

    private void ParseRange(string strInput)
    {
        string pattern = @"^VALIDATE.+\(\s{0,5}(?<lowerlimit>\d+)\s{0,5},\s{0,5}(?<upperlimit>\d+)\s{0,5}\)$";
        var rgx = new Regex(pattern, RegexOptions.IgnoreCase);

        Match mtch = rgx.Match(strInput);
        if (mtch.Success)
        {
            LowerLimit = int.Parse(mtch.Groups["lowerlimit"].Value);
            UpperLimit = int.Parse(mtch.Groups["upperlimit"].Value);
        }
    }

    private void ParseColSet(string strInput)
    {
        // this command returns the contents of the () in ValidateX(XXX)
        // It then parses by commas and returns a list

        var outputset = new List<string>();

        int IDXstart = strInput.IndexOf("(");
        int IDXend = PSScriptParser.AdvIndexOf(strInput, ")", IDXstart);
        string strValues = strInput.Substring(IDXstart + 1, IDXend - IDXstart - 1);

        int IDXLastComma = -1;
        int IDXcomma = PSScriptParser.AdvIndexOf(strValues, ",");
        while (IDXcomma > -1)
        {
            outputset.Add(PSScriptParser.CleanQuotedString(strValues.Substring(IDXLastComma + 1, IDXcomma - IDXLastComma - 1)));

            IDXLastComma = IDXcomma;
            IDXcomma = PSScriptParser.AdvIndexOf(strValues, ",", IDXLastComma);
        }

        // add last value
        outputset.Add(PSScriptParser.CleanQuotedString(strValues.Substring(IDXLastComma + 1)));

        Options = outputset;
    }

    private void SetPattern(string strInput)
    {
        var ptrn = strInput.Substring(strInput.IndexOf("(")).Trim();
        ptrn = ptrn.Substring(1, ptrn.Length - 2).Trim(); // remove the outer parenthesis
        // should now look like "'xxxxx'" where the interior single quotes could be double quotes
        Pattern = ptrn.Substring(1, ptrn.Length - 2); // remove the interior quotes
    }

    public bool IsValid
    {
        get
        {
            // may add other validation
            if (Type == ValType.Err)
            {
                return false;
            }

            return true;
        }
    }

    public string Rule
    {
        get
        {
            return prvRule;
        }
    }
}
