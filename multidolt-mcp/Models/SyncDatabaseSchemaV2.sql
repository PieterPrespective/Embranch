-- ============================================
-- DOLT SYNC MANAGER DATABASE SCHEMA V2
-- Bidirectional sync with generalized document storage
-- Based on V2 Architecture Plan (December 15, 2025)
-- ============================================

-- ============================================
-- MIGRATION: Drop old rigid schema tables
-- ============================================
-- WARNING: This will delete all existing data
-- Ensure you have backups before running this migration

DROP TABLE IF EXISTS document_sync_log;
DROP TABLE IF EXISTS issue_logs;
DROP TABLE IF EXISTS knowledge_docs;
DROP TABLE IF EXISTS projects;

-- ============================================
-- CORE GENERALIZED TABLES
-- ============================================

-- Collections table - represents ChromaDB collections
CREATE TABLE IF NOT EXISTS collections (
    collection_name VARCHAR(255) PRIMARY KEY,
    display_name VARCHAR(255),
    description TEXT,
    embedding_model VARCHAR(100) DEFAULT 'default',
    chunk_size INT DEFAULT 512,
    chunk_overlap INT DEFAULT 50,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    document_count INT DEFAULT 0,
    metadata JSON,
    
    INDEX idx_created_at (created_at),
    INDEX idx_updated_at (updated_at)
);

-- Documents table - generalized document storage with JSON metadata
CREATE TABLE IF NOT EXISTS documents (
    doc_id VARCHAR(64) NOT NULL,
    collection_name VARCHAR(255) NOT NULL,
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    title VARCHAR(500),                    -- Extracted field for common queries
    doc_type VARCHAR(100),                 -- Extracted field for categorization
    metadata JSON NOT NULL,                -- ALL user fields preserved here
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    
    PRIMARY KEY (doc_id, collection_name),
    FOREIGN KEY (collection_name) REFERENCES collections(collection_name) ON DELETE CASCADE,
    INDEX idx_content_hash (content_hash),
    INDEX idx_title (title),
    INDEX idx_doc_type (doc_type),
    INDEX idx_created_at (created_at),
    INDEX idx_updated_at (updated_at),
    -- JSON index for common metadata queries (MySQL 8.0+)
    INDEX idx_metadata_project ((CAST(metadata->>'$.project_id' AS CHAR(36)))),
    INDEX idx_metadata_issue ((CAST(metadata->>'$.issue_number' AS UNSIGNED)))
);

-- ============================================
-- SYNC STATE TRACKING TABLES (Updated)
-- ============================================

-- PP13-69: chroma_sync_state table removed from Dolt schema
-- Sync state now stored in SQLite to avoid versioning conflicts during branch operations
-- See SqliteDeletionTracker.CreateDatabaseSchemaAsync() for the new sync_state table

-- Document sync log - tracks individual document sync operations
CREATE TABLE IF NOT EXISTS document_sync_log (
    id INT AUTO_INCREMENT PRIMARY KEY,
    doc_id VARCHAR(64) NOT NULL,
    collection_name VARCHAR(255) NOT NULL,
    content_hash CHAR(64) NOT NULL,
    chroma_chunk_ids JSON,                 -- Array of chunk IDs in ChromaDB
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    sync_direction ENUM('dolt_to_chroma', 'chroma_to_dolt') NOT NULL,  -- NEW
    sync_action ENUM('added', 'modified', 'deleted', 'staged') NOT NULL, -- Added 'staged'
    embedding_model VARCHAR(100),
    
    UNIQUE KEY uk_doc_collection (doc_id, collection_name),
    FOREIGN KEY (collection_name) REFERENCES collections(collection_name) ON DELETE CASCADE,
    INDEX idx_content_hash (content_hash),
    INDEX idx_synced_at (synced_at),
    INDEX idx_sync_direction (sync_direction)
);

-- ============================================
-- BIDIRECTIONAL SYNC SUPPORT TABLES (NEW)
-- ============================================

-- Local changes tracking - documents modified in ChromaDB but not yet staged to Dolt
CREATE TABLE IF NOT EXISTS local_changes (
    doc_id VARCHAR(64) NOT NULL,
    collection_name VARCHAR(255) NOT NULL,
    change_type ENUM('new', 'modified', 'deleted') NOT NULL,
    detected_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    content_hash_chroma CHAR(64),          -- Current hash in ChromaDB
    content_hash_dolt CHAR(64),            -- Last known hash in Dolt
    metadata JSON,                          -- Change metadata (e.g., user, reason)
    
    PRIMARY KEY (doc_id, collection_name),
    FOREIGN KEY (collection_name) REFERENCES collections(collection_name) ON DELETE CASCADE,
    INDEX idx_change_type (change_type),
    INDEX idx_detected_at (detected_at)
);

-- ============================================
-- OPERATION AUDIT LOG (Updated)
-- ============================================

CREATE TABLE IF NOT EXISTS sync_operations (
    operation_id INT AUTO_INCREMENT PRIMARY KEY,
    operation_type ENUM('init', 'commit', 'push', 'pull', 'merge', 'checkout', 'reset', 'stage') NOT NULL,
    dolt_branch VARCHAR(255) NOT NULL,
    dolt_commit_before VARCHAR(40),
    dolt_commit_after VARCHAR(40),
    chroma_collections_affected JSON,
    documents_added INT DEFAULT 0,
    documents_modified INT DEFAULT 0,
    documents_deleted INT DEFAULT 0,
    documents_staged INT DEFAULT 0,        -- NEW: For Chroma→Dolt staging
    chunks_processed INT DEFAULT 0,
    operation_status ENUM('started', 'completed', 'failed', 'rolled_back', 'blocked') NOT NULL,
    blocked_reason VARCHAR(255),           -- NEW: e.g., "local_changes_exist"
    error_message TEXT,
    started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME,
    metadata JSON,
    
    INDEX idx_operation_type (operation_type),
    INDEX idx_operation_status (operation_status),
    INDEX idx_started_at (started_at),
    INDEX idx_branch (dolt_branch)
);

-- ============================================
-- HELPER VIEWS (For easier querying)
-- ============================================

-- View for documents with pending local changes
CREATE OR REPLACE VIEW pending_local_changes AS
SELECT 
    lc.doc_id,
    lc.collection_name,
    lc.change_type,
    lc.detected_at,
    d.title,
    d.doc_type,
    d.metadata
FROM local_changes lc
LEFT JOIN documents d ON lc.doc_id = d.doc_id AND lc.collection_name = d.collection_name
ORDER BY lc.detected_at DESC;

-- View for sync status summary (PP13-69: simplified without chroma_sync_state)
CREATE OR REPLACE VIEW sync_status_summary AS
SELECT 
    c.collection_name,
    c.display_name,
    c.document_count,
    (SELECT COUNT(*) FROM local_changes WHERE collection_name = c.collection_name) as local_changes_count
FROM collections c;

-- ============================================
-- MIGRATION HELPERS
-- ============================================

-- Function to migrate existing issue_logs to documents table (if needed)
DELIMITER $$
CREATE PROCEDURE IF NOT EXISTS migrate_issue_logs_to_documents()
BEGIN
    -- This would contain migration logic if upgrading existing system
    -- For now, just a placeholder
    SELECT 'Migration procedure placeholder' AS status;
END$$
DELIMITER ;

-- Function to migrate existing knowledge_docs to documents table (if needed)
DELIMITER $$
CREATE PROCEDURE IF NOT EXISTS migrate_knowledge_docs_to_documents()
BEGIN
    -- This would contain migration logic if upgrading existing system
    -- For now, just a placeholder
    SELECT 'Migration procedure placeholder' AS status;
END$$
DELIMITER ;