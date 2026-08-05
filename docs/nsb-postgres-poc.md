  My short take:

  > PostgreSQL transport is an excellent “boring infrastructure” choice for moderate-throughput business systems. A dedicated broker remains better when
  > messaging performance, isolation, latency, or topology sophistication are first-class requirements.

  ## PostgreSQL versus a dedicated broker

   Concern                      PostgreSQL transport                                             RabbitMQ / Azure Service Bus / similar
  ━━━━━━━━━━━━━━━━━━━━━━━━━━━  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
   Infrastructure               Reuses PostgreSQL                                                Additional service
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Operations                   Familiar backups, HA, security, tooling                          Broker-specific operational knowledge
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Latency                      Polling; idle queues default to up to ~1 second                  Generally push/event-driven and lower latency
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Throughput                   Shares database capacity                                         Designed specifically for messaging workloads
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Data consistency             Exceptional when business data and messaging share PostgreSQL    Usually requires NServiceBus Outbox or distributed coordination
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Failure isolation            Database trouble stops application data and messaging            Broker and database fail independently
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Pub/sub                      Supported through a shared subscription table                    Usually a core broker capability
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Scaling                      Competing consumers, but ultimately bounded by PostgreSQL        Usually stronger messaging-oriented scaling options
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Routing/topology             Queue tables and subscription records                            Exchanges, topics, filters, partitions, dead-letter facilities,
                                                                                                 etc.
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   External interoperability    Primarily NServiceBus/database-oriented                          Often broader protocol and ecosystem support
  ───────────────────────────  ───────────────────────────────────────────────────────────────  ─────────────────────────────────────────────────────────────────
   Cost                         Potentially no extra infrastructure bill                         Additional hosted service or operational footprint

  ## The big advantages

  ### 1. One less distributed system

  If PostgreSQL is already mandatory, eliminating RabbitMQ or a cloud broker means:

  - Fewer credentials, upgrades, dashboards, alerts, and backup/HA procedures
  - Less infrastructure in development and CI
  - Fewer failure modes for the team to understand
  - No separate broker licensing or hosting cost

  That is a substantial advantage, not just aesthetic simplicity.

  You would still need NServiceBus licensing; “no additional licensing” refers to the PostgreSQL transport infrastructure.

  ### 2. Really strong transactional consistency

  This is the killer feature.

  The transport receives a message by deleting its row within a PostgreSQL transaction. Sends and publishes are inserts into destination queue tables. In
  SendsAtomicWithReceive mode, consuming the input message and producing outgoing messages commit together. Transaction documentation
  (https://docs.particular.net/transports/postgresql/transactions)

  When combined with PostgreSQL SQL Persistence, NServiceBus can also share the connection and transaction with saga state. That lets it atomically:

  1. Consume the incoming message
  2. Change saga/business state
  3. Enqueue outgoing messages
  4. Commit everything—or none of it

  This removes an enormous class of dual-write problems without a distributed transaction. Persistence/transport integration
  (https://docs.particular.net/persistence/sql/postgresql-combining-persistence-with-transport)

  Important nuance: this guarantee only covers work participating in that PostgreSQL transaction. Calling an HTTP service, writing to another database, or
  sending email still requires idempotency or an outbox-style design.

  ### 3. Excellent fit for ordinary business workloads

  For workloads like orders, invoicing, provisioning, workflows, sagas, notifications, and domain events, message latency is commonly measured in hundreds of
  milliseconds or seconds—not microseconds.

  PostgreSQL is extremely mature at:

  - Durable storage
  - ACID transactions
  - Replication and failover
  - Access control
  - Managed hosting
  - Backup and disaster recovery

  Queue tables are also unusually inspectable. Operators can use familiar SQL tooling—though manual modifications should be treated with the same care as
  directly manipulating broker internals.

  ### 4. It has the important NServiceBus capabilities

  The current transport supports:

  - Native publish/subscribe
  - Native delayed delivery/timeouts
  - Competing consumers
  - Atomic send-with-receive
  - Queue creation through deployment scripts
  - Large message bodies, with the Data Bus recommended for very large payloads

  The implementation uses a table per queue. Sending is an INSERT; receiving is effectively a transactional DELETE of the lowest available sequence that another
  consumer has not locked. Transport design (https://docs.particular.net/transports/postgresql/design)

  ## The significant downsides

  ### 1. PostgreSQL becomes an even larger blast radius

  If one PostgreSQL deployment hosts business data, saga state, subscriptions, and queues, then a database outage stops everything:

  - No reads or writes
  - No message sends
  - No message processing
  - No local store-and-forward buffer

  A separate broker gives partial failure isolation. For example, applications may continue accepting work into a broker while a downstream database is
  unavailable.

  The other side of this argument is that tightly coupled data and message operations often cannot do useful work without PostgreSQL anyway. Whether isolation
  helps depends on the service.

  ### 2. Messaging competes with business queries

  Queue traffic consumes the same finite resources as normal persistence:

  - Connections
  - CPU
  - I/O and WAL bandwidth
  - Locks
  - Buffer cache
  - Replication bandwidth
  - Vacuum capacity

  Queue tables experience constant insert/delete churn, which can create dead tuples and autovacuum pressure. A message backlog also becomes a database-storage
  and backup concern.

  Particular explicitly notes that centralized throughput is shared across every endpoint. If the database sustains a certain aggregate message rate, adding
  endpoints divides that capacity rather than creating independent broker capacity.

  At sufficient scale, “we avoided operating a broker” can turn into “we made the primary database much harder to operate.”

  ### 3. Polling introduces latency and background load

  The transport polls empty queues. The default idle peek interval is one second; supported tuning is generally 100 ms to 10 seconds. Faster polling reduces
  latency but increases database load. Polling design (https://docs.particular.net/transports/postgresql/design)

  RabbitMQ and similar brokers are built around efficient message notification and delivery. They are preferable for:

  - Consistently low latency
  - Bursty workloads requiring rapid drain
  - Very high sustained throughput
  - Large numbers of mostly idle queues
  - Real-time fan-out

  Once messages are flowing, PostgreSQL’s receiver ramps up concurrent receive operations, so this is not simply “one message per polling interval.” The latency
  concern is mainly waking an idle queue.

  ### 4. Connection counts need active management

  Every endpoint and processing lane ultimately interacts with PostgreSQL. Npgsql pooling helps, but PostgreSQL commonly defaults to 100 server connections.

  Larger deployments may require:

  - Explicit pool sizing
  - PgBouncer or a managed proxy
  - Careful endpoint concurrency settings
  - Coordination with ordinary application connection usage

  Increasing max_connections indiscriminately is usually not the right answer. Particular calls this out explicitly in its deployment considerations
  (https://docs.particular.net/transports/postgresql/).

  ### 5. Pub/sub has database-shaped compromises

  Native pub/sub uses one shared subscription-routing table. Publishers query that table and insert an event into each subscriber’s queue.

  Subscription data is cached for five seconds by default. Consequently, a newly added subscriber can miss events during the cache window. You can shorten or
  disable caching, but that increases database reads. Native pub/sub documentation (https://docs.particular.net/transports/postgresql/native-publish-subscribe)

  That is likely harmless when subscriptions are stable at deployment time, but it matters for dynamic subscription scenarios.

  ### 6. Ordering remains nuanced

  Rows have an increasing sequence and receivers select the lowest unlocked sequence. That provides queue ordering at acquisition time.

  However, with concurrent handlers or multiple endpoint instances, completion order is not guaranteed. This is broadly true of competing-consumer transports,
  but the presence of a sequence column should not be mistaken for strict end-to-end FIFO processing.

  ### 7. Retry behavior under scale-out is slightly surprising

  Failure information is initially held in memory because the receive transaction must roll back. With multiple endpoint instances, retry observations are
  distributed across nodes. Particular warns that this can produce more retries than configured or cause immediate retry behavior on a different node.

  That is manageable, but worth including in failure testing rather than discovering during an incident.

  ### 8. Transaction modes require deliberate configuration

  The good mode is generally SendsAtomicWithReceive.

  ReceiveOnly makes receiving transactional but does not atomically include outgoing messages, so persistent side effects or “ghost messages” are possible unless
  the Outbox handles consistency.

  TransactionScope is deliberately unsupported because the Npgsql behavior can result in logical message loss. “No transactions” removes the message before
  handling and can therefore lose it if processing fails. Transaction modes (https://docs.particular.net/transports/postgresql/transactions)

  Also, the valid persistence/transport combinations are more constrained than the elevator pitch suggests. For example, SQL Persistence with the Outbox enabled
  supports ReceiveOnly, while its documented combination with SendsAtomicWithReceive is not supported.

  ## When I would choose it

  I would be enthusiastic about PostgreSQL transport when:

  - PostgreSQL is already a hard dependency
  - The workload is business-process messaging rather than streaming
  - Moderate latency is acceptable
  - Aggregate throughput has been load-tested with plenty of headroom
  - The same service owns its queue and persistence schema
  - Atomic business-state/message updates offer real value
  - Operational simplicity is more valuable than maximum broker capability
  - The team understands PostgreSQL performance and HA well

  I would lean toward a dedicated broker when:

  - Sub-100-ms idle-to-processing latency matters
  - Messaging volume is high or extremely bursty
  - A backlog must not affect the transactional database
  - Independent database and messaging failure domains matter
  - There are many queues/endpoints or extensive fan-out
  - Non-NServiceBus systems need standard broker protocols
  - Advanced routing, partitioning, filtering, or geographic messaging matters
  - Teams must scale messaging independently of application persistence

  ## My recommendation for Bifrost

  This looks worth a serious proof of concept. Given Bifrost’s apparent PostgreSQL and transactional persistence direction, the ability to place saga state,
  business changes, message consumption, and outgoing messages into one native PostgreSQL transaction is unusually aligned.

  I would not initially place the queue tables in the same database deployment as high-value production data merely because the transport permits it. I would
  evaluate two configurations:

  1. Same PostgreSQL database: maximum transactional simplicity and minimum infrastructure.
  2. Separate PostgreSQL messaging database/cluster: better workload and failure isolation, but gives up much of the shared-transaction advantage and may bring
     the Outbox back into the design.

  The deciding experiment should measure:

  - Idle-message latency at the chosen peek interval
  - Sustained and burst throughput
  - Database CPU, WAL, I/O, connection use, and autovacuum behavior
  - Recovery after a large backlog
  - PostgreSQL failover during message handling
  - Retry behavior with multiple endpoint replicas
  - Impact on ordinary application-query latency

  For a normal transactional system, I suspect the result will be: use PostgreSQL transport until measured requirements prove you need a specialized broker. That
  is a pleasantly boring default.