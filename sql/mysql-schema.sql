CREATE TABLE IF NOT EXISTS Productions
(
    ProductionId CHAR(36) NOT NULL,
    Prompt LONGTEXT NOT NULL,
    Style VARCHAR(256) NULL,
    TargetWordCount INT NOT NULL,
    DurationSeconds INT NULL,
    Status VARCHAR(64) NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    UpdatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (ProductionId)
);

CREATE TABLE IF NOT EXISTS ProductionEvents
(
    EventId CHAR(36) NOT NULL,
    ProductionId CHAR(36) NOT NULL,
    EventType VARCHAR(128) NOT NULL,
    CorrelationId CHAR(36) NOT NULL,
    CausationId CHAR(36) NULL,
    Producer VARCHAR(128) NOT NULL,
    SchemaVersion VARCHAR(32) NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    PayloadJson JSON NOT NULL,
    PRIMARY KEY (EventId),
    INDEX IX_ProductionEvents_ProductionId_CreatedAt (ProductionId, CreatedAt)
);

CREATE TABLE IF NOT EXISTS Artifacts
(
    ArtifactId CHAR(36) NOT NULL,
    ProductionId CHAR(36) NOT NULL,
    AgentRole VARCHAR(128) NOT NULL,
    ArtifactUri VARCHAR(2048) NOT NULL,
    MediaType VARCHAR(128) NOT NULL,
    Description VARCHAR(512) NULL,
    CreatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY (ArtifactId),
    INDEX IX_Artifacts_ProductionId_CreatedAt (ProductionId, CreatedAt)
);

CREATE TABLE IF NOT EXISTS AgentRuns
(
    AgentRunId CHAR(36) NOT NULL,
    ProductionId CHAR(36) NOT NULL,
    AgentRole VARCHAR(128) NOT NULL,
    ConsumedEventId CHAR(36) NOT NULL,
    PublishedEventId CHAR(36) NOT NULL,
    InputArtifactUri VARCHAR(2048) NULL,
    OutputArtifactUri VARCHAR(2048) NULL,
    Status VARCHAR(64) NOT NULL,
    StartedAt DATETIME(6) NOT NULL,
    CompletedAt DATETIME(6) NOT NULL,
    Notes LONGTEXT NULL,
    PRIMARY KEY (AgentRunId),
    INDEX IX_AgentRuns_ProductionId_StartedAt (ProductionId, StartedAt)
);
