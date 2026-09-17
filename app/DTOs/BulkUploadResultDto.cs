namespace TradeApi.DTOs;

public class BulkUploadResultDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<BulkUploadRowError> Errors { get; set; } = new();
}

public class BulkUploadRowError
{
    public int RowNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}