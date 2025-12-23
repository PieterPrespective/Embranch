using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Utilities;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that deletes a collection from ChromaDB
/// </summary>
[McpServerToolType]
public class ChromaDeleteCollectionTool
{
    private readonly ILogger<ChromaDeleteCollectionTool> _logger;
    private readonly IChromaDbService _chromaService;

    /// <summary>
    /// Initializes a new instance of the ChromaDeleteCollectionTool class
    /// </summary>
    public ChromaDeleteCollectionTool(ILogger<ChromaDeleteCollectionTool> logger, IChromaDbService chromaService)
    {
        _logger = logger;
        _chromaService = chromaService;
    }

    /// <summary>
    /// Deletes a collection from ChromaDB
    /// </summary>
    [McpServerTool]
    [Description("Delete a collection from ChromaDB.")]
    public virtual async Task<object> DeleteCollection(string collectionName)
    {
        const string toolName = nameof(ChromaDeleteCollectionTool);
        const string methodName = nameof(DeleteCollection);
        ToolLoggingUtility.LogToolStart(_logger, toolName, methodName, $"collectionName: {collectionName}");

        try
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Collection name is required");
                return new
                {
                    success = false,
                    error = "Collection name is required"
                };
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Deleting collection '{collectionName}'");

            var result = await _chromaService.DeleteCollectionAsync(collectionName);

            if (result)
            {
                ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName, $"Successfully deleted collection '{collectionName}'");
            }
            else
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Failed to delete collection");
            }

            return new
            {
                success = result,
                message = result ? $"Successfully deleted collection '{collectionName}'" : "Failed to delete collection"
            };
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = $"Failed to delete collection: {ex.Message}"
            };
        }
    }
}