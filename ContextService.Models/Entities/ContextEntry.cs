namespace ContextService.Models.Entities;

public class ContextEntry
{
    public long Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContextType { get; set; } = string.Empty;
    public decimal Importance { get; set; } = 0.5m;
    public int TokenCount { get; set; }
    public string? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;

    public string? MemoryScope { get; set; }

    public long? SupersededById { get; set; }   
}