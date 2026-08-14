import pytest

from autokill_data.ids import decode_garland_mob_id, encode_garland_mob_id


def test_decodes_a_garland_mob_id_into_sublocation_and_bnpc_name():
    ref = decode_garland_mob_id(1930000000273)
    assert ref.sublocation_id == 193
    assert ref.bnpc_name_id == 273


def test_decodes_a_four_digit_sublocation_prefix():
    ref = decode_garland_mob_id(12810000000879)
    assert ref.sublocation_id == 1281
    assert ref.bnpc_name_id == 879


def test_decodes_a_single_digit_sublocation_prefix():
    ref = decode_garland_mob_id(20000000002)
    assert ref.sublocation_id == 2
    assert ref.bnpc_name_id == 2


def test_encode_is_the_inverse_of_decode():
    for raw in (1930000000273, 12810000000879, 20000000002, 30000000003):
        ref = decode_garland_mob_id(raw)
        assert encode_garland_mob_id(ref.sublocation_id, ref.bnpc_name_id) == raw


def test_rejects_a_negative_id():
    with pytest.raises(ValueError):
        decode_garland_mob_id(-1)
