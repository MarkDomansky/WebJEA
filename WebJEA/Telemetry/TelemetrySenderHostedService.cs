using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace WebJEA.Telemetry;

public class TelemetrySenderHostedService : BackgroundService
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private readonly TelemetryChannel _channel;

    public TelemetrySenderHostedService(TelemetryChannel channel)
    {
        _channel = channel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (Dictionary<string, object> metrics in _channel.ReadAllAsync(stoppingToken))
            {
                try
                {
                    HostInfo.AddSystemMetrics(metrics);
                    AddTimeMetrics(metrics);
                    await SubmitToAwsQueueAsync(metrics);
                }
                catch
                {
                    // telemetry must never take the app down
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void AddTimeMetrics(Dictionary<string, object> metrics)
    {
        DateTime wints = DateTime.UtcNow;
        int ts = (int)(wints - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

        metrics["wints"] = wints.ToString("yyyy-MM-dd hh:mm:ss");
        metrics["unixts"] = ts;
        metrics["msgversion"] = "3"; // version of string format
    }

    private async Task SubmitToAwsQueueAsync(Dictionary<string, object> metrics)
    {
        string msg = JsonSerializer.Serialize(metrics);

        if (AwsSecrets.Enabled == "True")
        {
            try
            {
                dlog.Info("Sending Telemetry: " + msg);
                AWSCredentials cred = new BasicAWSCredentials(AwsSecrets.Key, AwsSecrets.KeySec);

                var conf = new AmazonSQSConfig();
                conf.Timeout = new TimeSpan(0, 0, 5);
                conf.ServiceURL = new Uri(AwsSecrets.QueueUrl).GetLeftPart(UriPartial.Authority);

                var client = new AmazonSQSClient(cred, conf);

                var req = new SendMessageRequest();
                req.QueueUrl = AwsSecrets.QueueUrl;
                req.MessageBody = msg;

                await client.SendMessageAsync(req);
            }
            catch
            {
                // if it errors, do nothing
            }
        }
    }
}
