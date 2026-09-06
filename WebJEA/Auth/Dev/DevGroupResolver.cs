namespace WebJEA.Auth.Dev;

/// <summary>
/// Development-only resolver: returns config group names unchanged so they can be
/// matched literally against the configured DevUser:Sids without needing AD.
/// </summary>
public class DevGroupResolver : IGroupResolver
{
    public string GetSID(string input)
    {
        return input;
    }
}
