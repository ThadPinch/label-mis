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
        Fedex[SandboxFedexClient]
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
        FedexAPI[FedEx API - sandbox/prod]
    end

    Browser --> Pages
    Operator --> Pages
    Tools --> EF
    Pages --> Services
    Services --> domain
    Services --> EF
    Poller --> Fedex
    Fedex --> FedexAPI
    EF --> PG
    PDF --> Services
```

## Layer rules

- **Domain** — business entities, estimating engine, FedEx/Email interfaces. No EF, no HTTP.
- **Infrastructure** — EF DbContext, migrations, external clients.
- **Web** — Razor Pages, services, PDF, background workers.
- **Tools** — CSV importers for cutover (no UI).

## Document numbering

Year-sequenced via `DocumentSequence` table: EST, SO, JOB, PO, SHP, INV, ROLL barcodes.

## Equipment polymorphism (JobOperation)

`JobOperation.EquipmentType` + `EquipmentId` discriminates Press vs FinishingOperation. Pack/Ship/Inspection use `EquipmentType.None`.
