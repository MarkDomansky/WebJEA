using System.Management.Automation;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebJEA.Api;

/// <summary>
/// POST /api/execute — near-1:1 port of ApiExecuteHandler.vb. The JSON contract is frozen
/// (PSWebParser.js depends on it), so this endpoint keeps the Newtonsoft JToken pipeline.
/// </summary>
public static class ExecuteEndpoint
{
    private static readonly NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    public static async Task Handle(HttpContext context)
    {
        context.Response.ContentType = "application/json";

        try
        {
            if (!HttpMethods.IsPost(context.Request.Method))
            {
                await WriteResponse(context, 405, "Method Not Allowed. Use POST.");
                return;
            }

            string contentType = context.Request.ContentType ?? "";
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponse(context, 415, "Unsupported Media Type. Use application/json.");
                return;
            }

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await WriteResponse(context, 401, "Authentication required.");
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.Body))
            {
                body = await reader.ReadToEndAsync();
            }

            JObject requestObj;
            try
            {
                requestObj = JObject.Parse(body);
            }
            catch (JsonReaderException)
            {
                await WriteResponse(context, 400, "Invalid JSON in request body.");
                return;
            }

            string cmdid = requestObj.Value<string>("cmdid") ?? "";
            if (string.IsNullOrWhiteSpace(cmdid))
            {
                await WriteResponse(context, 400, "cmdid is required.");
                return;
            }

            // Sanitize cmdid to alphanumeric, hyphens, underscores, and dots only
            if (!Regex.IsMatch(cmdid, @"^[a-zA-Z0-9_\-\.]+$"))
            {
                await WriteResponse(context, 400, "Invalid cmdid format.");
                return;
            }

            JObject requestParams = null;
            if (requestObj.TryGetValue("parameters", out JToken paramToken))
            {
                if (paramToken.Type == JTokenType.Object)
                {
                    requestParams = (JObject)paramToken;
                }
                else if (paramToken.Type != JTokenType.Null)
                {
                    await WriteResponse(context, 400, "parameters must be a JSON object.");
                    return;
                }
            }

            // Load config via CommandService
            CommandService cmdSvc;
            try
            {
                cmdSvc = EndpointHelpers.LoadCommandService(context);
            }
            catch (Exception ex)
            {
                dlog.Error("API: " + ex.Message + (ex.InnerException != null ? ": " + ex.InnerException.Message : ""));
                await WriteResponse(context, 500, "Internal server error: configuration failure.");
                return;
            }

            // Build the user context from the authenticated principal
            IUserContext uinfo = EndpointHelpers.GetUserContext(context);

            // Check authorization
            if (!cmdSvc.Auth.IsCommandAvailable(uinfo, cmdid))
            {
                dlog.Warn("API: User " + uinfo.UserName + " denied access to cmdid " + cmdid);
                await WriteResponse(context, 403, "Access denied. Command not available or insufficient permissions.");
                return;
            }

            bool runOnload = false;
            if (requestObj.TryGetValue("runOnload", out JToken runOnloadToken) && runOnloadToken.Type == JTokenType.Boolean)
            {
                runOnload = runOnloadToken.Value<bool>();
            }

            bool verbose = false;
            if (requestObj.TryGetValue("verbose", out JToken verboseToken) && verboseToken.Type == JTokenType.Boolean)
            {
                verbose = verboseToken.Value<bool>();
            }

            ConfigCmd cmd = cmdSvc.GetCommand(uinfo, cmdid);

            if (cmd == null)
            {
                dlog.Error("API: Command " + cmdid + " not found after authorization check");
                await WriteResponse(context, 403, "Command not found.");
                return;
            }

            PSCmd scriptCmd = runOnload ? cmdSvc.GetOnloadCmd(cmdid) : cmdSvc.GetScriptCmd(cmdid);

            if (scriptCmd == null)
            {
                if (runOnload)
                {
                    await WriteResponse(context, 400, "No onload script configured for cmdid.");
                }
                else
                {
                    await WriteResponse(context, 400, "No script configured for cmdid.");
                }

                return;
            }

            string userHostName = context.Connection.RemoteIpAddress?.ToString() ?? "";

            // Validate and build parameters
            var psParams = new Dictionary<string, object>();
            var validationErrors = new List<string>();

            if (scriptCmd.Parameters != null)
            {
                foreach (PSCmdParam param in scriptCmd.Parameters)
                {
                    // Handle WEBJEA* internal parameters
                    if (param.Name.ToUpper().StartsWith("WEBJEA"))
                    {
                        if (param.Name.ToUpper() == "WEBJEAUSERNAME")
                        {
                            psParams.Add(param.Name, uinfo.UserName);
                        }
                        else if (param.Name.ToUpper() == "WEBJEAHOSTNAME")
                        {
                            psParams.Add(param.Name, userHostName);
                        }
                        else
                        {
                            dlog.Warn("API: Parameter '" + param.Name + "' is not a recognized internal parameter.");
                        }

                        continue;
                    }

                    JToken rawValue = null;
                    bool hasValue = false;
                    if (requestParams != null)
                    {
                        // Perform a case-insensitive lookup so incoming JSON parameter names are not case sensitive
                        hasValue = requestParams.TryGetValue(param.Name, StringComparison.OrdinalIgnoreCase, out rawValue)
                                   && rawValue.Type != JTokenType.Null;
                    }

                    // Check mandatory
                    if (param.IsMandatory && !hasValue)
                    {
                        validationErrors.Add("Parameter '" + param.Name + "' is required.");
                        continue;
                    }

                    if (!hasValue) continue;

                    // Convert value based on param type
                    object convertedValue;
                    try
                    {
                        convertedValue = ConvertParameterValue(param, rawValue);
                    }
                    catch (Exception ex)
                    {
                        validationErrors.Add("Parameter '" + param.Name + "': " + ex.Message);
                        continue;
                    }

                    // Validate against rules
                    List<string> paramErrors = ValidateParameter(param, convertedValue);
                    if (paramErrors.Count > 0)
                    {
                        validationErrors.AddRange(paramErrors);
                        continue;
                    }

                    psParams.Add(param.Name, convertedValue);
                }
            }

            if (validationErrors.Count > 0)
            {
                await WriteResponse(context, 400, "Parameter validation failed.", null, validationErrors);
                return;
            }

            // Execute the command
            var scriptSvc = context.RequestServices.GetRequiredService<ScriptExecutionService>();
            IScriptEngine ps = scriptSvc.Execute(scriptCmd.Script, psParams, scriptCmd.LogParameters ?? true,
                                                 uinfo.UserName, userHostName,
                                                 verbose: verbose, pipeToOutString: false);

            const string NLOGPREFIX = "WEBJEA:";

            // Build messages from all streams in order; filter server-side log directives
            var messages = new JArray();
            Queue<OutputData> streamData = ps.GetOutputData();
            while (streamData.Count > 0)
            {
                OutputData item = streamData.Dequeue();
                if (item.Content.StartsWith(NLOGPREFIX))
                {
                    dlog.Info(item.Content.Substring(NLOGPREFIX.Length).Trim());
                    continue;
                }

                var msgObj = new JObject();
                msgObj["stream"] = item.OutputType.ToString().ToLower();
                msgObj["message"] = item.Content;
                messages.Add(msgObj);
            }

            // Serialize output objects
            var outputArray = new JArray();
            foreach (PSObject psObj in ps.GetOutputObjects())
            {
                outputArray.Add(ConvertPSObjectToJToken(psObj));
            }

            JToken output;
            if (outputArray.Count == 1)
            {
                output = outputArray[0];
            }
            else if (outputArray.Count == 0)
            {
                output = JValue.CreateNull();
            }
            else
            {
                output = outputArray;
            }

            int statusCode = 200;
            string statusMessage = "OK";

            // Check if there are any errors in the output stream
            if (messages.Any(m => m["stream"]?.ToString() == "err"))
            {
                statusMessage = "Completed with errors in stream.";
            }

            dlog.Info("API: Executed|" + cmdid + "|Onload=" + runOnload.ToString() + "|User=" + uinfo.UserName + "|Status=" + statusCode + "|Runtime=" + ps.Runtime);

            await WriteResponse(context, statusCode, statusMessage, output, null, messages);
        }
        catch (Exception ex)
        {
            dlog.Error("API: Unhandled exception: " + ex.ToString());
            await WriteResponse(context, 500, "Internal server error.");
        }
    }

    private static object ConvertParameterValue(PSCmdParam param, JToken rawValue)
    {
        if (param.IsMultiValued)
        {
            // Expect an array
            if (rawValue.Type == JTokenType.Array)
            {
                var arr = (JArray)rawValue;
                var strList = new List<string>();
                foreach (JToken item in arr)
                {
                    strList.Add(item.ToString());
                }

                return strList.ToArray();
            }

            // Single value provided, wrap in array
            return new string[] { rawValue.ToString() };
        }

        switch (param.ParamType)
        {
            case PSCmdParam.ParameterType.PSBoolean:
                {
                    if (rawValue.Type == JTokenType.Boolean)
                    {
                        return rawValue.Value<bool>();
                    }

                    string strVal = rawValue.ToString().ToLower();
                    if (strVal == "true" || strVal == "1") return true;
                    if (strVal == "false" || strVal == "0") return false;
                    throw new ArgumentException("Invalid boolean value.");
                }

            case PSCmdParam.ParameterType.PSInt:
                {
                    if (rawValue.Type == JTokenType.Integer)
                    {
                        return rawValue.Value<int>();
                    }

                    if (int.TryParse(rawValue.ToString(), out int intVal)) return intVal;
                    throw new ArgumentException("Invalid integer value.");
                }

            case PSCmdParam.ParameterType.PSFloat:
                {
                    if (rawValue.Type == JTokenType.Float || rawValue.Type == JTokenType.Integer)
                    {
                        return rawValue.Value<double>();
                    }

                    if (double.TryParse(rawValue.ToString(), out double dblVal)) return dblVal;
                    throw new ArgumentException("Invalid numeric value.");
                }

            case PSCmdParam.ParameterType.PSDate:
                {
                    if (DateTime.TryParse(rawValue.ToString(), out DateTime dtVal)) return dtVal;
                    throw new ArgumentException("Invalid date value.");
                }

            default:
                return rawValue.ToString();
        }
    }

    private static List<string> ValidateParameter(PSCmdParam param, object value)
    {
        var errors = new List<string>();

        foreach (PSCmdParamVal valObj in param.ValidationObjects)
        {
            switch (valObj.Type)
            {
                case PSCmdParamVal.ValType.SetCol:
                    if (param.IsMultiValued && value is string[] items)
                    {
                        foreach (string item in items)
                        {
                            if (!valObj.Options.Contains(item))
                            {
                                errors.Add("Parameter '" + param.Name + "': value '" + item + "' is not in the allowed set.");
                            }
                        }
                    }
                    else
                    {
                        if (!valObj.Options.Contains(value.ToString()))
                        {
                            errors.Add("Parameter '" + param.Name + "': value '" + value.ToString() + "' is not in the allowed set.");
                        }
                    }

                    break;

                case PSCmdParamVal.ValType.Length:
                    {
                        string strVal = value.ToString();
                        if (strVal.Length < valObj.LowerLimit || strVal.Length > valObj.UpperLimit)
                        {
                            errors.Add("Parameter '" + param.Name + "': length must be between " + valObj.LowerLimit + " and " + valObj.UpperLimit + ".");
                        }

                        break;
                    }

                case PSCmdParamVal.ValType.Range:
                    {
                        if (double.TryParse(value.ToString(), out double numVal))
                        {
                            if (numVal < valObj.LowerLimit || numVal > valObj.UpperLimit)
                            {
                                errors.Add("Parameter '" + param.Name + "': value must be between " + valObj.LowerLimit + " and " + valObj.UpperLimit + ".");
                            }
                        }

                        break;
                    }

                case PSCmdParamVal.ValType.Pattern:
                    if (!Regex.IsMatch(value.ToString(), valObj.Pattern))
                    {
                        errors.Add("Parameter '" + param.Name + "': value does not match the required pattern.");
                    }

                    break;

                case PSCmdParamVal.ValType.Count:
                    if (param.IsMultiValued && value is string[] arr)
                    {
                        if (arr.Length < valObj.LowerLimit || arr.Length > valObj.UpperLimit)
                        {
                            errors.Add("Parameter '" + param.Name + "': item count must be between " + valObj.LowerLimit + " and " + valObj.UpperLimit + ".");
                        }
                    }

                    break;
            }
        }

        return errors;
    }

    private static JToken ConvertPSObjectToJToken(PSObject psObj)
    {
        if (psObj == null) return JValue.CreateNull();

        object baseObj = psObj.BaseObject;

        // Primitives and strings - serialize directly
        if (baseObj is string || baseObj is bool ||
            baseObj is int || baseObj is long ||
            baseObj is double || baseObj is float ||
            baseObj is decimal || baseObj is DateTime ||
            baseObj is byte || baseObj is short)
        {
            return JToken.FromObject(baseObj);
        }

        // Hashtable
        if (baseObj is System.Collections.Hashtable ht)
        {
            var jobj = new JObject();
            foreach (object key in ht.Keys)
            {
                jobj[key.ToString()] = JToken.FromObject(ht[key] ?? "");
            }

            return jobj;
        }

        // Arrays and collections
        if (baseObj is System.Collections.IEnumerable enumerable && baseObj is not string)
        {
            var jarr = new JArray();
            foreach (var item in enumerable)
            {
                if (item is PSObject psItem)
                {
                    jarr.Add(ConvertPSObjectToJToken(psItem));
                }
                else
                {
                    jarr.Add(JToken.FromObject(item ?? ""));
                }
            }

            return jarr;
        }

        // Complex objects with properties
        if (psObj.Properties != null && psObj.Properties.Any())
        {
            var jobj = new JObject();
            foreach (PSPropertyInfo prop in psObj.Properties)
            {
                try
                {
                    object propVal = prop.Value;
                    if (propVal == null)
                    {
                        jobj[prop.Name] = JValue.CreateNull();
                    }
                    else if (propVal is PSObject psPropVal)
                    {
                        jobj[prop.Name] = ConvertPSObjectToJToken(psPropVal);
                    }
                    else
                    {
                        jobj[prop.Name] = JToken.FromObject(propVal);
                    }
                }
                catch
                {
                    jobj[prop.Name] = JValue.CreateNull();
                }
            }

            return jobj;
        }

        // Fallback
        return new JValue(psObj.ToString());
    }

    private static async Task WriteResponse(HttpContext context, int statusCode, string statusMessage,
                                            JToken output = null,
                                            List<string> validationErrors = null,
                                            JArray messages = null)
    {
        context.Response.StatusCode = statusCode;

        var responseObj = new JObject();
        responseObj["status"] = statusCode;
        responseObj["statusmessage"] = statusMessage;

        if (output != null)
        {
            responseObj["output"] = output;
        }
        else
        {
            responseObj["output"] = JValue.CreateNull();
        }

        if (messages != null)
        {
            responseObj["messages"] = messages;
        }
        else if (validationErrors != null)
        {
            var msgArr = new JArray();
            foreach (string err in validationErrors)
            {
                var msgObj = new JObject();
                msgObj["stream"] = "error";
                msgObj["message"] = err;
                msgArr.Add(msgObj);
            }

            responseObj["messages"] = msgArr;
        }
        else
        {
            responseObj["messages"] = new JArray();
        }

        await context.Response.WriteAsync(responseObj.ToString(Formatting.None));
    }
}
