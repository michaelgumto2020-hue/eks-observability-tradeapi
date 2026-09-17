using Microsoft.AspNetCore.Mvc;
using TradeApi.Models;
using TradeApi.DTOs;
using TradeApi.Interfaces;
using TradeApi.Services;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;


namespace TradeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradesController : ControllerBase
{
    private readonly ITradeService _tradeService;

    public TradesController(ITradeService tradeService)
    {
        _tradeService = tradeService;
    }


    [HttpPost]
    public async Task<IActionResult> CreateTrade(CreateTradeDto dto)
    {
        var trade = new Trade
        {
            AccountId = dto.AccountId,
            Symbol = dto.Symbol,
            TradeType = dto.TradeType,
            Quantity = dto.Quantity,
            Price = dto.Price,
            CreatedUtc = DateTime.UtcNow
        };

        var createdTrade = await _tradeService.CreateTrade(trade);
        return Ok(createdTrade);
    }


    [HttpGet]
    public async Task<IActionResult> GetTrades([FromQuery] TradeQueryParameters queryParameters)
    {
        var result = await _tradeService.GetTrades(queryParameters);
        return Ok(result);
    }

    [HttpPost("bulk-upload")]
    public async Task<IActionResult> BulkUpload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("A non-empty CSV file is required.");
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only .csv files are supported.");
        }

        List<TradeCsvRow> rows;

        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
            rows = csv.GetRecords<TradeCsvRow>().ToList();
        }
        catch (Exception ex)
        {
            return BadRequest($"Could not parse CSV file: {ex.Message}");
        }

        var result = await _tradeService.BulkCreateTrades(rows);
        return Ok(result);
    }

    [HttpGet("bulk-upload/template")]
    public IActionResult DownloadBulkUploadTemplate()
    {
        var csvContent = "AccountId,Symbol,TradeType,Quantity,Price\n" +
                        "1,AAPL,BUY,100,150.25\n" +
                        "2,MSFT,SELL,50,310.00\n";

        var fileBytes = System.Text.Encoding.UTF8.GetBytes(csvContent);

        return File(fileBytes, "text/csv", "trade-upload-template.csv");
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportTrades([FromQuery] TradeQueryParameters queryParameters)
    {
        var fileBytes = await _tradeService.ExportTradesToExcel(queryParameters);

        var fileName = $"trades-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}