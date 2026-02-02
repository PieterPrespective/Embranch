using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Embranch.Services;
using Embranch.Utilities;

namespace Embranch.Tools;

/// <summary>
/// MCP tool that gets detailed information about a specific ChromaDB collection
/// </summary>
[McpServerToolType]
public class ChromaGetCollectionInfoTool
{
    private readonly ILogger<ChromaGetCollectionInfoTool> _logger;
    private readonly IChromaDbService _chromaService;

    /// <summary>
    /// Initializes a new instance of the ChromaGetCollectionInfoTool class
    /// </summary>
    public ChromaGetCollectionInfoTool(ILogger<ChromaGetCollectionInfoTool> logger, IChromaDbService chromaService)
    {
        _logger = logger;
        _chromaService = chromaService;
    }

    /// <summary>
    /// Get detailed information about a specific collection including its configuration, metadata, and embedding function settings
    /// </summary>
    [McpServerTool]
    [Description("Get detailed information about a specific collection including its configuration, metadata, and embedding function settings.")]
    public virtual async Task<object> GetCollectionInfo(string collection_name)
    {
        const string toolName = nameof(ChromaGetCollectionInfoTool);
        const string methodName = nameof(GetCollectionInfo);
        ToolLoggingUtility.LogToolStart(_logger, toolName, methodName, $"collection_name: {collection_name}");

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

            ToolLoggingUtility.LogToolInfo(_logger, toolName, $"Getting info for collection: {collection_name}");

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

            // Get document count
            var documentCount = await _chromaService.GetCollectionCountAsync(collection_name);

            // PP13-100: Extract metadata from collection object returned by GetCollectionAsync
            // The collection object is already a Dictionary<string, object> with a "metadata" key
            var metadata = ExtractMetadataFromCollection(collection);

            ToolLoggingUtility.LogToolSuccess(_logger, toolName, methodName, $"Retrieved info for collection '{collection_name}' with {documentCount} documents");
            return new
            {
                success = true,
                name = collection_name,
                metadata = metadata,
                document_count = documentCount,
                message = $"Collection '{collection_name}' contains {documentCount} documents"
            };
        }
        catch (Exception ex)
        {
            ToolLoggingUtility.LogToolException(_logger, toolName, methodName, ex);
            return new
            {
                success = false,
                error = "OPERATION_FAILED",
                message = $"Failed to get collection info: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Extracts metadata from the collection object returned by GetCollectionAsync.
    /// The collection is a Dictionary containing name, id, and metadata fields.
    /// </summary>
    /// <param name="collectionData">The collection object from GetCollectionAsync</param>
    /// <returns>The metadata dictionary, or an empty dictionary if metadata cannot be extracted</returns>
    private Dictionary<string, object> ExtractMetadataFromCollection(object? collectionData)
    {
        if (collectionData == null)
        {
            return new Dictionary<string, object>();
        }

        try
        {
            // GetCollectionAsync returns a Dictionary<string, object> with "name", "id", and "metadata" keys
            if (collectionData is Dictionary<string, object> collectionDict)
            {
                if (collectionDict.TryGetValue("metadata", out var metadataValue))
                {
                    // The metadata value is already a Dictionary<string, object> from ConvertPyDictToDictionary
                    if (metadataValue is Dictionary<string, object> metadataDict)
                    {
                        return metadataDict;
                    }

                    _logger.LogWarning("Collection metadata value is not a Dictionary<string, object>, type: {Type}",
                        metadataValue?.GetType().Name ?? "null");
                }
            }
            else
            {
                _logger.LogWarning("Collection data is not a Dictionary<string, object>, type: {Type}",
                    collectionData.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract metadata from collection object");
        }

        return new Dictionary<string, object>();
    }
}