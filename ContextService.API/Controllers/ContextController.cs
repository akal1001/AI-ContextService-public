
using Microsoft.AspNetCore.Mvc;
using ContextService.Business.Services;
using ContextService.Models.Entities;
using ContextService.Models.DTOs.Requests;

namespace ContextService.API.Controllers;

[ApiController]
[Route("api/context")]
public class ContextController : ControllerBase
{
    private readonly ContextManager _contextManager;

    public ContextController(ContextManager contextManager)
    {
        _contextManager = contextManager;
    }

    [HttpPost]
    public async Task<ActionResult<long>> AddAsync([FromBody] ContextEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            long id = await _contextManager.AddAsync(entry, cancellationToken);
            return Ok(id);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("session/{agentId}/{sessionId:guid}")]
    public async Task<ActionResult<IReadOnlyList<ContextEntry>>> GetBySessionAsync(string agentId, Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _contextManager.GetBySessionAsync(agentId, sessionId, cancellationToken);
            return Ok(entries);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }


    [HttpGet("select/{agentId}/{sessionId:guid}")]
    public async Task<ActionResult<IReadOnlyList<ContextEntry>>> SelectRelevantAsync(string agentId, Guid sessionId, [FromQuery] string query, [FromQuery] int maxTokens = 500, CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = await _contextManager.SelectRelevantMemoriesAsync(agentId, sessionId, query, maxTokens, cancellationToken);
            return Ok(entries);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

   
    [HttpPost("evaluate-supersession/{agentId}/{existingEntryId:long}")]
    public async Task<IActionResult> EvaluateSupersessionAsync(string agentId,long existingEntryId,[FromBody] SupersessionEvaluationRequest request,CancellationToken cancellationToken)
    {
        bool shouldSupersede = await _contextManager.EvaluateMemoryReplacementAsync(existingEntryId,agentId,request.NewContent,cancellationToken);

        return Ok(new
        {
            ExistingEntryId = existingEntryId,
            ShouldSupersede = shouldSupersede
        });
    }



    /// <summary>
    /// Replaces an existing memory with a new one and deactivates the old memory.
    /// </summary>*

    [HttpPost("supersede/{existingEntryId:long}")]
    public async Task<ActionResult<long>> SupersedeAsync(long existingEntryId,[FromBody] ContextEntry newEntry,CancellationToken cancellationToken)
    {
        long newEntryId = await _contextManager.ReplaceMemoryAsync(existingEntryId, newEntry, cancellationToken);

        return Ok(newEntryId);
    }
}
