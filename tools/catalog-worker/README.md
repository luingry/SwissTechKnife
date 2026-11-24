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
2. For each repository, requests `GET /repos/<repo>/releases/latest` with `If-None-Match`. A `304` costs nothing
   against the rate limit.
3. A release counts once it has an uploaded `<id>-<x.y.z>.zip` asset for one of that repository's modules, so a
   release whose package is still uploading is picked up on a later tick, not missed.
4. When any repository has a new tag, it dispatches `sync-catalog.yml` on `main` once for that tick.

State is one KV key, `state`, in the `STATE` namespace: `{ repos: { "<owner>/<repo>": { tag, etag, etagSavedAt } } }`.
A repository seen for the first time (including the first run ever) is seeded without dispatching. KV is written
only when a tag changes, or when only the ETag changed and it was last saved 10+ minutes ago (asset download counts
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
- Force a re-dispatch test: edit the `state` key in KV and change one repository's `tag`; the next tick dispatches.
- Reset: delete the `state` key; the next tick re-seeds without dispatching.
- Token expired or revoked: ticks log `HTTP 401`; set a new one with `npx wrangler secret put GITHUB_TOKEN`.
