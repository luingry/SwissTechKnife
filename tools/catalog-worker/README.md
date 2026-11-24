# Catalog worker

A Cloudflare Worker (free plan) that gets new module releases into `catalog/catalog.json` within a minute or two.

## Why

`.github/workflows/sync-catalog.yml` runs `scripts/sync-catalog.mjs`, which commits new module releases to the
catalog every install reads. Its `*/15` cron is starved by GitHub: in practice scheduled runs happen 4 to 12 hours
apart. Module repositories could fire `repository_dispatch`, but each would need its own PAT secret, and personal
accounts have no shared secrets. This Worker holds the single token instead and polls.

The 15-minute cron stays as a fallback.

## What it does

Every minute (Cron Trigger `* * * * *`):

1. Reads the published catalog (`raw.githubusercontent.com/luingry/SwissTechKnife/main/catalog/catalog.json`) and
   derives each module's repository exactly like `sourceRepo()` in `scripts/sync-catalog.mjs`: the GitHub
   `homepage`, or this repository when there is none. New modules need no configuration.
2. For each repository, requests `GET /repos/<repo>/releases?per_page=10` with `If-None-Match`. A `304` costs
   nothing against the rate limit. Not `/releases/latest`: repositories that also publish app releases (SmoothMice)
   would hide their module package behind a release that has none.
3. Picks the highest-versioned uploaded `<id>-<x.y.z>.zip` asset among those releases (drafts and prereleases
   skipped, like `newestRelease()` in the sync script). A package still uploading is picked up on a later tick.
4. When any repository's newest package changed, it dispatches `sync-catalog.yml` on `main` once for that tick.

State is one KV key, `state`, in the `STATE` namespace: `{ repos: { "<owner>/<repo>": { pkg, etag, etagSavedAt } } }`.
A repository seen for the first time (including the first run ever) is seeded without dispatching. KV is written
only when a package changes, or when only the ETag changed and it was last saved 10+ minutes ago (asset download counts
change the ETag), which keeps writes well under the free limit of 1,000 a day.

Budget: about 8 GitHub requests a minute, almost all `304`; at most 10 subrequests per tick.

## Deploy

```powershell
cd tools/catalog-worker
npx wrangler deploy
npx wrangler secret put GITHUB_TOKEN
```

`GITHUB_TOKEN` is a fine-grained PAT with **Actions: Read and write** on `luingry/SwissTechKnife` (public module
repositories need nothing more). It lives only as a Worker secret; never put it in `wrangler.toml` or the logs.
The Worker has no HTTP endpoint (`workers_dev = false`).

## Operate

- Logs: `npx wrangler tail stk-catalog-worker`, or Workers > stk-catalog-worker > Logs in the dashboard
  (observability is enabled).
- Force a re-dispatch test: edit the `state` key in KV and change one repository's `pkg`; the next tick dispatches.
- Reset: delete the `state` key; the next tick re-seeds without dispatching.
- Token expired or revoked: ticks log `HTTP 401`; set a new one with `npx wrangler secret put GITHUB_TOKEN`.
