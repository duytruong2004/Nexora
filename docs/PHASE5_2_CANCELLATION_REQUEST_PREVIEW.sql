-- REVIEW PREVIEW ONLY: Do not execute directly. Use the reviewed EF Core migration workflow for an explicitly approved target.

BEGIN TRANSACTION;
ALTER TABLE [orders] ADD [cancel_decision_note] nvarchar(1000) NULL;

ALTER TABLE [orders] ADD [cancel_request_id] uniqueidentifier NULL;

ALTER TABLE [orders] ADD [cancel_request_reason] nvarchar(1000) NULL;

ALTER TABLE [orders] ADD [cancel_request_status] nvarchar(20) NULL;

ALTER TABLE [orders] ADD [cancel_requested_at] datetime2 NULL;

ALTER TABLE [orders] ADD [cancel_reviewed_at] datetime2 NULL;

ALTER TABLE [orders] ADD [cancel_reviewed_by] int NULL;

CREATE INDEX [ix_orders_cancel_pending_review] ON [orders] ([cancel_requested_at], [id]) WHERE [cancel_request_status] = 'PENDING';

CREATE INDEX [IX_orders_cancel_reviewed_by] ON [orders] ([cancel_reviewed_by]);

CREATE UNIQUE INDEX [ux_orders_cancel_request_id] ON [orders] ([cancel_request_id]) WHERE [cancel_request_id] IS NOT NULL;

ALTER TABLE [orders] ADD CONSTRAINT [ck_orders_cancel_request_consistent] CHECK (([cancel_request_status] IS NULL AND [cancel_request_reason] IS NULL AND [cancel_request_id] IS NULL AND [cancel_requested_at] IS NULL AND [cancel_reviewed_by] IS NULL AND [cancel_reviewed_at] IS NULL AND [cancel_decision_note] IS NULL)
OR
([cancel_request_status] = 'PENDING' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NULL AND [cancel_reviewed_at] IS NULL AND [cancel_decision_note] IS NULL)
OR
([cancel_request_status] = 'APPROVED' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NOT NULL AND [cancel_reviewed_at] IS NOT NULL AND [status] = 'CANCELLED')
OR
([cancel_request_status] = 'REJECTED' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NOT NULL AND [cancel_reviewed_at] IS NOT NULL AND [cancel_decision_note] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_decision_note]))) > 0));

ALTER TABLE [orders] ADD CONSTRAINT [FK_orders_users_cancel_reviewed_by] FOREIGN KEY ([cancel_reviewed_by]) REFERENCES [users] ([id]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261019174632_AddOrderCancellationRequestFields', N'10.0.11');

COMMIT;
GO
