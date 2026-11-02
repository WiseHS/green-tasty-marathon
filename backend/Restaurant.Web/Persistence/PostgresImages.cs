using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using APIHandler.Services;
using Npgsql;

namespace Restaurant.Web.Persistence;

public sealed class PostgresImages(NpgsqlDataSource source,string origin,string secret)
    : AmazonS3Client(new AnonymousAWSCredentials(),RegionEndpoint.EUCentral1), IAmazonS3
{
    public string PublicUrl(string key) => $"{origin.TrimEnd('/')}/media/{string.Join('/',key.Split('/').Select(Uri.EscapeDataString))}";
    private string Signature(string key,long expiry) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),Encoding.UTF8.GetBytes($"{key}:{expiry}")));
    public bool ValidSignature(string key,long expiry,string signature) => expiry>DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Signature(key,expiry)),Encoding.ASCII.GetBytes(signature));
    string IAmazonS3.GetPreSignedURL(GetPreSignedUrlRequest request)
    {
        var expiry=new DateTimeOffset((request.Expires ?? DateTime.UtcNow.AddHours(1)).ToUniversalTime()).ToUnixTimeSeconds();
        return $"{PublicUrl(request.Key)}?expires={expiry}&signature={Signature(request.Key,expiry)}";
    }
    public override async Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request,CancellationToken cancellationToken=default)
    {
        using var data=new MemoryStream();await request.InputStream.CopyToAsync(data,cancellationToken);
        if(data.Length>2*1024*1024)throw new InvalidOperationException("Image exceeds 2 MB");
        await using var command=source.CreateCommand("INSERT INTO app_images(key,content_type,data,is_public) VALUES($1,$2,$3,$4) ON CONFLICT(key) DO UPDATE SET content_type=EXCLUDED.content_type,data=EXCLUDED.data");
        command.Parameters.AddWithValue(request.Key);command.Parameters.AddWithValue(request.ContentType);command.Parameters.AddWithValue(data.ToArray());command.Parameters.AddWithValue(request.Key.StartsWith("assets/",StringComparison.Ordinal));
        await command.ExecuteNonQueryAsync(cancellationToken);return new() { HttpStatusCode=HttpStatusCode.OK };
    }
    public async Task<(byte[] Data,string ContentType,bool Public)?> Read(string key,CancellationToken token=default)
    {
        await using var command=source.CreateCommand("SELECT data,content_type,is_public FROM app_images WHERE key=$1");command.Parameters.AddWithValue(key);
        await using var reader=await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? (reader.GetFieldValue<byte[]>(0),reader.GetString(1),reader.GetBoolean(2)) : null;
    }
    public override async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request,CancellationToken cancellationToken=default)
    {
        var image=await Read(request.Key,cancellationToken) ?? throw new AmazonS3Exception("Image not found") { StatusCode=HttpStatusCode.NotFound };
        return new() { ResponseStream=new MemoryStream(image.Data),HttpStatusCode=HttpStatusCode.OK };
    }
    public override Task<GetObjectResponse> GetObjectAsync(string bucketName,string key,CancellationToken cancellationToken=default) => GetObjectAsync(new GetObjectRequest { BucketName=bucketName,Key=key },cancellationToken);
    public override async Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request,CancellationToken cancellationToken=default)
    {
        await using var command=source.CreateCommand("DELETE FROM app_images WHERE key=$1");command.Parameters.AddWithValue(request.Key);await command.ExecuteNonQueryAsync(cancellationToken);
        return new() { HttpStatusCode=HttpStatusCode.NoContent };
    }
    public override Task<DeleteObjectResponse> DeleteObjectAsync(string bucketName,string key,CancellationToken cancellationToken=default) => DeleteObjectAsync(new DeleteObjectRequest { BucketName=bucketName,Key=key },cancellationToken);
}

public sealed class PortableImageService(PostgresImages images) : S3ImageService(images)
{
    public override string GetPublicUrl(string bucketName,string imageKey) => images.PublicUrl(imageKey);
}
