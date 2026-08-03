--##########################################################
--Tenant Logic
--##########################################################

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

--##########################################################
--Company Logic
--##########################################################

CREATE TABLE IF NOT EXISTS "CompanyInfo" (
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

--##########################################################
--Meter Logic
--##########################################################

-- Create Meters table if it doesn't exist
CREATE TABLE IF NOT EXISTS "Meters" (
    "MeterId" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "Label" VARCHAR(150),
    "Unit" VARCHAR(20) NOT NULL DEFAULT '',
    "ParentId" INTEGER REFERENCES "Meters"("MeterId"),
    "LastReading" INTEGER DEFAULT 0,
    "Type" VARCHAR(10) CHECK ("Type" IN ('main', 'sub')) NOT NULL,
    "Active" BOOLEAN DEFAULT TRUE,
    "TenantID" INTEGER REFERENCES "Tenants"("TenantID"),
    "CompanyId" INTEGER 
);
-- Create index for faster meter queries
CREATE INDEX IF NOT EXISTS idx_meters_tenantid ON "Meters"("TenantID");
CREATE INDEX IF NOT EXISTS idx_meters_parentid ON "Meters"("ParentId");
CREATE INDEX IF NOT EXISTS idx_meters_label ON "Meters"("Label");


CREATE TABLE IF NOT EXISTS "MeterReadings" (
  "ReadingId" SERIAL PRIMARY KEY,
  "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
  "Timestamp" TIMESTAMP NOT NULL,
  "Value" NUMERIC NOT NULL,
  "Quality" INTEGER,
  "CompanyId" INTEGER NOT NULL 
);

-- Add indices for better performance
CREATE INDEX IF NOT EXISTS idx_meterreadings_meterid ON "MeterReadings"("MeterId");
CREATE INDEX IF NOT EXISTS idx_meterreadings_timestamp ON "MeterReadings"("Timestamp");

ALTER TABLE "MeterReadings" 
DROP CONSTRAINT IF EXISTS unique_meter_timestamp;
ALTER TABLE "MeterReadings" 
ADD CONSTRAINT unique_meter_timestamp 
UNIQUE ("MeterId", "Timestamp"); -- add value ###############################

-- Create Daily meter readings aggregate table
CREATE TABLE IF NOT EXISTS "MeterReadingsDaily" (
    "DailyReadingId" SERIAL PRIMARY KEY,
    "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
    "ReadingDate" DATE NOT NULL,
    "MinValue" NUMERIC,
    "MaxValue" NUMERIC,
    "AvgValue" NUMERIC,
    "SumValue" NUMERIC,
    "ReadingCount" INTEGER,
    "CompanyId" INTEGER NOT NULL, -- ✅ CORRECTION ICI
    "LastUpdated" TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create Monthly meter readings aggregate table
CREATE TABLE IF NOT EXISTS "MeterReadingsMonthly" (
    "MonthlyReadingId" SERIAL PRIMARY KEY,
    "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
    "Year" INTEGER NOT NULL,
    "Month" INTEGER NOT NULL,
    "MinValue" NUMERIC,
    "MaxValue" NUMERIC,
    "AvgValue" NUMERIC,
    "SumValue" NUMERIC,
    "ReadingCount" INTEGER,
    "CompanyId" INTEGER NOT NULL, -- ✅ CORRECTION ICI
    "LastUpdated" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_meter_month UNIQUE("MeterId", "Year", "Month")
);

-- Create Yearly meter readings aggregate table
CREATE TABLE IF NOT EXISTS "MeterReadingsYearly" (
    "YearlyReadingId" SERIAL PRIMARY KEY,
    "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
    "Year" INTEGER NOT NULL,
    "MinValue" NUMERIC,
    "MaxValue" NUMERIC,
    "AvgValue" NUMERIC,
    "SumValue" NUMERIC,
    "ReadingCount" INTEGER,
    "CompanyId" INTEGER NOT NULL, -- ✅ CORRECTION ICI
    "LastUpdated" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_meter_year UNIQUE("MeterId", "Year")
);

-- Add indices for better performance
CREATE INDEX IF NOT EXISTS idx_meterreadingsdaily_meterid ON "MeterReadingsDaily"("MeterId");
CREATE INDEX IF NOT EXISTS idx_meterreadingsdaily_date ON "MeterReadingsDaily"("ReadingDate");

CREATE INDEX IF NOT EXISTS idx_meterreadingsmonthly_meterid ON "MeterReadingsMonthly"("MeterId");
CREATE INDEX IF NOT EXISTS idx_meterreadingsmonthly_year_month ON "MeterReadingsMonthly"("Year", "Month");

CREATE INDEX IF NOT EXISTS idx_meterreadingsyearly_meterid ON "MeterReadingsYearly"("MeterId");
CREATE INDEX IF NOT EXISTS idx_meterreadingsyearly_year ON "MeterReadingsYearly"("Year");

-- Function to aggregate readings into daily table
CREATE OR REPLACE FUNCTION aggregate_daily_readings()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO "MeterReadingsDaily" ("MeterId", "ReadingDate", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "CompanyId", "LastUpdated")
    VALUES (
        NEW."MeterId",
        DATE(NEW."Timestamp"),
        NEW."Value",
        NEW."Value",
        NEW."Value",
        NEW."Value",
        1,
        NEW."CompanyId",
        CURRENT_TIMESTAMP
    )
    ON CONFLICT ("MeterId", "ReadingDate") DO UPDATE SET
        "MinValue" = LEAST("MeterReadingsDaily"."MinValue", NEW."Value"),
        "MaxValue" = GREATEST("MeterReadingsDaily"."MaxValue", NEW."Value"),
        "SumValue" = "MeterReadingsDaily"."SumValue" + NEW."Value",
        "ReadingCount" = "MeterReadingsDaily"."ReadingCount" + 1,
        "AvgValue" = ("MeterReadingsDaily"."SumValue" + NEW."Value") / ("MeterReadingsDaily"."ReadingCount" + 1),
        "CompanyId" = EXCLUDED."CompanyId",
        "LastUpdated" = CURRENT_TIMESTAMP;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
-- Function to aggregate readings into monthly table
CREATE OR REPLACE FUNCTION aggregate_monthly_readings()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO "MeterReadingsMonthly" ("MeterId", "Year", "Month", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "CompanyId", "LastUpdated")
    VALUES (
        NEW."MeterId",
        EXTRACT(YEAR FROM NEW."Timestamp"),
        EXTRACT(MONTH FROM NEW."Timestamp"),
        NEW."Value",
        NEW."Value",
        NEW."Value",
        NEW."Value",
        1,
        NEW."CompanyId",
        CURRENT_TIMESTAMP
    )
    ON CONFLICT ("MeterId", "Year", "Month") DO UPDATE SET
        "MinValue" = LEAST("MeterReadingsMonthly"."MinValue", NEW."Value"),
        "MaxValue" = GREATEST("MeterReadingsMonthly"."MaxValue", NEW."Value"),
        "SumValue" = "MeterReadingsMonthly"."SumValue" + NEW."Value",
        "ReadingCount" = "MeterReadingsMonthly"."ReadingCount" + 1,
        "AvgValue" = ("MeterReadingsMonthly"."SumValue" + NEW."Value") / ("MeterReadingsMonthly"."ReadingCount" + 1),
        "CompanyId" = EXCLUDED."CompanyId",
        "LastUpdated" = CURRENT_TIMESTAMP;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;


-- Function to aggregate readings into yearly table
CREATE OR REPLACE FUNCTION aggregate_yearly_readings()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO "MeterReadingsYearly" ("MeterId", "Year", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "CompanyId", "LastUpdated")
    VALUES (
        NEW."MeterId",
        EXTRACT(YEAR FROM NEW."Timestamp"),
        NEW."Value",
        NEW."Value",
        NEW."Value",
        NEW."Value",
        1,
        NEW."CompanyId",
        CURRENT_TIMESTAMP
    )
    ON CONFLICT ("MeterId", "Year") DO UPDATE SET
        "MinValue" = LEAST("MeterReadingsYearly"."MinValue", NEW."Value"),
        "MaxValue" = GREATEST("MeterReadingsYearly"."MaxValue", NEW."Value"),
        "SumValue" = "MeterReadingsYearly"."SumValue" + NEW."Value",
        "ReadingCount" = "MeterReadingsYearly"."ReadingCount" + 1,
        "AvgValue" = ("MeterReadingsYearly"."SumValue" + NEW."Value") / ("MeterReadingsYearly"."ReadingCount" + 1),
        "CompanyId" = EXCLUDED."CompanyId",
        "LastUpdated" = CURRENT_TIMESTAMP;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Add unique constraint to MeterReadingsDaily
ALTER TABLE "MeterReadingsDaily" DROP CONSTRAINT IF EXISTS unique_meter_day;
ALTER TABLE "MeterReadingsDaily" ADD CONSTRAINT unique_meter_day UNIQUE("MeterId", "ReadingDate");

-- Create trigger for daily aggregation
CREATE OR REPLACE TRIGGER trigger_aggregate_daily_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_daily_readings();

-- Create trigger for monthly aggregation
CREATE OR REPLACE TRIGGER trigger_aggregate_monthly_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_monthly_readings();

-- Create trigger for yearly aggregation
CREATE OR REPLACE TRIGGER trigger_aggregate_yearly_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_yearly_readings();

--##########################################################
-- Billing Logic (Moteur de facturation)
--##########################################################

-- 1. Ajouter l'abonnement mensuel fixe aux locataires existants
ALTER TABLE "TenantDetails" 
ADD COLUMN IF NOT EXISTS "AbonnementMensuel" NUMERIC(10,2) DEFAULT 0.00;

-- 2. Table principale des factures
CREATE TABLE IF NOT EXISTS "Bills" (
    "BillId" SERIAL PRIMARY KEY,
    "TenantID" INTEGER NOT NULL REFERENCES "Tenants"("TenantID"),
    "BillNumber" VARCHAR(50) UNIQUE,
    "PeriodStart" DATE NOT NULL,
    "PeriodEnd" DATE NOT NULL,
    "TotalKWh" NUMERIC(12,3) DEFAULT 0,
    "MontantHT" NUMERIC(10,2) DEFAULT 0,
    "MontantTVA" NUMERIC(10,2) DEFAULT 0,
    "MontantTTC" NUMERIC(10,2) DEFAULT 0,
    "Status" VARCHAR(20) DEFAULT 'Draft', -- Draft, Validated, Paid, Cancelled
    "GeneratedAt" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "ValidatedAt" TIMESTAMP,
    "PaidAt" TIMESTAMP,
    "Notes" TEXT
);

-- 3. Détail des factures (Lignes par compteur)
CREATE TABLE IF NOT EXISTS "BillLineItems" (
    "LineItemId" SERIAL PRIMARY KEY,
    "BillId" INTEGER NOT NULL REFERENCES "Bills"("BillId") ON DELETE CASCADE,
    "MeterId" INTEGER NOT NULL REFERENCES "Meters"("MeterId"),
    "MeterName" VARCHAR(100),
    "Consumption" NUMERIC(12,3),
    "Unit" VARCHAR(20),
    "UnitPrice" NUMERIC(10,4),
    "LineTotalHT" NUMERIC(10,2)
);


CREATE INDEX IF NOT EXISTS idx_bills_tenantid ON "Bills"("TenantID");
CREATE INDEX IF NOT EXISTS idx_bills_status ON "Bills"("Status");



CREATE TABLE IF NOT EXISTS "Companies" (
    "CompanyId" SERIAL PRIMARY KEY,
    "Name" VARCHAR(255) NOT NULL DEFAULT 'Default Company'
);

INSERT INTO "Companies" ("CompanyId", "Name") 
VALUES (1, 'PoWorks Default') 
ON CONFLICT ("CompanyId") DO NOTHING;

CREATE TABLE IF NOT EXISTS "WebServiceConnections" (
    "Id" SERIAL PRIMARY KEY,
    "ConnectionId" VARCHAR(100),
    "CompanyId" INTEGER,
    "ConnectionName" VARCHAR(255),
    "BaseUrl" VARCHAR(255),
    "ClientId" VARCHAR(255),
    "ClientSecret" TEXT,
    "Username" VARCHAR(255),
    "Password" TEXT,
    "ApiKey" VARCHAR(255),
    "AuthType" INTEGER DEFAULT 0,
    "TimeoutSeconds" INTEGER DEFAULT 30,
    "ProjectName" VARCHAR(255),
    "IsDefault" BOOLEAN DEFAULT FALSE,
    "IsActive" BOOLEAN DEFAULT TRUE
);

ALTER TABLE "WebServiceConnections" ADD COLUMN IF NOT EXISTS "EnableAutomaticImport" BOOLEAN DEFAULT FALSE;
ALTER TABLE "WebServiceConnections" ADD COLUMN IF NOT EXISTS "AutoImportIntervalMinutes" INTEGER DEFAULT 1;


CREATE TABLE IF NOT EXISTS "SqlServerConnections" (
    "Id" SERIAL PRIMARY KEY,
    "ConnectionId" VARCHAR(100),
    "CompanyId" INTEGER,
    "ConnectionName" VARCHAR(255),
    "Host" VARCHAR(255),
    "Port" VARCHAR(50),
    "Database" VARCHAR(255),
    "Username" VARCHAR(255),
    "Password" TEXT,
    "ProjectName" VARCHAR(255),
    "IsDefault" BOOLEAN DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS "Payments" (
    "PaymentId" SERIAL PRIMARY KEY,
    "BillId" INTEGER REFERENCES "Bills"("BillId") ON DELETE CASCADE,
    "TenantID" INTEGER REFERENCES "Tenants"("TenantID") ON DELETE CASCADE,
    "AmountPaid" NUMERIC(10,2) NOT NULL, 
    "PaymentDate" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    "PaymentMethod" VARCHAR(50),
    "Reference" VARCHAR(100),
    "CompanyId" INTEGER DEFAULT 1
);


ALTER TABLE "Meters" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "UserId" TEXT; 
ALTER TABLE "Payments" ADD COLUMN IF NOT EXISTS "Notes" TEXT;
ALTER TABLE "TenantDetails" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "Bills" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "Bills" ADD COLUMN IF NOT EXISTS "GrandTotal" NUMERIC(10,2) DEFAULT 0;

ALTER TABLE "MeterReadings" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "MeterReadingsDaily" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "MeterReadingsMonthly" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;
ALTER TABLE "MeterReadingsYearly" ADD COLUMN IF NOT EXISTS "CompanyId" INTEGER DEFAULT 1;



SELECT setval(pg_get_serial_sequence('"Companies"', 'CompanyId'), coalesce(max("CompanyId"), 1), max("CompanyId") IS NOT null) FROM "Companies";