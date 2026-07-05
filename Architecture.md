# ArXiv Aggregator (PaperAggro) — Architecture

## What this is

PaperAggro is a self-hosted AI news aggregator. It crawls RSS/Atom feeds and the
arXiv API on a schedule, categorizes and deduplicates what it finds, stores it in
SQLite, serves a browsable web UI, and emails daily and weekly digests to
subscribers. It is designed to run as a single process on a personal machine with
no external infrastructure — no Docker, no cloud database, no message queue.

This is the third incarnation of the project. The original inspiration was Zain
Ahmad's Python script that summarized arXiv abstracts and emailed them daily. A Go
version generalized that idea to many sources with concurrent collection and
MongoDB Atlas storage. This .NET 10 version adds the thing the previous two
lacked: a real user interface for browsing, searching, and configuring the
aggregator while it runs.

## Runtime model

The application is a single ASP.NET Core process built from the Blazor Web App
template using the Interactive Server render mode. That choice is deliberate and
worth understanding, because it is the opposite of a typical Blazor WASM
deployment.

Interactive Server means every UI interaction round-trips over a SignalR
connection to the server, and all component code executes server-side. In
exchange, the UI shares a process with the background scheduler, the SQLite
database, and the SMTP client. One `dotnet run` starts everything. Blazor
WebAssembly (including the Standalone template that sits one row below Blazor
Web App in your IDE's new-project dialog) cannot host this application: WASM has
no server process, so nothing would run the scheduled crawls, nothing could open
an SMTP connection, and there is no disk for SQLite.

For the same reason, this application cannot be deployed to Azure Static Web
Apps. SWA serves static assets only. Viable hosts are anything that runs a
persistent .NET process: a local machine, a home server, a VPS, or Azure App
Service at the Basic tier or above (Always On must be enabled, or the scheduler
dies when the app idles out; the SQLite file must live under the persisted
/home volume).

Because the scheduler lives inside the app process, the app is the
infrastructure: if the process is stopped, nothing collects and nothing sends.
The schedule logic is written forgivingly — "past the digest time and not yet
sent today," never "exactly at the digest time" — so a machine that was asleep
or an app started late sends the digest on the next timer tick rather than
skipping the day.

## Configuration and secrets

Configuration and secrets are deliberately split by who should hold them.

Configuration a user changes — feed sources, collection interval, digest
schedule, SMTP host/port/user — lives in the database and is editable from the
/settings page at runtime. The scheduler re-reads it every tick, so changes
apply without a restart.

Secrets — the SMTP app password and the optional OpenAI API key — live only in
environment variables, loaded at startup from a `.env` file via the DotNetEnv
package (`Env.TraversePath().Load()` as the first line of Program.cs). They are
never written to the database, so copying `paperaggro.db` exposes a feed list,
not credentials. The `.env` file is gitignored; `.env.example` documents its
shape. .NET's native alternative, user-secrets, works too — the services check
`IConfiguration` before falling back to environment variables — but `.env` was
chosen as the documented path because it is the convention readers arrive
already knowing.

## Components

**Program.cs** wires everything through the standard dependency injection
container. Services are registered as singletons and receive an
`IDbContextFactory<AppDbContext>` rather than a scoped DbContext — the factory
pattern is what allows the same services to be consumed safely by both Blazor
components and the singleton background service. On startup it ensures the
database exists and seeds the default settings row and six feed sources on
first run.

**CollectorService** fetches all enabled feed sources in parallel via
`Task.WhenAll`, the moral equivalent of the Go version's goroutines. A single
parser, `System.ServiceModel.Syndication`, handles both RSS 2.0 and Atom, which
matters because the arXiv API returns Atom. For arXiv entries the service
derives a direct PDF link from the abstract URL (the /abs/ to /pdf/
convention). Each source records its last run time, item count, and last error
to the database, so the settings page doubles as a feed health dashboard. A
failing feed logs a warning and surfaces as a red row in the UI; it never
crashes a collection run.

**CategoryService** classifies each article by keyword matching against the
title and description, producing a category (OpenAI, Anthropic/Claude,
Google/Gemini, RAG & Retrieval, Agents & Agentic, Research Papers, General AI),
a tag list, and a deep-dive flag. Deep-dive marks arXiv papers whose abstracts
contain novelty signals ("state-of-the-art", "we introduce", "outperform") —
these are surfaced first in digests and exported weekly as a JSON manifest of
PDF links for ingestion by a separate RAG-based paper Q&A tool.

**SummaryService** is the optional OpenAI integration. If an API key is present
(configuration key OpenAI:ApiKey or the OPENAI_API_KEY environment variable),
it condenses deep-dive abstracts into two bullet points using gpt-4o-mini. If
no key is present it returns null and the app falls back to the feed's own
description. The application is fully functional without any OpenAI account.

**DigestService** builds the HTML digest (deep-dive section first, then
articles grouped by category), sends it via MailKit over SMTP with subscribers
on BCC, and writes the weekly deep-dive manifest to papers_for_deep_dive/.
MailKit is used because System.Net.Mail.SmtpClient is deprecated. The send
path has three silent early-returns — no recent articles, no active
subscribers, no SMTP password — each of which logs and skips rather than
errors. Because silent success is indistinguishable from silent failure during
setup, the settings page includes a "Send test digest" button that fires the
real pipeline immediately with a two-day window and reports the outcome inline.

**SchedulerService** is a `BackgroundService` driven by a one-minute
`PeriodicTimer`. Each tick it re-reads the settings row, then triggers
collection every N hours, the daily digest at the configured time, and the
weekly digest plus manifest export on the configured day. This replaces the
external cron dependency the Go version carried. Its "already sent today"
state is held in memory, which is the source of one known limitation below.

## Data model

SQLite via EF Core, one file (paperaggro.db), four tables.

**Articles** holds the collected content. The ExternalId column (the article
link) carries a unique index, which is the entire deduplication mechanism —
the insert path checks for an existing ExternalId before adding, replacing the
history.txt file of earlier versions. Secondary indexes on Category and
PublishedAt support the UI's tab filtering and ordering.

**Feeds** and **Settings** hold the runtime-editable configuration described
above. Settings is a single-row table.

**Subscribers** holds digest recipients with a unique email index, populated
from the /subscribe page. The digest is skipped when this table has no active
rows — the most common first-run surprise, since the sending account
(Settings) and the receiving address (Subscribers) are configured in two
different places even when they are the same address.

The schema is created with EnsureCreated(), which is appropriate for a
personal tool but does not migrate existing databases. Schema changes require
either deleting the database file or switching to EF migrations.

## UI surface

Three pages, all Interactive Server. Home (/) is the reader: category tabs,
live search over titles and descriptions, deep-dive highlighting, PDF badges,
and a manual "Collect now" trigger. Subscribe (/subscribe) manages digest
signup. Settings (/settings) edits the collection interval, digest schedule,
and SMTP configuration, manages the feed list (add, remove, per-feed
enable/disable, health status, immediate validation of new URLs by triggering
a collection), and hosts the test digest button.

## Known limitations

Keyword categorization is crude by design; it mislabels occasionally and that
is acceptable for a skimming tool. Feed URLs rot — vendor blogs move their RSS
paths, which is why per-feed error reporting exists. The scheduler's
"already sent today" flags live in memory, so restarting the app around a
digest boundary can skip or repeat one send. SQLite constrains this to a
single process; running two instances against one database file is
unsupported. The test digest button sends to all active subscribers, not just
the operator. None of these are worth fixing for a personal tool; all of them
would need fixing before multi-user hosting, at which point the Go/MongoDB
variant is the better starting point.
