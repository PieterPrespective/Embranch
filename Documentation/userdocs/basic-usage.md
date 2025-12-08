# Basic Usage of DMMS

Once you have DMMS configured in Claude Desktop, you can start using its tools to manage Dolt databases.

## Available Tools

DMMS provides the following tools through the MCP interface:

### GetServerVersion
Returns the current version of the DMMS server.

**Example usage in Claude:**
```
"What version of DMMS is running?"
```

### Future Tools (To be implemented)
- **ExecuteDoltCommand**: Execute Dolt CLI commands
- **QueryDatabase**: Run SQL queries on Dolt databases
- **ManageDatabase**: Create, clone, and manage Dolt databases
- **BranchOperations**: Manage Dolt branches
- **CommitOperations**: Create and manage commits

## Basic Workflow Examples

### 1. Checking Server Status

Ask Claude:
```
"Can you check if the DMMS server is running and what version it is?"
```

Claude will use the GetServerVersion tool to provide this information.

### 2. Database Operations (Future)

Once additional tools are implemented, you'll be able to:

**Create a new database:**
```
"Create a new Dolt database called 'my_project'"
```

**Clone an existing database:**
```
"Clone the database from dolthub.com/myorg/mydb"
```

**Query data:**
```
"Show me all tables in the current database"
"Select the first 10 rows from the users table"
```

## Working with Multiple Databases

DMMS is designed to manage multiple Dolt databases simultaneously. You can:

1. **Switch between databases**: Specify which database to work with
2. **Compare databases**: View differences between databases
3. **Sync databases**: Keep multiple databases in sync

## Best Practices

### 1. Always Verify Connection
Before starting work, verify the DMMS connection:
```
"Is DMMS connected and ready?"
```

### 2. Use Descriptive Requests
Be specific about what you want to accomplish:
- ✅ Good: "Create a new Dolt database named 'inventory' with a products table"
- ❌ Vague: "Make a database"

### 3. Check Operation Results
After performing operations, verify the results:
```
"Did the last operation complete successfully? Show me the current database status."
```

## Understanding MCP Tools

MCP (Model Context Protocol) tools are functions that Claude can execute on your behalf. When you ask Claude to perform database operations, it:

1. Interprets your request
2. Selects the appropriate DMMS tool
3. Executes the tool with proper parameters
4. Returns the results to you

## Error Handling

If you encounter errors:

1. **Connection errors**: Claude will inform you if DMMS is not responding
2. **Operation errors**: Claude will report any errors from database operations
3. **Permission errors**: Ensure DMMS has proper permissions for database operations

## Getting Help

If you need help with specific operations:

1. Ask Claude for available tools: "What DMMS tools are available?"
2. Request examples: "Show me an example of creating a Dolt database"
3. Check documentation: Refer to the [Advanced Usage](advanced-usage.md) guide

## Next Steps

- Learn about [Advanced Configuration](advanced-configuration.md)
- Explore [Database Management](database-management.md)
- Read about [Security Best Practices](security.md)