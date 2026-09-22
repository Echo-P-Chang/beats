IF OBJECT_ID(N'dbo.AgentRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AgentRuns
    (
        AgentRunId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AgentRuns PRIMARY KEY,
        ProductionId UNIQUEIDENTIFIER NOT NULL,
        AgentRole NVARCHAR(128) NOT NULL,
        ConsumedEventId UNIQUEIDENTIFIER NOT NULL,
        PublishedEventId UNIQUEIDENTIFIER NOT NULL,
        InputArtifactUri NVARCHAR(2048) NULL,
        OutputArtifactUri NVARCHAR(2048) NULL,
        Status NVARCHAR(64) NOT NULL,
        StartedAt DATETIMEOFFSET(7) NOT NULL,
        CompletedAt DATETIMEOFFSET(7) NOT NULL,
        Notes NVARCHAR(MAX) NULL
    );
END;

IF OBJECT_ID(N'dbo.Artifacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Artifacts
    (
        ArtifactId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Artifacts PRIMARY KEY,
        ProductionId UNIQUEIDENTIFIER NOT NULL,
        AgentRole NVARCHAR(128) NOT NULL,
        ArtifactUri NVARCHAR(2048) NOT NULL,
        MediaType NVARCHAR(128) NOT NULL,
        Description NVARCHAR(512) NULL,
        CreatedAt DATETIMEOFFSET(7) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.ProductionEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionEvents
    (
        EventId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProductionEvents PRIMARY KEY,
        ProductionId UNIQUEIDENTIFIER NOT NULL,
        EventType NVARCHAR(128) NOT NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        Producer NVARCHAR(128) NOT NULL,
        SchemaVersion NVARCHAR(32) NOT NULL,
        CreatedAt DATETIMEOFFSET(7) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.Productions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Productions
    (
        ProductionId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Productions PRIMARY KEY,
        Prompt NVARCHAR(MAX) NOT NULL,
        Style NVARCHAR(256) NULL,
        TargetWordCount INT NOT NULL,
        DurationSeconds INT NULL,
        Status NVARCHAR(64) NOT NULL,
        CreatedAt DATETIMEOFFSET(7) NOT NULL,
        UpdatedAt DATETIMEOFFSET(7) NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProductionEvents_ProductionId_CreatedAt')
BEGIN
    CREATE INDEX IX_ProductionEvents_ProductionId_CreatedAt
    ON dbo.ProductionEvents (ProductionId, CreatedAt);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Artifacts_ProductionId_CreatedAt')
BEGIN
    CREATE INDEX IX_Artifacts_ProductionId_CreatedAt
    ON dbo.Artifacts (ProductionId, CreatedAt);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentRuns_ProductionId_StartedAt')
BEGIN
    CREATE INDEX IX_AgentRuns_ProductionId_StartedAt
    ON dbo.AgentRuns (ProductionId, StartedAt);
END;
