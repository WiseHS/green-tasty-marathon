using System.Net;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.DynamoDBv2.DataModel;
using APIHandler.Entities;
using APIHandler.Models.Reports;

namespace Restaurant.Web.Persistence;

// Reports are persisted and aggregated synchronously; no AWS queue or worker is required.
public sealed class PostgresReports(PostgresStore store) : AmazonSQSClient(new AnonymousAWSCredentials(),RegionEndpoint.EUCentral1)
{
    public sealed class CompletedReport
    {
        [DynamoDBHashKey] public required string Id { get; set; }
        public required Report Report { get; set; }
    }
    public override Task<GetQueueUrlResponse> GetQueueUrlAsync(GetQueueUrlRequest request,CancellationToken cancellationToken=default) =>
        Task.FromResult(new GetQueueUrlResponse { QueueUrl="postgres://completed-reports",HttpStatusCode=HttpStatusCode.OK });
    public override Task<GetQueueUrlResponse> GetQueueUrlAsync(string queueName,CancellationToken cancellationToken=default) => GetQueueUrlAsync(new GetQueueUrlRequest(),cancellationToken);
    public override async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request,CancellationToken cancellationToken=default)
    {
        var report=JsonSerializer.Deserialize<Report>(request.MessageBody) ?? throw new InvalidOperationException("Invalid report");
        await store.Save(new CompletedReport { Id=report.ReservationId,Report=report });
        var date=report.ReservationStart.Date;
        var reports=(await store.Find<CompletedReport>()).Select(r=>r.Report).Where(r=>r.WaiterId==report.WaiterId&&r.ReservationStart.Date==date).ToList();
        await store.Save(new DailyAggregate
        {
            WaiterId=report.WaiterId,LocationId=report.LocationId,Date=date,
            AverageServiceRating=reports.Average(r=>r.ServiceRating),MinimumServiceRating=reports.Min(r=>r.ServiceRating),
            AverageCuisineRating=reports.Average(r=>r.CuisineRating),MinimumCuisineRating=reports.Min(r=>r.CuisineRating),
            TotalRevenue=reports.Sum(r=>r.Revenue),HoursWorked=reports.Sum(r=>(r.ReservationEnd-r.ReservationStart).TotalHours),OrdersProcessed=reports.Count
        });
        return new SendMessageResponse { MessageId=report.ReservationId,HttpStatusCode=HttpStatusCode.OK };
    }
}
