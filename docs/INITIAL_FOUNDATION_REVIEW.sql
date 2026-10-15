IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [brands] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(120) NOT NULL,
    [description] nvarchar(1000) NULL,
    [logo_url] nvarchar(2048) NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_brands] PRIMARY KEY ([id])
);

CREATE TABLE [categories] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(120) NOT NULL,
    [description] nvarchar(1000) NULL,
    [parent_id] int NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_categories] PRIMARY KEY ([id]),
    CONSTRAINT [FK_categories_categories_parent_id] FOREIGN KEY ([parent_id]) REFERENCES [categories] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [promotions] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(200) NOT NULL,
    [description] nvarchar(2000) NULL,
    [discount_type] nvarchar(30) NOT NULL,
    [discount_value] decimal(18,2) NOT NULL,
    [start_date] datetime2 NOT NULL,
    [end_date] datetime2 NOT NULL,
    [is_active] bit NOT NULL,
    CONSTRAINT [PK_promotions] PRIMARY KEY ([id]),
    CONSTRAINT [ck_promotions_dates_valid] CHECK ([end_date] >= [start_date]),
    CONSTRAINT [ck_promotions_discount_nonnegative] CHECK ([discount_value] >= 0),
    CONSTRAINT [ck_promotions_discount_type_valid] CHECK ([discount_type] IN ('PERCENTAGE','FIXED_AMOUNT')),
    CONSTRAINT [ck_promotions_percentage_range] CHECK ([discount_type] <> 'PERCENTAGE' OR [discount_value] <= 100)
);

CREATE TABLE [roles] (
    [id] int NOT NULL IDENTITY,
    [description] nvarchar(500) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [name] nvarchar(256) NOT NULL,
    [normalized_name] nvarchar(256) NOT NULL,
    [concurrency_stamp] nvarchar(max) NULL,
    CONSTRAINT [PK_roles] PRIMARY KEY ([id])
);

CREATE TABLE [specifications] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(160) NOT NULL,
    [unit] nvarchar(40) NULL,
    [data_type] nvarchar(30) NOT NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_specifications] PRIMARY KEY ([id])
);

CREATE TABLE [usage_needs] (
    [id] int NOT NULL IDENTITY,
    [code] nvarchar(80) NOT NULL,
    [name] nvarchar(160) NOT NULL,
    [description] nvarchar(1000) NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_usage_needs] PRIMARY KEY ([id])
);

CREATE TABLE [users] (
    [id] int NOT NULL IDENTITY,
    [full_name] nvarchar(200) NOT NULL,
    [address] nvarchar(500) NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [user_name] nvarchar(256) NOT NULL,
    [normalized_user_name] nvarchar(256) NOT NULL,
    [email] nvarchar(256) NOT NULL,
    [normalized_email] nvarchar(256) NOT NULL,
    [email_confirmed] bit NOT NULL,
    [password_hash] nvarchar(max) NULL,
    [security_stamp] nvarchar(max) NULL,
    [concurrency_stamp] nvarchar(max) NULL,
    [phone_number] nvarchar(32) NULL,
    [phone_number_confirmed] bit NOT NULL,
    [two_factor_enabled] bit NOT NULL,
    [lockout_end] datetimeoffset NULL,
    [lockout_enabled] bit NOT NULL,
    [access_failed_count] int NOT NULL,
    CONSTRAINT [PK_users] PRIMARY KEY ([id])
);

CREATE TABLE [products] (
    [id] int NOT NULL IDENTITY,
    [category_id] int NOT NULL,
    [brand_id] int NOT NULL,
    [name] nvarchar(240) NOT NULL,
    [slug] nvarchar(260) NOT NULL,
    [sku] nvarchar(80) NOT NULL,
    [description] nvarchar(4000) NULL,
    [price] decimal(18,2) NOT NULL,
    [discount_price] decimal(18,2) NULL,
    [warranty_months] int NOT NULL,
    [status] nvarchar(30) NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_products] PRIMARY KEY ([id]),
    CONSTRAINT [ck_products_discount_price_nonnegative] CHECK ([discount_price] IS NULL OR [discount_price] >= 0),
    CONSTRAINT [ck_products_price_nonnegative] CHECK ([price] >= 0),
    CONSTRAINT [ck_products_warranty_nonnegative] CHECK ([warranty_months] >= 0),
    CONSTRAINT [FK_products_brands_brand_id] FOREIGN KEY ([brand_id]) REFERENCES [brands] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_products_categories_category_id] FOREIGN KEY ([category_id]) REFERENCES [categories] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [AspNetRoleClaims] (
    [id] int NOT NULL IDENTITY,
    [role_id] int NOT NULL,
    [claim_type] nvarchar(max) NULL,
    [claim_value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([id]),
    CONSTRAINT [FK_AspNetRoleClaims_roles_role_id] FOREIGN KEY ([role_id]) REFERENCES [roles] ([id]) ON DELETE CASCADE
);

CREATE TABLE [category_specifications] (
    [id] int NOT NULL IDENTITY,
    [category_id] int NOT NULL,
    [specification_id] int NOT NULL,
    [is_filterable] bit NOT NULL,
    [is_required] bit NOT NULL,
    [sort_order] int NOT NULL,
    CONSTRAINT [PK_category_specifications] PRIMARY KEY ([id]),
    CONSTRAINT [ak_category_specifications_category_specification] UNIQUE ([category_id], [specification_id]),
    CONSTRAINT [FK_category_specifications_categories_category_id] FOREIGN KEY ([category_id]) REFERENCES [categories] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_category_specifications_specifications_specification_id] FOREIGN KEY ([specification_id]) REFERENCES [specifications] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [AspNetUserClaims] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [claim_type] nvarchar(max) NULL,
    [claim_value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([id]),
    CONSTRAINT [FK_AspNetUserClaims_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [login_provider] nvarchar(128) NOT NULL,
    [provider_key] nvarchar(128) NOT NULL,
    [provider_display_name] nvarchar(256) NULL,
    [user_id] int NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([login_provider], [provider_key]),
    CONSTRAINT [FK_AspNetUserLogins_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [user_id] int NOT NULL,
    [login_provider] nvarchar(128) NOT NULL,
    [name] nvarchar(128) NOT NULL,
    [value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([user_id], [login_provider], [name]),
    CONSTRAINT [FK_AspNetUserTokens_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
);

CREATE TABLE [carts] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_carts] PRIMARY KEY ([id]),
    CONSTRAINT [FK_carts_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [orders] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [customer_name] nvarchar(200) NOT NULL,
    [phone] nvarchar(32) NOT NULL,
    [shipping_address] nvarchar(1000) NOT NULL,
    [order_date] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [total_amount] decimal(18,2) NOT NULL,
    [status] nvarchar(20) NOT NULL,
    [note] nvarchar(2000) NULL,
    [confirmed_by] int NULL,
    [confirmed_at] datetime2 NULL,
    [cancelled_at] datetime2 NULL,
    [cancel_reason] nvarchar(1000) NULL,
    [completed_at] datetime2 NULL,
    CONSTRAINT [PK_orders] PRIMARY KEY ([id]),
    CONSTRAINT [ak_orders_id_user_id] UNIQUE ([id], [user_id]),
    CONSTRAINT [ck_orders_status_valid] CHECK ([status] IN ('PENDING','CONFIRMED','PREPARING','EXPORTED','SHIPPING','COMPLETED','CANCELLED')),
    CONSTRAINT [ck_orders_total_amount_nonnegative] CHECK ([total_amount] >= 0),
    CONSTRAINT [FK_orders_users_confirmed_by] FOREIGN KEY ([confirmed_by]) REFERENCES [users] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_orders_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [user_roles] (
    [user_id] int NOT NULL,
    [role_id] int NOT NULL,
    [assigned_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_user_roles] PRIMARY KEY ([user_id], [role_id]),
    CONSTRAINT [FK_user_roles_roles_role_id] FOREIGN KEY ([role_id]) REFERENCES [roles] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_user_roles_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
);

CREATE TABLE [favorites] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [product_id] int NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_favorites] PRIMARY KEY ([id]),
    CONSTRAINT [FK_favorites_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_favorites_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [inventories] (
    [id] int NOT NULL IDENTITY,
    [product_id] int NOT NULL,
    [quantity_in_stock] int NOT NULL,
    [reserved_quantity] int NOT NULL,
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [row_version] rowversion NOT NULL,
    CONSTRAINT [PK_inventories] PRIMARY KEY ([id]),
    CONSTRAINT [ck_inventories_quantities_valid] CHECK ([quantity_in_stock] >= 0 AND [reserved_quantity] >= 0 AND [reserved_quantity] <= [quantity_in_stock]),
    CONSTRAINT [FK_inventories_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [product_images] (
    [id] int NOT NULL IDENTITY,
    [product_id] int NOT NULL,
    [image_url] nvarchar(2048) NOT NULL,
    [is_primary] bit NOT NULL,
    [sort_order] int NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_product_images] PRIMARY KEY ([id]),
    CONSTRAINT [FK_product_images_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE CASCADE
);

CREATE TABLE [product_specifications] (
    [id] int NOT NULL IDENTITY,
    [product_id] int NOT NULL,
    [specification_id] int NOT NULL,
    [value] nvarchar(1000) NOT NULL,
    [numeric_value] decimal(18,4) NULL,
    [display_value] nvarchar(300) NULL,
    CONSTRAINT [PK_product_specifications] PRIMARY KEY ([id]),
    CONSTRAINT [FK_product_specifications_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_product_specifications_specifications_specification_id] FOREIGN KEY ([specification_id]) REFERENCES [specifications] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [promotion_products] (
    [promotion_id] int NOT NULL,
    [product_id] int NOT NULL,
    CONSTRAINT [PK_promotion_products] PRIMARY KEY ([promotion_id], [product_id]),
    CONSTRAINT [FK_promotion_products_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_promotion_products_promotions_promotion_id] FOREIGN KEY ([promotion_id]) REFERENCES [promotions] ([id]) ON DELETE CASCADE
);

CREATE TABLE [recommendation_rules] (
    [id] int NOT NULL IDENTITY,
    [usage_need_id] int NOT NULL,
    [category_id] int NOT NULL,
    [specification_id] int NOT NULL,
    [operator] nvarchar(30) NOT NULL,
    [threshold_value] nvarchar(300) NOT NULL,
    [weight] decimal(9,4) NOT NULL,
    [is_required] bit NOT NULL,
    [is_active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_recommendation_rules] PRIMARY KEY ([id]),
    CONSTRAINT [ck_recommendation_rules_operator_valid] CHECK ([operator] IN ('EQ','NE','GT','GTE','LT','LTE','CONTAINS')),
    CONSTRAINT [ck_recommendation_rules_weight_nonnegative] CHECK ([weight] >= 0),
    CONSTRAINT [FK_recommendation_rules_category_specifications_category_id_specification_id] FOREIGN KEY ([category_id], [specification_id]) REFERENCES [category_specifications] ([category_id], [specification_id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_recommendation_rules_specifications_specification_id] FOREIGN KEY ([specification_id]) REFERENCES [specifications] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_recommendation_rules_usage_needs_usage_need_id] FOREIGN KEY ([usage_need_id]) REFERENCES [usage_needs] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [cart_items] (
    [id] int NOT NULL IDENTITY,
    [cart_id] int NOT NULL,
    [product_id] int NOT NULL,
    [quantity] int NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_cart_items] PRIMARY KEY ([id]),
    CONSTRAINT [ck_cart_items_quantity_positive] CHECK ([quantity] > 0),
    CONSTRAINT [FK_cart_items_carts_cart_id] FOREIGN KEY ([cart_id]) REFERENCES [carts] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_cart_items_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [inventory_transactions] (
    [id] int NOT NULL IDENTITY,
    [product_id] int NOT NULL,
    [user_id] int NOT NULL,
    [order_id] int NULL,
    [transaction_type] nvarchar(20) NOT NULL,
    [quantity] int NOT NULL,
    [note] nvarchar(1000) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_inventory_transactions] PRIMARY KEY ([id]),
    CONSTRAINT [ck_inventory_transactions_quantity_positive] CHECK ([quantity] > 0),
    CONSTRAINT [ck_inventory_transactions_type_valid] CHECK ([transaction_type] IN ('IN','OUT','ADJUST','RESERVE','RELEASE')),
    CONSTRAINT [FK_inventory_transactions_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [orders] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_inventory_transactions_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_inventory_transactions_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [order_details] (
    [id] int NOT NULL IDENTITY,
    [order_id] int NOT NULL,
    [product_id] int NOT NULL,
    [product_name] nvarchar(240) NOT NULL,
    [quantity] int NOT NULL,
    [unit_price] decimal(18,2) NOT NULL,
    [discount_amount] decimal(18,2) NOT NULL,
    [total_price] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_order_details] PRIMARY KEY ([id]),
    CONSTRAINT [ak_order_details_id_product_order] UNIQUE ([id], [product_id], [order_id]),
    CONSTRAINT [ck_order_details_discount_nonnegative] CHECK ([discount_amount] >= 0),
    CONSTRAINT [ck_order_details_quantity_positive] CHECK ([quantity] > 0),
    CONSTRAINT [ck_order_details_total_matches_formula] CHECK ([total_price] = ([unit_price] * [quantity]) - [discount_amount]),
    CONSTRAINT [ck_order_details_total_nonnegative] CHECK ([total_price] >= 0),
    CONSTRAINT [ck_order_details_unit_price_nonnegative] CHECK ([unit_price] >= 0),
    CONSTRAINT [FK_order_details_orders_order_id] FOREIGN KEY ([order_id]) REFERENCES [orders] ([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_order_details_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION
);

CREATE TABLE [reviews] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [product_id] int NOT NULL,
    [order_id] int NOT NULL,
    [order_detail_id] int NOT NULL,
    [rating] int NOT NULL,
    [comment] nvarchar(3000) NULL,
    [status] nvarchar(30) NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_reviews] PRIMARY KEY ([id]),
    CONSTRAINT [ck_reviews_rating_range] CHECK ([rating] >= 1 AND [rating] <= 5),
    CONSTRAINT [FK_reviews_order_details_order_detail_id_product_id_order_id] FOREIGN KEY ([order_detail_id], [product_id], [order_id]) REFERENCES [order_details] ([id], [product_id], [order_id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_reviews_orders_order_id_user_id] FOREIGN KEY ([order_id], [user_id]) REFERENCES [orders] ([id], [user_id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_reviews_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [products] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_reviews_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AspNetRoleClaims_role_id] ON [AspNetRoleClaims] ([role_id]);

CREATE INDEX [IX_AspNetUserClaims_user_id] ON [AspNetUserClaims] ([user_id]);

CREATE INDEX [IX_AspNetUserLogins_user_id] ON [AspNetUserLogins] ([user_id]);

CREATE UNIQUE INDEX [ux_brands_name] ON [brands] ([name]);

CREATE INDEX [IX_cart_items_product_id] ON [cart_items] ([product_id]);

CREATE UNIQUE INDEX [ux_cart_items_cart_product] ON [cart_items] ([cart_id], [product_id]);

CREATE UNIQUE INDEX [ux_carts_user_id] ON [carts] ([user_id]);

CREATE INDEX [IX_categories_parent_id] ON [categories] ([parent_id]);

CREATE INDEX [IX_category_specifications_specification_id] ON [category_specifications] ([specification_id]);

CREATE INDEX [IX_favorites_product_id] ON [favorites] ([product_id]);

CREATE UNIQUE INDEX [ux_favorites_user_product] ON [favorites] ([user_id], [product_id]);

CREATE UNIQUE INDEX [ux_inventories_product_id] ON [inventories] ([product_id]);

CREATE INDEX [IX_inventory_transactions_order_id] ON [inventory_transactions] ([order_id]);

CREATE INDEX [IX_inventory_transactions_product_id_created_at] ON [inventory_transactions] ([product_id], [created_at]);

CREATE INDEX [IX_inventory_transactions_user_id] ON [inventory_transactions] ([user_id]);

CREATE INDEX [IX_order_details_order_id] ON [order_details] ([order_id]);

CREATE INDEX [IX_order_details_product_id] ON [order_details] ([product_id]);

CREATE INDEX [IX_orders_confirmed_by] ON [orders] ([confirmed_by]);

CREATE INDEX [ix_orders_status_order_date] ON [orders] ([status], [order_date]);

CREATE INDEX [ix_orders_user_status_order_date] ON [orders] ([user_id], [status], [order_date]);

CREATE INDEX [IX_product_images_product_id_sort_order] ON [product_images] ([product_id], [sort_order]);

CREATE UNIQUE INDEX [ux_product_images_primary_product] ON [product_images] ([product_id]) WHERE [is_primary] = 1;

CREATE INDEX [ix_product_specifications_specification_numeric] ON [product_specifications] ([specification_id], [numeric_value]);

CREATE UNIQUE INDEX [ux_product_specifications_product_specification] ON [product_specifications] ([product_id], [specification_id]);

CREATE INDEX [IX_products_brand_id] ON [products] ([brand_id]);

CREATE INDEX [ix_products_category_brand_price_status] ON [products] ([category_id], [brand_id], [price], [status]);

CREATE UNIQUE INDEX [ux_products_sku] ON [products] ([sku]);

CREATE UNIQUE INDEX [ux_products_slug] ON [products] ([slug]);

CREATE INDEX [IX_promotion_products_product_id] ON [promotion_products] ([product_id]);

CREATE INDEX [IX_promotions_is_active_start_date_end_date] ON [promotions] ([is_active], [start_date], [end_date]);

CREATE INDEX [IX_recommendation_rules_category_id_specification_id] ON [recommendation_rules] ([category_id], [specification_id]);

CREATE INDEX [ix_recommendation_rules_need_category_active] ON [recommendation_rules] ([usage_need_id], [category_id], [is_active]);

CREATE INDEX [IX_recommendation_rules_specification_id] ON [recommendation_rules] ([specification_id]);

CREATE INDEX [IX_reviews_order_detail_id_product_id_order_id] ON [reviews] ([order_detail_id], [product_id], [order_id]);

CREATE INDEX [IX_reviews_order_id_user_id] ON [reviews] ([order_id], [user_id]);

CREATE INDEX [IX_reviews_product_id] ON [reviews] ([product_id]);

CREATE INDEX [IX_reviews_user_id] ON [reviews] ([user_id]);

CREATE UNIQUE INDEX [ux_reviews_order_detail] ON [reviews] ([order_detail_id]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [roles] ([normalized_name]);

CREATE INDEX [ix_usage_needs_code_active] ON [usage_needs] ([code], [is_active]);

CREATE UNIQUE INDEX [ux_usage_needs_code] ON [usage_needs] ([code]);

CREATE INDEX [IX_user_roles_role_id] ON [user_roles] ([role_id]);

CREATE UNIQUE INDEX [UserNameIndex] ON [users] ([normalized_user_name]);

CREATE UNIQUE INDEX [ux_users_normalized_email] ON [users] ([normalized_email]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261015173046_InitialFoundation', N'10.0.11');

COMMIT;
GO

