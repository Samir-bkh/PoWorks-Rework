-- Synthetic data for the PoWorks Default workspace (CompanyId = 1).
-- This is a presentation dataset in PostgreSQL, not PcVue historical data.
-- Safe to run again: existing meters and readings are reused.

BEGIN;

WITH demo_meters(name, label, unit) AS (
    VALUES
        ('DEMO.Building.Power.kW', 'Démo · Puissance bâtiment', 'kW'),
        ('DEMO.Building.Energy.kWh', 'Démo · Énergie cumulée', 'kWh'),
        ('DEMO.Building.Water.m3', 'Démo · Eau cumulée', 'm³'),
        ('DEMO.Building.Temperature.C', 'Démo · Température', '°C')
)
INSERT INTO "Meters" ("Name", "Label", "Unit", "Type", "Active", "CompanyId")
SELECT name, label, unit, 'main', TRUE, 1
FROM demo_meters AS demo
WHERE NOT EXISTS (
    SELECT 1 FROM "Meters" AS meter
    WHERE meter."CompanyId" = 1 AND meter."Name" = demo.name
);

WITH sample_times AS (
    SELECT moment AS ts
    FROM generate_series(
        timestamp '2024-01-01 00:00:00',
        date_trunc('hour', now() AT TIME ZONE 'Europe/Paris'),
        interval '6 hours'
    ) AS points(moment)
),
daily_pattern AS (
    SELECT ts,
        GREATEST(1.0,
            12.0
            + 7.0 * (1.0 + cos(2.0 * pi() * EXTRACT(DOY FROM ts) / 365.25)) / 2.0
            + 8.0 * GREATEST(0.0, sin(2.0 * pi() * (EXTRACT(HOUR FROM ts) - 6.0) / 24.0))
            - CASE WHEN EXTRACT(ISODOW FROM ts) IN (6, 7) THEN 3.0 ELSE 0.0 END
        ) AS power_kw,
        12.0 + 10.0 * sin(2.0 * pi() * (EXTRACT(DOY FROM ts) - 80.0) / 365.25)
             + 2.0 * sin(2.0 * pi() * (EXTRACT(HOUR FROM ts) - 8.0) / 24.0) AS temperature_c,
        0.12 + 0.15 * GREATEST(0.0, sin(2.0 * pi() * (EXTRACT(HOUR FROM ts) - 6.0) / 24.0))
             + CASE WHEN EXTRACT(ISODOW FROM ts) IN (6, 7) THEN 0.03 ELSE 0.0 END AS water_m3h
    FROM sample_times
),
series AS (
    SELECT ts,
        round(power_kw::numeric, 2) AS power_kw,
        round((10000.0 + sum(power_kw * 6.0) OVER (ORDER BY ts))::numeric, 2) AS energy_kwh,
        round((1000.0 + sum(water_m3h * 6.0) OVER (ORDER BY ts))::numeric, 2) AS water_m3,
        round(temperature_c::numeric, 2) AS temperature_c
    FROM daily_pattern
),
demo_meters AS (
    SELECT DISTINCT ON ("Name") "MeterId", "Name"
    FROM "Meters"
    WHERE "CompanyId" = 1
      AND "Name" IN (
          'DEMO.Building.Power.kW', 'DEMO.Building.Energy.kWh',
          'DEMO.Building.Water.m3', 'DEMO.Building.Temperature.C')
    ORDER BY "Name", "MeterId"
)
INSERT INTO "MeterReadings" ("MeterId", "Timestamp", "Value", "Quality", "CompanyId")
SELECT meter."MeterId", series.ts,
    CASE meter."Name"
        WHEN 'DEMO.Building.Power.kW' THEN series.power_kw
        WHEN 'DEMO.Building.Energy.kWh' THEN series.energy_kwh
        WHEN 'DEMO.Building.Water.m3' THEN series.water_m3
        WHEN 'DEMO.Building.Temperature.C' THEN series.temperature_c
    END,
    192, 1
FROM demo_meters AS meter
CROSS JOIN series
WHERE TRUE
ON CONFLICT ("MeterId", "Timestamp") DO NOTHING;

COMMIT;

SELECT meter."Name", count(*) AS points,
       min(reading."Timestamp") AS first_reading,
       max(reading."Timestamp") AS last_reading
FROM "Meters" AS meter
JOIN "MeterReadings" AS reading ON reading."MeterId" = meter."MeterId"
WHERE meter."CompanyId" = 1
  AND reading."CompanyId" = 1
  AND meter."Name" LIKE 'DEMO.%'
GROUP BY meter."Name"
ORDER BY meter."Name";
