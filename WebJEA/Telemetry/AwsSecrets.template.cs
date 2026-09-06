// Copy this file to AwsSecrets.cs and fill in your values.
// AwsSecrets.cs is excluded from source control via .gitignore.
// During CI builds, this template is processed automatically with values from GitHub secrets.
// When AwsSecrets.cs is absent, the build compiles this template instead (placeholder
// values make every send attempt fail silently, which telemetry is designed to tolerate).
namespace WebJEA.Telemetry;

internal static class AwsSecrets
{
    public const string Enabled = "True";
    public const string Key = "{{AWS_KEY}}";
    public const string KeySec = "{{AWS_KEYSEC}}";
    public const string QueueUrl = "{{AWS_QUEUE_URL}}";
}
