using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradeApi.Configuration;
using TradeApi.Data;
using TradeApi.Interfaces;
using TradeApi.Models;
using TradeApi.DTOs;
using TradeApi.Exceptions;
using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;

namespace TradeApi.Services;

public class TradeService : ITradeService
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);
    private readonly AppDbContext _context;
    private readonly IAmazonSQS _sqsClient;
    private readonly string _queueUrl;
    private readonly ILogger<TradeService> _logger;

    public TradeService(
        AppDbContext context,
        IAmazonSQS sqsClient,
        IOptions<SqsOptions> sqsOptions,
        ILogger<TradeService> logger)
    {
        _context = context;
        _sqsClient = sqsClient;
        _queueUrl = sqsOptions.Value.QueueUrl;
        _logger = logger;
    }

    public async Task<Trade> CreateTrade(Trade trade)
    {
        trade.Symbol = trade.Symbol.ToUpper();

        var cutoff = DateTime.UtcNow - DuplicateWindow;

        var isDuplicate = await _context.Trades.AnyAsync(t =>
            t.AccountId == trade.AccountId &&
            t.Symbol == trade.Symbol &&
            t.TradeType == trade.TradeType &&
            t.Quantity == trade.Quantity &&
            t.Price == trade.Price &&
            t.CreatedUtc >= cutoff);

        if (isDuplicate)
        {
            throw new DuplicateTradeException(
                $"A matching trade for account {trade.AccountId} was submitted within the last {DuplicateWindow.TotalSeconds} seconds.");
        }

        trade.CreatedUtc = DateTime.UtcNow;

        _context.Trades.Add(trade);
        await _context.SaveChangesAsync();

        try
        {
            await QueueTrade(trade);
        }
        catch (Exception ex) // widen temporarily — ServiceBusException won't catch a config/auth problem
        {
            _logger.LogError(ex, "Failed to queue trade {TradeId}", trade.Id);
        }

        return trade;
    }

    public async Task<PagedResult<Trade>> GetTrades(TradeQueryParameters queryParameters)
    {
        var query = _context.Trades.AsQueryable();

        if (queryParameters.AccountId.HasValue)
        {
            query = query.Where(t => t.AccountId == queryParameters.AccountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(queryParameters.Symbol))
        {
            query = query.Where(t => t.Symbol == queryParameters.Symbol);
        }

        if (!string.IsNullOrWhiteSpace(queryParameters.TradeType))
        {
            query = query.Where(t => t.TradeType == queryParameters.TradeType);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(t => t.CreatedUtc)
            .Skip((queryParameters.Page - 1) * queryParameters.PageSize)
            .Take(queryParameters.PageSize)
            .ToListAsync();

        return new PagedResult<Trade>
        {
            Items = items,
            TotalCount = totalCount,
            Page = queryParameters.Page,
            PageSize = queryParameters.PageSize
        };
    }

    public async Task QueueTrade(Trade trade)
    {
        var tradeEvent = new TradeEvent
        {
            AccountId = trade.AccountId,
            Symbol = trade.Symbol,
            TradeType = trade.TradeType,
            Quantity = trade.Quantity,
            Price = trade.Price,
            CreatedUtc = trade.CreatedUtc
        };

        var request = new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(tradeEvent),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["Subject"] = new MessageAttributeValue
                {
                    DataType = "String",
                    StringValue = "TradeCreated"
                }
            }
        };

        await _sqsClient.SendMessageAsync(request);

        _logger.LogInformation(
            "Trade {TradeId} successfully queued to SQS queue {QueueUrl}",
            trade.Id,
            _queueUrl);
    }

    public async Task<BulkUploadResultDto> BulkCreateTrades(List<TradeCsvRow> rows)
    {
        var result = new BulkUploadResultDto { TotalRows = rows.Count };
        var tradesToInsert = new List<Trade>();

        for (int i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 2; // +1 for zero-index, +1 for header row
            var row = rows[i];

            var validationErrors = ValidateRow(row);
            if (validationErrors.Count > 0)
            {
                result.Errors.Add(new BulkUploadRowError
                {
                    RowNumber = rowNumber,
                    Reason = string.Join("; ", validationErrors)
                });
                continue;
            }

            tradesToInsert.Add(new Trade
            {
                AccountId = row.AccountId,
                Symbol = row.Symbol.ToUpper(),
                TradeType = row.TradeType,
                Quantity = row.Quantity,
                Price = row.Price,
                CreatedUtc = DateTime.UtcNow
            });
        }

        if (tradesToInsert.Count > 0)
        {
            _context.Trades.AddRange(tradesToInsert);
            await _context.SaveChangesAsync();

            foreach (var trade in tradesToInsert)
            {
                try
                {
                    await QueueTrade(trade);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to queue bulk-uploaded trade {TradeId}", trade.Id);
                }
            }
        }

        result.SuccessCount = tradesToInsert.Count;
        result.FailureCount = result.Errors.Count;

        return result;
    }

    private static List<string> ValidateRow(TradeCsvRow row)
    {
        var errors = new List<string>();

        if (row.AccountId <= 0)
            errors.Add("AccountId must be a positive integer.");

        if (string.IsNullOrWhiteSpace(row.Symbol) || row.Symbol.Length > 10)
            errors.Add("Symbol is required and must be 10 characters or fewer.");

        if (string.IsNullOrWhiteSpace(row.TradeType))
            errors.Add("TradeType is required.");

        if (row.Quantity < 1)
            errors.Add("Quantity must be at least 1.");

        if (row.Price < 0.01m)
            errors.Add("Price must be at least 0.01.");

        return errors;
    }

    public async Task<byte[]> ExportTradesToExcel(TradeQueryParameters queryParameters)
    {
        var query = _context.Trades.AsQueryable();

        if (queryParameters.AccountId.HasValue)
            query = query.Where(t => t.AccountId == queryParameters.AccountId.Value);

        if (!string.IsNullOrWhiteSpace(queryParameters.Symbol))
        {
            var symbolUpper = queryParameters.Symbol.ToUpper();
            query = query.Where(t => t.Symbol.ToUpper() == symbolUpper);
        }

        if (!string.IsNullOrWhiteSpace(queryParameters.TradeType))
        {
            var tradeType = queryParameters.TradeType.ToUpper();
            query = query.Where(t => t.TradeType.ToUpper() == tradeType);
        }

        var trades = await query
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Trades");

        worksheet.Cell(1, 1).Value = "Id";
        worksheet.Cell(1, 2).Value = "AccountId";
        worksheet.Cell(1, 3).Value = "Symbol";
        worksheet.Cell(1, 4).Value = "TradeType";
        worksheet.Cell(1, 5).Value = "Quantity";
        worksheet.Cell(1, 6).Value = "Price";
        worksheet.Cell(1, 7).Value = "CreatedUtc";
        worksheet.Row(1).Style.Font.Bold = true;

        for (int i = 0; i < trades.Count; i++)
        {
            var row = i + 2;
            var trade = trades[i];

            worksheet.Cell(row, 1).Value = trade.Id;
            worksheet.Cell(row, 2).Value = trade.AccountId;
            worksheet.Cell(row, 3).Value = trade.Symbol;
            worksheet.Cell(row, 4).Value = trade.TradeType;
            worksheet.Cell(row, 5).Value = trade.Quantity;
            worksheet.Cell(row, 6).Value = trade.Price;
            worksheet.Cell(row, 7).Value = trade.CreatedUtc;
            worksheet.Cell(row, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
        }
    
}  