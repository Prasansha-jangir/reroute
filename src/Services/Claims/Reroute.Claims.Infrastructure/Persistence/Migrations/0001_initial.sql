-- Claims service schema: event store (write side), read models (query side), outbox/inbox (messaging).
IF SCHEMA_ID('claims') IS NULL EXEC('CREATE SCHEMA claims');
GO

-- ===== Event store =====================================================================
-- Append-only. GlobalPosition is the clustered key (sequential inserts, cheap "read all after N").
-- UQ (StreamId, StreamVersion) IS the optimistic-concurrency guard.
CREATE TABLE claims.Events (
    GlobalPosition BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Events PRIMARY KEY CLUSTERED,
    StreamId       UNIQUEIDENTIFIER     NOT NULL,
    StreamVersion  INT                  NOT NULL,
    EventType      NVARCHAR(200)        NOT NULL,
    Data           NVARCHAR(MAX)        NOT NULL,
    Metadata       NVARCHAR(MAX)        NOT NULL,
    OccurredAt     DATETIMEOFFSET(3)    NOT NULL,
    CONSTRAINT UQ_Events_Stream UNIQUE NONCLUSTERED (StreamId, StreamVersion),
    CONSTRAINT CK_Events_Data_IsJson CHECK (ISJSON(Data) = 1)
);
GO

CREATE TABLE claims.Snapshots (
    StreamId      UNIQUEIDENTIFIER  NOT NULL CONSTRAINT PK_Snapshots PRIMARY KEY,
    StreamVersion INT               NOT NULL,
    Data          NVARCHAR(MAX)     NOT NULL,
    CreatedAt     DATETIMEOFFSET(3) NOT NULL
);
GO

-- ===== Read model ======================================================================
CREATE TABLE claims.ClaimSummaries (
    ClaimId            UNIQUEIDENTIFIER  NOT NULL CONSTRAINT PK_ClaimSummaries PRIMARY KEY CLUSTERED,
    TravellerId        NVARCHAR(64)      NOT NULL,
    FlightNumber       VARCHAR(8)        NOT NULL,
    FlightDate         DATE              NOT NULL,
    Origin             CHAR(3)           NOT NULL,
    Destination        CHAR(3)           NOT NULL,
    DisruptionType     VARCHAR(20)       NOT NULL,
    Status             VARCHAR(24)       NOT NULL,
    CompensationAmount DECIMAL(19,4)     NOT NULL CONSTRAINT DF_ClaimSummaries_Amount DEFAULT 0,
    Currency           CHAR(3)           NOT NULL CONSTRAINT DF_ClaimSummaries_Currency DEFAULT 'EUR',
    EligibilityReason  NVARCHAR(400)     NOT NULL CONSTRAINT DF_ClaimSummaries_Reason DEFAULT '',
    DistanceKm         FLOAT             NOT NULL CONSTRAINT DF_ClaimSummaries_Distance DEFAULT 0,
    Version            INT               NOT NULL,
    FiledAt            DATETIMEOFFSET(3) NOT NULL,
    UpdatedAt          DATETIMEOFFSET(3) NOT NULL
);
GO

-- "My claims, newest first" — covering index: one seek, zero key lookups.
CREATE NONCLUSTERED INDEX IX_ClaimSummaries_Traveller
    ON claims.ClaimSummaries (TravellerId, FiledAt DESC)
    INCLUDE (Status, FlightNumber, FlightDate, CompensationAmount, Currency);
GO

-- Ops work queue — filtered index stays tiny (only claims waiting on someone).
CREATE NONCLUSTERED INDEX IX_ClaimSummaries_Active
    ON claims.ClaimSummaries (Status, UpdatedAt)
    WHERE Status IN ('SubmittedToAirline', 'RejectedByAirline', 'Escalated');
GO

-- PII kept OUT of the immutable event stream so it can be erased (GDPR Art. 17).
CREATE TABLE claims.TravellerPersonalData (
    TravellerId NVARCHAR(64)  NOT NULL CONSTRAINT PK_TravellerPersonalData PRIMARY KEY,
    FullName    NVARCHAR(200) NULL,
    Email       NVARCHAR(320) NULL,
    Iban        VARCHAR(34)   NULL
);
GO

-- ===== Messaging =======================================================================
CREATE TABLE claims.OutboxMessages (
    Id          UNIQUEIDENTIFIER  NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY NONCLUSTERED,
    Topic       VARCHAR(200)      NOT NULL,
    MessageKey  VARCHAR(200)      NOT NULL,
    Type        VARCHAR(200)      NOT NULL,
    Payload     NVARCHAR(MAX)     NOT NULL,
    TraceParent VARCHAR(55)       NULL,
    OccurredAt  DATETIMEOFFSET(3) NOT NULL,
    ProcessedAt DATETIME2(3)      NULL
);
GO

CREATE CLUSTERED INDEX CIX_OutboxMessages_OccurredAt ON claims.OutboxMessages (OccurredAt);
GO

-- Filtered: only pending rows → the publisher's poll query stays O(pending), not O(history).
CREATE NONCLUSTERED INDEX IX_OutboxMessages_Pending
    ON claims.OutboxMessages (OccurredAt) INCLUDE (Topic, MessageKey, Type)
    WHERE ProcessedAt IS NULL;
GO

CREATE TABLE claims.InboxMessages (
    MessageId   UNIQUEIDENTIFIER NOT NULL,
    Consumer    VARCHAR(100)     NOT NULL,
    ProcessedAt DATETIME2(3)     NOT NULL,
    CONSTRAINT PK_InboxMessages PRIMARY KEY (MessageId, Consumer)
);
GO
