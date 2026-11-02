using Amazon.Lambda.Core;

namespace Restaurant.Web;

public sealed class LambdaContext(ILogger logger, string requestId) : ILambdaContext
{
    public string AwsRequestId => requestId;
    public IClientContext ClientContext => null!;
    public ICognitoIdentity Identity => null!;
    public string FunctionName => "Restaurant.Web";
    public string FunctionVersion => "1";
    public string InvokedFunctionArn => "";
    public string LogGroupName => "Restaurant.Web";
    public string LogStreamName => "http";
    public int MemoryLimitInMB => 512;
    public TimeSpan RemainingTime => TimeSpan.FromSeconds(60);
    public ILambdaLogger Logger { get; } = new LambdaLogger(logger);
    private sealed class LambdaLogger(ILogger logger) : ILambdaLogger
    {
        public void Log(string message) => logger.LogInformation("{Message}", message);
        public void LogLine(string message) => logger.LogInformation("{Message}", message);
    }
}
