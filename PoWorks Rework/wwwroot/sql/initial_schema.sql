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

CREATE TABLE "CompanyInfo" (
    "CompanyInfoId" SERIAL PRIMARY KEY,
    "CompanyName" VARCHAR(100) NOT NULL,
    "RegistrationNumber" VARCHAR(50),
    "Address1" VARCHAR(255),
    "Address2" VARCHAR(255),
    "PostCode" VARCHAR(20),
    "Country" VARCHAR(100),
    "City" VARCHAR(100),
    "GstId" VARCHAR(50),
    "GstPercentage" DECIMAL(5,2),
    "Phone" VARCHAR(50),
    "Fax" VARCHAR(50),
    "Email" VARCHAR(100),
    "LogoPath" VARCHAR(255)
);


CREATE TABLE "MeterReadings" (
  "ReadingId" SERIAL PRIMARY KEY,
  "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
  "Timestamp" TIMESTAMP NOT NULL,
  "Value" NUMERIC NOT NULL,
  "Quality" INTEGER
);

-- Add indices for better performance
CREATE INDEX idx_meterreadings_meterid ON "MeterReadings"("MeterId");
CREATE INDEX idx_meterreadings_timestamp ON "MeterReadings"("Timestamp");