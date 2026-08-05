# NServiceBus SQL Server Transport — Proof of Concept Recommendation

My short take:

> SQL Server transport is an excellent "boring infrastructure" choice for moderate-throughput business systems that already depend on SQL Server. RabbitMQ, Azure Service Bus, Amazon SQS, and similar brokers remain better when messaging performance, failure isolation, elastic scale, low latency, or broker-native capabilities are first-class requirements.

## SQL Server versus a dedicated or managed broker

| Concern | SQL Server transport | RabbitMQ / Azure Service Bus / Amazon SQS / similar |
|---|---|---|
| Infrastructure | Reuses SQL Server or Azure SQL | Additional broker or cloud service |
| Operations | Familiar backups, HA, security, monitoring, and SQL tooling | Broker- or cloud-specific operational knowledge |
| Latency | Polling; an idle queue defaults to up to roughly one second | Generally event-driven or long-polled and better suited to low latency |
| Throughput | Shares finite SQL Server capacity across all endpoints | Designed and priced to scale messaging independently |
| Data consistency | Exceptional when business data, persistence, and queues share a catalog | Usually requires the NServiceBus Outbox or distributed coordination |
| Failure isolation | SQL Server trouble stops application data and messaging | Broker and database fail independently |
| Pub/sub | Native through a shared subscription-routing table | Native exchanges, topics, or service-specific fan-out facilities |
| Delayed delivery | Native through endpoint-owned delayed-message tables | Native, although limits and semantics vary by broker |
| Scaling | Competing consumers; primarily scale-up at the database tier | Usually stronger messaging-oriented horizontal or managed scaling |
| Routing/topology | Queue tables, schemas, catalogs, and subscription rows | Exchanges, topics, filters, partitions, dead-letter facilities, policies, etc. |
| External interoperability | Primarily NServiceBus and SQL-oriented integrations | Often broader protocols, SDKs, languages, and ecosystem support |
| Cost | Potentially no new infrastructure bill when SQL Server capacity is available | Additional hosting, licensing, requests, transfer, or operational cost |

This is not an argument that SQL Server is a better message broker in the abstract. It is an argument that, for a SQL Server-only organization, the marginal value of adding another distributed system may be smaller than the marginal value of keeping transport and application work in one mature transactional platform.

## The big advantages

### 1. One less distributed system

If SQL Server is already mandatory and well operated, using it for transport means:

- Fewer credentials, upgrades, dashboards, alerts, and HA procedures
- No RabbitMQ cluster or additional cloud messaging service in development and CI
- Less vendor-specific knowledge for application and operations teams
- Familiar diagnosis through SQL Server Management Studio and existing monitoring
- Potentially no additional infrastructure licensing or consumption bill

That is a substantial advantage for a team whose database is already the center of gravity.

You would still need NServiceBus licensing. "No additional licensing" refers to transport infrastructure, and sufficient production SQL Server capacity may itself require paid editions or a larger managed-service tier. Particular recommends a commercial SQL Server edition and an appropriate support arrangement for production, even though the transport can run on SQL Server Express. See the [SQL Server transport overview](https://docs.particular.net/transports/sql/).

### 2. Really strong transactional consistency

This is the killer feature.

Each queue is a SQL Server table. Sending inserts a row; receiving deletes and returns the lowest unlocked row inside a transaction. In `SendsAtomicWithReceive` mode, consuming the incoming message and producing outgoing messages commit together. See the [transport design](https://docs.particular.net/transports/sql/design) and [transaction support](https://docs.particular.net/transports/sql/transactions).

When transport, SQL Persistence, and business data are deliberately aligned, a handler can atomically:

1. Consume the incoming message
2. Change saga and business state
3. Enqueue outgoing messages
4. Commit everything—or none of it

With one SQL Server catalog, this can be achieved with a local database transaction rather than a distributed transaction. Across storage boundaries, SQL Server transport also supports `TransactionScope` and MSDTC, while the NServiceBus Outbox offers an alternative that does not require transport data to share the business-data store.

That flexibility is stronger than the usual database-plus-broker dual write, but the topology and transaction mode have to be designed together. The valid connection-sharing combinations are specific; enabling the Outbox does not merely add another guarantee to every transaction mode. See [combining SQL Persistence and transport](https://docs.particular.net/persistence/sql/sqlserver-combining-persistence-wth-transport).

The guarantee also covers only work enlisted in the transaction. HTTP calls, email, object storage, or writes to unrelated systems still require idempotency, an Outbox-style boundary, or another explicit consistency design.

### 3. It fits ordinary business workloads very well

Orders, invoicing, provisioning, workflows, sagas, notifications, and domain events commonly tolerate latency measured in hundreds of milliseconds or seconds. For those workloads, SQL Server already provides mature:

- Durable storage and ACID transactions
- Backup, restore, replication, and failover
- Access control, auditing, and encryption options
- On-premises and managed Azure hosting
- Operational and performance tooling

Queue tables are unusually inspectable. Operators can see backlog data with familiar SQL tools, although manual modification should be treated with the same care as manipulating broker internals.

### 4. It includes the important NServiceBus transport capabilities

The current SQL Server transport supports:

- Native publish/subscribe
- Native delayed delivery and timeouts
- Competing consumers
- All NServiceBus transport transaction modes
- SQL-scripted deployment and optional installers
- Native integration for non-NServiceBus senders
- Arbitrary message sizes within available SQL Server resources, with the Data Bus recommended for very large payloads
- SQL Server, Azure SQL, and Microsoft Entra ID authentication

Native pub/sub uses a common subscription-routing table. Native delayed delivery uses a dedicated table owned by the sending endpoint and moves due messages to destination queues in batches. See [native publish/subscribe](https://docs.particular.net/transports/sql/native-publish-subscribe) and [native delayed delivery](https://docs.particular.net/transports/sql/native-delayed-delivery).

## The significant downsides

### 1. SQL Server becomes an even larger blast radius

If one SQL Server deployment hosts business data, saga state, subscriptions, and queues, a database outage can stop everything:

- No business reads or writes
- No message sends
- No message processing
- No local store-and-forward buffer

A separate RabbitMQ cluster, Azure Service Bus namespace, or SQS service creates an independent messaging failure domain. It may accept and retain work while an application database is unavailable, allowing downstream processing to resume from a backlog.

The other side is that a service which cannot access its database may be unable to do useful work anyway. Whether the extra failure domain is valuable depends on the service, not on an abstract preference for more infrastructure.

### 2. Messaging competes with business queries

Queue traffic consumes the same finite resources as ordinary persistence:

- Connections and worker capacity
- CPU and I/O
- Transaction-log bandwidth
- Locks, memory, and buffer cache
- Replication and backup capacity

Queue tables experience constant inserts and deletes. Backlogs consume database storage and can enlarge backup, recovery, maintenance, and failover concerns.

Particular explicitly warns that centralized throughput belongs to the whole SQL Server deployment, not to every endpoint independently. Adding endpoints divides shared capacity. A dedicated or managed broker is designed to absorb, partition, and drain message traffic without contending directly with application queries.

### 3. Polling trades latency for database load

An empty queue is polled. The default idle peek interval is one second, with a recommended range from 100 milliseconds to 10 seconds. Shortening it reduces wake-up latency but increases database activity. See the [receiving design](https://docs.particular.net/transports/sql/design).

Once messages are present, the transport estimates the backlog and starts concurrent receive operations, so it is not limited to one message per polling interval. The concern is principally how quickly an idle endpoint wakes and how much background work many mostly idle queues generate.

RabbitMQ, Azure Service Bus, SQS, and similar services are preferable when:

- Consistently low idle-to-processing latency matters
- Bursty workloads must drain rapidly
- Sustained throughput is high
- There are many mostly idle endpoints
- Messaging must scale without scaling the transactional database

### 4. A broker has a richer messaging control plane

NServiceBus intentionally provides an abstraction over transport-specific facilities, and SQL Server transport has the essentials. It does not turn SQL Server into a full broker platform.

Dedicated and managed brokers commonly provide combinations of:

- Topic filters and routing rules
- Partitions and explicit throughput units
- Broker policies and dead-letter controls
- Cross-region or cross-account messaging
- Protocol interoperability and first-party SDKs across languages
- Service-native identity, diagnostics, and autoscaling integrations
- Retention or replay capabilities beyond an endpoint queue

The exact comparison differs: RabbitMQ offers exchange-driven routing and operational control; Azure Service Bus offers managed topics, subscriptions, filters, sessions, and Azure integration; SQS offers a highly managed AWS queue with elastic scale but uses SNS or EventBridge for richer fan-out. The important point is that those products are messaging infrastructure, while SQL Server transport is queueing implemented on a relational database.

### 5. Catalog topology and transaction choices are coupled

SQL Server transport supports single-schema, multi-schema, and multi-catalog deployments. This is useful, but spreading endpoint data across catalogs or instances changes the consistency story.

- One catalog is the simplest route to local atomicity and a coherent backup.
- Multiple schemas improve ownership and permissions without necessarily requiring DTC.
- Multiple catalogs improve separation, but business-data coordination may require MSDTC or the Outbox depending on the processing boundary.
- A dedicated transport database improves workload and backup isolation but gives up some of the strongest single-transaction simplicity.

MSDTC is a real capability, not a free default. It adds deployment, networking, security, observability, and failure-recovery complexity, and Azure environments have their own distributed-transaction constraints. Prefer a local transaction when the service boundary genuinely fits one catalog; prefer the Outbox when it does not. See [deployment options](https://docs.particular.net/transports/sql/deployment-options).

### 6. Connection pools need explicit sizing

The transport uses ADO.NET pooling, which may be shared with persistence and business logic in the same process. Endpoint concurrency can therefore exhaust the pool or crowd out ordinary queries. Particular logs a warning when maximum pool size is not explicit. See [connection settings](https://docs.particular.net/transports/sql/connection-settings).

Capacity planning must include:

- Endpoint and instance concurrency
- Handler database access
- Saga and Outbox activity
- Delayed-delivery processing
- Error and audit traffic
- Failover reconnection behavior

### 7. Pub/sub has database-shaped compromises

All participating endpoints share one subscription-routing table. Publishers query it and insert an event into each subscriber queue.

Subscription data is cached for five seconds by default. A newly added or dynamic subscriber can therefore miss events during the cache window. Shortening or disabling the cache increases database reads. All endpoints must also agree on the subscription table's catalog, schema, and name.

This is usually acceptable when subscriptions are stable at deployment time, but it is less natural for highly dynamic subscriptions or sophisticated event filtering.

### 8. Ordering and retry behavior remain nuanced

The receiver selects the lowest unlocked `RowVersion`, which establishes FIFO order at acquisition. Multiple concurrent handlers or endpoint instances can still complete out of order. The queue's identity column should not be mistaken for strict end-to-end FIFO processing.

On handler failure, retry information is initially held in the processing node's memory because the receive transaction must roll back. With multiple endpoint instances, failure observations are distributed. Particular warns that this can cause more retries than configured or an immediate retry on another node even when immediate retries are disabled.

These behaviors are manageable, but they belong in the POC's concurrency and failure tests.

### 9. Azure SQL is supported, but it does not erase the trade-offs

Azure SQL removes much of the server administration, but queue work still consumes the same provisioned database resources and still polls. Particular's published throughput figures are useful only as rough examples and explicitly are not representative of most real systems. Capacity must be tested with Ratatoskr's handler work, endpoint count, topology, and service tier. See [SQL Server transport in Azure SQL](https://docs.particular.net/transports/sql/sql-azure).

Azure SQL failover can also leave stale connections in client pools, so failover and reconnection deserve explicit testing rather than an assumption that a managed database makes them invisible.

## How it compares to the common alternatives

### RabbitMQ

Choose RabbitMQ when low latency, burst absorption, flexible routing, messaging isolation, or broker control matters enough to operate it. Choose SQL Server transport when RabbitMQ would be a second stateful platform added mainly to support moderate business-process messaging and the team already has excellent SQL Server operations.

RabbitMQ is the stronger broker. SQL Server may be the stronger whole-system simplification.

### Azure Service Bus

Choose Azure Service Bus for a managed Azure messaging boundary, independent scaling, topics and filters, service isolation, and cloud-native integration. Choose SQL Server transport when the workload and data already live naturally in SQL Server or Azure SQL and local transactional consistency is more valuable than a separate managed failure domain.

Azure Service Bus reduces broker operations, but it does not remove the distributed-system boundary or the dual-write problem with SQL business data.

### Amazon SQS

Choose SQS for an AWS-native, highly managed, elastic queueing service with minimal broker administration. It is especially appealing when consumers, accounts, or data stores are distributed throughout AWS. Choose SQL Server transport when the service is fundamentally SQL Server-centric and introducing AWS messaging would create a new platform and consistency boundary.

SQS is deliberately simpler than a traditional broker; richer pub/sub generally involves SNS or EventBridge. Its operational simplicity therefore narrows one SQL Server advantage, while SQL Server's local transaction remains the differentiator.

### Other managed brokers

The same decision applies to Google Pub/Sub, Amazon MQ, hosted RabbitMQ, and similar products: they buy independent capacity, failure isolation, and messaging-native features. SQL Server transport buys platform consolidation and unusually direct transactional alignment. The right choice follows the system's dominant constraint.

## When I would choose it

I would be enthusiastic about SQL Server transport when:

- SQL Server or Azure SQL is already a hard dependency
- The organization wants to stay primarily or entirely on the Microsoft data stack
- The workload is business-process messaging rather than streaming
- Moderate latency is acceptable
- Aggregate throughput has been load-tested with substantial headroom
- Atomic business-state and outgoing-message updates have real value
- Endpoint ownership maps cleanly to catalogs or schemas
- Operational simplicity is more valuable than maximum broker capability
- The team already understands SQL Server performance, security, backup, and HA

I would lean toward RabbitMQ, Azure Service Bus, SQS, or another dedicated service when:

- Sub-100-millisecond idle-to-processing latency matters
- Volume is high, highly variable, or extremely bursty
- A backlog must not affect the transactional database
- Independent database and messaging failure domains matter
- Messaging must scale or be paid for independently
- There are many endpoints, extensive fan-out, or dynamic subscriptions
- Non-NServiceBus systems need standard protocols or first-party SDKs
- Advanced routing, filtering, partitioning, replay, or geographic messaging matters
- The organization already operates the broker well, eliminating the consolidation advantage

## My recommendation for Ratatoskr

SQL Server transport is a compelling default to prove for Ratatoskr specifically because Ratatoskr represents the SQL Server side of the same architectural choice that makes PostgreSQL transport attractive to PostgreSQL-only teams.

The thesis is straightforward:

> A team that has already chosen SQL Server should not be required to adopt RabbitMQ, Azure Service Bus, or SQS merely to obtain durable NServiceBus messaging at ordinary business-system scale.

The first proof of concept should use a single SQL Server catalog, with separate schemas where useful for ownership, and align transport, SQL Persistence, and representative business writes so the local-transaction advantage is tested honestly. That is the configuration with the clearest reason to choose this transport.

It should then evaluate a dedicated queue catalog as the principal alternative. That configuration improves workload, backup, and failure isolation, but it changes the consistency mechanism and may bring the Outbox or MSDTC into the design. MSDTC should be demonstrated only if Ratatoskr actually intends to support that operational model; it should not be adopted simply because SQL Server transport can use it.

The deciding experiments should measure:

- Idle-message latency at the chosen peek interval
- Sustained and burst throughput across multiple endpoints
- Recovery time and business-query impact after a large backlog
- CPU, I/O, transaction-log, storage, lock, and connection-pool pressure
- Failover during message handling, including Azure SQL connection-pool behavior if applicable
- Retry behavior with multiple endpoint replicas
- Native pub/sub and delayed-delivery behavior
- Local transaction rollback across receive, business data, saga state, and outgoing messages
- Operational deployment through generated SQL scripts with least-privilege principals

For a normal SQL Server-backed transactional system, I expect the result to mirror the PostgreSQL conclusion: use the database transport until measured requirements prove that a specialized broker provides value worth its additional boundary. That is a pleasantly boring default—and exactly the kind of option Ratatoskr should make easy.
