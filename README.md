# Funda Assignment

A .NET console application that uses the Funda partner API to find the agents (makelaars) in Amsterdam with the most properties for sale. It prints two tables:

1. Top 10 agents in Amsterdam
2. Top 10 agents in Amsterdam for properties with a garden (tuin)

## Running it

**Requirements:** the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a Funda API key.

The API key is not stored in the repository. Set it with user secrets:

```bash
dotnet user-secrets set "Funda:ApiKey" "<your-key>" --project FundaAssignment/FundaAssignment.Console
```

or as an environment variable:

```bash
# PowerShell
$env:Funda__ApiKey = "<your-key>"
# bash
export Funda__ApiKey="<your-key>"
```

Then run the app and the tests from the repository root:

```bash
dotnet run --project FundaAssignment/FundaAssignment.Console
dotnet test FundaAssignment/FundaAssignment.slnx
```

A full run takes a few minutes. That is intentional; see [Rate limiting](#rate-limiting).

### Sample output

<!-- TODO: run the app once and replace both tables with the real output. Remove the log lines, keep only the tables. -->

Output of a run on 13/11/2026:

Top 10 makelaars in Amsterdam
=============================
#  Agent                                    Id  Listings
---------------------------------------------------------
1  Heeren Makelaars                      24648       201
2  Eefje Voogd Makelaardij               24705       192
3  Hallie & Van Klooster Makelaardij     24605       172
4  Broersma Wonen                        24067       155
5  Ramón Mossel Makelaardij o.g. B.V.    24592       129
6  DSTRCT | Forbes Global Properties     24599       104
7  KRK Makelaars Amsterdam               24607        78
8  Makelaardij Van der Linden Amsterdam  24079        76
9  VON POLL REAL ESTATE                  24789        71
10  Nieuw West Makelaardij B.V.           24614        70

Top 10 makelaars in Amsterdam with a garden (tuin)
==================================================
#  Agent                                   Id  Listings
--------------------------------------------------------
1  Broersma Wonen                       24067        30
2  Heeren Makelaars                     24648        20
3  Hoekstra en Van Eck Amsterdam Noord  24594        20
4  DSTRCT | Forbes Global Properties    24599        18
5  Carla van den Brink B.V.             24065        17
6  Hallie & Van Klooster Makelaardij    24605        15
7  Makelaarsland                        12285        15
8  Linger OG Makelaars en Taxateurs     60557        14
9  Eefje Voogd Makelaardij              24705        12
10  Fransen & Kroes Makelaars Amsterdam  24819        10

The run took about [N] minutes for [N] requests in total.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Both reports were printed |
| 1 | The API returned an HTTP error after all retries |
| 2 | Funda kept returning an HTML page instead of JSON (rate limit or invalid API key) |
| 3 | Invalid configuration, such as a missing API key or `RequestsPerMinute` above 90 |
| 130 | Cancelled with Ctrl+C |

## Configuration

Settings live in `FundaAssignment.Console/appsettings.json` under `Funda`. They are validated at startup, so a missing key or an invalid value stops the app before any request is sent, with a message saying what is wrong:

```
Invalid configuration:
  - Funda:RequestsPerMinute must be between 1 and 90. Funda rejects more than 100 requests per minute, so the app stays safely below that limit.
```

| Setting | Default | Description |
|---|---|---|
| `ApiKey` | (none) | Required. Set via user secrets or `Funda__ApiKey`. |
| `PageSize` | 25 | Objects per page. The API does not return more than 25. |
| `RequestsPerMinute` | 60 | Maximum request rate, 1""�90. The assignment states that more than 100 requests per minute are rejected, so higher values are refused at startup. |
| `MaxRetryAttempts` | 4 | Retries per request; 0 disables retrying. |
| `RetryBaseDelaySeconds` | 5 | Base delay for the exponential backoff (5s, 10s, 20s, ...). |
| `BlockedRetryDelaySeconds` | 30 | Base delay after an HTML response instead of JSON (30s, 60s, 90s, ...). |

## Project structure

```
FundaAssignment/
 FundaAssignment.Core            Domain models, interfaces and the ranking logic. No HTTP or JSON.
 FundaAssignment.Infrastructure  Funda API client, DTOs, mapping, rate limiting and retries.
 FundaAssignment.Console         Composition root: configuration, DI, the two reports and table output.
 FundaAssignment.Tests           NUnit tests for the ranking, mapping, API client and retry policy.
```

The solution is split by concern: Core holds the business logic (ranking agents), Infrastructure talks to Funda, and Console handles configuration, input and output.

Dependencies point inwards: Console "�� Infrastructure "�� Core. Core only knows the `IListingSource` interface, so the ranking logic can be tested without the API, and the data source could be swapped without touching it.

## Avoiding and handling API errors

**Avoiding them:** a rate limiter spaces requests evenly at 60 per minute by default, well under the documented limit of 100. Values above 90 are refused at startup, so a misconfiguration cannot cause a burst of rejected requests.

**Handling them:** transient failures (network errors, 408, 429, 5xx) are retried with exponential backoff. An HTML response instead of JSON is retried with a much longer delay. If a request still fails, the app stops with a clear message and a distinct exit code instead of a stack trace.

The sections below describe both in detail.

## Design decisions

### Ranking

`TopAgentsService` collects all listings for a search, groups them by agent and returns the top N.

- **Grouped by agent ID, not name.** Two agencies can share a display name.
- **Duplicates removed by listing ID.** Listings can shift between pages while paging through a live API, so the same listing can show up twice. Each is counted once.
- **Ties broken by name** (case-insensitive, culture-independent). Without a tie-breaker, the agent at position 10 could change between runs on the same data.
- **Child listings are included.** A project listing can contain child listings; each one counts towards the agent's total.

### HTTP client

- A **typed `HttpClient`** registered through `IHttpClientFactory`, so handlers and connections are managed by the framework.
- The search query (`zo`) is built as `/amsterdam/` or `/amsterdam/tuin/`. The slashes are sent unencoded; the city name itself is escaped, so unusual characters cannot break the query string.

### Rate limiting

The assignment states that more than 100 requests per minute are rejected. A `TokenBucketRateLimiter` with a single token spaces requests evenly (one every 60/N seconds) instead of sending them in bursts. It is a singleton, so the limit holds for the whole process, and it sits inside the retry handler, so retries are throttled too.

The Amsterdam search has more than 5,600 listings, which is about 226 pages of 25. At 60 requests per minute, the first report alone takes close to 4 minutes. Staying well under the limit is slower, but it avoids rejected requests (see below).

### Retries and the HTML response

Retries use Polly via `Microsoft.Extensions.Http.Resilience`:

- **Retried:** network failures, 408, 429 and 5xx responses, with exponential backoff and jitter. A `Retry-After` header from the server takes precedence.
- **Not retried:** other 4xx responses, since they will fail every time.

Instead of a clear error such as a 429, the API at times returned **HTTP 200 with an HTML page** ("Je bent bijna op de pagina die je zoekt") instead of JSON. At one point this kept happening for every request until Funda provided a new API key, so the cause can also be on Funda's side. Either way, the client handles this response explicitly instead of failing on a JSON parse error. It:

1. detects it as a 2xx response without a JSON content type,
2. retries with a much longer, growing delay (30s, 60s, 90s, 120s by default), because retrying quickly only adds to the load,
3. stops with a `FundaApiBlockedException` and a message explaining the likely causes.

### Configuration and secrets

Options are bound to `FundaApiOptions` and validated on startup. The API key is read from user secrets or an environment variable, never from a committed file.

## Tests

The code is built to be testable: Core depends only on the `IListingSource` interface, and the API client receives its `HttpClient` from the outside, so both can be tested without the network.

The tests never call the real API. The API client is tested with a fake `HttpMessageHandler` that returns prepared JSON, HTML or error responses and records the requested URLs. The retry policy is tested both through its individual decisions and in a real Polly pipeline with the delays switched off, so the whole suite runs in well under a second.

| Area | What is covered |
|---|---|
| `TopAgentsService` | ordering, top-N limit, duplicates, ties, grouping by ID, input validation |
| `ListingMapper` | field mapping, trimming, placeholder for a missing agent name |
| `FundaApiClient` | paging, empty results, child listings, query building and escaping, HTML responses and HTTP errors, cancellation |
| `FundaRetryPolicy` | HTML response detection, which failures are retried, delays, `Retry-After`, recovery and giving up |

## How I approached it

**Starting point.** I started by looking at the API response to understand its structure, since everything else would be built on it. From there I noticed the page size cap of 25 and the child listings. After that I set up a simple project structure: Core holds the business rules, and both Infrastructure and Console depend on it. I chose this because the assignment values separation of concerns.

**The core problem.** Technically, the main challenge should have been the API: getting through the large number of pages while staying under the limit of 100 requests per minute. With AI, that part wasn't difficult. The biggest challenge came when the API returned an HTML page ("Je bent bijna op de pagina die je zoekt") instead of JSON, which took real investigation to find the actual cause. The other challenge was reviewing the AI's choices: as I mention under Use of AI, its tendency to over-engineer always needed oversight. For example, it encoded the slashes in the search query (`%2Famsterdam%2Ftuin%2F`). I tested the plain form (`/amsterdam/tuin/`) against the real API, found it worked just as well, and kept the simpler version.

**What went wrong and how I found out.** At one point the API answered with `200 OK` but returned an HTML page ("Je bent bijna op de pagina die je zoekt") instead of JSON. The AI first pointed to the URL encoding of the search query, which didn't make sense to me, so I tested it. I wrote scripts that sent the same request with and without encoding, and the unencoded one only failed because it was sent quickly after the first. Once I spaced the requests out, the encoding made no difference. I then checked the request in Postman, Windows PowerShell 5.1 and curl, and later PowerShell 7, which uses the same HTTP stack as .NET 10. All of them got the same HTML page, so the problem wasn't in my code. I contacted Funda, who gave me a new API key and endpoint, and from there it worked.

**Trade-offs.** Instead of fetching all listings once and filtering the garden ones myself, I let the API filter with a separate search per report. It costs more requests, but keeps each report simple and works the same for other cities or filters.

I chose LINQ over streaming for the ranking because it's more readable, and holding a few thousand listings in memory is no problem. In a larger project I'd stream instead: a dictionary keyed by agent ID with the name and count, plus a set of seen listing IDs to skip duplicates.

## What I would do with more time

- **Pass the city and options on the command line** instead of hard-coding the two reports.
- **Show progress** as one updating line instead of a log line per page.
- **Add an integration test** against a recorded API response, to catch changes in the JSON format.

## Use of AI

I used Claude throughout this assignment, as a pair programmer and reviewer.

**How I used it**
I set up the base project myself, with Core, Infrastructure and Console projects. Then I gave Claude the API response and the assignment, and asked it for templates of what I needed. As the project went on, I used it to write code and suggest changes, while I fixed the issues I found or rejected what didn't fit.

**What it helped with**
- Drafting code for the models, ranking service, API client, DTOs, rate limiting and retry policy, which I then adapted, renamed and integrated.
- Drafting the unit tests, which I then reviewed and trimmed.
- Explaining trade-offs I asked about: `sealed`, streaming vs LINQ, how to keep the search query extensible.
- Investigating the HTML responses from the API.
- Drafting this README, which I then edited and completed.

**Decisions I made**
- Switched the ranking to LINQ over the streaming version (see Trade-offs), and simplified tie-breaking.
- Introduced `ListingSearch` to keep Funda's query format out of Core, and chose a simple query builder over a more abstract rules-based one.
- Kept listings that are "sold subject to conditions", rather than filtering them out.
- Sent the `zo` query with unencoded slashes after testing both forms against the real API.
- Capped `RequestsPerMinute` at 90 and moved the API key out of the repository into user secrets.
- Reviewed the generated tests: removed and merged overlapping ones, enforced Arrange/Act/Assert, and kept plain NUnit instead of adding an assertion library.

**Where I had to verify or correct it**
- It tended to over-engineer, both in the API client and in the tests, so I simplified both (see The core problem).
- Its first explanation for the HTML responses was wrong; see What went wrong and how I found out.
- I checked API assumptions (page size cap, child listings) against real responses rather than taking them on trust.

**Why**
To move faster on boilerplate and to get a second opinion on design choices, while keeping the decisions and the verification my own.