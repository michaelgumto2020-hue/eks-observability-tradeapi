using System.ComponentModel.DataAnnotations;

namespace TradeApi.DTOs;

public class CreateTradeDto
{
    [Required]
    public int AccountId { get; set; }

    [Required]
    [MaxLength(10)]
    public string Symbol { get; set; } = string.Empty;

    [Required]
    public string TradeType { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }
}