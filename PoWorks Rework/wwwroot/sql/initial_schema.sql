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
    "LastUpdated" TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT unique_meter_year UNIQUE("MeterId", "Year")
);

-- Add indices for better performance
CREATE INDEX idx_meterreadingsdaily_meterid ON "MeterReadingsDaily"("MeterId");
CREATE INDEX idx_meterreadingsdaily_date ON "MeterReadingsDaily"("ReadingDate");

CREATE INDEX idx_meterreadingsmonthly_meterid ON "MeterReadingsMonthly"("MeterId");
CREATE INDEX idx_meterreadingsmonthly_year_month ON "MeterReadingsMonthly"("Year", "Month");

CREATE INDEX idx_meterreadingsyearly_meterid ON "MeterReadingsYearly"("MeterId");
CREATE INDEX idx_meterreadingsyearly_year ON "MeterReadingsYearly"("Year");

-- Function to aggregate readings into daily table
CREATE OR REPLACE FUNCTION aggregate_daily_readings()
RETURNS TRIGGER AS $$
BEGIN
    -- Insert or update daily aggregation
    INSERT INTO "MeterReadingsDaily" ("MeterId", "ReadingDate", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "LastUpdated")
    SELECT 
        NEW."MeterId",
        DATE(NEW."Timestamp"),
        COALESCE(MIN("Value"), NEW."Value"),
        COALESCE(MAX("Value"), NEW."Value"),
        AVG("Value"),
        SUM("Value"),
        COUNT(*),
        CURRENT_TIMESTAMP
    FROM "MeterReadings"
    WHERE "MeterId" = NEW."MeterId" AND DATE("Timestamp") = DATE(NEW."Timestamp")
    GROUP BY "MeterId", DATE("Timestamp")
    ON CONFLICT ("MeterId", "ReadingDate") DO UPDATE SET
        "MinValue" = EXCLUDED."MinValue",
        "MaxValue" = EXCLUDED."MaxValue",
        "AvgValue" = EXCLUDED."AvgValue", 
        "SumValue" = EXCLUDED."SumValue",
        "ReadingCount" = EXCLUDED."ReadingCount",
        "LastUpdated" = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Function to aggregate readings into monthly table
CREATE OR REPLACE FUNCTION aggregate_monthly_readings()
RETURNS TRIGGER AS $$
BEGIN
    -- Insert or update monthly aggregation
    INSERT INTO "MeterReadingsMonthly" ("MeterId", "Year", "Month", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "LastUpdated")
    SELECT 
        NEW."MeterId",
        EXTRACT(YEAR FROM NEW."Timestamp"),
        EXTRACT(MONTH FROM NEW."Timestamp"),
        COALESCE(MIN("Value"), NEW."Value"),
        COALESCE(MAX("Value"), NEW."Value"),
        AVG("Value"),
        SUM("Value"),
        COUNT(*),
        CURRENT_TIMESTAMP
    FROM "MeterReadings"
    WHERE "MeterId" = NEW."MeterId" 
        AND EXTRACT(YEAR FROM "Timestamp") = EXTRACT(YEAR FROM NEW."Timestamp")
        AND EXTRACT(MONTH FROM "Timestamp") = EXTRACT(MONTH FROM NEW."Timestamp")
    GROUP BY "MeterId", EXTRACT(YEAR FROM "Timestamp"), EXTRACT(MONTH FROM "Timestamp")
    ON CONFLICT ("MeterId", "Year", "Month") DO UPDATE SET
        "MinValue" = EXCLUDED."MinValue",
        "MaxValue" = EXCLUDED."MaxValue",
        "AvgValue" = EXCLUDED."AvgValue", 
        "SumValue" = EXCLUDED."SumValue",
        "ReadingCount" = EXCLUDED."ReadingCount",
        "LastUpdated" = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Function to aggregate readings into yearly table
CREATE OR REPLACE FUNCTION aggregate_yearly_readings()
RETURNS TRIGGER AS $$
BEGIN
    -- Insert or update yearly aggregation
    INSERT INTO "MeterReadingsYearly" ("MeterId", "Year", "MinValue", "MaxValue", "AvgValue", "SumValue", "ReadingCount", "LastUpdated")
    SELECT 
        NEW."MeterId",
        EXTRACT(YEAR FROM NEW."Timestamp"),
        COALESCE(MIN("Value"), NEW."Value"),
        COALESCE(MAX("Value"), NEW."Value"),
        AVG("Value"),
        SUM("Value"),
        COUNT(*),
        CURRENT_TIMESTAMP
    FROM "MeterReadings"
    WHERE "MeterId" = NEW."MeterId" 
        AND EXTRACT(YEAR FROM "Timestamp") = EXTRACT(YEAR FROM NEW."Timestamp")
    GROUP BY "MeterId", EXTRACT(YEAR FROM "Timestamp")
    ON CONFLICT ("MeterId", "Year") DO UPDATE SET
        "MinValue" = EXCLUDED."MinValue",
        "MaxValue" = EXCLUDED."MaxValue",
        "AvgValue" = EXCLUDED."AvgValue", 
        "SumValue" = EXCLUDED."SumValue",
        "ReadingCount" = EXCLUDED."ReadingCount",
        "LastUpdated" = CURRENT_TIMESTAMP;
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Add unique constraint to MeterReadingsDaily
ALTER TABLE "MeterReadingsDaily" ADD CONSTRAINT unique_meter_day UNIQUE("MeterId", "ReadingDate");

-- Create trigger for daily aggregation
CREATE TRIGGER trigger_aggregate_daily_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_daily_readings();

-- Create trigger for monthly aggregation
CREATE TRIGGER trigger_aggregate_monthly_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_monthly_readings();

-- Create trigger for yearly aggregation
CREATE TRIGGER trigger_aggregate_yearly_readings
AFTER INSERT ON "MeterReadings"
FOR EACH ROW
EXECUTE FUNCTION aggregate_yearly_readings();