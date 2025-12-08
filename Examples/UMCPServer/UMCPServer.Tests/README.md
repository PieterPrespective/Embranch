# UMCPServer Tests

This project contains tests for the UMCPServer, including unit tests and integration tests that use a multi-step IEnumerator pattern.

## Test Structure

- **UnitTests**: Contains basic unit tests that verify individual components in isolation.
- **IntegrationTests**: Contains integration tests that test multiple components working together, using the IEnumerator pattern.
  - **UnityBridge**: Contains real integration tests that establish actual UMCP Bridge connections with Unity3D (no mocking).

## IEnumerator-Based Integration Tests

The integration tests in this project use an IEnumerator-based approach to create multi-step tests with the following benefits:

1. **Improved Readability**: Each step of the test is clearly separated
2. **Sequential Execution**: Steps are executed in a defined order
3. **Logical Grouping**: Related assertions are grouped in steps
4. **Self-Documentation**: Each step can be labeled for clarity
5. **Async Support**: Easily integrates with asynchronous operations

## Unity Bridge Integration Tests

The UnityBridge folder contains special integration tests that create real connections with Unity3D:

1. **UMCPBridgeIntegrationTest**: Automatically starts Unity in headless mode and connects to it
2. **UMCPBridgeRealConnectionTest**: Connects to an already running Unity instance with UMCP Client

These tests verify the actual UMCP Bridge functionality without any mocking. See the UnityBridge/README.md for detailed instructions.

## Running the Tests

### Using the Command Line

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=Unit"

# Run tests for a specific tool
dotnet test --filter "FullyQualifiedName~GetServerVersionTool"

# Run Unity Bridge integration tests
dotnet test --filter "FullyQualifiedName~UMCPBridge"

# Run only tests that require Unity to be running
dotnet test --filter "Category=RequiresUnity"

# Run with detailed output
dotnet test -v n
```

### Using Visual Studio

1. Open the solution in Visual Studio
2. Use the Test Explorer window (View > Test Explorer)
3. Select tests to run and click "Run"
4. You can group tests by category, namespace, or other criteria

## Writing New Tests

### Unit Tests

Create a new class in the `UnitTests` folder:

```csharp
[TestFixture]
public class YourToolTests
{
    [Test]
    public void YourMethod_WhenCondition_ShouldBehavior()
    {
        // Arrange
        
        // Act
        
        // Assert
    }
}
```

### Integration Tests with IEnumerator

Create a new class in the `IntegrationTests` folder:

```csharp
[TestFixture]
public class YourToolIntegrationTests : IntegrationTestBase
{
    [Test]
    public void YourMethod_WhenCondition_ShouldBehavior()
    {
        ExecuteTestSteps(YourTestSteps());
        Assert.That(TestCompleted, Is.True);
    }
    
    private IEnumerator YourTestSteps()
    {
        // Step 1: Setup
        Console.WriteLine($"Step {CurrentStep + 1}: Setting up...");
        // Setup code
        yield return null;
        
        // Step 2: Action
        Console.WriteLine($"Step {CurrentStep + 1}: Executing...");
        // Action code that might return a Task
        Task<object> resultTask = SomeAsyncMethod();
        yield return resultTask;
        
        // Step 3: Assertion
        Console.WriteLine($"Step {CurrentStep + 1}: Verifying...");
        var result = resultTask.Result;
        Assert.That(result, Is.Not.Null);
        yield return null;
    }
}
```

## When to Use Each Testing Approach

**Unit Tests**:
- Testing a single component in isolation
- Fast execution is important
- Testing simple logic with few dependencies

**IEnumerator Integration Tests**:
- Testing complex workflows with multiple steps
- Testing interaction between multiple components
- When the test has a clear sequence of operations
- When you want to make the test steps clear and readable

## Mock Guidelines

This test suite uses Moq for mocking dependencies:

```csharp
// Create a mock logger
var mockLogger = new Mock<ILogger<YourTool>>();

// Setup mock behavior
mockService.Setup(m => m.SomeMethod(It.IsAny<string>()))
    .Returns("mocked result");

// Verify mock interactions
mockService.Verify(m => m.SomeMethod(It.IsAny<string>()), Times.Once);
```