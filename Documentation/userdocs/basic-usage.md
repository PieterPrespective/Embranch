# Basic Usage Guide

This guide covers the essential workflows for using the VM RAG MCP Server with Claude. You'll learn how to manage your version-controlled knowledge base, add documents, search for information, and collaborate with teams.

## First Steps

### Verify Connection

Before starting, make sure Claude can access the VM RAG server:

**Ask Claude:** "What version of the VM RAG server is running?"

✅ **Expected response:** Version information (e.g., "VM RAG MCP Server v1.0.0")  
❌ **Problem:** "I don't have access to those tools" → See [Troubleshooting Guide](troubleshooting.md)

### Check Your Setup

**Ask Claude:** "What's the status of my knowledge base?"

This will show:
- Current branch and commit
- Any uncommitted changes
- Remote connection status

---

## Core Workflows

### 🏁 Starting Fresh (New Knowledge Base)

**Initialize a new knowledge base:**
```
Claude: "Initialize a new knowledge base for my project"
```

**What happens:**
1. Creates a new Dolt repository for version control
2. Sets up ChromaDB for document storage  
3. Creates an initial commit
4. Optionally connects to DoltHub for sharing

### 🤝 Joining a Team (Clone Existing)

**Clone a team knowledge base:**
```
Claude: "Clone the knowledge base from myorg/team-kb"
```

**What happens:**
1. Downloads the knowledge base from DoltHub
2. Sets up local ChromaDB with team's documents
3. Configures remote tracking

---

## Document Management

### ➕ Adding Documents

**Add a single document:**
```
Claude: "Add this document to my knowledge base:

[Paste your content here]

Please include metadata: title='API Authentication Guide', category='documentation', version='1.0'"
```

**Add multiple documents:**
```
Claude: "Add these API documentation files to the knowledge base:

1. Authentication: [content]
2. User Management: [content]  
3. Data Access: [content]

Tag them all as 'api-docs' and 'v2.0'"
```

### 🔍 Finding Information

**Simple search:**
```
Claude: "How do I authenticate with the API?"
Claude: "Find information about error handling"
Claude: "What documentation do we have about user permissions?"
```

**Advanced search with filters:**
```
Claude: "Find all API documentation from version 2.0"
Claude: "Show me meeting notes from last month"
Claude: "Search for documents tagged with 'security' that mention 'authentication'"
```

### ✏️ Updating Documents

**Update content:**
```
Claude: "Update the authentication guide with this new section: [content]"
```

**Update metadata:**
```
Claude: "Mark the old API docs as deprecated and set version to '1.0-deprecated'"
```

### 🗑️ Removing Content

**Delete specific documents:**
```
Claude: "Delete the outdated user guide from 2023"
```

**Remove by criteria:**
```
Claude: "Remove all documents marked as 'draft' that are older than 30 days"
```

---

## Version Control Workflows

### 💾 Saving Your Work

**Commit changes:**
```
Claude: "Save my current changes with the message 'Added new API documentation'"
```

**What to commit:** Always commit after:
- Adding new documents
- Updating existing content
- Removing outdated information
- Before switching branches
- Before pulling team changes

### 🔄 Team Collaboration

**Get latest changes:**
```
Claude: "Pull the latest updates from the team"
```

**Share your work:**
```
Claude: "Push my changes to share with the team"
```

**Check what teammates have done:**
```
Claude: "Show me recent commits from the team"
Claude: "What changed in the last week?"
```

### 🌿 Working with Branches

**Create a feature branch:**
```
Claude: "Create a new branch called 'api-v3-docs' for the new API documentation"
```

**Switch between branches:**
```
Claude: "Switch to the main branch"
Claude: "Switch to feature/api-v3-docs"
```

**Important:** When you switch branches, the documents visible in ChromaDB change to match that branch's content.

---

## Daily Workflows

### 🌅 Starting Your Day

1. **Check status and get updates:**
   ```
   Claude: "What's the status of my knowledge base?"
   Claude: "Pull any new changes from the team"
   ```

2. **See what's available:**
   ```
   Claude: "List my document collections"
   Claude: "How many documents do we have in total?"
   ```

### 📝 During Work Sessions

1. **Find information:**
   ```
   Claude: "Find documentation about [topic]"
   ```

2. **Add new content:**
   ```
   Claude: "Add this meeting summary to the knowledge base..."
   ```

3. **Save periodically:**
   ```
   Claude: "Commit my changes with message 'Added meeting notes and updated project status'"
   ```

### 🌆 End of Day

1. **Review what you've done:**
   ```
   Claude: "Show me what I've changed today"
   ```

2. **Save and share:**
   ```
   Claude: "Commit all my changes with message 'End of day updates'"
   Claude: "Push my changes to the team"
   ```

---

## Working with Collections

### Understanding Collections

**Collections** organize your documents by topic or purpose:
- `vmrag_main` - Default collection for general documents
- `api_docs` - API documentation
- `meeting_notes` - Meeting recordings and summaries
- `research` - Research papers and findings

### Collection Management

**List what you have:**
```
Claude: "List all my collections"
Claude: "Show me document counts for each collection"
```

**Create focused collections:**
```
Claude: "Create a collection called 'customer_feedback' for storing customer interviews and surveys"
```

**View collection contents:**
```
Claude: "Show me some sample documents from the api_docs collection"
```

---

## Metadata and Organization

### Using Metadata Effectively

Good metadata makes finding documents easier:

```json
{
  "title": "User Authentication API",
  "category": "api-documentation", 
  "version": "2.1",
  "status": "current",
  "last_updated": "2025-12-16",
  "tags": ["authentication", "security", "api"],
  "author": "engineering-team",
  "review_date": "2026-03-15"
}
```

### Organizing Tips

1. **Consistent naming:**
   - Use standardized categories: `documentation`, `meeting-notes`, `research`
   - Standardized statuses: `draft`, `review`, `approved`, `deprecated`

2. **Useful tags:**
   - Technical: `api`, `security`, `deployment`, `troubleshooting`
   - Organizational: `quarterly-review`, `onboarding`, `process`
   - Priority: `critical`, `important`, `reference`

3. **Date formatting:**
   - Use ISO format: `2025-12-16`
   - Include review cycles for keeping content fresh

---

## Search Strategies

### Effective Queries

**Natural language works best:**
```
✅ Good: "How do I set up SSL certificates for the API?"
✅ Good: "What are the steps for user onboarding?"
✅ Good: "Find troubleshooting guides for database connection issues"
```

**Combine search with filters:**
```
Claude: "Find API documentation from version 2.0 that mentions authentication"
Claude: "Show me meeting notes from Q4 2024 about budget planning"
Claude: "Search for troubleshooting guides that are marked as 'approved'"
```

### When You Can't Find Something

1. **Broaden your search:**
   ```
   Instead of: "API v2.1 authentication with OAuth"
   Try: "API authentication" or "OAuth setup"
   ```

2. **Check different collections:**
   ```
   Claude: "Search all collections for information about [topic]"
   ```

3. **Look at document lists:**
   ```
   Claude: "Show me all documents tagged with 'authentication'"
   ```

4. **Check other branches:**
   ```
   Claude: "Switch to the development branch and search for [topic]"
   ```

---

## Handling Conflicts and Problems

### Uncommitted Changes

**Issue:** "UNCOMMITTED_CHANGES" error when trying to pull/switch branches

**Solution:**
```
Claude: "I have uncommitted changes. Commit them first, then pull the latest updates"
```

### Merge Conflicts

**Issue:** Conflicting changes when pulling

**What Claude will tell you:** There are conflicts that need manual resolution

**Solution:** Work with your team to resolve conflicts or reset to a known good state:
```
Claude: "Reset my local changes and use the team's version"
```

### Lost Documents

**Issue:** Documents seem to have disappeared

**Common causes:**
1. **Branch switch:** Documents may be on a different branch
2. **Pull changes:** Team may have moved/deleted documents
3. **Uncommitted changes reset:** Local changes were discarded

**Debugging:**
```
Claude: "What branch am I on?"
Claude: "Show me recent commits to see what changed"
Claude: "Switch to main branch and check if documents are there"
```

---

## Best Practices

### 📁 Organization

1. **Use clear document titles** in metadata
2. **Tag consistently** across your team
3. **Set up review cycles** for keeping content current
4. **Use descriptive commit messages** for easy history browsing

### 🔄 Workflow

1. **Pull before you start work** each day
2. **Commit frequently** with descriptive messages
3. **Push regularly** to keep team in sync
4. **Use branches** for experimental documentation

### 🤝 Team Collaboration

1. **Establish metadata standards** with your team
2. **Agree on collection organization** 
3. **Use consistent naming conventions**
4. **Document your documentation process** (meta!)

### 🔐 Data Safety

1. **Commit before major operations** (branch switches, pulls)
2. **Push important changes** for backup
3. **Test searches** to verify content is accessible
4. **Keep local backups** of critical documents

---

## Common Questions

**Q: How do I know if my changes were saved?**
A: Ask Claude: "What's my repository status?" to see uncommitted changes.

**Q: Can I work offline?**
A: Yes! You can add/edit documents and search locally. Sync with team when online.

**Q: What happens when I switch branches?**
A: ChromaDB content changes to match the branch. Your documents from other branches are safe in version control.

**Q: How do I share documents with someone not on my team?**
A: Commit and push changes, then share the DoltHub repository URL.

**Q: Can I export my documents?**
A: Yes, ask Claude to show you documents and copy the content, or use repository cloning.

**Q: What if I accidentally delete something important?**
A: If it was committed, you can recover from version history. If not committed, you may need to recreate it.

---

## Next Steps

### Explore Advanced Features

- **[Tools Reference](tools-reference.md)** - Complete guide to all available tools
- **[Configuration Guide](configuration.md)** - Advanced setup options
- **[Troubleshooting](troubleshooting.md)** - Solutions to common problems

### Integration Ideas

- **Documentation workflows:** Connect to your existing docs platform
- **Meeting automation:** Automatically add meeting transcripts  
- **Knowledge mining:** Import existing documentation for search
- **Team onboarding:** Create guided learning paths through your knowledge base

Remember: The VM RAG MCP Server combines the power of semantic search (ChromaDB) with version control (Dolt) to give you a knowledge base that's both searchable and collaborative!