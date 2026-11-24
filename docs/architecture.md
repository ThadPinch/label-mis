# Labels MIS — System Architecture

Tier 1 stack for a narrow-web label shop MIS (6 users, HP Indigo 6800).

```mermaid
flowchart TB
    subgraph clients [Clients]
        Browser[Razor Pages UI]
        Operator[Operator Tablet UI]
        Tools[LabelsMis.Tools CLI]
    end

    subgraph web [LabelsMis.Web]
        Pages[Razor Pages]
        Services[Application Services]
        PDF[QuestPDF Generators]
        Poller[ShipmentTrackingPoller]
    end

    subgraph infra [LabelsMis.Infrastructure]
        EF[EF Core + Npgsql]
        Carrier[WwexCarrierClient]
        Email[LoggingEmailSender]
        Identity[ASP.NET Identity]
    end

    subgraph domain [LabelsMis.Domain]
        Entities[Entities + Enums]
        Engine[Estimating Engine]
        Rules[Domain Rules]
    end

    subgraph external [External]
        PG[(PostgreSQL 16)]
        CarrierAPI[Worldwide Express SpeedShip API - UPS labels]
    end

    Browser --> Pages
    Operator --> Pages
    Tools --> EF
    Pages --> Services
    Services --> domain
    Services --> EF
    Services --> Carrier
    Poller --> Carrier
    Carrier --> CarrierAPI
    EF --> PG
    PDF --> Services
```

## Layer rules

- **Domain** — business entities, estimating engine, carrier/Email interfaces. No EF, no HTTP.
- **Infrastructure** — EF DbContext, migrations, external clients.
- **Web** — Razor Pages, services, PDF, background workers.
- **Tools** — CSV importers for cutover (no UI).

## Document numbering

`PREFIX-YEAR-NUMBER` via the `DocumentSequence` table (one row per type and year): EST, SO, JOB, PO, SHP, INV, ROLL
barcodes. The year is the year the document was made. The number does **not** start again each year: a new year's row
begins at the highest number the type has reached (`DocumentNumberService`), so `INV-2026-00549` is followed by
`INV-2027-00550` and the number alone identifies the document.

## Equipment polymorphism (JobOperation)

`JobOperation.EquipmentType` + `EquipmentId` discriminates Press vs FinishingOperation. Pack/Ship/Inspection use `EquipmentType.None`.
