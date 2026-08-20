from autokill_data.positions import extract_positions

MAPS = {
    "695": {"territory_id": 956, "dungeon": False, "housing": False},
    "22": {"territory_id": 145, "dungeon": False, "housing": False},
    "900": {"territory_id": 1, "dungeon": True, "housing": False},
    "901": {"territory_id": 2, "dungeon": False, "housing": True},
}


def position(map_id=695, x=20.7, y=12.1, fate=0, level=41):
    return {"map": map_id, "x": x, "y": y, "fate": fate, "level": level}


def test_keeps_ordinary_open_world_positions():
    monsters = {"10673": {"positions": [position()]}}
    assert extract_positions(monsters, MAPS) == {"10673": [[695, 20.7, 12.1, 41]]}


def test_drops_fate_positions():
    # FATE spawns only exist while their FATE is up, so they are no use as a
    # place to go and farm.
    monsters = {"1": {"positions": [position(fate=1234)]}}
    assert extract_positions(monsters, MAPS) == {}


def test_drops_dungeon_and_housing_maps():
    monsters = {"1": {"positions": [position(map_id=900), position(map_id=901)]}}
    assert extract_positions(monsters, MAPS) == {}


def test_drops_positions_on_unknown_maps():
    monsters = {"1": {"positions": [position(map_id=99999)]}}
    assert extract_positions(monsters, MAPS) == {}


def test_rounds_coordinates_to_one_decimal():
    monsters = {"1": {"positions": [position(x=20.7431, y=12.1789)]}}
    assert extract_positions(monsters, MAPS) == {"1": [[695, 20.7, 12.2, 41]]}


def test_keeps_the_usable_positions_of_a_partly_unusable_mob():
    monsters = {"1": {"positions": [position(fate=7), position(x=1.0, y=2.0)]}}
    assert extract_positions(monsters, MAPS) == {"1": [[695, 1.0, 2.0, 41]]}


def test_omits_mobs_with_nothing_recorded():
    assert extract_positions({"1": {"positions": []}, "2": {}}, MAPS) == {}


def test_handles_several_mobs_and_maps():
    monsters = {
        "1": {"positions": [position(), position(map_id=22, x=15.9, y=23.5, level=6)]},
        "2": {"positions": [position(map_id=22, x=1.0, y=2.0, level=6)]},
    }
    assert extract_positions(monsters, MAPS) == {
        "1": [[695, 20.7, 12.1, 41], [22, 15.9, 23.5, 6]],
        "2": [[22, 1.0, 2.0, 6]],
    }


def test_keeps_the_level_each_point_was_seen_at():
    # Levels vary within a zone, so they belong to the point rather than to the
    # mob: one recorded level for a mob standing across half an expansion says
    # the wrong thing about most of where it stands.
    monsters = {"1": {"positions": [position(level=41), position(x=1.0, y=2.0, level=44)]}}
    assert extract_positions(monsters, MAPS) == {
        "1": [[695, 20.7, 12.1, 41], [695, 1.0, 2.0, 44]]
    }


def test_records_an_unknown_level_as_zero():
    # Plenty of points carry no level at all. Zero says unrecorded, which has to
    # stay tellable from a genuinely low level.
    monsters = {
        "1": {"positions": [{"map": 695, "x": 1.0, "y": 2.0, "fate": 0}]},
        "2": {"positions": [position(level=0)]},
    }
    assert extract_positions(monsters, MAPS) == {
        "1": [[695, 1.0, 2.0, 0]],
        "2": [[695, 20.7, 12.1, 0]],
    }


def test_levels_are_whole_numbers():
    monsters = {"1": {"positions": [position(level=41.0)]}}
    assert extract_positions(monsters, MAPS) == {"1": [[695, 20.7, 12.1, 41]]}
