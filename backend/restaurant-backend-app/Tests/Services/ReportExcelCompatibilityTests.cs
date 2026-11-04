using APIHandler.Entities.Reports;
using APIHandler.Services.Reports;
using NPOI.HSSF.UserModel;

namespace APIHandler.Tests.Services;

public class ReportExcelCompatibilityTests
{
    [Fact]
    public void GenerateExcelProducesReadableXlsAfterDependencyUpdate()
    {
        var report = new ReportHistoryLocation
        {
            Id = "test", UserId = "admin", LocationId = "demo", LocationName = "Kyiv",
            StartDate = new DateOnly(2026, 11, 1), EndDate = new DateOnly(2026, 11, 4),
            TotalOrders = 3, Revenue = 120m
        };
        var result = new ReportFileService().GenerateExcel([report]);
        Assert.Equal("application/vnd.ms-excel", result.ContentType);
        Assert.EndsWith(".xls", result.FileName);
        using var stream = new MemoryStream(result.FileContent);
        using var workbook = new HSSFWorkbook(stream);
        var sheet = workbook.GetSheetAt(0);
        Assert.Equal("Report", sheet.SheetName);
        Assert.Equal("Location Name", sheet.GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal("Kyiv", sheet.GetRow(1).GetCell(0).StringCellValue);
    }
}
