// Cloudflare Worker that makes new module releases reach catalog/catalog.json within a minute or two.
//
// GitHub starves the 15-minute cron of .github/workflows/sync-catalog.yml (scheduled runs happen hours apart), so
// this Worker polls instead: every minute it reads the module list from the published catalog, asks GitHub for
// the recent releases of each module repository (with ETags, so unchanged repositories cost nothing), and when one
// has a new release carrying its <id>-<x.y.z>.zip asset it dispatches the Sync catalog workflow once.
//
// Module repositories are derived exactly like scripts/sync-catalog.mjs does: the GitHub `homepage` of each
// catalog module, or this repository for modules without one. New modules need no configuration here.
//
// State lives in one KV key (STATE_KEY) and is written only when something changed:
//   { repos: { "<owner>/<repo>": { pkg, etag, etagSavedAt } } }   (pkg: newest module zip name)
// A repository seen for the first time (including the very first run) is seeded without dispatching.

const OWN_REPO = 'luingry/SwissTechKnife';
const CATALOG_URL = `https://raw.githubusercontent.com/${OWN_REPO}/main/catalog/catalog.json`;
const WORKFLOW = 'sync-catalog.yml';
const STATE_KEY = 'state';
const USER_AGENT = 'stk-catalog-worker';
// An ETag can change without a new release (asset download counts are part of the response). Saving such
// ETag-only changes at most this often keeps KV writes far below the free daily limit.
const ETAG_SAVE_INTERVAL_MS = 10 * 60 * 1000;
// Enough to reach past a few app-only releases to the newest module package.
const RELEASES_PER_PAGE = 10;

export default {
  async scheduled(_controller, env, ctx) {
    ctx.waitUntil(tick(env));
  },
};

async function tick(env) {
  if (!env.GITHUB_TOKEN) {
    console.error('GITHUB_TOKEN secret is not set');
    return;
  }
  const raw = await env.STATE.get(STATE_KEY, 'json');
  const firstRun = raw === null;
  const state = raw ?? { repos: {} };

  let modules;
  try {
    modules = await catalogModules();
  } catch (error) {
    console.error(`catalog: ${error.message}`);
    return;
  }

  const now = Date.now();
  const released = [];
  let dirty = false;

  // Sequential on purpose: a handful of repositories, and it keeps subrequests well under the free limit.
  for (const [repo, ids] of modules) {
    const seen = state.repos[repo];
    let result;
    try {
      result = await recentReleases(env, repo, seen?.etag);
    } catch (error) {
      console.error(`${repo}: ${error.message}`);
      continue;
    }
    if (result.notModified) continue;

    const { releases, etag } = result;
    // The newest uploaded module package across recent releases. A release whose package is still uploading
    // does not count yet (the sync would find nothing), so the next tick looks again.
    const pkg = newestPackage(releases, ids) ?? seen?.pkg ?? null;

    if (!seen) {
      state.repos[repo] = { pkg, etag, etagSavedAt: now };
      dirty = true;
      console.log(`${repo}: seeded at ${pkg ?? 'no release'}`);
    } else if (pkg !== seen.pkg) {
      state.repos[repo] = { pkg, etag, etagSavedAt: now };
      dirty = true;
      released.push(`${repo} ${seen.pkg ?? 'none'} -> ${pkg}`);
    } else if (etag !== seen.etag && now - (seen.etagSavedAt ?? 0) >= ETAG_SAVE_INTERVAL_MS) {
      state.repos[repo] = { ...seen, etag, etagSavedAt: now };
      dirty = true;
    }
  }

  if (released.length) {
    console.log(`new releases: ${released.join('; ')}`);
    if (firstRun) {
      console.log('first run: seeded only, not dispatching');
    } else {
      try {
        await dispatchSync(env);
        console.log(`dispatched ${WORKFLOW}`);
      } catch (error) {
        // Forget the new packages so the next tick dispatches again.
        console.error(`dispatch: ${error.message}`);
        if (raw) {
          for (const entry of released) {
            const repo = entry.split(' ')[0];
            if (raw.repos[repo]) state.repos[repo] = raw.repos[repo];
          }
        }
      }
    }
  }

  if (dirty) await env.STATE.put(STATE_KEY, JSON.stringify(state));
}

// Map of "<owner>/<repo>" (lower case) -> module ids published from it.
async function catalogModules() {
  const response = await fetch(CATALOG_URL, { headers: { 'User-Agent': USER_AGENT }, cf: { cacheTtl: 30 } });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const catalog = await response.json();
  const repos = new Map();
  for (const entry of catalog.modules ?? []) {
    if (!entry?.id) continue;
    const repo = sourceRepo(entry).toLowerCase();
    if (!repos.has(repo)) repos.set(repo, []);
    repos.get(repo).push(entry.id);
  }
  return repos;
}

// Same rule as sourceRepo() in scripts/sync-catalog.mjs.
function sourceRepo(entry) {
  const match = /^https:\/\/github\.com\/([^/]+\/[^/#?]+)/i.exec(entry.homepage || '');
  return match ? match[1].replace(/\.git$/i, '') : OWN_REPO;
}

// Name of the highest-versioned uploaded <id>-<x.y.z>.zip asset, like newestRelease() in scripts/sync-catalog.mjs.
// Repositories that also publish app releases (e.g. tags vX alongside module-vX) are why /releases/latest is not
// enough: the latest release may carry no module package at all.
function newestPackage(releases, ids) {
  let best = null;
  for (const release of releases) {
    if (release.draft || release.prerelease) continue;
    for (const asset of release.assets ?? []) {
      if (asset.state !== 'uploaded') continue;
      for (const id of ids) {
        if (!asset.name.startsWith(`${id}-`)) continue;
        const match = /^(\d+)\.(\d+)\.(\d+)\.zip$/.exec(asset.name.slice(id.length + 1));
        if (!match) continue;
        const version = match.slice(1).map(Number);
        if (!best || compareVersions(version, best.version) > 0) best = { name: asset.name, version };
      }
    }
  }
  return best?.name ?? null;
}

function compareVersions(a, b) {
  for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] - b[i];
  return 0;
}

function githubHeaders(env) {
  return {
    Authorization: `Bearer ${env.GITHUB_TOKEN}`,
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
    'User-Agent': USER_AGENT,
  };
}

async function recentReleases(env, repo, etag) {
  const headers = githubHeaders(env);
  if (etag) headers['If-None-Match'] = etag;
  const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=${RELEASES_PER_PAGE}`, {
    headers,
  });
  if (response.status === 304) return { notModified: true };
  if (!response.ok) {
    const remaining = response.headers.get('X-RateLimit-Remaining');
    throw new Error(`HTTP ${response.status}${remaining === '0' ? ' (rate limited)' : ''}`);
  }
  return { releases: await response.json(), etag: response.headers.get('ETag') };
}

async function dispatchSync(env) {
  const response = await fetch(`https://api.github.com/repos/${OWN_REPO}/actions/workflows/${WORKFLOW}/dispatches`, {
    method: 'POST',
    headers: { ...githubHeaders(env), 'Content-Type': 'application/json' },
    body: JSON.stringify({ ref: 'main' }),
  });
  if (response.status !== 204 && !response.ok) throw new Error(`HTTP ${response.status}`);
}
