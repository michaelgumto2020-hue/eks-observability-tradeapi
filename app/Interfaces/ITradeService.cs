using TradeApi.Models;
using TradeApi.DTOs;

namespace TradeApi.Interfaces;

public interface ITradeService
{
    Task<Trade> CreateTrade(Trade trade);

    Task<PagedResult<Trade>> GetTrades(TradeQueryParameters queryParameters);

    Task QueueTrade(Trade trade);

    Task<BulkUploadResultDto> BulkCreateTrades(List<TradeCsvRow> rows);

    Task<byte[]> ExportTradesToExcel(TradeQueryParameters queryParameters);
}