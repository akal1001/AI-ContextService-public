
using ContextService.Data.Repositories;
using ContextService.LLM.Providers;
using ContextService.Models.DTOs.Responses;
using ContextService.Models.Entities;
using System.Text.Json;

namespace ContextService.Business.Services;

public class ContextManager
{
    private readonly ContextRepository _repository;
    private readonly OllamaClient _llmClient;

    public ContextManager(ContextRepository repository, OllamaClient llmClient)
    {
        _repository = repository;
        _llmClient = llmClient;
    }


    public async Task<decimal> EvaluateRelevanceAsync(string query, string context, CancellationToken cancellationToken = default)
    {
        return await _llmClient.EvaluateRelevanceAsync(query, context, cancellationToken);
    }

    public async Task<bool> EvaluateMemoryReplacementAsync(long existingEntryId, string agentId,string newContent,CancellationToken cancellationToken = default)
    {
        if (existingEntryId <= 0)
        {
            throw new ArgumentException("Invalid existing entry ID.");
        }

        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new ArgumentException("New content is required.");
        }

        ContextEntry? existingEntry = await _repository.GetByIdAsync(existingEntryId, agentId, cancellationToken);

        if (existingEntry == null || !existingEntry.IsActive)
        {
            throw new InvalidOperationException("Existing memory not found or inactive.");
        }

        return await _llmClient.EvaluateMemoryReplacementAsync(
            existingEntry.Content, newContent, cancellationToken);
    }
    public async Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default)
    {
        return await _llmClient.SummarizeAsync(content, cancellationToken);
    }

    private async Task<List<ContextEntry>> FindRelatedMemoriesAsync(ContextEntry newEntry,CancellationToken cancellationToken)  
    {
        var candidates = new List<ContextEntry>();
        var seenIds = new HashSet<long>();

        var sessionEntries = await _repository.GetBySessionAsync(newEntry.AgentId, newEntry.SessionId, cancellationToken);

        var longTermEntries = await _repository.GetLongTermByAgentAsync(newEntry.AgentId, cancellationToken);

        foreach (var entry in sessionEntries)
        {
            if (seenIds.Add(entry.Id))
            {
                candidates.Add(entry);
            }
        }

        foreach (var entry in longTermEntries)
        {
            if (seenIds.Add(entry.Id))
            {
                candidates.Add(entry);
            }
        }

        return candidates;
    }
    public async Task<IReadOnlyList<SelectedContextResponse>> SelectRelevantMemoriesAsync(string agentId, Guid sessionId, string query, int maxTokens, CancellationToken cancellationToken = default)
    {
        ValidateSelectionRequest(agentId, sessionId, query, maxTokens);

        var entries = await GetUniqueEntriesAsync(agentId, sessionId, cancellationToken);
        entries = await RemoveSemanticDuplicatesAsync(entries, cancellationToken);
        var scoredEntries = await EvaluateEntriesAsync(entries, query, cancellationToken);

        RankEntries(scoredEntries);

        return await ApplyTokenBudgetAsync(scoredEntries, maxTokens, cancellationToken);
    }

    public async Task<long> ReplaceMemoryAsync(long existingEntryId, ContextEntry newEntry, CancellationToken cancellationToken = default)
    {
        if (newEntry == null)
        {
            throw new ArgumentNullException(nameof(newEntry));
        }

        ValidateEntry(newEntry);
        ValidateId(existingEntryId);

        ContextEntry? existingEntry = await _repository.GetByIdAsync(existingEntryId, newEntry.AgentId, cancellationToken);

        if (existingEntry == null || !existingEntry.IsActive)
        {
            throw new InvalidOperationException("Existing memory not found or inactive.");
        }

        if (existingEntry.MemoryScope == "LongTerm")
        {
            throw new InvalidOperationException("Long-term memory supersession requires explicit approval.");
        }

        bool shouldSupersede = await _llmClient.EvaluateMemoryReplacementAsync(existingEntry.Content, newEntry.Content, cancellationToken);

        if (!shouldSupersede)
        {
            throw new InvalidOperationException("Ollama did not identify a clear memory replacement.");
        }

        newEntry.MemoryScope = await ResolveMemoryScopeAsync(newEntry.MemoryScope, newEntry.Content, cancellationToken);
        newEntry.TokenCount = await _llmClient.CountTokensAsync(newEntry.Content, cancellationToken);

        return await _repository.InsertAndSupersedeAsync(existingEntryId, newEntry, cancellationToken);
    }

    //public async Task<long> AddAsync(ContextEntry entry, CancellationToken cancellationToken = default)
    //{
    //    ValidateEntry(entry);

    //    entry.MemoryScope = await ResolveMemoryScopeAsync(entry.MemoryScope, entry.Content, cancellationToken);

    //    entry.TokenCount = await _llmClient.CountTokensAsync(entry.Content, cancellationToken);

    //    return await _repository.AddAsync(entry, cancellationToken);
    //}


    public async Task<long> AddAsync(ContextEntry entry, CancellationToken cancellationToken = default)
    {
        ValidateEntry(entry);

        ContextEntry? existingMemory = await FindMemoryToReplaceAsync(entry, cancellationToken);

        if (existingMemory != null && existingMemory.MemoryScope != "LongTerm")
        {
            return await ReplaceMemoryAsync(existingMemory.Id, entry, cancellationToken);
        }

        entry.MemoryScope = await ResolveMemoryScopeAsync(entry.MemoryScope, entry.Content, cancellationToken);

        entry.TokenCount = await _llmClient.CountTokensAsync(entry.Content, cancellationToken);

        return await _repository.AddAsync(entry, cancellationToken);
    }

    public async Task<ContextEntry?> GetByIdAsync(long id, string agentId, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ValidateAgentId(agentId);

        return await _repository.GetByIdAsync(id, agentId, cancellationToken);
    }

    public async Task<IReadOnlyList<ContextEntry>> GetBySessionAsync(string agentId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        ValidateAgentId(agentId);

        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        return await _repository.GetBySessionAsync(agentId, sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ContextEntry>> GetByAgentAsync(string agentId, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        ValidateAgentId(agentId);

        if (maxResults < 1 || maxResults > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxResults), "MaxResults must be between 1 and 1000.");

        return await _repository.GetByAgentAsync(agentId, maxResults, cancellationToken);
    }

    public async Task<bool> UpdateAsync(ContextEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateId(entry.Id);
        ValidateAgentId(entry.AgentId);
        ValidateContent(entry.Content);
        ValidateImportance(entry.Importance);
        ValidateTokenCount(entry.TokenCount);
        ValidateMetadata(entry.Metadata);

        return await _repository.UpdateAsync(entry, cancellationToken);
    }

    public async Task<bool> DeactivateAsync(long id, string agentId, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        ValidateAgentId(agentId);

        return await _repository.DeactivateAsync(id, agentId, cancellationToken);
    }


    private async Task<ContextEntry?> FindMemoryToReplaceAsync(ContextEntry newEntry, CancellationToken cancellationToken)
    {
        var relatedMemories = await FindRelatedMemoriesAsync(newEntry, cancellationToken);

        foreach (var memory in relatedMemories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool shouldReplace = await _llmClient.EvaluateMemoryReplacementAsync(
                memory.Content, newEntry.Content, cancellationToken);

            if (shouldReplace)
            {
                return memory;
            }
        }

        return null;
    }

    private void ValidateSelectionRequest(string agentId, Guid sessionId, string query, int maxTokens)
    {
        ValidateAgentId(agentId);

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("SessionId is required.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty.", nameof(query));
        }

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be greater than zero.");
        }
    }


    private async Task<List<ContextEntry>> RemoveSemanticDuplicatesAsync(List<ContextEntry> entries,CancellationToken cancellationToken)
    {
        var uniqueEntries = new List<ContextEntry>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool isDuplicate = false;

            foreach (var existing in uniqueEntries)
            {
                bool isEquivalent = await _llmClient.AreMemoriesEquivalentAsync(
                    existing.Content, entry.Content, cancellationToken);

                if (isEquivalent)
                {
                    isDuplicate = true;
                    break;
                }
            }

            if (!isDuplicate)
            {
                uniqueEntries.Add(entry);
            }
        }

        return uniqueEntries;
    }


    private async Task<List<ContextEntry>> GetUniqueEntriesAsync(string agentId, Guid sessionId, CancellationToken cancellationToken)
    {
        var sessionEntries = await _repository.GetBySessionAsync(agentId, sessionId, cancellationToken);

        var longTermEntries = await _repository.GetLongTermByAgentAsync(agentId, cancellationToken);

        var uniqueEntries = new List<ContextEntry>();
        var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in sessionEntries)
        {
            AddUniqueEntry(entry, uniqueEntries, seenContent);
        }

        foreach (var entry in longTermEntries)
        {
            AddUniqueEntry(entry, uniqueEntries, seenContent);
        }

        return uniqueEntries;
    }


    private void AddUniqueEntry(ContextEntry entry, List<ContextEntry> uniqueEntries, HashSet<string> seenContent)
    {
        string content = entry.Content.Trim();

        if (seenContent.Add(content))
        {
            uniqueEntries.Add(entry);
        }
    }

    private async Task<List<(ContextEntry Entry, decimal Relevance)>> EvaluateEntriesAsync(List<ContextEntry> entries, string query, CancellationToken cancellationToken)
    {
        var scoredEntries = new List<(ContextEntry Entry, decimal Relevance)>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            decimal relevance = await _llmClient.EvaluateRelevanceAsync(query, entry.Content, cancellationToken);

            if (relevance >= 0.70m)
            {
                scoredEntries.Add((entry, relevance));
            }
        }

        return scoredEntries;
    }

    private void RankEntries(List<(ContextEntry Entry, decimal Relevance)> entries)
    {
        entries.Sort((a, b) =>
        {
            int relevanceComparison = b.Relevance.CompareTo(a.Relevance);

            if (relevanceComparison != 0)
            {
                return relevanceComparison;
            }

            return b.Entry.Importance.CompareTo(a.Entry.Importance);
        });
    }

    private List<ContextEntry> ApplyTokenBudget(List<(ContextEntry Entry, decimal Relevance)> entries, int maxTokens)
    {
        var selectedEntries = new List<ContextEntry>();
        int usedTokens = 0;

        foreach (var item in entries)
        {
            if (item.Entry.TokenCount <= 0)
            {
                continue;
            }

            if (item.Entry.TokenCount > maxTokens - usedTokens)
            {
                continue;
            }

            selectedEntries.Add(item.Entry);
            usedTokens += item.Entry.TokenCount;
        }

        return selectedEntries;
    }


    private async Task<List<SelectedContextResponse>> ApplyTokenBudgetAsync(List<(ContextEntry Entry, decimal Relevance)> entries, int maxTokens, CancellationToken cancellationToken)
    {
        var selectedEntries = new List<SelectedContextResponse>();
        int usedTokens = 0;

        foreach (var item in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int remainingTokens = maxTokens - usedTokens;

            if (remainingTokens <= 0)
            {
                break;
            }

            if (item.Entry.TokenCount <= 0)
            {
                continue;
            }

            string content = item.Entry.Content;
            int tokenCount = item.Entry.TokenCount;
            bool isSummarized = false;

            if (tokenCount > remainingTokens)
            {
                //if (remainingTokens < 20)
                //{
                //    continue;
                //}

                string summary = await _llmClient.SummarizeAsync(content, cancellationToken);
                int summaryTokens = await _llmClient.CountTokensAsync(summary, cancellationToken);

                if (summaryTokens >= tokenCount || summaryTokens > remainingTokens)
                {
                    continue;
                }

                content = summary;
                tokenCount = summaryTokens;
                isSummarized = true;
            }

            selectedEntries.Add(new SelectedContextResponse
            {
                Id = item.Entry.Id,
                Content = content,
                TokenCount = tokenCount,
                Relevance = item.Relevance,
                IsSummarized = isSummarized
            });

            usedTokens += tokenCount;
        }

        return selectedEntries;
    }


    private async Task<string> ResolveMemoryScopeAsync(string? memoryScope, string content, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(memoryScope))
        {
            string scope = memoryScope.Trim();

            if (scope == "Working" || scope == "Session" || scope == "LongTerm")
            {
                return scope;
            }

            throw new ArgumentException("Invalid memory scope.");
        }

        try
        {
            return await _llmClient.ClassifyMemoryAsync(content, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return "Session";
        }
    }

    private static void ValidateEntry(ContextEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateAgentId(entry.AgentId);
        ValidateContent(entry.Content);
        ValidateImportance(entry.Importance);
        ValidateTokenCount(entry.TokenCount);
        ValidateMetadata(entry.Metadata);

        if (entry.SessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.");

        if (string.IsNullOrWhiteSpace(entry.ContextType) || entry.ContextType.Length > 50)
            throw new ArgumentException("ContextType must contain between 1 and 50 characters.");

        if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= DateTime.UtcNow)
            throw new ArgumentException("ExpiresAt must be in the future.");
    }

    private static void ValidateId(long id)
    {
        if (id <= 0)
            throw new ArgumentException("Id must be greater than zero.", nameof(id));
    }

    private static void ValidateAgentId(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 100)
            throw new ArgumentException("AgentId must contain between 1 and 100 characters.");
    }

    private static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content cannot be empty.");
    }

    private static void ValidateImportance(decimal importance)
    {
        if (importance < 0 || importance > 1)
            throw new ArgumentOutOfRangeException(nameof(importance), "Importance must be between 0 and 1.");
    }

    private static void ValidateTokenCount(int tokenCount)
    {
        if (tokenCount < 0)
            throw new ArgumentOutOfRangeException(nameof(tokenCount), "TokenCount cannot be negative.");
    }

    private static void ValidateMetadata(string? metadata)
    {
        if (metadata == null) return;

        try
        {
            using var document = JsonDocument.Parse(metadata);
        }
        catch (JsonException)
        {
            throw new ArgumentException("Metadata must contain valid JSON.", nameof(metadata));
        }
    }
}
