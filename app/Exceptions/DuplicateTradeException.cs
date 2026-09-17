namespace TradeApi.Exceptions;

public class DuplicateTradeException : Exception
{
    public DuplicateTradeException(string message) : base(message) { }
}