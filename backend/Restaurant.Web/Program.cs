using Amazon.CognitoIdentityProvider;
using Amazon.Lambda.APIGatewayEvents;
using APIHandler.Routing;
using Npgsql;
using Restaurant.Web;
using Restaurant.Web.Auth;
using Restaurant.Web.Persistence;
using Restaurant.Web.GroupOrders;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore",Microsoft.Extensions.Logging.LogLevel.Warning);
builder.Logging.AddFilter("Restaurant.Web",Microsoft.Extensions.Logging.LogLevel.Warning);
// Local secrets are loaded on the server only. Hosting platforms inject env vars.
var root = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../.."));
var localEnv = Path.Combine(root, ".env.local");
if (builder.Environment.IsDevelopment() && File.Exists(localEnv))
    foreach (var line in File.ReadLines(localEnv))
    {
        var parts = line.Split('=', 2);
        if (parts.Length == 2 && !parts[0].StartsWith('#') && Environment.GetEnvironmentVariable(parts[0]) is null)
            Environment.SetEnvironmentVariable(parts[0], parts[1].Trim().Trim('"', '\''));
    }
var databaseUrl = Environment.GetEnvironmentVariable(args.Contains("--migrate") ? "DATABASE_URL_UNPOOLED" : "DATABASE_URL") ?? throw new InvalidOperationException("DATABASE_URL is required");
var key = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? throw new InvalidOperationException("JWT_SIGNING_KEY is required");
var source = NpgsqlDataSource.Create(Database.ConnectionString(databaseUrl));
builder.Services.AddSingleton(source);
builder.Services.AddSingleton<PostgresStore>();
builder.Services.AddSingleton(new PostgresIdentity(source,key));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 4 * 1024 * 1024);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins((Environment.GetEnvironmentVariable("FRONTEND_ORIGIN") ?? "http://localhost:4200").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode=429;
    options.AddPolicy("auth",context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Request.Path=="/signin"||context.Request.Path=="/register" ? "auth:" : "api:") + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"), _ => new FixedWindowRateLimiterOptions
        { PermitLimit=context.Request.Path is var path && (path=="/signin"||path=="/register") ? 20 : 300, Window=TimeSpan.FromMinutes(1), QueueLimit=0 }));
});
// Legacy handlers still read names for their DTOs; these names do not designate
// live AWS resources. The portable repositories replace every domain repository.
var legacyNames = new[] { "FEEDBACK_TABLE_NAME","WaiterListTableName","locationsTable","dishesTable","profileImagesTable",
    "reportsTable","reportsHistoryTable","COGNITO_USER_POOL_ID","COGNITO_USER_POOL_CLIENT_ID","reservationsTable",
    "groupReservationsTable","tablesTable","ordersTable","notificationsTable","notificationsUserIdIndex",
    "reservationsTableGlobalIndex","waitersGlobalIndex","reservationsCustomerIdGlobalIndex","reservationsWaiterIdGlobalIndex",
    "imagesBucketName","feedbackGlobalIndex","profileImagesBucketName","OrderIndexReservationId","reports_queue_name",
    "guestSessionTable","tableLocationIdIndex" };
foreach(var name in legacyNames) Environment.SetEnvironmentVariable(name,"portable-"+name);
Environment.SetEnvironmentVariable("REGION","eu-central-1");
var app=builder.Build();
var store=app.Services.GetRequiredService<PostgresStore>();
var identity=app.Services.GetRequiredService<PostgresIdentity>();
var images=new PostgresImages(source,Environment.GetEnvironmentVariable("PUBLIC_BACKEND_URL") ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL") ?? "http://127.0.0.1:5080",key);
if(args.Contains("--migrate")) { await Database.Migrate(source); Console.WriteLine("PostgreSQL migrations completed.");return; }
if(args.Contains("--check-db")) { Console.WriteLine(await store.CheckConnection(CancellationToken.None) ? "PostgreSQL SELECT 1: PASS" : "PostgreSQL SELECT 1: FAIL");return; }
if(args.Contains("--seed-demo")) { await DemoData.Seed(store,identity);Console.WriteLine("Demo restaurant, menu, tables and waiter: ready.");return; }
var repositories=new PostgresRepositories(store);
var routes=RouteRegistry.BuildRoutes(services =>
{
    services.AddSingleton<AmazonCognitoIdentityProviderClient>(identity);
    services.AddSingleton<IAmazonCognitoIdentityProvider>(identity);
    services.AddSingleton<APIHandler.Repositories.Auth.IUserRepository,APIHandler.Repositories.Auth.UserRepository>();
    services.AddSingleton<APIHandler.Repositories.IApplicationData>(new PostgresApplicationData(store));
    services.AddSingleton<Amazon.S3.IAmazonS3>(images);
    services.AddSingleton<APIHandler.Services.S3ImageService>(new PortableImageService(images));
    services.AddSingleton<Amazon.SQS.AmazonSQSClient>(new PostgresReports(store));
    foreach(var contract in typeof(PostgresRepositories).GetInterfaces()) services.AddSingleton(contract,repositories);
});
var function=new APIHandler.Function(routes);
app.UseCors();
app.UseRateLimiter();
app.MapGet("/health",()=>Results.Ok(new { status="ok" }));
app.MapGet("/media/{**imageKey}",async(string imageKey,HttpContext context,CancellationToken token)=>
{
    var image=await images.Read(imageKey,token);
    if(image is null)return Results.NotFound();
    if(!image.Value.Public && (!long.TryParse(context.Request.Query["expires"],out var expiry) || !images.ValidSignature(imageKey,expiry,context.Request.Query["signature"].ToString())))return Results.StatusCode(403);
    context.Response.Headers.XContentTypeOptions="nosniff";
    return Results.File(image.Value.Data,image.Value.ContentType);
});
app.MapGet("/health/ready",async(CancellationToken token)=>
{
    try { return await store.CheckConnection(token) ? Results.Ok(new { status="ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapMethods("/{**path}",["GET","POST","PUT","PATCH","DELETE"],async(HttpContext context)=>
{
    var path=context.Request.Path.Value ?? "/";
    Dictionary<string,string>? claims=null;
    var bearer=context.Request.Headers.Authorization.ToString();
    var isGuest=path.StartsWith("/guest/",StringComparison.Ordinal);
    if(bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))
        claims=await identity.Validate(bearer[7..]);
    var publicRead=context.Request.Method=="GET" && (path=="/locations" || Regex.IsMatch(path,@"^/locations/[^/]+(?:/dishes)?$") ||
        path=="/dishes" || path.StartsWith("/dishes/") || path=="/available-tables" || Regex.IsMatch(path,@"^/tables/[^/]+/available-time-slots$") || path.StartsWith("/feedback"));
    var publicWrite=context.Request.Method=="POST" && (path=="/register"||path=="/signin");
    if(!isGuest&&!publicRead&&!publicWrite&&claims is null) { context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new { message="Authentication required" });return; }
    var administrativeWrite=context.Request.Method!="GET" && Regex.IsMatch(path,@"^/(locations|tables|dishes|waiters)(/|$)");
    if(administrativeWrite && !(claims?.GetValueOrDefault("cognito:groups")?.Split(',').Contains("Admin") ?? false))
    { context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new { error="Administrator role required" });return; }
    var staff=claims?.GetValueOrDefault("cognito:groups")?.Split(',').Any(role=>role is "Admin" or "Waiter") ?? false;
    var userMatch=Regex.Match(path,@"^/users/([^/]+)/(?:profile-image|name)$");
    var staffAction=path=="/users" || Regex.IsMatch(path,@"^/reservations/[^/]+/(?:status|table|order/(?:confirmations|cancellations)/[^/]+)$");
    if((staffAction&&!staff)||(userMatch.Success&&!staff&&userMatch.Groups[1].Value!=claims?.GetValueOrDefault("sub")))
    { context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new { error="Access denied" });return; }
    var reservationMatch=Regex.Match(path,@"^/(?:reservations|orders)/([^/]+)(?:/|$)");
    if(reservationMatch.Success && claims is not null)
    {
        var reservation=await store.Get<APIHandler.Entities.Reservation>(reservationMatch.Groups[1].Value);
        if(reservation is not null && reservation.CustomerId!=claims["sub"] &&
            !(reservation.WaiterId==claims["sub"]&&claims["cognito:groups"].Split(',').Contains("Waiter")))
        { context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new { error="Access denied" });return; }
    }
    if(path.StartsWith("/reports") && !(claims?["cognito:groups"].Split(',').Any(role=>role is "Waiter" or "Admin") ?? false))
    { context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new { error="Staff role required" });return; }
    var request=new APIGatewayProxyRequest
    {
        HttpMethod=context.Request.Method,Path=path,
        Body=await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted),
        Headers=context.Request.Headers.ToDictionary(h=>h.Key,h=>h.Value.ToString(),StringComparer.OrdinalIgnoreCase),
        QueryStringParameters=context.Request.Query.ToDictionary(q=>q.Key,q=>q.Value.ToString()),
        RequestContext=new APIGatewayProxyRequest.ProxyRequestContext
        { Authorizer=new APIGatewayCustomAuthorizerContext { Claims=claims ?? new() } }
    };
    try
    {
        var isGroupOrderRoute=Regex.IsMatch(path,@"^/reservations/[^/]+/group-order(?:/submit)?$");
        await using var operation=context.Request.Method=="GET" && !isGroupOrderRoute ? null : store.BeginOperation();
        if(operation is not null)await operation.Begin(context.RequestAborted);
        var groupOrderMatch=Regex.Match(path,@"^/reservations/([^/]+)/group-order(?:/submit)?$");
        APIGatewayProxyResponse response;
        if(groupOrderMatch.Success && (context.Request.Method=="GET" && !path.EndsWith("/submit") || context.Request.Method=="POST" && path.EndsWith("/submit")))
            response=await new GroupOrderService(store).Handle(groupOrderMatch.Groups[1].Value,context.Request.Method,claims!,request.Body);
        else if(context.Request.Method!="GET" && Regex.IsMatch(path,@"^/orders/[^/]+(?:/(?:submit|unsubmit))?$") && reservationMatch.Success &&
            await store.Get<GroupOrderSubmission>(reservationMatch.Groups[1].Value) is not null)
            response=new APIGatewayProxyResponse { StatusCode=409,Body="{\"error\":\"The consolidated group order has been sent and is locked.\"}",Headers=new Dictionary<string,string>{{"Content-Type","application/json"}} };
        else response=await function.FunctionHandler(request,new LambdaContext(app.Logger,context.TraceIdentifier));
        if(operation is not null && response.StatusCode<400)await operation.Commit(context.RequestAborted);
        context.Response.StatusCode=response.StatusCode;
        foreach(var header in response.Headers ?? new Dictionary<string,string>())
            if(!header.Key.StartsWith("Access-Control-",StringComparison.OrdinalIgnoreCase))context.Response.Headers[header.Key]=header.Value;
        if(response.IsBase64Encoded)await context.Response.Body.WriteAsync(Convert.FromBase64String(response.Body),context.RequestAborted);
        else await context.Response.WriteAsync(response.Body ?? "",context.RequestAborted);
    }
    catch(Exception error)
    { app.Logger.LogError("HTTP request failed: {Type}",error.GetType().Name);context.Response.StatusCode=500;await context.Response.WriteAsJsonAsync(new { message="Request could not be completed" }); }
}).RequireRateLimiting("auth");
app.Run();
