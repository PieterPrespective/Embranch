using DMMS.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;
using System.Collections;
using System.Text.Json;
using System.Xml.Linq;

namespace DMMS.Services;

/// <summary>
/// ChromaDB service using Python.NET to interact with the chromadb Python library
/// This provides a robust implementation that directly uses the official Python library
/// </summary>
public class ChromaPythonService : IChromaDbService, IDisposable
{
    private readonly ILogger<ChromaPythonService> _logger;
    private readonly ServerConfiguration _configuration;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _clientId;
    private readonly string _configurationString;
    private bool _disposed = false;
    private bool _clientInitialized = false;
    private readonly object _initLock = new object();

    /// <summary>
    /// Initializes a new instance of ChromaPythonService
    /// </summary>
    public ChromaPythonService(ILogger<ChromaPythonService> logger, IOptions<ServerConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        // Generate unique client ID and configuration string
        _clientId = GenerateClientId();
        _configurationString = GenerateConfigurationString();
        
        // Set up the client pool logger
        ChromaClientPool.SetLogger(_logger);

        // Client initialization is deferred until first use
        _logger.LogInformation("Created ChromaPythonService with client ID: {ClientId}", _clientId);
    }

    /// <summary>
    /// Ensures the ChromaDB client is initialized
    /// </summary>
    private async Task EnsureClientInitializedAsync()
    {
        if (_clientInitialized)
            return;

        // Use a lock-free approach with double-check pattern
        if (!_clientInitialized)
        {
            lock (_initLock)
            {
                if (!_clientInitialized)
                {
                    // Initialize client on the Python thread
                    var initTask = InitializeChromaClientAsync();
                    initTask.Wait(); // We must wait synchronously within the lock
                    _clientInitialized = true;
                }
            }
        }
    }

    /// <summary>
    /// Initializes the ChromaDB client asynchronously on the Python thread
    /// </summary>
    private async Task<bool> InitializeChromaClientAsync()
    {
        // For PersistentClient, check compatibility first
        if (!string.IsNullOrEmpty(_configuration.ChromaDataPath))
        {
            string dataPath = Path.GetFullPath(_configuration.ChromaDataPath);
            Directory.CreateDirectory(dataPath);
            
            // Check for ChromaDB version compatibility issues and migrate if needed
            _logger.LogInformation("Checking ChromaDB compatibility for pre-existing database");
            if (!await ChromaCompatibilityHelper.EnsureCompatibilityAsync(_logger, dataPath))
            {
                _logger.LogWarning("ChromaDB compatibility check failed - proceeding with initialization anyway");
            }
        }
        
        return await PythonContext.ExecuteAsync(() =>
        {
            try
            {
                // Create client using the new client pool
                dynamic client = ChromaClientPool.GetOrCreateClient(_clientId, _configurationString);
                
                _logger.LogInformation("Initialized ChromaDB client {ClientId} with config: {Config}", _clientId, _configurationString);

                // For backward compatibility, set this as the default client
                ChromaDbReferences.SetDefaultClientId(_clientId);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize ChromaDB client");
                throw new InvalidOperationException("Failed to initialize ChromaDB client", ex);
            }
        }, timeoutMs: 30000, operationName: "InitializeChromaClient");
    }

    /// <summary>
    /// Lists all collections in ChromaDB
    /// </summary>
    public async Task<List<string>> ListCollectionsAsync(int? limit = null, int? offset = null)
    {
        await EnsureClientInitializedAsync();
        
        _logger.LogInformation($"[ChromaPythonService.ListCollectionsAsync] Attempting to list # collections with limit { ((limit.HasValue) ? limit.Value.ToString() : "Null") }, offset: {((offset.HasValue) ? offset.Value.ToString() : "Null")}");

        return await PythonContext.ExecuteAsync(() =>
        {
            _logger.LogInformation($"[ChromaPythonService.ListCollectionsAsync] executing on Python thread");
            
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collections = client.list_collections();
            var result = new List<string>();
            
            foreach (dynamic collection in collections)
            {
                result.Add(collection.ToString());
            }

            if (offset.HasValue)
                result = result.Skip(offset.Value).ToList();
            if (limit.HasValue)
                result = result.Take(limit.Value).ToList();

            _logger.LogInformation($"[ChromaPythonService.ListCollectionsAsync] returning result");
            return result;
        }, timeoutMs: 30000, operationName: "ListCollections");
    }

    /// <summary>
    /// Creates a new collection in ChromaDB
    /// </summary>
    public async Task<bool> CreateCollectionAsync(string name, Dictionary<string, object>? metadata = null)
    {
        await EnsureClientInitializedAsync();
        
        _logger.LogInformation($"Attempting to create collection '{name}'; python context is running: {PythonContext.IsInitialized}");
        
        return await PythonContext.ExecuteAsync(() =>
        {
            _logger.LogInformation($"Before PyObject");
            dynamic client = ChromaClientPool.GetClient(_clientId);
            PyObject? metadataObj = null;
            
            if (metadata != null && metadata.Count > 0)
            {
                metadataObj = ConvertDictionaryToPyDict(metadata);
            }

            _logger.LogInformation($"Attempting to create collection within Python '{name}'");
            if (metadataObj != null)
            {
                client.create_collection(name: name, metadata: metadataObj);
            }
            else
            {
                client.create_collection(name: name);
            }
            
            _logger.LogInformation($"Created collection '{name}'");
            return true;
        }, timeoutMs: 30000, operationName: $"CreateCollection_{name}");
    }

    /// <summary>
    /// Gets a collection from ChromaDB
    /// </summary>
    public async Task<object?> GetCollectionAsync(string name)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: name);
            
            var result = new Dictionary<string, object>
            {
                ["name"] = name,
                ["id"] = collection.id.ToString(),
                ["metadata"] = ConvertPyDictToDictionary(collection.metadata)
            };
            
            return (object?)result;
        }, timeoutMs: 30000, operationName: $"GetCollection_{name}");
    }

    /// <summary>
    /// Deletes a collection from ChromaDB
    /// </summary>
    public async Task<bool> DeleteCollectionAsync(string name)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            client.delete_collection(name: name);
            _logger.LogInformation($"Deleted collection '{name}'");
            return true;
        }, timeoutMs: 30000, operationName: $"DeleteCollection_{name}");
    }

    /// <summary>
    /// Adds documents to a ChromaDB collection
    /// </summary>
    public async Task<bool> AddDocumentsAsync(string collectionName, List<string> documents, List<string> ids, List<Dictionary<string, object>>? metadatas = null, bool allowDuplicateIds = false)
    {
        await EnsureClientInitializedAsync();
        
        // Check for duplicate IDs if not allowed (this can happen outside Python thread)
        if (!allowDuplicateIds)
        {
            var existingDocs = await GetDocumentsAsync(collectionName, ids, null, 1);

            _logger.LogInformation($"Gotten Existing Documents");

            if (existingDocs != null && existingDocs is Dictionary<string, object> result)
            {
                if (result.TryGetValue("ids", out var existingIds) && existingIds is List<object> idList && idList.Count > 0)
                {
                    _logger.LogInformation($"Found conflicting ID!");

                    var existingId = idList[0]?.ToString();
                    throw new InvalidOperationException($"Document with ID '{existingId}' already exists in collection '{collectionName}'");
                }
            }
        }
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_or_create_collection(name: collectionName);
            
            // Convert C# lists to Python lists
            PyObject pyIds = ConvertListToPyList(ids);
            PyObject pyDocuments = ConvertListToPyList(documents);
            PyObject? pyMetadatas = null;

            if (metadatas != null && metadatas.Count > 0)
            {
                pyMetadatas = ConvertMetadatasToPyList(metadatas);
            }

            // Add documents to collection
            if (pyMetadatas != null)
            {
                collection.add(
                    ids: pyIds,
                    documents: pyDocuments,
                    metadatas: pyMetadatas
                );
            }
            else
            {
                collection.add(
                    ids: pyIds,
                    documents: pyDocuments
                );
            }

            _logger.LogInformation($"Added {documents.Count} documents to collection '{collectionName}'");
            return true;
        }, timeoutMs: 60000, operationName: $"AddDocuments_{collectionName}");
    }

    /// <summary>
    /// Queries documents in a ChromaDB collection
    /// </summary>
    public async Task<object?> QueryDocumentsAsync(string collectionName, List<string> queryTexts, int nResults = 5,
        Dictionary<string, object>? where = null, Dictionary<string, object>? whereDocument = null)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: collectionName);
            
            PyObject pyQueryTexts = ConvertListToPyList(queryTexts);
            PyObject? pyWhere = where != null ? ConvertDictionaryToPyDict(where) : null;
            PyObject? pyWhereDocument = whereDocument != null ? ConvertDictionaryToPyDict(whereDocument) : null;

            dynamic results;
            if (pyWhere != null && pyWhereDocument != null)
            {
                results = collection.query(
                    query_texts: pyQueryTexts,
                    n_results: nResults,
                    where: pyWhere,
                    where_document: pyWhereDocument
                );
            }
            else if (pyWhere != null)
            {
                results = collection.query(
                    query_texts: pyQueryTexts,
                    n_results: nResults,
                    where: pyWhere
                );
            }
            else if (pyWhereDocument != null)
            {
                results = collection.query(
                    query_texts: pyQueryTexts,
                    n_results: nResults,
                    where_document: pyWhereDocument
                );
            }
            else
            {
                results = collection.query(
                    query_texts: pyQueryTexts,
                    n_results: nResults
                );
            }

            // Convert results to C# objects
            var result = new Dictionary<string, object>
            {
                ["ids"] = ConvertPyListToList(results["ids"]),
                ["documents"] = results["documents"] != null ? ConvertPyListToList(results["documents"]) : new List<object>(),
                ["metadatas"] = results["metadatas"] != null ? ConvertPyListToMetadatasList(results["metadatas"]) : new List<object>(),
                ["distances"] = ConvertPyListToList(results["distances"])
            };

            return (object?)result;
        }, timeoutMs: 60000, operationName: $"QueryDocuments_{collectionName}");
    }

    /// <summary>
    /// Gets documents from a ChromaDB collection
    /// </summary>
    public async Task<object?> GetDocumentsAsync(string collectionName, List<string>? ids = null,
        Dictionary<string, object>? where = null, int? limit = null)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: collectionName);
            
            PyObject? pyIds = ids != null && ids.Count > 0 ? ConvertListToPyList(ids) : null;
            PyObject? pyWhere = where != null ? ConvertDictionaryToPyDict(where) : null;

            dynamic results;
            if (pyIds != null && pyWhere != null && limit.HasValue)
            {
                results = collection.get(ids: pyIds, where: pyWhere, limit: limit.Value);
            }
            else if (pyIds != null && pyWhere != null)
            {
                results = collection.get(ids: pyIds, where: pyWhere);
            }
            else if (pyIds != null && limit.HasValue)
            {
                results = collection.get(ids: pyIds, limit: limit.Value);
            }
            else if (pyWhere != null && limit.HasValue)
            {
                results = collection.get(where: pyWhere, limit: limit.Value);
            }
            else if (pyIds != null)
            {
                results = collection.get(ids: pyIds);
            }
            else if (pyWhere != null)
            {
                results = collection.get(where: pyWhere);
            }
            else if (limit.HasValue)
            {
                results = collection.get(limit: limit.Value);
            }
            else
            {
                results = collection.get();
            }

            // Convert results to C# objects
            var result = new Dictionary<string, object>
            {
                ["ids"] = ConvertPyListToList(results["ids"]),
                ["documents"] = results["documents"] != null ? ConvertPyListToList(results["documents"]) : new List<object>(),
                ["metadatas"] = results["metadatas"] != null ? ConvertPyListToMetadatasList(results["metadatas"]) : new List<object>()
            };

            return (object?)result;
        }, timeoutMs: 60000, operationName: $"GetDocuments_{collectionName}");
    }

    /// <summary>
    /// Updates documents in a ChromaDB collection
    /// </summary>
    public async Task<bool> UpdateDocumentsAsync(string collectionName, List<string> ids,
        List<string>? documents = null, List<Dictionary<string, object>>? metadatas = null)
    {
        await EnsureClientInitializedAsync();
        
        if (documents == null && metadatas == null)
        {
            throw new ArgumentException("At least one of documents or metadatas must be provided");
        }
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: collectionName);
            
            PyObject pyIds = ConvertListToPyList(ids);
            PyObject? pyDocuments = documents != null ? ConvertListToPyList(documents) : null;
            PyObject? pyMetadatas = metadatas != null ? ConvertMetadatasToPyList(metadatas) : null;

            if (pyDocuments != null && pyMetadatas != null)
            {
                collection.update(ids: pyIds, documents: pyDocuments, metadatas: pyMetadatas);
            }
            else if (pyDocuments != null)
            {
                collection.update(ids: pyIds, documents: pyDocuments);
            }
            else if (pyMetadatas != null)
            {
                collection.update(ids: pyIds, metadatas: pyMetadatas);
            }

            _logger.LogInformation($"Updated {ids.Count} documents in collection '{collectionName}'");
            return true;
        }, timeoutMs: 60000, operationName: $"UpdateDocuments_{collectionName}");
    }

    /// <summary>
    /// Deletes documents from a ChromaDB collection
    /// </summary>
    public async Task<bool> DeleteDocumentsAsync(string collectionName, List<string> ids)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: collectionName);
            PyObject pyIds = ConvertListToPyList(ids);
            collection.delete(ids: pyIds);
            
            _logger.LogInformation($"Deleted {ids.Count} documents from collection '{collectionName}'");
            return true;
        }, timeoutMs: 30000, operationName: $"DeleteDocuments_{collectionName}");
    }

    /// <summary>
    /// Gets the document count in a ChromaDB collection
    /// </summary>
    public async Task<int> GetCollectionCountAsync(string collectionName)
    {
        await EnsureClientInitializedAsync();
        
        return await PythonContext.ExecuteAsync(() =>
        {
            dynamic client = ChromaClientPool.GetClient(_clientId);
            dynamic collection = client.get_collection(name: collectionName);
            int count = collection.count();
            return count;
        }, timeoutMs: 30000, operationName: $"GetCollectionCount_{collectionName}");
    }

    #region Python Conversion Helpers

    /// <summary>
    /// Converts a C# list to a Python list
    /// </summary>
    private PyObject ConvertListToPyList(List<string> list)
    {
        dynamic pyList = PythonEngine.Eval("[]");
        foreach (var item in list)
        {
            pyList.append(item);
        }
        return pyList;
    }

    /// <summary>
    /// Converts a C# dictionary to a Python dictionary
    /// </summary>
    private PyObject ConvertDictionaryToPyDict(Dictionary<string, object> dict)
    {
        dynamic pyDict = PythonEngine.Eval("{}");
        foreach (var kvp in dict)
        {
            if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                pyDict[kvp.Key] = ConvertDictionaryToPyDict(nestedDict);
            }
            else if (kvp.Value is List<object> listValue)
            {
                dynamic pyList = PythonEngine.Eval("[]");
                foreach (var item in listValue)
                {
                    pyList.append(item);
                }
                pyDict[kvp.Key] = pyList;
            }
            else
            {
                // Handle different value types explicitly
                if (kvp.Value is string stringValue)
                {
                    pyDict[kvp.Key] = new PyString(stringValue);
                }
                else if (kvp.Value is int intValue)
                {
                    pyDict[kvp.Key] = new PyInt(intValue);
                }
                else if (kvp.Value is bool boolValue)
                {
                    pyDict[kvp.Key] = boolValue.ToPython();
                }
                else if (kvp.Value is float floatValue)
                {
                    pyDict[kvp.Key] = new PyFloat(floatValue);
                }
                else if (kvp.Value is double doubleValue)
                {
                    pyDict[kvp.Key] = new PyFloat(doubleValue);
                }
                else
                {
                    pyDict[kvp.Key] = new PyString(kvp.Value?.ToString() ?? "");
                }
            }
        }
        return pyDict;
    }

    /// <summary>
    /// Converts a list of metadata dictionaries to Python list
    /// </summary>
    private PyObject ConvertMetadatasToPyList(List<Dictionary<string, object>> metadatas)
    {
        dynamic pyList = PythonEngine.Eval("[]");
        foreach (var metadata in metadatas)
        {
            pyList.append(ConvertDictionaryToPyDict(metadata));
        }
        return pyList;
    }

    /// <summary>
    /// Converts a Python list to C# list
    /// </summary>
    private List<object> ConvertPyListToList(dynamic pyList)
    {
        var result = new List<object>();
        foreach (var item in pyList)
        {
            if (item is PyObject pyObj)
            {
                // Check if it's a nested list (not a string)
                var pyType = pyObj.GetPythonType();
                if (pyObj.HasAttr("__iter__") && !pyType.Name.Equals("str"))
                {
                    result.Add(ConvertPyListToList(item));
                }
                else
                {
                    result.Add(item.ToString());
                }
            }
            else
            {
                result.Add(item?.ToString() ?? string.Empty);
            }
        }
        return result;
    }

    /// <summary>
    /// Converts Python list of metadata dictionaries to C# list
    /// </summary>
    private List<object> ConvertPyListToMetadatasList(dynamic pyList)
    {
        var result = new List<object>();
        if (pyList == null) return result;
        
        foreach (var item in pyList)
        {
            if (item == null)
            {
                result.Add(new Dictionary<string, object>());
                continue;
            }
            
            if (item is PyObject pyObj)
            {
                // Check if it's iterable (list of metadata dictionaries)
                var pyType = pyObj.GetPythonType();
                if (pyObj.HasAttr("__iter__") && !pyType.Name.Equals("str") && !pyType.Name.Equals("dict"))
                {
                    var subList = new List<Dictionary<string, object>>();
                    foreach (var metadata in item)
                    {
                        if (metadata != null)
                        {
                            subList.Add(ConvertPyDictToDictionary(metadata));
                        }
                        else
                        {
                            subList.Add(new Dictionary<string, object>());
                        }
                    }
                    result.Add(subList);
                }
                else if (pyType.Name.Equals("dict"))
                {
                    // It's a single dictionary, not a list of dictionaries
                    result.Add(ConvertPyDictToDictionary(item));
                }
                else
                {
                    result.Add(new Dictionary<string, object>());
                }
            }
            else
            {
                result.Add(ConvertPyDictToDictionary(item));
            }
        }
        return result;
    }

    /// <summary>
    /// Converts a Python dictionary to C# dictionary
    /// </summary>
    private Dictionary<string, object> ConvertPyDictToDictionary(dynamic pyDict)
    {
        var result = new Dictionary<string, object>();
        if (pyDict == null)
            return result;

        foreach (var key in pyDict)
        {
            string keyStr = key.ToString();
            var value = pyDict[key];
            
            if (value is PyObject pyObj)
            {
                var pyType = pyObj.GetPythonType();
                if (pyObj.HasAttr("__iter__") && !pyType.Name.Equals("str"))
                {
                    result[keyStr] = ConvertPyListToList(value);
                }
                else
                {
                    result[keyStr] = value.ToString();
                }
            }
            else
            {
                result[keyStr] = value?.ToString() ?? string.Empty;
            }
        }
        return result;
    }

    #endregion

    /// <summary>
    /// Generates a unique client ID for this service instance
    /// </summary>
    private string GenerateClientId()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        var guid = Guid.NewGuid().ToString("N")[..8]; // First 8 characters of GUID
        return $"ChromaPythonService_{timestamp}_{guid}";
    }

    /// <summary>
    /// Generates a configuration string for the client pool
    /// </summary>
    private string GenerateConfigurationString()
    {
        if (!string.IsNullOrEmpty(_configuration.ChromaDataPath))
        {
            return $"persistent:{_configuration.ChromaDataPath}";
        }
        else if (!string.IsNullOrEmpty(_configuration.ChromaHost))
        {
            return $"http:{_configuration.ChromaHost}:{_configuration.ChromaPort}";
        }
        else
        {
            throw new InvalidOperationException("ChromaDB configuration must specify either ChromaDataPath (persistent) or ChromaHost (http)");
        }
    }

    /// <summary>
    /// Gets the client ID for this service instance
    /// </summary>
    public string GetClientId() => _clientId;

    /// <summary>
    /// Disposes of the Python resources
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes of the Python resources
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _logger?.LogInformation("Disposing ChromaPythonService with client ID: {ClientId}", _clientId);
                
                // Dispose this specific client from the pool
                ChromaClientPool.DisposeClient(_clientId);
                _clientInitialized = false;
                
                _logger?.LogInformation("ChromaPythonService disposed successfully");
            }
            _disposed = true;
        }
    }
}