namespace WebJEA;

public class PSCmdParam
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public enum ParameterType
    {
        PSString,
        PSInt,
        PSFloat,
        PSDate,
        PSBoolean
    }

    public string Name { get; set; }
    public string HelpMessage { get; set; } = "";
    public string HelpDetail { get; set; } = "";
    public bool DirectiveMultiline { get; set; } = false;
    public bool DirectiveDateTime { get; set; } = false;
    public string VarType { get; set; } = "";
    public object DefaultValue { get; set; } = null;
    // TODO: Add support for more than just string default values - can we support arrays?
    public List<string> Validation { get; set; } = new List<string>();

    public bool IsMandatory
    {
        get
        {
            foreach (string val in Validation)
            {
                if (val.ToUpper() == "MANDATORY")
                {
                    // parameter is required
                    return true;
                }
            }

            return false;
        }
    }

    public ParameterType ParamType
    {
        get
        {
            var vartypestr = VarType.ToLower();
            if (vartypestr.StartsWith("string"))
            {
                return ParameterType.PSString;
            }
            else if (vartypestr == "datetime")
            {
                return ParameterType.PSDate;
            }
            else if (vartypestr.StartsWith("single") || vartypestr.StartsWith("double") || vartypestr.StartsWith("float"))
            {
                return ParameterType.PSFloat;
            }
            else if (vartypestr.StartsWith("bool") || vartypestr == "switch")
            {
                return ParameterType.PSBoolean;
            }
            else if (vartypestr.StartsWith("int") || vartypestr.StartsWith("uint") || vartypestr.StartsWith("byte") || vartypestr.StartsWith("long"))
            {
                return ParameterType.PSInt;
            }

            // by default we treat a value as string.  This includes PSCredential
            return ParameterType.PSString;
        }
    }

    public bool IsMultiValued
    {
        get
        {
            return VarType.Contains("[]");
        }
    }

    public List<string> AllowedValues
    {
        get
        {
            // this param does not explicitly define allowed values
            if (IsSelect == false) return null;

            foreach (PSCmdParamVal valobj in ValidationObjects)
            {
                if (valobj.Type == PSCmdParamVal.ValType.SetCol)
                {
                    return valobj.Options;
                }
            }

            // should not have gotten here
            return null;
        }
    }

    public bool IsSelect
    {
        get
        {
            foreach (PSCmdParamVal valobj in ValidationObjects)
            {
                if (valobj.Type == PSCmdParamVal.ValType.SetCol)
                {
                    // parameter is restricted
                    return true;
                }
            }

            // no validateset found
            return false;
        }
    }

    public List<PSCmdParamVal> ValidationObjects
    {
        get
        {
            var retobjs = new List<PSCmdParamVal>();
            foreach (string rule in Validation)
            {
                var obj = new PSCmdParamVal(rule);
                if (obj.IsValid)
                {
                    retobjs.Add(obj);
                }
            }

            return retobjs;
        }
    }

    public void AddValidation(string valstring)
    {
        // TODO add support for properly managing conflicting validation options
        if (valstring.ToUpper().StartsWith("VALIDATE") || valstring.ToUpper().StartsWith("ALLOW") || valstring.ToUpper().StartsWith("MANDATORY"))
        {
            if (!Validation.Contains(valstring))
            {
                // don't add precise duplicates.  Doesn't stop from adding incompatible validation commands
                Validation.Add(valstring);
            }
        }
        else if (valstring.ToUpper().StartsWith("ALIAS"))
        {
            // do nothing
        }
        else // variable
        {
            dlog.Warn("Unexpected Validation Type not supported: " + valstring);
        }
    }

    public PSCmdParam Clone()
    {
        var psparam = new PSCmdParam();
        psparam.Name = Name;
        psparam.HelpMessage = HelpMessage;
        psparam.HelpDetail = HelpDetail;
        psparam.VarType = VarType;
        psparam.DirectiveDateTime = DirectiveDateTime;
        psparam.DirectiveMultiline = DirectiveMultiline;
        psparam.DefaultValue = DefaultValue;
        foreach (string val in Validation)
        {
            psparam.AddValidation(val);
        }

        return psparam;
    }

    public void MergeUnder(PSCmdParam psparam)
    {
        // this will merge "under" the current parameter.
        // it will NOT overwrite properties (Help, etc), but if there is no value specified, it will add the value

        if (string.IsNullOrWhiteSpace(HelpMessage)) HelpMessage = psparam.HelpMessage;
        if (string.IsNullOrWhiteSpace(HelpDetail)) HelpDetail = psparam.HelpDetail;
        if (string.IsNullOrWhiteSpace(VarType)) VarType = psparam.VarType;
        if (IsBlankString(DefaultValue)) DefaultValue = psparam.DefaultValue;
        foreach (string valstr in psparam.Validation)
        {
            AddValidation(valstr);
        }
    }

    public void MergeOver(PSCmdParam psparam)
    {
        // this will merge "over" the current parameter.
        // it WILL overwrite properties (Help, etc), if specified
        // validation is always merge

        if (!string.IsNullOrWhiteSpace(psparam.HelpMessage)) HelpMessage = psparam.HelpMessage;
        if (!string.IsNullOrWhiteSpace(psparam.HelpDetail)) HelpDetail = psparam.HelpDetail;
        if (!string.IsNullOrWhiteSpace(psparam.VarType)) VarType = psparam.VarType;
        if (!IsBlankString(psparam.DefaultValue)) DefaultValue = psparam.DefaultValue;
        foreach (string valstr in psparam.Validation)
        {
            AddValidation(valstr);
        }
    }

    public string FieldName
    {
        get
        {
            return Name;
        }
    }

    private static bool IsBlankString(object value)
    {
        if (value == null) return true;
        if (value is string s) return string.IsNullOrWhiteSpace(s);
        return false;
    }
}
