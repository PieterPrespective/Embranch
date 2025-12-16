# Tools Reference Guide

This document provides a comprehensive reference for all tools available in the VM RAG MCP Server. Tools are organized into two main categories: **Document Operations** (ChromaDB) and **Version Control** (Dolt).

## Tool Categories

### Document Operations (ChromaDB Tools)
These tools handle document storage, search, and management. Use these for reading, writing, and organizing your knowledge base content.

### Version Control (Dolt Tools)  
These tools manage versioning, branching, and collaboration. Use these for saving changes, switching versions, and sharing with teams.

---

## Document Operations (ChromaDB Tools)

### Collection Management

#### chroma_list_collections
Lists all collections in the ChromaDB database.

**When to use:** To discover what collections exist or find the right collection to work with.

**Parameters:**
- `limit` *(optional)*: Maximum collections to return (default: 100)
- `offset` *(optional)*: Number to skip for pagination (default: 0)

**Example:**
```
User: "What knowledge bases do we have?"
Claude uses: chroma_list_collections()
```

**Response format:**
```json
{
  "collections": ["vmrag_main", "api_docs"],
  "total_count": 2
}
```

---

#### chroma_create_collection
Creates a new collection for storing documents.

**When to use:** Setting up a new knowledge base or organizing documents by topic.

**Parameters:**
- `collection_name` *(required)*: Name for the new collection
- `metadata` *(optional)*: Collection metadata (description, settings)
- `embedding_function` *(optional)*: Embedding function to use (default: "default")
- `get_or_create` *(optional)*: Return existing if already exists (default: false)

**Example:**
```
User: "Create a new collection for API documentation"
Claude uses: chroma_create_collection(
  collection_name="api_docs",
  metadata={"description": "REST API documentation"}
)
```

---

#### chroma_get_collection_info
Gets detailed information about a specific collection.

**When to use:** To understand a collection's configuration and settings.

**Parameters:**
- `collection_name` *(required)*: Name of the collection

**Example:**
```
User: "Tell me about the main knowledge base"
Claude uses: chroma_get_collection_info(collection_name="vmrag_main")
```

---

#### chroma_get_collection_count
Gets the number of documents in a collection.

**When to use:** Quick size check or before bulk operations.

**Parameters:**
- `collection_name` *(required)*: Name of the collection

**Example:**
```
User: "How many documents are in our knowledge base?"
Claude uses: chroma_get_collection_count(collection_name="vmrag_main")
```

---

#### chroma_peek_collection
Views a sample of documents from a collection.

**When to use:** To quickly see what content is stored or understand document structure.

**Parameters:**
- `collection_name` *(required)*: Name of the collection
- `limit` *(optional)*: Number of sample documents (default: 5, max: 20)

**Example:**
```
User: "Show me some examples from the knowledge base"
Claude uses: chroma_peek_collection(collection_name="vmrag_main", limit=3)
```

---

#### chroma_modify_collection
Updates a collection's name or metadata.

**When to use:** Renaming collections or updating descriptions.

**Parameters:**
- `collection_name` *(required)*: Current collection name
- `new_name` *(optional)*: New name for the collection
- `new_metadata` *(optional)*: New metadata to set

**Example:**
```
User: "Rename the api_docs collection to rest_api_v2"
Claude uses: chroma_modify_collection(
  collection_name="api_docs",
  new_name="rest_api_v2"
)
```

---

#### chroma_delete_collection
Deletes a collection and all its documents.

⚠️ **Warning:** This is destructive and cannot be undone!

**When to use:** Removing unused collections or cleaning up test data.

**Parameters:**
- `collection_name` *(required)*: Name of the collection to delete
- `confirm` *(required)*: Must be true to confirm deletion

**Example:**
```
User: "Delete the old test collection"
Claude: "This will permanently delete the collection and all its documents. Are you sure?"
User: "Yes, delete it"
Claude uses: chroma_delete_collection(collection_name="test_collection", confirm=true)
```

---

### Document Operations

#### chroma_add_documents
Adds new documents to a collection.

**When to use:** Adding new knowledge, importing documents, building up knowledge base.

**Parameters:**
- `collection_name` *(required)*: Name of the collection
- `documents` *(required)*: Array of document texts to add
- `ids` *(optional)*: Unique IDs for documents (auto-generated if not provided)
- `metadatas` *(optional)*: Array of metadata objects for each document

**Example:**
```
User: "Add this documentation to the knowledge base: [content]"
Claude uses: chroma_add_documents(
  collection_name="vmrag_main",
  documents=["# Authentication API\nThe auth endpoint..."],
  metadatas=[{
    "title": "Authentication API",
    "doc_type": "api_reference",
    "version": "1.0"
  }]
)
```

**Response:**
```json
{
  "success": true,
  "documents_added": 1,
  "ids": ["doc_a1b2c3d4"],
  "chunks_created": 3,
  "message": "Added 1 document (3 chunks). Use dolt_commit to save this change."
}
```

---

#### chroma_query_documents
**🎯 Primary RAG Tool** - Searches for documents using semantic similarity.

**When to use:** Finding relevant documents for questions, RAG retrieval, exploring content.

**Parameters:**
- `collection_name` *(required)*: Name of the collection to query
- `query_texts` *(required)*: Array of query strings to search for
- `n_results` *(optional)*: Number of results per query (default: 5)
- `where` *(optional)*: Metadata filter using ChromaDB operators
- `where_document` *(optional)*: Document content filter
- `include` *(optional)*: What to include in results (default: documents, metadatas, distances)

**Filter Examples:**
```javascript
// Simple metadata filter
where: {"doc_type": "api_reference"}

// Comparison filter
where: {"priority": {"$gt": 5}}

// Logical AND
where: {"$and": [{"status": "active"}, {"category": "auth"}]}

// Document content filter
where_document: {"$contains": "authentication"}
```

**Example:**
```
User: "How do I authenticate with the API?"
Claude uses: chroma_query_documents(
  collection_name="vmrag_main",
  query_texts=["how to authenticate with API"],
  n_results=5,
  where={"doc_type": "api_reference"}
)
```

**Response:**
```json
{
  "results": [{
    "query": "how to authenticate with API",
    "matches": [{
      "id": "doc_a1b2c3d4_chunk_0",
      "document": "# Authentication API\nThe auth endpoint accepts...",
      "metadata": {
        "title": "Authentication API",
        "doc_type": "api_reference"
      },
      "distance": 0.234
    }]
  }]
}
```

---

#### chroma_get_documents
Retrieves specific documents by ID or filter.

**When to use:** Getting specific documents you know exist, retrieving by metadata filter.

**Parameters:**
- `collection_name` *(required)*: Name of the collection
- `ids` *(optional)*: Array of document IDs to retrieve
- `where` *(optional)*: Metadata filter
- `where_document` *(optional)*: Document content filter
- `include` *(optional)*: What to include (default: documents, metadatas)
- `limit` *(optional)*: Maximum documents to return (default: 100)
- `offset` *(optional)*: Number to skip for pagination (default: 0)

**Example:**
```
User: "Get the document with ID doc_a1b2c3d4"
Claude uses: chroma_get_documents(
  collection_name="vmrag_main",
  ids=["doc_a1b2c3d4"]
)

User: "Show me all deprecated API docs"
Claude uses: chroma_get_documents(
  collection_name="vmrag_main",
  where={"status": "deprecated"}
)
```

---

#### chroma_update_documents
Updates existing documents' content or metadata.

**When to use:** Correcting document content, updating metadata, refreshing information.

**Parameters:**
- `collection_name` *(required)*: Name of the collection
- `ids` *(required)*: Array of document IDs to update
- `documents` *(optional)*: New document texts (must match ids length)
- `metadatas` *(optional)*: New metadata objects (replaces existing)
- `embeddings` *(optional)*: New embeddings (usually auto-generated)

**Example:**
```
User: "Mark the authentication doc as deprecated"
Claude uses: chroma_update_documents(
  collection_name="vmrag_main",
  ids=["doc_a1b2c3d4"],
  metadatas=[{
    "title": "Authentication API",
    "status": "deprecated",
    "deprecated_date": "2025-12-16"
  }]
)
```

---

#### chroma_delete_documents
Deletes specific documents from a collection.

**When to use:** Removing outdated documents, cleaning up incorrect entries.

**Parameters:**
- `collection_name` *(required)*: Name of the collection
- `ids` *(required)*: Array of document IDs to delete
- `where` *(optional)*: Delete documents matching filter instead of by IDs
- `where_document` *(optional)*: Delete by document content filter

**Example:**
```
User: "Delete the old authentication doc"
Claude uses: chroma_delete_documents(
  collection_name="vmrag_main",
  ids=["doc_a1b2c3d4"]
)

User: "Remove all deprecated documents"
Claude uses: chroma_delete_documents(
  collection_name="vmrag_main",
  where={"status": "deprecated"}
)
```

---

## Version Control (Dolt Tools)

### Status and Information

#### dolt_status
Gets the current version control status.

**⭐ Always call this first** when dealing with version control operations.

**When to use:** Before any operation to understand current state, checking for uncommitted changes.

**Parameters:**
- `verbose` *(optional)*: Include detailed list of changed documents (default: false)

**Example:**
```
User: "What's the current state of my knowledge base?"
Claude uses: dolt_status()
```

**Response:**
```json
{
  "success": true,
  "branch": "main",
  "commit": {
    "hash": "abc123def456...",
    "short_hash": "abc123d",
    "message": "Added API documentation",
    "author": "user@example.com",
    "timestamp": "2025-12-16T10:30:00Z"
  },
  "local_changes": {
    "has_changes": true,
    "summary": {
      "added": 2,
      "modified": 1,
      "deleted": 0,
      "total": 3
    }
  },
  "message": "On branch 'main' with 3 uncommitted changes"
}
```

---

#### dolt_branches
Lists available branches on the remote repository.

**When to use:** To see what branches exist before checkout, understanding project structure.

**Parameters:**
- `include_local` *(optional)*: Include local-only branches (default: true)
- `filter` *(optional)*: Filter by name pattern (supports * wildcard)

**Example:**
```
User: "What branches are available?"
Claude uses: dolt_branches()
```

---

#### dolt_commits
Lists commits on a specific branch.

**When to use:** To see history, find specific commits, understand changes over time.

**Parameters:**
- `branch` *(optional)*: Branch name (default: current branch)
- `limit` *(optional)*: Max commits to return (default: 20, max: 100)
- `offset` *(optional)*: Number to skip for pagination (default: 0)
- `since` *(optional)*: Only commits after this date (ISO 8601)
- `until` *(optional)*: Only commits before this date (ISO 8601)

**Example:**
```
User: "Show me the last 5 commits on main"
Claude uses: dolt_commits(branch="main", limit=5)
```

---

#### dolt_show
Shows detailed information about a specific commit.

**When to use:** To see what documents changed in a commit, review before checkout.

**Parameters:**
- `commit` *(required)*: Commit hash or reference (e.g., 'HEAD', 'abc123d')
- `include_diff` *(optional)*: Include content diff (default: false)
- `diff_limit` *(optional)*: Max documents to include diff for (default: 10)

**Example:**
```
User: "What changed in commit abc123d?"
Claude uses: dolt_show(commit="abc123d")
```

---

#### dolt_find
Searches for commits by hash pattern or message content.

**When to use:** Finding commits when you remember partial info, searching by topic.

**Parameters:**
- `query` *(required)*: Search query (matches hash prefix and message content)
- `search_type` *(optional)*: What to search - "all", "hash", "message" (default: "all")
- `branch` *(optional)*: Limit search to specific branch
- `limit` *(optional)*: Max results (default: 10)

**Example:**
```
User: "Find the commit where we added the API docs"
Claude uses: dolt_find(query="API docs", search_type="message")

User: "Find commit starting with abc1"
Claude uses: dolt_find(query="abc1", search_type="hash")
```

---

### Repository Setup

#### dolt_init
Initializes a new Dolt repository.

**When to use:** Starting a new knowledge base, adding version control to existing ChromaDB.

**Parameters:**
- `remote_url` *(optional)*: DoltHub remote URL (e.g., 'myorg/my-knowledge-base')
- `initial_branch` *(optional)*: Name of initial branch (default: 'main')
- `import_existing` *(optional)*: Import existing ChromaDB documents (default: true)
- `commit_message` *(optional)*: Message for initial commit (default: 'Initial import')

**Example:**
```
User: "Initialize a new knowledge base for my project"
Claude uses: dolt_init(
  remote_url="myorg/my-knowledge-base",
  import_existing=true
)
```

---

#### dolt_clone
Clones an existing Dolt repository from DoltHub.

**When to use:** Joining existing project, setting up on new machine, getting team's knowledge base.

**Parameters:**
- `remote_url` *(required)*: DoltHub repository URL
- `branch` *(optional)*: Branch to checkout after clone (default: repository default)
- `commit` *(optional)*: Specific commit to checkout (overrides branch)

**Example:**
```
User: "Clone the team knowledge base from DoltHub"
Claude uses: dolt_clone(remote_url="myteam/shared-knowledge")

User: "Clone and use the feature branch"
Claude uses: dolt_clone(
  remote_url="myteam/shared-knowledge",
  branch="feature/new-docs"
)
```

---

### Remote Synchronization

#### dolt_fetch
Fetches updates from remote without applying them.

**When to use:** To see what updates are available before pulling.

**Parameters:**
- `remote` *(optional)*: Remote name (default: 'origin')
- `branch` *(optional)*: Specific branch to fetch (default: all branches)

**Example:**
```
User: "Check if there are any updates available"
Claude uses: dolt_fetch()
```

---

#### dolt_pull
Fetches and merges changes from the remote.

**When to use:** Getting latest changes from team, updating local knowledge base.

**Parameters:**
- `remote` *(optional)*: Remote name (default: 'origin')
- `branch` *(optional)*: Remote branch to pull (default: current branch upstream)
- `if_uncommitted` *(optional)*: Action for uncommitted changes - "abort", "commit_first", "reset_first", "stash" (default: "abort")
- `commit_message` *(optional)*: Message if if_uncommitted="commit_first"

**Example:**
```
User: "Get the latest updates from the team"
Claude uses: dolt_pull()

# If uncommitted changes exist:
User: "Commit my changes first, then pull"
Claude uses: dolt_pull(
  if_uncommitted="commit_first",
  commit_message="WIP: My local changes"
)
```

---

#### dolt_push
Pushes local commits to the remote repository.

**When to use:** Sharing commits with team, backing up work to DoltHub.

**Parameters:**
- `remote` *(optional)*: Remote name (default: 'origin')
- `branch` *(optional)*: Branch to push (default: current branch)
- `set_upstream` *(optional)*: Set upstream tracking (default: true for new branches)
- `force` *(optional)*: Force push - ⚠️ **dangerous** (default: false)

**Example:**
```
User: "Push my changes to the team"
Claude uses: dolt_push()
```

---

### Local Operations

#### dolt_commit
Commits current ChromaDB state to the repository.

**When to use:** Saving your work as a version, before switching branches, before pulling.

**Parameters:**
- `message` *(required)*: Commit message describing the changes
- `author` *(optional)*: Author name/email (default: configured user)

**Example:**
```
User: "Save my current work"
Claude uses: dolt_commit(message="Added documentation for REST API endpoints")
```

**Response:**
```json
{
  "success": true,
  "commit": {
    "hash": "abc123def456...",
    "short_hash": "abc123d",
    "message": "Added documentation for REST API endpoints",
    "author": "user@example.com"
  },
  "changes_committed": {
    "added": 5,
    "modified": 2,
    "deleted": 0,
    "total": 7
  }
}
```

---

#### dolt_checkout
Switches to a different branch or commit.

**When to use:** Switching to different branch, viewing historical state, starting feature work.

⚠️ **Warning:** This changes what documents are visible in ChromaDB!

**Parameters:**
- `target` *(required)*: Branch name or commit hash to checkout
- `create_branch` *(optional)*: Create new branch with given name (default: false)
- `from` *(optional)*: Base for new branch (default: current HEAD)
- `if_uncommitted` *(optional)*: Action for uncommitted changes - "abort", "commit_first", "reset_first", "carry" (default: "abort")
- `commit_message` *(optional)*: Message if if_uncommitted="commit_first"

**Example:**
```
User: "Switch to the feature branch"
Claude uses: dolt_checkout(target="feature/new-docs")

User: "Create a new branch for my work"
Claude uses: dolt_checkout(
  target="feature/my-changes",
  create_branch=true
)

User: "Go back to how things were 3 commits ago"
Claude uses: dolt_checkout(target="HEAD~3")
```

---

#### dolt_reset
Resets to a specific commit, discarding local changes.

⚠️ **Warning:** This is destructive and discards uncommitted changes permanently!

**When to use:** Discarding all local changes, resetting to previous commit, syncing with remote.

**Parameters:**
- `target` *(optional)*: Commit to reset to - "HEAD", "origin/main", or commit hash (default: "HEAD")
- `confirm_discard` *(required)*: Must be true to confirm discarding changes

**Example:**
```
User: "Discard all my local changes"
Claude uses: dolt_reset(target="HEAD", confirm_discard=true)

User: "Reset to match the remote exactly"
Claude uses: dolt_reset(target="origin/main", confirm_discard=true)
```

---

## Common Workflows

### 📖 Daily Reading/Research Workflow
```
1. Query knowledge base: chroma_query_documents()
2. Get specific documents: chroma_get_documents()
3. Peek at collections: chroma_peek_collection()
```

### ✏️ Adding Content Workflow
```
1. Add documents: chroma_add_documents()
2. Check status: dolt_status()
3. Save work: dolt_commit()
4. Share: dolt_push()
```

### 🔄 Team Collaboration Workflow
```
1. Check status: dolt_status()
2. Get updates: dolt_pull()
3. Add/modify content: chroma_* tools
4. Save changes: dolt_commit()
5. Share: dolt_push()
```

### 🌿 Branch Workflow
```
1. See available branches: dolt_branches()
2. Create/switch branch: dolt_checkout(create_branch=true)
3. Work with documents: chroma_* tools
4. Save: dolt_commit()
5. Push branch: dolt_push()
6. Switch back: dolt_checkout(target="main")
```

### 🚨 Recovery Workflow
```
# Undo uncommitted changes:
1. dolt_reset(target="HEAD", confirm_discard=true)

# Reset to remote state:
1. dolt_fetch()
2. dolt_reset(target="origin/main", confirm_discard=true)

# Find old version:
1. dolt_find(query="before the problem")
2. dolt_show(commit="found_hash")
3. dolt_checkout(target="found_hash")
```

---

## Error Handling

### Common Error Codes

| Error Code | Description | Common Causes |
|------------|-------------|---------------|
| `NOT_INITIALIZED` | No Dolt repository | Use `dolt_init` or `dolt_clone` first |
| `UNCOMMITTED_CHANGES` | Local changes block operation | Commit, reset, or use `if_uncommitted` parameter |
| `COLLECTION_NOT_FOUND` | ChromaDB collection doesn't exist | Check collection name or create it first |
| `DOCUMENT_NOT_FOUND` | Document ID doesn't exist | Verify document ID or use query to find it |
| `REMOTE_UNREACHABLE` | Cannot connect to DoltHub | Check network and remote URL |
| `AUTHENTICATION_FAILED` | Not authorized for DoltHub | Run `dolt login` in terminal |

### Error Response Format
```json
{
  "success": false,
  "error": "ERROR_CODE",
  "message": "Human-readable error description",
  "suggestions": ["Suggested action 1", "Suggested action 2"]
}
```

---

## Best Practices

### Document Operations
1. **Use descriptive metadata** - Include title, type, category for better filtering
2. **Chunk large documents** - The system automatically chunks, but consider logical breaks
3. **Use semantic queries** - Ask natural questions rather than keyword searches
4. **Filter when possible** - Use `where` parameters to narrow search scope

### Version Control
1. **Commit frequently** - Small, focused commits are easier to understand
2. **Write clear commit messages** - Future you will thank present you
3. **Check status first** - Always run `dolt_status()` before other operations
4. **Pull before push** - Get latest changes before sharing your work
5. **Use branches for experiments** - Keep main branch stable

### Performance
1. **Limit query results** - Use `n_results` parameter appropriately
2. **Use pagination** - For large result sets, use `limit` and `offset`
3. **Monitor collection size** - Large collections may have slower operations
4. **Regular commits** - Don't let too many uncommitted changes accumulate

For more detailed troubleshooting and common issues, see the [Troubleshooting Guide](troubleshooting.md).