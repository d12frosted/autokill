import pytest

from autokill_data.coords import map_to_world, world_to_map


def test_map_centre_is_the_world_origin():
    # At scale 100 with no offset, map coordinate 21.5 is world 0. This is the
    # well known anchor of the game's map projection and is what pins the whole
    # conversion down.
    assert map_to_world(21.5, size_factor=100, offset=0) == pytest.approx(0.0, abs=1e-6)


def test_map_one_is_the_left_edge():
    assert map_to_world(1.0, size_factor=100, offset=0) == pytest.approx(-1024.0, abs=1e-6)


def test_offset_shifts_the_result():
    without = map_to_world(21.5, size_factor=100, offset=0)
    with_offset = map_to_world(21.5, size_factor=100, offset=200)
    assert with_offset == pytest.approx(without - 200.0, abs=1e-6)


def test_size_factor_scales_the_span():
    # A map drawn at scale 200 covers half the world distance per map unit.
    big = map_to_world(1.0, size_factor=100, offset=0)
    small = map_to_world(1.0, size_factor=200, offset=0)
    assert small == pytest.approx(big / 2.0, abs=1e-6)


@pytest.mark.parametrize(
    "value,size_factor,offset",
    [
        (15.9, 100, 0),
        (23.5, 100, 0),
        (30.2, 200, 0),
        (12.4, 95, -300),
        (1.0, 400, 512),
    ],
)
def test_round_trips_back_to_the_same_map_coordinate(value, size_factor, offset):
    world = map_to_world(value, size_factor=size_factor, offset=offset)
    assert world_to_map(world, size_factor=size_factor, offset=offset) == pytest.approx(value, abs=1e-6)


def test_a_known_eastern_thanalan_spawn_lands_inside_the_zone():
    # Myotragus Billy, Eastern Thanalan (map 22, size factor 100, no offset).
    x = map_to_world(15.9, size_factor=100, offset=0)
    y = map_to_world(23.5, size_factor=100, offset=0)
    assert -1024.0 < x < 1024.0
    assert -1024.0 < y < 1024.0
    assert x == pytest.approx(-279.6, abs=1.0)
