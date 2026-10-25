-- Hand-written SQL Server 2025 schema for the identity-explicit-writes spike (spec §3, §3.1, §4.6, §6.1).
-- Users is engine-native system-versioned with HIDDEN period columns that are absent from the EF model,
-- the shape TemporalOnPrincipalTableOnly() will emit; the UserCredentials fragment gets nothing.
-- Batches are separated by GO lines; Program splits on them.

CREATE TABLE [dbo].[Users] (
	[Id] uniqueidentifier NOT NULL,
	[UserName] nvarchar(256) NOT NULL,
	[Email] nvarchar(256) NOT NULL,
	[EmailConfirmed] bit NOT NULL,
	[PhoneNumber] nvarchar(256) NULL,
	[PhoneNumberConfirmed] bit NOT NULL,
	[TwoFactorEnabled] bit NOT NULL,
	[LockoutEnabled] bit NOT NULL,
	[ConcurrencyStamp] uniqueidentifier NOT NULL,
	[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
	[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
	PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]),
	CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
)
WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory]));
GO

CREATE TABLE [dbo].[UserCredentials] (
	[Id] uniqueidentifier NOT NULL,
	[PasswordHash] varbinary(128) NULL,
	[SecurityStamp] nvarchar(32) NOT NULL,
	[AccessFailedCount] int NOT NULL,
	[LockoutEnd] datetimeoffset NULL,
	[NormalizedEmail] nvarchar(256) NOT NULL,
	[NormalizedUserName] nvarchar(256) NOT NULL,
	CONSTRAINT [PK_UserCredentials] PRIMARY KEY ([Id]),
	CONSTRAINT [FK_UserCredentials_Users_Id] FOREIGN KEY ([Id]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE,
	CONSTRAINT [CK_UserCredentials_EmailIsUserName] CHECK ([NormalizedEmail] = [NormalizedUserName])
);
GO

CREATE UNIQUE INDEX [IX_UserCredentials_NormalizedUserName] ON [dbo].[UserCredentials] ([NormalizedUserName]);
GO

CREATE TABLE [dbo].[UserClaims] (
	[Id] int IDENTITY(1, 1) NOT NULL,
	[UserId] uniqueidentifier NOT NULL,
	[ClaimType] nvarchar(256) NOT NULL,
	[ClaimValue] nvarchar(max) NOT NULL,
	CONSTRAINT [PK_UserClaims] PRIMARY KEY ([Id]),
	CONSTRAINT [FK_UserClaims_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [dbo].[UserTokens] (
	[UserId] uniqueidentifier NOT NULL,
	[LoginProvider] nvarchar(128) NOT NULL,
	[Name] nvarchar(128) NOT NULL,
	[Value] nvarchar(max) NULL,
	CONSTRAINT [PK_UserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
	CONSTRAINT [FK_UserTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [dbo].[UserPasskeys] (
	[CredentialId] varbinary(1024) NOT NULL,
	[UserId] uniqueidentifier NOT NULL,
	[Data] json NOT NULL,
	CONSTRAINT [PK_UserPasskeys] PRIMARY KEY ([CredentialId]),
	CONSTRAINT [FK_UserPasskeys_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
);
GO
