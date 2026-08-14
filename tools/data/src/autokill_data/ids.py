"""Garland Tools mob identifiers.

Garland keys a mob by a composite number: the PlaceName id of the sub-location it
lives in, shifted up by ten digits, plus the game's BNpcName id. So the same
creature standing in two named sub-areas gets two Garland entries that share a
BNpcName id. That id is what the game itself uses and what every other dataset
keys on, so it is the join column for the whole pipeline.
"""

from dataclasses import dataclass

SUBLOCATION_SHIFT = 10**10


@dataclass(frozen=True, slots=True)
class GarlandMobRef:
    sublocation_id: int
    bnpc_name_id: int


def decode_garland_mob_id(raw: int) -> GarlandMobRef:
    if raw < 0:
        raise ValueError(f"garland mob id cannot be negative: {raw}")
    sublocation, bnpc_name = divmod(raw, SUBLOCATION_SHIFT)
    return GarlandMobRef(sublocation_id=sublocation, bnpc_name_id=bnpc_name)


def encode_garland_mob_id(sublocation_id: int, bnpc_name_id: int) -> int:
    return sublocation_id * SUBLOCATION_SHIFT + bnpc_name_id
