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


-- Create Bills table
CREATE TABLE "Bills" (
    "BillId" SERIAL PRIMARY KEY,
    "BillNumber" VARCHAR(50) NOT NULL,
    "TenantID" INTEGER REFERENCES "Tenants"("TenantID"),
    "MeterId" INTEGER REFERENCES "Meters"("MeterId"),
    "BillDate" DATE NOT NULL,
    "DueDate" DATE NOT NULL,
    "TotalConsumption" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "BaseAmount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "TaxAmount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "TotalAmount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "PaidAmount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "OutstandingAmount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "StartReading" INTEGER,
    "EndReading" INTEGER,
    "BillingPeriodStart" DATE,
    "BillingPeriodEnd" DATE,
    "Notes" TEXT,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'Unpaid',
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP
);

-- Create index for better query performance
CREATE INDEX idx_bills_tenant ON "Bills"("TenantID");
CREATE INDEX idx_bills_meter ON "Bills"("MeterId");
CREATE INDEX idx_bills_status ON "Bills"("Status");
CREATE INDEX idx_bills_billnumber ON "Bills"("BillNumber");

-- Create Payments table
CREATE TABLE "Payments" (
    "PaymentId" SERIAL PRIMARY KEY,
    "PaymentNumber" VARCHAR(50) NOT NULL,
    "BillId" INTEGER REFERENCES "Bills"("BillId"),
    "TenantID" INTEGER REFERENCES "Tenants"("TenantID"),
    "PaymentDate" DATE NOT NULL,
    "Amount" DECIMAL(12,2) NOT NULL DEFAULT 0,
    "PaymentMethod" VARCHAR(50) NOT NULL DEFAULT 'Cash',
    "ReferenceNumber" VARCHAR(100),
    "Notes" TEXT,
    "Status" VARCHAR(50) NOT NULL DEFAULT 'Completed',
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" TIMESTAMP
);

-- Create index for better query performance
CREATE INDEX idx_payments_bill ON "Payments"("BillId");
CREATE INDEX idx_payments_tenant ON "Payments"("TenantID");
CREATE INDEX idx_payments_paymentnumber ON "Payments"("PaymentNumber");

-- Create a trigger function to update Bills after a Payment is made
CREATE OR REPLACE FUNCTION update_bill_after_payment()
RETURNS TRIGGER AS $$
BEGIN
    -- Update the PaidAmount and OutstandingAmount in the Bills table
    UPDATE "Bills"
    SET 
        "PaidAmount" = "PaidAmount" + NEW."Amount",
        "OutstandingAmount" = "TotalAmount" - ("PaidAmount" + NEW."Amount"),
        "Status" = CASE 
                     WHEN ("TotalAmount" - ("PaidAmount" + NEW."Amount")) <= 0 THEN 'Paid'
                     ELSE 'Partially Paid'
                   END,
        "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE "BillId" = NEW."BillId";
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Create a trigger to run after inserting a new payment
CREATE TRIGGER after_payment_insert
AFTER INSERT ON "Payments"
FOR EACH ROW
EXECUTE FUNCTION update_bill_after_payment();

-- Create a trigger function to update Bills if a Payment is updated
CREATE OR REPLACE FUNCTION update_bill_after_payment_update()
RETURNS TRIGGER AS $$
DECLARE
    total_paid DECIMAL(12,2);
    bill_total DECIMAL(12,2);
BEGIN
    -- Calculate total paid amount for this bill
    SELECT SUM("Amount") INTO total_paid FROM "Payments" WHERE "BillId" = NEW."BillId";
    
    -- Get bill total amount
    SELECT "TotalAmount" INTO bill_total FROM "Bills" WHERE "BillId" = NEW."BillId";
    
    -- Update the Bill with the recalculated values
    UPDATE "Bills"
    SET 
        "PaidAmount" = total_paid,
        "OutstandingAmount" = bill_total - total_paid,
        "Status" = CASE 
                     WHEN (bill_total - total_paid) <= 0 THEN 'Paid'
                     WHEN total_paid > 0 THEN 'Partially Paid'
                     ELSE 'Unpaid'
                   END,
        "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE "BillId" = NEW."BillId";
    
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

--############################################################################################
--############################################################################################

-- Create a trigger to run after updating a payment
CREATE TRIGGER after_payment_update
AFTER UPDATE ON "Payments"
FOR EACH ROW
EXECUTE FUNCTION update_bill_after_payment_update();

-- Create a trigger function to update Bills after a Payment is deleted
CREATE OR REPLACE FUNCTION update_bill_after_payment_delete()
RETURNS TRIGGER AS $$
DECLARE
    total_paid DECIMAL(12,2);
    bill_total DECIMAL(12,2);
BEGIN
    -- Calculate total paid amount for this bill (without the deleted payment)
    SELECT COALESCE(SUM("Amount"), 0) INTO total_paid FROM "Payments" WHERE "BillId" = OLD."BillId";
    
    -- Get bill total amount
    SELECT "TotalAmount" INTO bill_total FROM "Bills" WHERE "BillId" = OLD."BillId";
    
    -- Update the Bill with the recalculated values
    UPDATE "Bills"
    SET 
        "PaidAmount" = total_paid,
        "OutstandingAmount" = bill_total - total_paid,
        "Status" = CASE 
                     WHEN (bill_total - total_paid) <= 0 THEN 'Paid'
                     WHEN total_paid > 0 THEN 'Partially Paid'
                     ELSE 'Unpaid'
                   END,
        "UpdatedAt" = CURRENT_TIMESTAMP
    WHERE "BillId" = OLD."BillId";
    
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

-- Create a trigger to run after deleting a payment
CREATE TRIGGER after_payment_delete
AFTER DELETE ON "Payments"
FOR EACH ROW
EXECUTE FUNCTION update_bill_after_payment_delete();

-- Function to generate a new bill number
CREATE OR REPLACE FUNCTION generate_bill_number()
RETURNS VARCHAR AS $$
DECLARE
    next_number INTEGER;
    bill_prefix VARCHAR := 'BILL-';
    formatted_number VARCHAR;
BEGIN
    -- Get the current highest bill number
    SELECT COALESCE(
        MAX(
            CAST(
                NULLIF(
                    REGEXP_REPLACE(
                        SUBSTRING("BillNumber" FROM LENGTH(bill_prefix) + 1), 
                        '[^0-9]', '', 'g'
                    ), 
                    ''
                ) AS INTEGER
            )
        ), 
        0
    ) + 1 INTO next_number
    FROM "Bills";
    
    -- Format the bill number with leading zeros
    formatted_number := bill_prefix || LPAD(next_number::VARCHAR, 6, '0');
    
    RETURN formatted_number;
END;
$$ LANGUAGE plpgsql;

-- Function to generate a new payment number
CREATE OR REPLACE FUNCTION generate_payment_number()
RETURNS VARCHAR AS $$
DECLARE
    next_number INTEGER;
    payment_prefix VARCHAR := 'PMT-';
    formatted_number VARCHAR;
BEGIN
    -- Get the current highest payment number
    SELECT COALESCE(
        MAX(
            CAST(
                NULLIF(
                    REGEXP_REPLACE(
                        SUBSTRING("PaymentNumber" FROM LENGTH(payment_prefix) + 1), 
                        '[^0-9]', '', 'g'
                    ), 
                    ''
                ) AS INTEGER
            )
        ), 
        0
    ) + 1 INTO next_number
    FROM "Payments";
    
    -- Format the payment number with leading zeros
    formatted_number := payment_prefix || LPAD(next_number::VARCHAR, 6, '0');
    
    RETURN formatted_number;
END;
$$ LANGUAGE plpgsql;