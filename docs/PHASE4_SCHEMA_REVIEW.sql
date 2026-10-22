BEGIN TRANSACTION;
ALTER TABLE [orders] ADD [checkout_request_id] uniqueidentifier NULL;

CREATE UNIQUE INDEX [ux_orders_checkout_request_id] ON [orders] ([checkout_request_id]) WHERE [checkout_request_id] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261016133620_AddCheckoutRequestId', N'10.0.11');

COMMIT;
GO
