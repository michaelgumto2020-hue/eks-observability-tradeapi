namespace TradeApi.Models;

public class TradeEvent
{
    public int AccountId { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string TradeType { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreatedUtc { get; set; }
}