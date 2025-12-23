using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using DMMS.Services;
using DMMS.Utilities;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that deletes documents from a ChromaDB collection
/// </summary>
[McpServerToolType]
public class ChromaDeleteDocumentsTool
{
    private readonly ILogger<ChromaDeleteDocumentsTool> _logger;
    private readonly IChromaDbService _chromaService;

    /// <summary>
    /// Initializes a new instance of the ChromaDeleteDocumentsTool class
    /// </summary>
    public ChromaDeleteDocumentsTool(ILogger<ChromaDeleteDocumentsTool> logger, IChromaDbService chromaService)
    {
        _logger = logger;
        _chromaService = chromaService;
    }

    /// <summary>
    /// Deletes specific documents from a ChromaDB collection
    /// </summary>
    [McpServerTool]
    [Description("Delete specific documents from a collection.")]
    public virtual async Task<object> DeleteDocuments(string collectionName, string idsJson)
    {
        const string toolName = nameof(ChromaDeleteDocumentsTool);
        const string methodName = nameof(DeleteDocuments);
        
        try
        {
            ToolLoggingUtility.LogToolStart(_logger, toolName, methodName, 
                $"Collection: '{collectionName}', IDs: {idsJson?.Length ?? 0} chars");
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                const string error = "Collection name is required";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error
                };
            }

            if (string.IsNullOrWhiteSpace(idsJson))
            {
                const string error = "IDs JSON is required";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error
                };
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Deleting documents from collection '{collectionName}'");

            List<string> ids;
            try
            {
                ids = JsonSerializer.Deserialize<List<string>>(idsJson) ?? new List<string>();
            }
            catch (JsonException ex)
            {
                var error = $"Invalid IDs JSON format: {ex.Message}";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error
                };
            }

            if (ids.Count == 0)
            {
                const string error = "IDs list cannot be empty";
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, error);
                return new
                {
                    success = false,
                    error = error
                };
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Attempting to delete {ids.Count} documents from collection '{collectionName}'");
            var startTime = DateTime.UtcNow;
            var result = await _chromaService.DeleteDocumentsAsync(collectionName, ids);
            var duration = DateTime.UtcNow - startTime;

            var response = new
            {
                success = result,
                message = result ? $"Successfully deleted {ids.Count} documents from collection '{collectionName}'" : "Failed to delete documents"
            };

            if (result)
            {
                var resultMessage = $"Deleted {ids.Count} documents from '{collectionName}' in {duration.TotalMilliseconds:F1}ms";
                ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName, resultMessage);
            }
            else
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Failed to delete documents");
            }
            
            return response;
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = $"Failed to delete documents: {ex.Message}"
            };
        }
    }
}