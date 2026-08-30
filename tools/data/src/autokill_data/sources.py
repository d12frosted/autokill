"""Fetching and caching the upstream datasets.

Nothing here is clever. Everything is cached on disk under .cache so a rebuild
costs no requests, because the per-mob Garland fetch is thousands of small hits
against a volunteer-run site and there is no reason to repeat it.
"""

from __future__ import annotations

import asyncio
import csv
import json
from pathlib import Path
from typing import Any, Iterable

import httpx

TEAMCRAFT_JSON = (
    "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft"
    "/staging/libs/data/src/lib/json/{name}.json"
)
DATAMINING_CSV = (
    "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/{name}.csv"
)
SUPPLEMENTAL_CSV = (
    "https://raw.githubusercontent.com/Critical-Impact/LuminaSupplemental"
    "/main/src/LuminaSupplemental.Excel/Generated/{name}.csv"
)
GARLAND_MOB_INDEX = "https://www.garlandtools.org/db/doc/browse/en/2/mob.json"
GARLAND_MOB_DOC = "https://www.garlandtools.org/db/doc/mob/en/2/{mob_id}.json"

USER_AGENT = "autokill-data (https://github.com/d12frosted/autokill)"
CONCURRENCY = 4


class Cache:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.root.mkdir(parents=True, exist_ok=True)

    def path(self, key: str) -> Path:
        p = self.root / key
        p.parent.mkdir(parents=True, exist_ok=True)
        return p

    def read(self, key: str) -> bytes | None:
        p = self.path(key)
        return p.read_bytes() if p.exists() else None

    def write(self, key: str, data: bytes) -> None:
        self.path(key).write_bytes(data)

    def get(self, key: str, url: str) -> bytes:
        cached = self.read(key)
        if cached is not None:
            return cached
        with httpx.Client(headers={"User-Agent": USER_AGENT}, timeout=60.0, follow_redirects=True) as client:
            response = client.get(url)
            response.raise_for_status()
        self.write(key, response.content)
        return response.content

    def get_json(self, key: str, url: str) -> Any:
        return json.loads(self.get(key, url))


def teamcraft(cache: Cache, name: str) -> Any:
    return cache.get_json(f"teamcraft/{name}.json", TEAMCRAFT_JSON.format(name=name))


def datamining_csv(cache: Cache, name: str) -> list[dict[str, str]]:
    """Read a datamining CSV. Row 0 holds column names, rows 1+ hold data."""
    raw = cache.get(f"datamining/{name}.csv", DATAMINING_CSV.format(name=name))
    rows = list(csv.reader(raw.decode("utf-8-sig").splitlines()))
    header = rows[0]
    return [dict(zip(header, row)) for row in rows[1:] if row]


def supplemental_csv(cache: Cache, name: str) -> list[dict[str, str]]:
    """Read a LuminaSupplemental CSV.

    The plugin gets these at runtime out of the LuminaSupplemental package
    rather than from here, so this is only for measuring what the plugin will
    have. Same shape, one header row.
    """
    raw = cache.get(f"supplemental/{name}.csv", SUPPLEMENTAL_CSV.format(name=name))
    rows = list(csv.reader(raw.decode("utf-8-sig").splitlines()))
    header = rows[0]
    return [dict(zip(header, row)) for row in rows[1:] if row]


def garland_mob_index(cache: Cache) -> list[dict[str, Any]]:
    return cache.get_json("garland/browse-mob.json", GARLAND_MOB_INDEX)["browse"]


async def _fetch_one(
    client: httpx.AsyncClient,
    cache: Cache,
    mob_id: int,
    semaphore: asyncio.Semaphore,
) -> tuple[int, dict[str, Any] | None]:
    key = f"garland/mob/{mob_id}.json"
    cached = cache.read(key)
    if cached is not None:
        try:
            return mob_id, json.loads(cached)
        except json.JSONDecodeError:
            return mob_id, None

    async with semaphore:
        for attempt in range(3):
            try:
                response = await client.get(GARLAND_MOB_DOC.format(mob_id=mob_id))
            except httpx.HTTPError:
                await asyncio.sleep(1.0 * (attempt + 1))
                continue
            if response.status_code == 404:
                cache.write(key, b"null")
                return mob_id, None
            if response.status_code == 200:
                cache.write(key, response.content)
                try:
                    return mob_id, response.json()
                except json.JSONDecodeError:
                    return mob_id, None
            await asyncio.sleep(1.0 * (attempt + 1))
    return mob_id, None


async def _fetch_all(cache: Cache, mob_ids: list[int], progress) -> dict[int, dict[str, Any]]:
    semaphore = asyncio.Semaphore(CONCURRENCY)
    out: dict[int, dict[str, Any]] = {}
    async with httpx.AsyncClient(
        headers={"User-Agent": USER_AGENT}, timeout=60.0, follow_redirects=True
    ) as client:
        tasks = [_fetch_one(client, cache, mob_id, semaphore) for mob_id in mob_ids]
        for done, coro in enumerate(asyncio.as_completed(tasks), start=1):
            mob_id, doc = await coro
            if doc:
                out[mob_id] = doc
            if progress and done % 100 == 0:
                progress(done, len(mob_ids))
    return out


def garland_mob_docs(
    cache: Cache, mob_ids: Iterable[int], progress=None
) -> dict[int, dict[str, Any]]:
    return asyncio.run(_fetch_all(cache, list(mob_ids), progress))
