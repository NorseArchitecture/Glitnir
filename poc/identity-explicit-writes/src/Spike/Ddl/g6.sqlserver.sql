-- G6 scratch schema (SQL Server 2025): UserClaims in the EF-native IsTemporal() shape (visible period columns,
-- history table created by the engine from SYSTEM_VERSIONING = ON) with a PERSISTED claim value hash.
-- CAST(... AS binary(32)): bare HASHBYTES types the column varbinary(8000), which G6 shows separately.
-- Batches are separated by GO lines.

CREATE TABLE [dbo].[Users] (
	[Id] uniqueidentifier NOT NULL,
	CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [dbo].[UserClaims] (
	[Id] int IDENTITY(1, 1) NOT NULL,
	[UserId] uniqueidentifier NOT NULL,
	[ClaimType] nvarchar(256) NOT NULL,
	[ClaimValue] nvarchar(max) NOT NULL,
	[ClaimValueHash] AS CAST(HASHBYTES('SHA2_256', [ClaimValue]) AS binary(32)) PERSISTED NOT NULL,
	[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
	[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
	PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]),
	CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
	CONSTRAINT [FK_UserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UserClaimsHistory]));
GO

CREATE UNIQUE INDEX [IX_UserClaims_UserId_ClaimType_ClaimValueHash] ON [dbo].[UserClaims] ([UserId], [ClaimType], [ClaimValueHash]);
GO
