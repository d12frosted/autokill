from autokill_data.positions import extract_positions

MAPS = {
    "695": {"territory_id": 956, "dungeon": False, "housing": False},
    "22": {"territory_id": 145, "dungeon": False, "housing": False},
    "900": {"territory_id": 1, "dungeon": True, "housing": False},
    "901": {"territory_id": 2, "dungeon": False, "housing": True},
}


def position(map_id=695, x=20.7, y=12.1, fate=0):
    return {"map": map_id, "x": x, "y": y, "fate": fate}


def test_keeps_ordinary_open_world_positions():
    monsters = {"10673": {"positions": [position()]}}
    assert extract_positions(monsters, MAPS) == {"10673": [[695, 20.7, 12.1]]}


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
    assert extract_positions(monsters, MAPS) == {"1": [[695, 20.7, 12.2]]}


def test_keeps_the_usable_positions_of_a_partly_unusable_mob():
    monsters = {"1": {"positions": [position(fate=7), position(x=1.0, y=2.0)]}}
    assert extract_positions(monsters, MAPS) == {"1": [[695, 1.0, 2.0]]}


def test_omits_mobs_with_nothing_recorded():
    assert extract_positions({"1": {"positions": []}, "2": {}}, MAPS) == {}


def test_handles_several_mobs_and_maps():
    monsters = {
        "1": {"positions": [position(), position(map_id=22, x=15.9, y=23.5)]},
        "2": {"positions": [position(map_id=22, x=1.0, y=2.0)]},
    }
    assert extract_positions(monsters, MAPS) == {
        "1": [[695, 20.7, 12.1], [22, 15.9, 23.5]],
        "2": [[22, 1.0, 2.0]],
    }
