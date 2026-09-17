namespace TradeApi.Models;

public class Trade
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public string Symbol { get; set; } = "";

    public string TradeType { get; set; } = "";

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public DateTime CreatedUtc { get; set; }
}