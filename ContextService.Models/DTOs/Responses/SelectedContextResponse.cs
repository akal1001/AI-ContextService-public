
namespace ContextService.Models.DTOs.Responses;

public class SelectedContextResponse
{
    public long Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public decimal Relevance { get; set; }
    public bool IsSummarized { get; set; }
}
