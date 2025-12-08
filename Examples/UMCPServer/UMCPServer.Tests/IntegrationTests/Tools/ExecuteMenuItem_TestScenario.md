# ExecuteMenuItem Integration Test Scenario

This document describes a comprehensive integration test scenario for the ExecuteMenuItem tool in the UMCP MCP Server.

## Test Overview

The test demonstrates the complete workflow of:
1. Creating a Unity script with a custom menu item
2. Executing the menu item via the MCP Server
3. Verifying that the menu item performed its action (creating a file)
4. Cleaning up the test artifacts

## Prerequisites

1. Unity Editor must be running with the UMCP Unity Client project open
2. UMCP Bridge must be active (should start automatically)
3. UMCP MCP Server must be running

## Test Steps

### Step 1: Create Test Script

Create a Unity script that adds a custom menu item. The script should be placed in the Unity project's Assets folder.

**Script Name:** `TestMenuItemCreator.cs`

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;

public class TestMenuItemCreator : MonoBehaviour
{
    [MenuItem("Test/Create Test File")]
    private static void CreateTestFile()
    {
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string filePath = Path.Combine(projectPath, "TestMenuItemOutput.txt");
        
        // Create a test file with timestamp
        string content = $"Test file created by menu item at: {System.DateTime.Now}\n";
        content += $"Unity Version: {Application.unityVersion}\n";
        content += $"Project Path: {projectPath}";
        
        File.WriteAllText(filePath, content);
        
        Debug.Log($"Test file created at: {filePath}");
        
        // Show a dialog to confirm
        EditorUtility.DisplayDialog("Test Complete", 
            $"Test file created successfully at:\n{filePath}", "OK");
        
        // Refresh the asset database
        AssetDatabase.Refresh();
    }
    
    [MenuItem("Test/Delete Test File")]
    private static void DeleteTestFile()
    {
        string projectPath = Application.dataPath.Replace("/Assets", "");
        string filePath = Path.Combine(projectPath, "TestMenuItemOutput.txt");
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            Debug.Log($"Test file deleted: {filePath}");
            EditorUtility.DisplayDialog("Cleanup Complete", 
                "Test file deleted successfully", "OK");
        }
        else
        {
            Debug.LogWarning("Test file not found");
            EditorUtility.DisplayDialog("File Not Found", 
                "Test file does not exist", "OK");
        }
        
        AssetDatabase.Refresh();
    }
}
```

### Step 2: Execute Menu Item via MCP

Using the MCP client, execute the following command:

```json
{
  "method": "tools/call",
  "params": {
    "name": "ExecuteMenuItem",
    "arguments": {
      "action": "execute",
      "menuPath": "Test/Create Test File"
    }
  }
}
```

### Step 3: Verify File Creation

Check the Unity project root directory for `TestMenuItemOutput.txt`. The file should contain:
- Timestamp of creation
- Unity version
- Project path

### Step 4: Cleanup

Execute the cleanup menu item:

```json
{
  "method": "tools/call",
  "params": {
    "name": "ExecuteMenuItem",
    "arguments": {
      "action": "execute",
      "menuPath": "Test/Delete Test File"
    }
  }
}
```

## Automated Test Implementation

The integration test in `ExecuteMenuItemToolTests.cs` simulates this workflow:

```csharp
[Test]
public void ExecuteMenuItem_WithCustomScriptMenuItem_ShouldCreateFile()
{
    // This test simulates the workflow described above
    // It mocks the Unity connection and verifies the file operations
}
```

## Expected Results

1. **Menu Item Execution**: The MCP Server should successfully forward the command to Unity
2. **File Creation**: A text file should be created at the project root
3. **Unity Feedback**: Unity console should log the file creation
4. **Cleanup**: The file should be successfully deleted

## Error Scenarios

The test also covers these error scenarios:

1. **Unity Not Connected**: Should return appropriate error message
2. **Invalid Menu Path**: Should report that the menu item was not found
3. **Missing Parameters**: Should validate required parameters

## Running the Full Integration Test

To run the complete integration test with a real Unity connection:

```bash
# From the test project directory
dotnet test --filter "FullyQualifiedName~ExecuteMenuItemRealUnityTest"
```

## Notes

- The menu item blacklist in `ExecuteMenuItem.cs` prevents execution of potentially dangerous menu items
- Menu items are executed asynchronously using `EditorApplication.delayCall`
- The test verifies both the MCP Server functionality and the Unity-side execution
