namespace WebJEA;

public enum OutputType
{
    Unknown,
    Output,
    Info,
    Warn,
    Err,
    Verbose,
    Debug
}

public struct OutputData
{
    public OutputType OutputType;
    public string Content;
}
