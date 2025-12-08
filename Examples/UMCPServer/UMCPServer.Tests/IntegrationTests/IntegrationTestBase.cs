using System.Collections;
using NUnit.Framework;

namespace UMCPServer.Tests.IntegrationTests;

/// <summary>
/// Base class for integration tests that use a multi-step IEnumerator pattern.
/// </summary>
public abstract class IntegrationTestBase
{
    /// <summary>
    /// The current test step being executed.
    /// </summary>
    protected int CurrentStep { get; private set; }
    
    /// <summary>
    /// Whether the test has completed all steps.
    /// </summary>
    protected bool TestCompleted { get; private set; }
    
    /// <summary>
    /// Setup method that runs before each test.
    /// </summary>
    [SetUp]
    public virtual void Setup()
    {
        CurrentStep = 0;
        TestCompleted = false;
    }
    
    /// <summary>
    /// Tear down method that runs after each test.
    /// </summary>
    [TearDown]
    public virtual void TearDown()
    {
        // Clean up any resources
    }
    
    /// <summary>
    /// The main test executor that runs through all the steps in the IEnumerator.
    /// </summary>
    /// <param name="testCoroutine">The test coroutine to execute.</param>
    protected void ExecuteTestSteps(IEnumerator testCoroutine)
    {
        Assert.That(testCoroutine, Is.Not.Null, "Test coroutine cannot be null");
        
        // Run through all steps in the enumerator
        while (testCoroutine.MoveNext())
        {
            CurrentStep++;
            
            // If the current value is a Task, we wait for it to complete
            if (testCoroutine.Current is Task task)
            {
                task.GetAwaiter().GetResult();
            }

            
            //if (testCoroutine.Current is YieldInstruction yieldInstruction)
            //{
            //    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            //    while (!yieldInstruction.IsDone && sw.ElapsedMilliseconds < yieldInstruction.TimeoutDuration)
            //    {
            //        // Wait for the yield instruction to complete
            //    }

            //    if(sw.ElapsedMilliseconds >= yieldInstruction.TimeoutDuration)
            //    {
            //        Assert.Fail($"Yield instruction timed out after {yieldInstruction.TimeoutDuration} ms at step {CurrentStep}");
            //    }
            //    sw.Stop();
            //}
            

            // If the current value is another IEnumerator, we run it as a nested sequence
            if (testCoroutine.Current is IEnumerator nestedEnumerator)
            {
                ExecuteTestSteps(nestedEnumerator);
            }
        }
        
        TestCompleted = true;
    }
}