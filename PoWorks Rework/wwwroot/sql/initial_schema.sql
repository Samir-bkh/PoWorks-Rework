-- Tenants table
CREATE TABLE IF NOT EXISTS Tenants (
    TenantID SERIAL PRIMARY KEY,
    DisplayName VARCHAR(100) NOT NULL,
    Misc VARCHAR(250) NULL
);

-- Tenant Details table
CREATE TABLE IF NOT EXISTS TenantDetails (
    ID SERIAL PRIMARY KEY,
    TenantID SMALLINT NOT NULL,
    ContactName VARCHAR(50) NOT NULL,
    ContactPhone VARCHAR(20) NULL,
    ContactMobile VARCHAR(20) NULL,
    ContactEmail VARCHAR(50) NOT NULL,
    CompanyName VARCHAR(50) NOT NULL,
    CompanyAddress VARCHAR(100) NOT NULL,
    CompanyLocation VARCHAR(100) NULL,
    CompanyMisc VARCHAR(250) NULL,
    Tarif_1 MONEY NOT NULL,
    Tarif_2 MONEY NULL,
    Tarif_3 MONEY NULL,
    CONSTRAINT FK_TenantDetails_Tenants FOREIGN KEY (TenantID) REFERENCES Tenants(TenantID)
);