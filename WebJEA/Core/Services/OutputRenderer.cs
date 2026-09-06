using System.Net;
using System.Text.RegularExpressions;

namespace WebJEA;

public class OutputRenderer
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public string ConvertToHTML(Queue<OutputData> outputData)
    {
        const string NLOGPREFIX = "WEBJEA:";
        string outputstr = "";

        foreach (OutputData line in outputData)
        {
            if (line.Content.StartsWith(NLOGPREFIX))
            {
                dlog.Info(line.Content.Substring(NLOGPREFIX.Length).Trim());
            }
            else
            {
                switch (line.OutputType)
                {
                    case OutputType.Debug:
                        outputstr += EncodeOutput("DEBUG: " + line.Content, "psdebug");
                        break;
                    case OutputType.Err:
                        outputstr += EncodeOutput(line.Content, "pserror");
                        break;
                    case OutputType.Warn:
                        outputstr += EncodeOutput("WARNING: " + line.Content, "pswarning");
                        break;
                    case OutputType.Info:
                        outputstr += EncodeOutput(line.Content, "psoutput");
                        break;
                    case OutputType.Verbose:
                        outputstr += EncodeOutput("VERBOSE: " + line.Content, "psverbose");
                        break;
                    case OutputType.Output:
                        outputstr += EncodeOutput(line.Content, "psoutput");
                        break;
                    default:
                        outputstr += EncodeOutput(line.Content, "psoutput");
                        break;
                }
            }
        }

        return outputstr;
    }

    private string EncodeOutput(string input, string baseclass)
    {
        string output = input;
        output = WebUtility.HtmlEncode(output);

        output = EncodeOutputTags(output);

        output = "<span class=\"" + baseclass + "\">" + output + "</span><br/>";
        return output;
    }

    internal string EncodeOutputTags(string input)
    {
        const RegexOptions rexopt = RegexOptions.IgnoreCase | RegexOptions.Multiline;
        const string rexA = @"\[\[a\|(.+?)\|(.+?)\]\]";
        const string repA = "<a href='$1'>$2</a>";
        var rgxA = new Regex(rexA, rexopt);
        const string rexSpan = @"\[\[span\|(.+?)\|(.+?)\]\]";
        const string repSpan = "<span Class='$1'>$2</span>";
        var rgxSpan = new Regex(rexSpan, rexopt);
        const string rexImg = @"\[\[img\|(.*?)\|(.+?)\]\]";
        const string repImg = "<img class='$1' src='$2' />";
        var rgxImg = new Regex(rexImg, rexopt);

        int idx = input.LastIndexOf("[[");
        while (idx > -1)
        {
            input = rgxA.Replace(input, repA, 1, idx);
            input = rgxSpan.Replace(input, repSpan, 1, idx);
            input = rgxImg.Replace(input, repImg, 1, idx);
            if (idx > 0)
            {
                idx = input.LastIndexOf("[[", idx - 1);
            }
            else
            {
                idx = -1;
            }
        }

        return input;
    }
}
