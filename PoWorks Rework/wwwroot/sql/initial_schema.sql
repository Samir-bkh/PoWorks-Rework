-- Create Tenants table if it doesn't exist
CREATE TABLE IF NOT EXISTS "Tenants" (
    "TenantID" SERIAL PRIMARY KEY,
    "DisplayName" VARCHAR(100) NOT NULL,
    "Misc" VARCHAR(255)
);
-- Create TenantDetails table if it doesn't exist
CREATE TABLE IF NOT EXISTS "TenantDetails" (
    "ID" SERIAL PRIMARY KEY,
    "TenantID" INTEGER NOT NULL REFERENCES "Tenants"("TenantID"),
    "ContactName" VARCHAR(100),
    "ContactPhone" VARCHAR(20),
    "ContactMobile" VARCHAR(20),
    "ContactEmail" VARCHAR(100),
    "CompanyName" VARCHAR(100) NOT NULL,
    "CompanyAddress" TEXT,
    "CompanyLocation" VARCHAR(100),
    "CompanyMisc" VARCHAR(100),
    "Tarif_1" MONEY DEFAULT 0,
    "Tarif_2" MONEY DEFAULT 0,
    "Tarif_3" MONEY DEFAULT 0,
    "StartDate" DATE DEFAULT CURRENT_DATE,
    "Period" VARCHAR(20) DEFAULT 'Monthly',
    "Deposit" MONEY DEFAULT 0,
    "Active" BOOLEAN DEFAULT TRUE,
    "EmailAlert" BOOLEAN DEFAULT TRUE,
    "PrintBill" BOOLEAN DEFAULT TRUE,
    "EmailBill" BOOLEAN DEFAULT TRUE
);
-- Create index for faster tenant-related queries
CREATE INDEX IF NOT EXISTS idx_tenantdetails_tenantid ON "TenantDetails"("TenantID");
-- Create Meters table if it doesn't exist
CREATE TABLE IF NOT EXISTS "Meters" (
    "MeterId" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Unit" VARCHAR(20) NOT NULL DEFAULT '',
    "ParentId" INTEGER REFERENCES "Meters"("MeterId"),
    "LastReading" INTEGER DEFAULT 0,
    "Type" VARCHAR(10) CHECK ("Type" IN ('main', 'sub')) NOT NULL,
    "Active" BOOLEAN DEFAULT TRUE,
    "TenantID" INTEGER REFERENCES "Tenants"("TenantID")
);
-- Create index for faster meter queries
CREATE INDEX IF NOT EXISTS idx_meters_tenantid ON "Meters"("TenantID");
CREATE INDEX IF NOT EXISTS idx_meters_parentid ON "Meters"("ParentId");