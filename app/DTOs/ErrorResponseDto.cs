namespace TradeApi.DTOs;

public class ErrorResponseDto
{
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Detail { get; set; }
    public string TraceId { get; set; } = string.Empty;
}