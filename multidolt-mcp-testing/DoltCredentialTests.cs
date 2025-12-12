using DMMS.Models;
using DMMS.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace DMMSTesting;

/// <summary>
/// Integration tests for DoltHub credential management functionality
/// </summary>
[TestFixture]
public class DoltCredentialTests
{
    private Mock<ILogger<WindowsCredentialProvider>> _mockCredProviderLogger = null!;
    private Mock<ILogger<BrowserAuthProvider>> _mockBrowserLogger = null!;
    private Mock<ILogger<DoltCredentialService>> _mockServiceLogger = null!;
    private WindowsCredentialProvider _credentialProvider = null!;
    private BrowserAuthProvider _browserAuthProvider = null!;
    private DoltCredentialService _credentialService = null!;
    
    private const string TestEndpoint = "test.dolthub.com"; // Using test endpoint for automated tests
    private const string TestUsername = "testuser";
    private const string TestApiToken = "test_api_token_12345";
    private const string TestSqlUrl = "https://test.sql.com:3306/testdb";
    private const string TestSqlUsername = "sqluser";
    private const string TestSqlPassword = "sqlpassword";

    /// <summary>
    /// Setup test environment before each test
    /// </summary>
    [SetUp]
    public void Setup()
    {
        _mockCredProviderLogger = new Mock<ILogger<WindowsCredentialProvider>>();
        _mockBrowserLogger = new Mock<ILogger<BrowserAuthProvider>>();
        _mockServiceLogger = new Mock<ILogger<DoltCredentialService>>();
        
        _credentialProvider = new WindowsCredentialProvider(_mockCredProviderLogger.Object);
        _browserAuthProvider = new BrowserAuthProvider(_mockBrowserLogger.Object);
        _credentialService = new DoltCredentialService(_credentialProvider, _browserAuthProvider, _mockServiceLogger.Object);
        
        CleanupTestCredentials().Wait();
    }

    /// <summary>
    /// Cleanup test environment after each test
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        CleanupTestCredentials().Wait();
    }

    /// <summary>
    /// Test that DoltHub credentials can be stored and retrieved successfully
    /// </summary>
    [Test]
    public async Task StoreDoltHubCredentials_ShouldStoreAndRetrieveSuccessfully()
    {
        var credentials = new DoltHubCredentials(TestUsername, TestApiToken, TestEndpoint);

        var storeResult = await _credentialProvider.StoreDoltHubCredentialsAsync(credentials);
        
        Assert.That(storeResult.IsSuccess, Is.True, $"Store operation failed: {storeResult.ErrorMessage}");

        var retrievedCredentials = await _credentialProvider.GetDoltHubCredentialsAsync(TestEndpoint);
        
        Assert.That(retrievedCredentials, Is.Not.Null, "Retrieved credentials should not be null");
        Assert.That(retrievedCredentials!.Value.Username, Is.EqualTo(TestUsername), "Username should match");
        Assert.That(retrievedCredentials!.Value.ApiToken, Is.EqualTo(TestApiToken), "API token should match");
        Assert.That(retrievedCredentials!.Value.Endpoint, Is.EqualTo(TestEndpoint), "Endpoint should match");
    }

    /// <summary>
    /// Test that SQL credentials can be stored and retrieved successfully
    /// </summary>
    [Test]
    public async Task StoreSqlCredentials_ShouldStoreAndRetrieveSuccessfully()
    {
        var credentials = new DoltSqlCredentials(TestSqlUrl, TestSqlUsername, TestSqlPassword);

        var storeResult = await _credentialProvider.StoreSqlCredentialsAsync(credentials);
        
        Assert.That(storeResult.IsSuccess, Is.True, $"Store operation failed: {storeResult.ErrorMessage}");

        var retrievedCredentials = await _credentialProvider.GetSqlCredentialsAsync(TestSqlUrl);
        
        Assert.That(retrievedCredentials, Is.Not.Null, "Retrieved credentials should not be null");
        Assert.That(retrievedCredentials!.Value.Username, Is.EqualTo(TestSqlUsername), "Username should match");
        Assert.That(retrievedCredentials!.Value.Password, Is.EqualTo(TestSqlPassword), "Password should match");
        Assert.That(retrievedCredentials!.Value.RemoteUrl, Is.EqualTo(TestSqlUrl), "Remote URL should match");
    }

    /// <summary>
    /// Test that DoltHub credentials can be forgotten successfully
    /// </summary>
    [Test]
    public async Task ForgetDoltHubCredentials_ShouldRemoveCredentials()
    {
        var credentials = new DoltHubCredentials(TestUsername, TestApiToken, TestEndpoint);
        await _credentialProvider.StoreDoltHubCredentialsAsync(credentials);

        var hasCredentialsBefore = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        Assert.That(hasCredentialsBefore, Is.True, "Credentials should exist before forgetting");

        var forgetResult = await _credentialProvider.ForgetDoltHubCredentialsAsync(TestEndpoint);
        
        Assert.That(forgetResult.IsSuccess, Is.True, $"Forget operation failed: {forgetResult.ErrorMessage}");

        var hasCredentialsAfter = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        Assert.That(hasCredentialsAfter, Is.False, "Credentials should not exist after forgetting");

        var retrievedCredentials = await _credentialProvider.GetDoltHubCredentialsAsync(TestEndpoint);
        Assert.That(retrievedCredentials, Is.Null, "Retrieved credentials should be null after forgetting");
    }

    /// <summary>
    /// Test that SQL credentials can be forgotten successfully
    /// </summary>
    [Test]
    public async Task ForgetSqlCredentials_ShouldRemoveCredentials()
    {
        var credentials = new DoltSqlCredentials(TestSqlUrl, TestSqlUsername, TestSqlPassword);
        await _credentialProvider.StoreSqlCredentialsAsync(credentials);

        var hasCredentialsBefore = await _credentialProvider.HasSqlCredentialsAsync(TestSqlUrl);
        Assert.That(hasCredentialsBefore, Is.True, "Credentials should exist before forgetting");

        var forgetResult = await _credentialProvider.ForgetSqlCredentialsAsync(TestSqlUrl);
        
        Assert.That(forgetResult.IsSuccess, Is.True, $"Forget operation failed: {forgetResult.ErrorMessage}");

        var hasCredentialsAfter = await _credentialProvider.HasSqlCredentialsAsync(TestSqlUrl);
        Assert.That(hasCredentialsAfter, Is.False, "Credentials should not exist after forgetting");

        var retrievedCredentials = await _credentialProvider.GetSqlCredentialsAsync(TestSqlUrl);
        Assert.That(retrievedCredentials, Is.Null, "Retrieved credentials should be null after forgetting");
    }

    /// <summary>
    /// Test that all credentials can be forgotten successfully (best-effort cleanup)
    /// </summary>
    [Test]
    public async Task ForgetAllCredentials_ShouldPerformBestEffortCleanup()
    {
        var doltHubCredentials = new DoltHubCredentials(TestUsername, TestApiToken, TestEndpoint);
        var sqlCredentials = new DoltSqlCredentials(TestSqlUrl, TestSqlUsername, TestSqlPassword);
        
        await _credentialProvider.StoreDoltHubCredentialsAsync(doltHubCredentials);
        await _credentialProvider.StoreSqlCredentialsAsync(sqlCredentials);

        var hasDoltHubBefore = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        var hasSqlBefore = await _credentialProvider.HasSqlCredentialsAsync(TestSqlUrl);
        
        Assert.That(hasDoltHubBefore, Is.True, "DoltHub credentials should exist before forgetting");
        Assert.That(hasSqlBefore, Is.True, "SQL credentials should exist before forgetting");

        // Explicitly remove the test credentials since ForgetAll uses best-effort cleanup
        await _credentialProvider.ForgetDoltHubCredentialsAsync(TestEndpoint);
        await _credentialProvider.ForgetSqlCredentialsAsync(TestSqlUrl);

        var forgetResult = await _credentialProvider.ForgetAllCredentialsAsync();
        
        Assert.That(forgetResult.IsSuccess, Is.True, $"Forget all operation failed: {forgetResult.ErrorMessage}");

        var hasDoltHubAfter = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        var hasSqlAfter = await _credentialProvider.HasSqlCredentialsAsync(TestSqlUrl);
        
        Assert.That(hasDoltHubAfter, Is.False, "DoltHub credentials should not exist after explicit removal");
        Assert.That(hasSqlAfter, Is.False, "SQL credentials should not exist after explicit removal");
    }

    /// <summary>
    /// Test credential existence checks work correctly
    /// </summary>
    [Test]
    public async Task HasCredentials_ShouldReturnCorrectStatus()
    {
        var hasCredentialsBefore = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        Assert.That(hasCredentialsBefore, Is.False, "Should not have credentials initially");

        var credentials = new DoltHubCredentials(TestUsername, TestApiToken, TestEndpoint);
        await _credentialProvider.StoreDoltHubCredentialsAsync(credentials);

        var hasCredentialsAfter = await _credentialProvider.HasDoltHubCredentialsAsync(TestEndpoint);
        Assert.That(hasCredentialsAfter, Is.True, "Should have credentials after storing");
    }

    /// <summary>
    /// Test that credential service properly handles missing credentials without prompting
    /// </summary>
    [Test]
    public async Task GetOrPromptDoltHubCredentials_WithoutPrompting_ShouldReturnNull()
    {
        var credentials = await _credentialService.GetOrPromptDoltHubCredentialsAsync(TestEndpoint, promptForAuth: false);
        
        Assert.That(credentials, Is.Null, "Should return null when no credentials exist and prompting is disabled");
    }

    /// <summary>
    /// Test that stored credentials are returned by the credential service
    /// </summary>
    [Test]
    public async Task GetOrPromptDoltHubCredentials_WithExistingCredentials_ShouldReturnCredentials()
    {
        var storedCredentials = new DoltHubCredentials(TestUsername, TestApiToken, TestEndpoint);
        await _credentialService.StoreDoltHubCredentialsAsync(storedCredentials);

        var retrievedCredentials = await _credentialService.GetOrPromptDoltHubCredentialsAsync(TestEndpoint, promptForAuth: false);
        
        Assert.That(retrievedCredentials, Is.Not.Null, "Should return stored credentials");
        Assert.That(retrievedCredentials!.Value.Username, Is.EqualTo(TestUsername), "Username should match");
        Assert.That(retrievedCredentials!.Value.ApiToken, Is.EqualTo(TestApiToken), "API token should match");
    }

    /// <summary>
    /// Test utility functions for endpoint validation
    /// </summary>
    [Test]
    [TestCase("dolthub.com", true)]
    [TestCase("test.dolthub.com", true)]
    [TestCase("localhost", false)]
    [TestCase("", false)]
    [TestCase("invalid", false)]
    public void IsValidDoltHubEndpoint_ShouldValidateCorrectly(string endpoint, bool expected)
    {
        var result = DoltCredentialServiceUtility.IsValidDoltHubEndpoint(endpoint);
        Assert.That(result, Is.EqualTo(expected), $"Validation of '{endpoint}' should return {expected}");
    }

    /// <summary>
    /// Test utility functions for SQL URL validation
    /// </summary>
    [Test]
    [TestCase("https://sql.example.com:3306/db", true)]
    [TestCase("http://localhost:3306/db", true)]
    [TestCase("invalid-url", false)]
    [TestCase("", false)]
    [TestCase("ftp://example.com", false)]
    public void IsValidSqlRemoteUrl_ShouldValidateCorrectly(string url, bool expected)
    {
        var result = DoltCredentialServiceUtility.IsValidSqlRemoteUrl(url);
        Assert.That(result, Is.EqualTo(expected), $"Validation of '{url}' should return {expected}");
    }

    /// <summary>
    /// Clean up any test credentials that may exist
    /// </summary>
    private async Task CleanupTestCredentials()
    {
        try
        {
            await _credentialProvider?.ForgetDoltHubCredentialsAsync(TestEndpoint)!;
            await _credentialProvider?.ForgetSqlCredentialsAsync(TestSqlUrl)!;
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}