using System.Globalization;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace WebJEA;

public class PSScriptParser
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private string prvScript;
    private string prvScriptPath;
    private string prvSynopsis;
    private string prvDescription;
    private List<string> prvExamples = new List<string>();
    private Dictionary<string, string> prvParameterHelp = new Dictionary<string, string>();
    private List<PSCmdParam> prvPSParam = new List<PSCmdParam>();
    private bool prvIsValid = false;

    public PSScriptParser(string scriptPath)
    {
        LoadScript(scriptPath);
    }

    public void LoadScript(string scriptPath)
    {
        dlog.Trace("ScriptParser: LoadScript");
        prvScriptPath = scriptPath;
        prvScript = "";
        prvSynopsis = "";
        prvDescription = "";
        prvExamples = new List<string>();
        prvParameterHelp = new Dictionary<string, string>();
        prvPSParam = new List<PSCmdParam>();
        prvIsValid = false;

        if (!File.Exists(prvScriptPath))
        {
            dlog.Trace("PSScriptParser|File Not Exist: '" + prvScriptPath + "'");
            return;
        }

        prvScript = Helpers.GetFileContent(prvScriptPath);

        StartParser();
    }

    public bool IsValid => prvIsValid;

    public string ScriptPath => prvScriptPath;

    public string Synopsis => prvSynopsis;

    public string Description => prvDescription;

    public List<string> Examples => prvExamples;

    public List<PSCmdParam> GetParameters()
    {
        return prvPSParam;
    }

    private void StartParser()
    {
        if (string.IsNullOrWhiteSpace(prvScript)) return;

        if (prvScript.IndexOf("<#") > -1)
        {
            ParseCommentBlock();
        }

        ScriptBlockAst ast = Parser.ParseInput(prvScript, out Token[] tokens, out _);

        if (ast != null && ast.ParamBlock != null)
        {
            ParseAstParameters(ast.ParamBlock, tokens);
        }
    }

    private void ParseCommentBlock()
    {
        dlog.Trace("ScriptParser: Parsing Comment Block");

        int startIDX = prvScript.IndexOf("<#");
        int endIDX = prvScript.IndexOf("\n#>");

        if (startIDX == -1 || endIDX == -1)
        {
            return;
        }

        string commentBlock = prvScript.Substring(startIDX, endIDX - startIDX);
        string[] cbSet = Regex.Split(commentBlock, @"(?=\n\.)");

        foreach (var cbItem in cbSet)
        {
            ParseCommentBlockSection(cbItem.Trim());
        }
    }

    private void ParseCommentBlockSection(string section)
    {
        section = section.Trim();

        string[] sectionarr = section.Split(new[] { '\n' }, 2);
        if (sectionarr.Length != 2) return;

        string header = sectionarr[0].Trim();
        string comment = sectionarr[1].Trim();

        if (header.StartsWith("."))
        {
            header = header.Trim('.');
            if (header.ToUpper() == "SYNOPSIS")
            {
                dlog.Trace("ScriptParser: CommentBlockSection: Adding SYNOPSIS");
                prvSynopsis = comment;
            }
            else if (header.ToUpper() == "DESCRIPTION")
            {
                dlog.Trace("ScriptParser: CommentBlockSection: Adding DESCRIPTION");
                prvDescription = comment;
            }
            else if (header.ToUpper() == "EXAMPLE")
            {
                dlog.Trace("ScriptParser: CommentBlockSection: Adding EXAMPLE");
                prvExamples.Add(comment);
            }
            else if (header.ToUpper().StartsWith("PARAMETER"))
            {
                string paramname = header.ToUpper().Replace("PARAMETER", "").Trim();
                dlog.Trace("ScriptParser: CommentBlockSection: Adding Description for PARAMETER: " + paramname);
                prvParameterHelp.Add(paramname, comment);
            }
            else
            {
                dlog.Error("ScriptParser: Could not parse commentblock: " + header);
            }
        }
    }

    private void ParseAstParameters(ParamBlockAst paramBlock, Token[] tokens)
    {
        dlog.Trace("ScriptParser: ParseAstParameters");

        var multilineLines = new HashSet<int>();
        var datetimeLines = new HashSet<int>();

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.Comment)
            {
                var commentText = token.Text.Trim();
                if (commentText.Equals("#WEBJEA-MULTILINE", StringComparison.OrdinalIgnoreCase))
                {
                    multilineLines.Add(token.Extent.StartLineNumber);
                }
                else if (commentText.Equals("#WEBJEA-DATETIME", StringComparison.OrdinalIgnoreCase))
                {
                    datetimeLines.Add(token.Extent.StartLineNumber);
                }
            }
        }

        int prevEndLine = paramBlock.Extent.StartLineNumber;

        foreach (var paramAst in paramBlock.Parameters)
        {
            var psparam = new PSCmdParam();

            psparam.Name = paramAst.Name.VariablePath.UserPath;
            dlog.Trace("ScriptParser: ParseAstParameters: Processing parameter: " + psparam.Name);

            foreach (var line in multilineLines)
            {
                if (line >= prevEndLine && line <= paramAst.Extent.EndLineNumber)
                {
                    dlog.Trace("ScriptParser: ParseAstParameters: #WEBJEA-MULTILINE for: " + psparam.Name);
                    psparam.DirectiveMultiline = true;
                }
            }

            foreach (var line in datetimeLines)
            {
                if (line >= prevEndLine && line <= paramAst.Extent.EndLineNumber)
                {
                    dlog.Trace("ScriptParser: ParseAstParameters: #WEBJEA-DATETIME for: " + psparam.Name);
                    psparam.DirectiveDateTime = true;
                }
            }

            foreach (var attr in paramAst.Attributes)
            {
                if (attr is TypeConstraintAst typeConstraint)
                {
                    ProcessTypeConstraint(psparam, typeConstraint);
                }
                else if (attr is AttributeAst attribute)
                {
                    ProcessAttribute(psparam, attribute);
                }
            }

            if (paramAst.DefaultValue != null)
            {
                psparam.DefaultValue = ParseDefaultValue(paramAst.DefaultValue.Extent.Text);
            }

            if (prvParameterHelp.ContainsKey(psparam.Name.ToUpper()))
            {
                psparam.HelpDetail = prvParameterHelp[psparam.Name.ToUpper()];
            }

            prvPSParam.Add(psparam);
            prevEndLine = paramAst.Extent.EndLineNumber;
        }

        dlog.Trace("ScriptParser: ParseAstParameters: Found " + prvPSParam.Count.ToString() + " parameters");
    }

    private void ProcessTypeConstraint(PSCmdParam psparam, TypeConstraintAst typeAst)
    {
        string typeName = typeAst.TypeName.Name;
        dlog.Trace("ScriptParser: ProcessTypeConstraint: " + typeName);

        if (typeName.StartsWith("boolean", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("datetime", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("switch", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("int", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("uint", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("float", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("double", StringComparison.InvariantCultureIgnoreCase) ||
            typeName.StartsWith("string", StringComparison.InvariantCultureIgnoreCase))
        {
            psparam.VarType = typeName;
        }
        else
        {
            dlog.Warn("ScriptParser: Unrecognized type: " + typeName);
        }
    }

    private void ProcessAttribute(PSCmdParam psparam, AttributeAst attrAst)
    {
        var attrName = attrAst.TypeName.Name;
        dlog.Trace("ScriptParser: ProcessAttribute: " + attrName);

        if (string.Equals(attrName, "Parameter", StringComparison.InvariantCultureIgnoreCase))
        {
            ProcessParameterAttribute(psparam, attrAst);
        }
        else if (attrName.StartsWith("ValidateLength", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidateRange", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidatePattern", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidateCount", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidateSet", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidateNotNull", StringComparison.InvariantCultureIgnoreCase) ||
                 attrName.StartsWith("ValidateNotNullOrEmpty", StringComparison.InvariantCultureIgnoreCase))
        {
            var extentText = attrAst.Extent.Text;
            var valstring = extentText.Substring(1, extentText.LastIndexOf("]") - 1);
            dlog.Trace("ScriptParser: ProcessAttribute: Adding validation: " + valstring);
            psparam.AddValidation(valstring);
        }
    }

    private void ProcessParameterAttribute(PSCmdParam psparam, AttributeAst attrAst)
    {
        foreach (var namedArg in attrAst.NamedArguments)
        {
            if (string.Equals(namedArg.ArgumentName, "Mandatory", StringComparison.InvariantCultureIgnoreCase))
            {
                if (namedArg.ExpressionOmitted)
                {
                    dlog.Trace("ScriptParser: ProcessParameterAttribute: Mandatory (expression omitted)");
                    psparam.AddValidation("Mandatory");
                }
                else if (namedArg.Argument is VariableExpressionAst varExpr)
                {
                    if (!string.Equals(varExpr.VariablePath.UserPath, "false", StringComparison.InvariantCultureIgnoreCase))
                    {
                        dlog.Trace("ScriptParser: ProcessParameterAttribute: Mandatory=$true");
                        psparam.AddValidation("Mandatory");
                    }
                }
                else
                {
                    psparam.AddValidation("Mandatory");
                }
            }
            else if (string.Equals(namedArg.ArgumentName, "HelpMessage", StringComparison.InvariantCultureIgnoreCase))
            {
                if (namedArg.Argument is StringConstantExpressionAst strConst)
                {
                    psparam.HelpMessage = strConst.Value;
                    dlog.Trace("ScriptParser: ProcessParameterAttribute: HelpMessage: " + psparam.HelpMessage);
                }
            }
        }
    }

    public static int AdvIndexOf(string strInput, string chars, int startidx = -1)
    {
        // pass one char easily
        return AdvIndexOf(strInput, new List<string> { chars }, startidx);
    }

    public static int AdvIndexOf(string strInput, List<string> chars, int startidx = -1)
    {
        // this is kind of like indexOf, except we search character by character and if we encounter certain characters ('(','[','{',''','"'),
        //   we recurse the same function until we eventually get the end of file or find the correct closing character
        // this is because a command like [ValidateScript({$_ -eq "[`"]"})] is valid but would cause a parsing issue if we just indexof the closing character.

        // looks for multiple possible characters, but still support nested
        int endIDX = strInput.Length;
        int IDX = startidx;

        if (strInput == "") return -1;

        while (IDX < endIDX - 1)
        {
            IDX += 1;
            string IDXchar = strInput.Substring(IDX, 1);
            if (IDXchar == "`")
            {
                IDX += 1; // skip the next character
            }
            else if (IDXchar == "(")
            {
                IDX = AdvIndexOf(strInput, ")", IDX);
            }
            else if (IDXchar == "[")
            {
                IDX = AdvIndexOf(strInput, "]", IDX);
            }
            else if (IDXchar == "{")
            {
                IDX = AdvIndexOf(strInput, "}", IDX);
            }
            else if (IDXchar == "\"" && !chars.Contains("\"")) // if we're looking for a " to return, we don't want to recurse again
            {
                IDX = AdvIndexOf(strInput, "\"", IDX);
            }
            else if (IDXchar == "'" && !chars.Contains("'") && !chars.Contains("\"")) // if we're looking for a ' to return, we don't want to recurse again
            {
                IDX = AdvIndexOf(strInput, "'", IDX);
            }
            else
            {
                foreach (var charstr in chars)
                {
                    if (string.Equals(strInput.Substring(IDX, charstr.Length), charstr, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return IDX;
                    }
                }
            }
        }

        // if the character isn't found, then return -1 like indexof
        return -1;
    }

    public static string ReplaceBackTicks(string strInput)
    {
        return strInput.Replace("`\"", "\"").Replace("`r", "\r").Replace("`n", "\n").Replace("`$", "$");
    }

    public static object ParseDefaultValue(string strInput)
    {
        strInput = strInput.Trim();

        // if number, treat as num
        if (IsNumeric(strInput))
        {
            return strInput;
            // we dont have to try and parse or convert because it is still just a string in html
        }
        else if (strInput.StartsWith("\""))
        {
            // is a basic string
            return CleanQuotedString(strInput);
        }
        else if (strInput.StartsWith("@"))
        {
            var retList = new List<string>(); // these have to be strings
            int IDX = strInput.IndexOf("(");
            while (IDX < strInput.Length - 1)
            {
                int endIDX = AdvIndexOf(strInput, new List<string> { ",", ")" }, IDX);
                string subval = strInput.Substring(IDX + 1, endIDX - IDX - 1);
                retList.Add(Convert.ToString(ParseDefaultValue(subval))); // this parses properly as string or integer.
                IDX = endIDX;
            }

            return retList;
        }
        else if (strInput.StartsWith("$"))
        {
            if (strInput.ToLower().Contains("true"))
            {
                return true;
            }
            else if (strInput.ToLower().Contains("false"))
            {
                return false;
            }
            else
            {
                return strInput;
            }
        }
        else
        {
            return CleanQuotedString(strInput);
        }
    }

    public static string CleanQuotedString(string strInput)
    {
        string stroutput = strInput.Trim();
        int quotecharidx = AdvIndexOf(stroutput, new List<string> { "\"", "'" });
        if (quotecharidx == 0)
        {
            // get the first quote character
            string quotechar = stroutput.Substring(quotecharidx, 1);

            if (quotechar == "\"") stroutput = ReplaceBackTicks(stroutput);

            // remove outer quotes
            stroutput = stroutput.Substring(1, stroutput.Length - 2);
        }

        return stroutput;
    }

    private static bool IsNumeric(string strInput)
    {
        return double.TryParse(strInput, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }
}
