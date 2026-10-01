-- Synthetic, reproducible presentation data in the PoWorks Default workspace (CompanyId = 1).
-- Not a PcVue archive. Running this script again REPLACES only the four DEMO meters' readings.
-- Weather spells, weekday occupancy, operational changes and occasional peaks create irregular curves.

BEGIN;

WITH definitions(name, label, unit) AS (
    VALUES
        ('DEMO.Building.Power.kW', 'Démo · Puissance bâtiment', 'kW'),
        ('DEMO.Building.Energy.kWh', 'Démo · Énergie cumulée', 'kWh'),
        ('DEMO.Building.Water.m3', 'Démo · Eau cumulée', 'm³'),
        ('DEMO.Building.Temperature.C', 'Démo · Température extérieure', '°C')
)
INSERT INTO "Meters" ("Name", "Label", "Unit", "Type", "Active", "CompanyId")
SELECT name, label, unit, 'main', TRUE, 1
FROM definitions AS demo
WHERE NOT EXISTS (
    SELECT 1 FROM "Meters" AS meter
    WHERE meter."CompanyId" = 1 AND meter."Name" = demo.name
);

WITH definitions(name, label, unit) AS (
    VALUES
        ('DEMO.Building.Power.kW', 'Démo · Puissance bâtiment', 'kW'),
        ('DEMO.Building.Energy.kWh', 'Démo · Énergie cumulée', 'kWh'),
        ('DEMO.Building.Water.m3', 'Démo · Eau cumulée', 'm³'),
        ('DEMO.Building.Temperature.C', 'Démo · Température extérieure', '°C')
)
UPDATE "Meters" AS meter
SET "Label" = demo.label, "Unit" = demo.unit, "Active" = TRUE
FROM definitions AS demo
WHERE meter."CompanyId" = 1 AND meter."Name" = demo.name;

CREATE TEMP TABLE demo_meter_ids ON COMMIT DROP AS
SELECT DISTINCT ON ("Name") "MeterId", "Name"
FROM "Meters"
WHERE "CompanyId" = 1
  AND "Name" IN (
      'DEMO.Building.Power.kW', 'DEMO.Building.Energy.kWh',
      'DEMO.Building.Water.m3', 'DEMO.Building.Temperature.C')
ORDER BY "Name", "MeterId";

-- Rebuild aggregates through the normal insert triggers; UPDATE would leave
-- the old daily, monthly and yearly sine-wave aggregates in place.
DELETE FROM "MeterReadingsDaily" WHERE "MeterId" IN (SELECT "MeterId" FROM demo_meter_ids);
DELETE FROM "MeterReadingsMonthly" WHERE "MeterId" IN (SELECT "MeterId" FROM demo_meter_ids);
DELETE FROM "MeterReadingsYearly" WHERE "MeterId" IN (SELECT "MeterId" FROM demo_meter_ids);
DELETE FROM "MeterReadings" WHERE "MeterId" IN (SELECT "MeterId" FROM demo_meter_ids);

WITH calendar AS (
    SELECT day::date AS day,
           abs(hashtext(day::date::text || ':weather')::bigint) / 2147483648.0 AS weather_seed,
           abs(hashtext(day::date::text || ':activity')::bigint) / 2147483648.0 AS activity_seed
    FROM generate_series(date '2023-12-25',
        (now() AT TIME ZONE 'Europe/Paris')::date, interval '1 day') AS days(day)
),
weather AS (
    SELECT day, activity_seed,
           avg(weather_seed - 0.5) OVER (
               ORDER BY day ROWS BETWEEN 3 PRECEDING AND 3 FOLLOWING
           ) * 24.0 AS weather_shift
    FROM calendar
),
times AS (
    SELECT moment AS ts, weather.activity_seed, weather.weather_shift,
           abs(hashtext(moment::text || ':meter')::bigint) / 2147483648.0 AS meter_seed,
           abs(hashtext(moment::text || ':event')::bigint) / 2147483648.0 AS event_seed
    FROM generate_series(timestamp '2024-01-01 00:00:00',
        date_trunc('hour', now() AT TIME ZONE 'Europe/Paris'),
        interval '3 hours') AS points(moment)
    JOIN weather ON weather.day = moment::date
),
temperatures AS (
    SELECT ts, activity_seed, meter_seed, event_seed,
           12.0 + 10.0 * sin(2.0 * pi() * (EXTRACT(DOY FROM ts) - 80.0) / 365.25)
             + weather_shift
             + CASE EXTRACT(HOUR FROM ts)::int
                 WHEN 0 THEN -1.4 WHEN 3 THEN -2.1 WHEN 6 THEN -2.4
                 WHEN 9 THEN -0.8 WHEN 12 THEN 1.7 WHEN 15 THEN 2.4
                 WHEN 18 THEN 1.1 ELSE -0.4 END
             + CASE WHEN ts::date BETWEEN date '2024-08-05' AND date '2024-08-12'
                    THEN 4.5 ELSE 0.0 END
             + CASE WHEN ts::date BETWEEN date '2025-01-08' AND date '2025-01-16'
                    THEN -5.0 ELSE 0.0 END AS temperature_c
    FROM times
),
operations AS (
    SELECT ts, temperature_c,
           GREATEST(1.0,
               7.5
               + CASE WHEN EXTRACT(ISODOW FROM ts) IN (6, 7)
                   THEN CASE WHEN EXTRACT(HOUR FROM ts) BETWEEN 9 AND 18 THEN 4.0 ELSE 1.0 END
                   ELSE CASE WHEN EXTRACT(HOUR FROM ts) BETWEEN 6 AND 18 THEN 13.0
                             WHEN EXTRACT(HOUR FROM ts) = 21 THEN 5.0 ELSE 2.0 END
                 END
               + GREATEST(0.0, 16.0 - temperature_c) * 0.75
               + GREATEST(0.0, temperature_c - 25.0) * 0.90
               + (activity_seed - 0.5) * 12.0
               + (meter_seed - 0.5) * 7.0
               + CASE WHEN event_seed > 0.995 THEN 18.0 ELSE 0.0 END
               - CASE WHEN event_seed < 0.008 THEN 8.0 ELSE 0.0 END
               - CASE WHEN ts::date BETWEEN date '2025-07-14' AND date '2025-08-17'
                      THEN 5.0 ELSE 0.0 END
               + CASE WHEN ts::date >= date '2026-02-01' THEN 2.5 ELSE 0.0 END
           ) AS power_kw,
           GREATEST(0.02,
               0.06
               + CASE WHEN EXTRACT(ISODOW FROM ts) NOT IN (6, 7)
                           AND EXTRACT(HOUR FROM ts) BETWEEN 6 AND 18 THEN 0.17 ELSE 0.03 END
               + activity_seed * 0.07 + meter_seed * 0.04
               + CASE WHEN ts::date BETWEEN date '2025-03-10' AND date '2025-03-26'
                      THEN 0.16 ELSE 0.0 END
           ) AS water_m3h
    FROM temperatures
),
series AS (
    SELECT ts,
           round(power_kw::numeric, 2) AS power_kw,
           round((10000.0 + sum(power_kw * 3.0) OVER (ORDER BY ts))::numeric, 2) AS energy_kwh,
           round((1000.0 + sum(water_m3h * 3.0) OVER (ORDER BY ts))::numeric, 2) AS water_m3,
           round(temperature_c::numeric, 2) AS temperature_c
    FROM operations
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
FROM demo_meter_ids AS meter
CROSS JOIN series
WHERE TRUE
ON CONFLICT ("MeterId", "Timestamp") DO NOTHING;

COMMIT;

SELECT meter."Name", count(*) AS points,
       min(reading."Timestamp") AS first_reading,
       max(reading."Timestamp") AS last_reading
FROM "Meters" AS meter
JOIN "MeterReadings" AS reading ON reading."MeterId" = meter."MeterId"
WHERE meter."CompanyId" = 1 AND reading."CompanyId" = 1
  AND meter."Name" LIKE 'DEMO.%'
GROUP BY meter."Name"
ORDER BY meter."Name";
