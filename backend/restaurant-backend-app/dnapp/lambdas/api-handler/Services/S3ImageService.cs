using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using APIHandler.Constants;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace APIHandler.Services;

public class S3ImageService
{
    private readonly IAmazonS3 _s3Client;

    public S3ImageService(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<string> UploadImageAsync(byte[] imageBytes, string bucketName, string fileName, ILambdaLogger logger,
        string? folder = null)
    {
        using var stream = new MemoryStream(imageBytes);
        string contentType = GetImageContentType(imageBytes);
        var key = string.IsNullOrWhiteSpace(folder)
            ? $"{fileName}"
            : $"{folder}/{fileName}";

        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request);

        return request.Key;
    }

    public virtual string GetPublicUrl(string bucketName, string imageKey)
    {
        return $"https://{bucketName}.s3.{ResourceNames.Region}.amazonaws.com/{imageKey}";
    }

    public string GetImageUrl(string bucketName, string imageKey)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = imageKey,
            Expires = DateTime.UtcNow.AddMinutes(10)
        };
        return _s3Client.GetPreSignedURL(request);
    }

    public string GetPreSignedImageUrl(string bucketName, string imageKey)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = imageKey,
            Expires = DateTime.UtcNow.AddHours(1),
        };
        return _s3Client.GetPreSignedURL(request);
    }

    public static byte[] GetImageBytesFromBase64(string base64String)
    {
        int commaIndex = base64String.IndexOf(',');
        if (commaIndex != -1)
        {
            base64String = base64String.Substring(commaIndex + 1);
        }

        try { return Convert.FromBase64String(base64String); }
        catch (FormatException) { throw new APIHandler.Exceptions.ValidationException("Invalid base64 image."); }
    }

    public async Task<string> GetImageKey(string imageBase64, string bucketName, string folderName, string fileName, ILambdaLogger logger)
    {
        var imageBytes = GetImageBytesFromBase64(imageBase64);

        var imageKey =
            await UploadImageAsync(imageBytes, bucketName, fileName, logger, folderName);
        return imageKey;
    }

    public async Task<byte[]> DownloadImageAsync(string bucketName, string key)
    {
        var response = await _s3Client.GetObjectAsync(bucketName, key);
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task DeleteImageAsync(string bucketName, string key)
    {
        await _s3Client.DeleteObjectAsync(bucketName, key);
    }

    public static bool CompareImagesHash(byte[] a, byte[] b)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(a).SequenceEqual(sha.ComputeHash(b));
    }

    public static string GetImageContentType(byte[] imgData)
    {
        if (imgData.Length > 2 * 1024 * 1024)
            throw new APIHandler.Exceptions.ValidationException("Image must not exceed 2 MB.");
        // No resizing or decoding is needed: recognize a bounded set of raster
        // containers without allocating a decoded image or accepting SVG/HTML.
        var bytes = imgData.AsSpan();
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) &&
            bytes.Slice(12,4).SequenceEqual("IHDR"u8)) return "image/png";
        if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[^2] == 0xff && bytes[^1] == 0xd9) return "image/jpeg";
        if (bytes.Length >= 13 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8,4).SequenceEqual("WEBP"u8)) return "image/webp";
        throw new APIHandler.Exceptions.ValidationException("Unsupported image format. Use PNG, JPEG, GIF or WebP.");
    }
}
