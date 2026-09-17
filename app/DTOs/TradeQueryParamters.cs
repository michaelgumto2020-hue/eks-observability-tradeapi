namespace TradeApi.DTOs;

public class TradeQueryParameters
{
    public int? AccountId { get; set; }
    public string? Symbol { get; set; }
    public string? TradeType { get; set; }

    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value > MaxPageSize ? MaxPageSize : value;
    }
}