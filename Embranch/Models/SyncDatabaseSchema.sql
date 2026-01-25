-- ============================================
-- DOLT SYNC MANAGER DATABASE SCHEMA
-- Tables for coordinating Dolt and ChromaDB synchronization
-- Based on Section 4.1 of Dolt Interface Implementation Plan
-- ============================================

-- Core document tables (these might already exist)
CREATE TABLE IF NOT EXISTS projects (
    project_id VARCHAR(36) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    repository_url VARCHAR(500),
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    metadata JSON
);

CREATE TABLE IF NOT EXISTS issue_logs (
    log_id VARCHAR(36) PRIMARY KEY,
    project_id VARCHAR(36) NOT NULL,
    issue_number INT NOT NULL,
    title VARCHAR(500),
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    log_type ENUM('investigation', 'implementation', 'resolution', 'postmortem') DEFAULT 'implementation',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    metadata JSON,
    
    FOREIGN KEY (project_id) REFERENCES projects(project_id),
    UNIQUE KEY uk_project_issue_type (project_id, issue_number, log_type),
    INDEX idx_content_hash (content_hash),
    INDEX idx_project_issue (project_id, issue_number)
);

CREATE TABLE IF NOT EXISTS knowledge_docs (
    doc_id VARCHAR(36) PRIMARY KEY,
    category VARCHAR(100) NOT NULL,
    tool_name VARCHAR(255) NOT NULL,
    tool_version VARCHAR(50),
    title VARCHAR(500) NOT NULL,
    content LONGTEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    metadata JSON,
    
    INDEX idx_content_hash (content_hash),
    INDEX idx_tool (tool_name, tool_version),
    INDEX idx_category (category)
);

-- ============================================
-- SYNC STATE TRACKING TABLES
-- ============================================

CREATE TABLE IF NOT EXISTS chroma_sync_state (
    collection_name VARCHAR(255) PRIMARY KEY,
    last_sync_commit VARCHAR(40),
    last_sync_at DATETIME,
    document_count INT DEFAULT 0,
    chunk_count INT DEFAULT 0,
    embedding_model VARCHAR(100),
    sync_status ENUM('synced', 'pending', 'error', 'in_progress') DEFAULT 'pending',
    error_message TEXT,
    metadata JSON
);

CREATE TABLE IF NOT EXISTS document_sync_log (
    id INT AUTO_INCREMENT PRIMARY KEY,
    source_table ENUM('issue_logs', 'knowledge_docs') NOT NULL,
    source_id VARCHAR(36) NOT NULL,
    content_hash CHAR(64) NOT NULL,
    chroma_collection VARCHAR(255) NOT NULL,
    chunk_ids JSON,
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    embedding_model VARCHAR(100),
    sync_action ENUM('added', 'modified', 'deleted') NOT NULL,
    
    UNIQUE KEY uk_source_collection (source_table, source_id, chroma_collection),
    INDEX idx_content_hash (content_hash),
    INDEX idx_collection (chroma_collection)
);

-- ============================================
-- OPERATION AUDIT LOG
-- ============================================

CREATE TABLE IF NOT EXISTS sync_operations (
    operation_id INT AUTO_INCREMENT PRIMARY KEY,
    operation_type ENUM('commit', 'push', 'pull', 'merge', 'checkout', 'reset') NOT NULL,
    dolt_branch VARCHAR(255) NOT NULL,
    dolt_commit_before VARCHAR(40),
    dolt_commit_after VARCHAR(40),
    chroma_collections_affected JSON,
    documents_added INT DEFAULT 0,
    documents_modified INT DEFAULT 0,
    documents_deleted INT DEFAULT 0,
    chunks_processed INT DEFAULT 0,
    operation_status ENUM('started', 'completed', 'failed', 'rolled_back') NOT NULL,
    error_message TEXT,
    started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME,
    metadata JSON
);