# ArXiv Aggregator (PaperAggro)

A self-hosted AI news aggregator, built with .NET 10 and Blazor. It crawls
RSS/Atom feeds and the arXiv API, categorizes articles by the vocabulary that
actually matters (OpenAI, Claude, Gemini, RAG, agentic), flags papers worth a
deep dive with direct PDF links, and emails you daily and weekly digests.

The database is a file. It creates itself. There is no MongoDB, no Docker, and
no cloud setup — that's the point.



## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). That's the
only prerequisite.

    git clone https://github.com/saedarm/ArXiv-Aggregator
    cd ArXiv-Aggregator
    cp .env.example .env
    dotnet run

Open the URL shown in the console. The SQLite database (`paperaggro.db`)
creates and seeds itself with six sources — OpenAI, Anthropic, Google AI,
Hugging Face, and two arXiv categories — and the first crawl runs on startup.
Expect 60–100 articles on the first run, and a much smaller trickle after
that once deduplication kicks in.

You can run it exactly like this, with an empty `.env`, and just use the web
UI. Email and AI summaries are both optional layers on top.

## Optional: email delivery

Two things need your email address, and missing the second one is the most
common "why didn't I get my digest" cause:

1. **Settings page (`/settings`)** — the SMTP account the digest is sent
   *from*. Host and port default to Gmail (`smtp.gmail.com:465`); fill in
   your address as the user.
2. **Subscribe page (`/subscribe`)** — who the digest is sent *to*. Yes,
   you're emailing yourself. The subscriber list starts empty, and an empty
   list means digests are silently skipped.

Then the password. Gmail requires an **app password** — your normal password
won't work by design:

1. Google Account → Security → enable 2-Step Verification
2. Search "app passwords" in your account settings and create one
3. Put the 16-character code in `.env`:

       SMTP_PASSWORD=abcd efgh ijkl mnop

Restart the app after editing `.env`. Then go to `/settings` and hit
**Send test digest** — it fires the real pipeline immediately and tells you
what happened, so you don't have to wait until tomorrow morning to find out
about a typo. If it says "Sent" but nothing arrives, check the console: no
articles, no subscribers, and no password each skip silently with a log line.
Your first test email will probably land in spam (you, emailing you, forty
links); mark it not-spam once.

## Optional: AI summaries

    OPENAI_API_KEY=sk-...

Add that to `.env` and deep-dive paper abstracts get condensed to two bullet
points by gpt-4o-mini — roughly a cent a day. Without it, the feeds' own
descriptions are used and nothing breaks. The tool is fully functional with
zero AI spend.

## Configuration

Everything lives on the `/settings` page and applies without restarting:

- **Sources** — add, remove, or disable feeds (RSS/Atom or arXiv API URLs).
  Each row shows last run time, article count, and last error, so a feed
  that breaks shows up red instead of silently disappearing.
- **Schedule** — collection interval, daily digest time, weekly digest day.
- **SMTP** — host, port, and sending account.

Secrets (`SMTP_PASSWORD`, `OPENAI_API_KEY`) live only in `.env` and are never
written to the database. Copying `paperaggro.db` exposes your feed list, not
your credentials.

## Keeping it running

The scheduler lives inside the app process — no app, no digest. The schedule
is forgiving ("past 7:30 and not sent today" rather than "exactly 7:30"), so
starting the app late just sends the digest late rather than skipping it.
For hands-off delivery, publish and run it outside the IDE:

    dotnet publish -c Release

…then point Windows Task Scheduler at the exe ("At log on", working directory
set to the publish folder so `.env` and the database resolve), or use a
systemd unit on Linux / a Raspberry Pi.

## Deep-dive pipeline

arXiv papers whose abstracts contain novelty signals ("state-of-the-art",
"we introduce") are flagged, shown first in digests, and exported weekly to
`papers_for_deep_dive/` as a JSON manifest with direct PDF links — ready for
ingestion by a RAG-based paper Q&A tool.

## Docs

See [ARCHITECTURE.md](https://github.com/saedarm/ArXiv-Aggregator/Architecture.md) for how it works and why
it's built this way — including why it can't run on Azure Static Web Apps.

## Hosting note

This is not a static site. It needs a live server process (the scheduler,
SMTP, SQLite on disk), so Azure Static Web Apps is out. It's built to run
where you live: your laptop, a home server, a Pi, or an App Service Basic
tier with Always On if you insist on the cloud.
