from autokill_data.build import invert_drop_sources


def test_inverts_an_item_to_mob_mapping_into_mob_to_items():
    assert invert_drop_sources({"36203": [10656, 11120]}) == {
        10656: {36203},
        11120: {36203},
    }


def test_collects_every_item_a_mob_drops():
    inverted = invert_drop_sources({"1": [10], "2": [10], "3": [11]})
    assert inverted == {10: {1, 2}, 11: {3}}


def test_ignores_items_with_no_mobs():
    assert invert_drop_sources({"1": [], "2": [10]}) == {10: {2}}


def test_ignores_malformed_values():
    assert invert_drop_sources({"1": None, "2": [10]}) == {10: {2}}


def test_no_sources_gives_nothing():
    assert invert_drop_sources({}) == {}
