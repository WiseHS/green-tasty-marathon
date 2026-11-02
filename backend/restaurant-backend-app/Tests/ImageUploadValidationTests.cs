using APIHandler.Exceptions;
using APIHandler.Services;
using System.Text;

namespace APIHandler.Tests;

public class ImageUploadValidationTests
{
    [Fact]
    public void RecognizesPngFromContent()
    {
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jXioAAAAASUVORK5CYII=");
        Assert.Equal("image/png",S3ImageService.GetImageContentType(bytes));
    }
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("")]
    public void RejectsActiveContentAndEmptyUploads(string text) =>
        Assert.Throws<ValidationException>(()=>S3ImageService.GetImageContentType(Encoding.UTF8.GetBytes(text)));
    [Fact]
    public void RejectsOversizeUploadBeforeDecoding() =>
        Assert.Throws<ValidationException>(()=>S3ImageService.GetImageContentType(new byte[2*1024*1024+1]));
}
