using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using DMMS.Services;
using DMMS.Utilities;

namespace DMMS.Tools;

/// <summary>
/// MCP tool that modifies a ChromaDB collection's name or metadata
/// </summary>
[McpServerToolType]
public class ChromaModifyCollectionTool
{
    private readonly ILogger<ChromaModifyCollectionTool> _logger;
    private readonly IChromaDbService _chromaService;

    /// <summary>
    /// Initializes a new instance of the ChromaModifyCollectionTool class
    /// </summary>
    public ChromaModifyCollectionTool(ILogger<ChromaModifyCollectionTool> logger, IChromaDbService chromaService)
    {
        _logger = logger;
        _chromaService = chromaService;
    }

    /// <summary>
    /// Update a collection's name or metadata. Note: Changing HNSW parameters after creation has no effect on existing data
    /// </summary>
    [McpServerTool]
    [Description("Update a collection's name or metadata. Note: Changing HNSW parameters after creation has no effect on existing data.")]
    public virtual async Task<object> ModifyCollection(string collection_name, string? new_name = null, Dictionary<string, object>? new_metadata = null)
    {
        const string toolName = nameof(ChromaModifyCollectionTool);
        const string methodName = nameof(ModifyCollection);
        ToolLoggingUtility.LogToolStart(_logger, toolName, methodName, $"collection_name: {collection_name}, new_name: {new_name}, new_metadata: {new_metadata != null}");

        try
        {
            if (string.IsNullOrWhiteSpace(collection_name))
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Collection name is required");
                return new
                {
                    success = false,
                    error = "COLLECTION_NAME_REQUIRED",
                    message = "Collection name is required"
                };
            }

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Modifying collection: {collection_name}");

            // Check if collection exists
            var collection = await _chromaService.GetCollectionAsync(collection_name);
            if (collection == null)
            {
                ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, $"Collection '{collection_name}' does not exist");
                return new
                {
                    success = false,
                    error = "COLLECTION_NOT_FOUND",
                    message = $"Collection '{collection_name}' does not exist"
                };
            }

            // STUB: Backend method not yet implemented
            // TODO: Implement ModifyCollectionAsync in IChromaDbService
            ToolLoggingUtility.LogToolWarning(_logger, toolName, "Backend method ModifyCollectionAsync not yet implemented");
            
            ToolLoggingUtility.LogToolFailure(_logger, toolName, methodName, "Collection modification is not yet implemented in the backend service");
            return new
            {
                success = false,
                error = "NOT_IMPLEMENTED",
                message = "Collection modification is not yet implemented in the backend service",
                stub = true,
                required_backend_method = "IChromaDbService.ModifyCollectionAsync"
            };

            // When backend is implemented, the code would be:
            /*
            var result = await _chromaService.ModifyCollectionAsync(collection_name, new_name, new_metadata);
            
            return new
            {
                success = true,
                collection = new
                {
                    name = result.Name,
                    metadata = result.Metadata
                },
                changes = new
                {
                    name_changed = new_name != null,
                    metadata_changed = new_metadata != null
                },
                message = $"Collection '{collection_name}' modified successfully"
            };
            */
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to modify collection: {ex.Message}"
            };
        }
    }
}