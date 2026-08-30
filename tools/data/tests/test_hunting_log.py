from autokill_data.hunting_log import (
    LogTarget,
    level_agreement,
    measure,
    rank_shape,
    parse_entries,
    parse_targets,
    summarise,
)


def note(row_id, name="Lancer 01", reward=75, targets=((1, 3),)):
    row = {"#": str(row_id), "Name": name, "Reward": str(reward)}
    for slot in range(4):
        target, count = targets[slot] if slot < len(targets) else (0, 0)
        row[f"MonsterNoteTarget[{slot}]"] = str(target)
        row[f"Count[{slot}]"] = str(count)
    return row


def target(row_id, bnpc=49, zones=(30, 0, 0), locations=(161, 0, 0)):
    row = {"#": str(row_id), "BNpcName": str(bnpc)}
    for slot in range(3):
        row[f"PlaceNameZone[{slot}]"] = str(zones[slot])
        row[f"PlaceNameLocation[{slot}]"] = str(locations[slot])
    return row


def test_reads_the_class_and_the_position_in_the_log_out_of_the_row_id():
    [entry] = parse_entries([note(40011, name="Lancer 11")])
    assert entry.class_job_id == 4
    assert entry.grand_company_id == 0
    assert entry.index == 11
    assert entry.rank == 2


def test_reads_a_grand_company_log_out_of_the_row_id():
    [entry] = parse_entries([note(2000021, name="Order of the Twin Adder 21")])
    assert entry.class_job_id == 0
    assert entry.grand_company_id == 2
    assert entry.index == 21
    assert entry.rank == 3


def test_keeps_every_mob_an_entry_names_with_its_own_count():
    [entry] = parse_entries([note(10001, targets=((5, 3), (6, 4)))])
    assert entry.kills == ((5, 3), (6, 4))
    assert entry.total_kills == 7


def test_drops_the_empty_rows_that_pad_a_grand_company_log():
    # Every log is 50 rows wide in the sheet; a Grand Company one only fills 30.
    assert parse_entries([note(1000031, targets=())]) == []


def test_keeps_only_the_zones_a_target_actually_names():
    [entry] = parse_targets([target(1, zones=(30, 54, 0), locations=(161, 68, 0))])
    assert entry.zones == (30, 54)
    assert entry.locations == (161, 68)


def test_a_target_is_covered_when_the_zone_it_names_has_positions():
    entries = parse_entries([note(10001, targets=((1, 3),))])
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49, zones=(30, 0, 0))])}
    # territory 148 is the zone whose PlaceName is 30
    covered = measure(entries, targets, {49: {148: 12}}, {148: 30})
    assert [(c.spots, c.named_spots) for c in covered] == [(12, 12)]


def test_a_target_standing_only_somewhere_the_entry_did_not_name_is_not_covered():
    # The Grand Company logs send you into dungeons, and the zone they name is
    # the one the dungeon entrance sits in rather than where the mob stands.
    entries = parse_entries([note(1000002, targets=((1, 3),))])
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49, zones=(30, 0, 0))])}
    covered = measure(entries, targets, {49: {166: 4}}, {148: 30, 166: 99})
    assert [(c.spots, c.named_spots) for c in covered] == [(4, 0)]


def test_a_target_nobody_has_recorded_is_not_covered_at_all():
    entries = parse_entries([note(10001, targets=((1, 3),))])
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49)])}
    covered = measure(entries, targets, {}, {148: 30})
    assert [(c.spots, c.named_spots) for c in covered] == [(0, 0)]


def test_an_entry_counts_as_reachable_only_when_every_mob_on_it_is():
    entries = parse_entries([note(10001, targets=((1, 3), (2, 3)))])
    targets = {
        t.row_id: t
        for t in parse_targets([target(1, bnpc=49), target(2, bnpc=50)])
    }
    covered = measure(entries, targets, {49: {148: 12}}, {148: 30})
    [row] = summarise(covered)
    assert (row.entries, row.reachable_entries) == (1, 0)
    assert (row.targets, row.named_targets) == (2, 1)


def test_summarises_one_row_per_log():
    entries = parse_entries(
        [
            note(10001, name="Gladiator 01"),
            note(10002, name="Gladiator 02"),
            note(40011, name="Lancer 11"),
        ]
    )
    targets = {t.row_id: t for t in parse_targets([target(1)])}
    rows = summarise(measure(entries, targets, {49: {148: 3}}, {148: 30}))
    assert [(r.log, r.entries) for r in rows] == [("Gladiator", 2), ("Lancer", 1)]


def test_a_target_is_the_same_row_in_two_logs_without_being_counted_once():
    # Several classes send you after the same mob. Each log is measured on its
    # own, because a mob nobody can reach costs every log that names it.
    entries = parse_entries(
        [note(10001, name="Gladiator 01"), note(20001, name="Pugilist 01")]
    )
    targets = {t.row_id: t for t in parse_targets([target(1)])}
    rows = summarise(measure(entries, targets, {}, {148: 30}))
    assert [(r.log, r.targets, r.named_targets) for r in rows] == [
        ("Gladiator", 1, 0),
        ("Pugilist", 1, 0),
    ]


def test_a_target_row_the_sheet_does_not_have_is_left_out_rather_than_guessed():
    entries = parse_entries([note(10001, targets=((999, 3),))])
    assert measure(entries, {}, {}, {}) == []


def test_the_level_an_entry_is_written_for_is_its_place_in_a_class_log():
    [entry] = parse_entries([note(40011, name="Lancer 11")])
    assert entry.level == 11


def test_a_grand_company_entry_says_nothing_about_its_level():
    # Their thirty entries do not climb one level at a time the way a class log
    # does, so the position data has to supply the level instead.
    [entry] = parse_entries([note(1000021)])
    assert entry.level is None


def test_a_rank_is_measured_by_the_zones_its_entries_send_you_to():
    entries = parse_entries(
        [
            note(10001, name="Gladiator 01", targets=((1, 3),)),
            note(10002, name="Gladiator 02", targets=((2, 3),)),
        ]
    )
    targets = {
        t.row_id: t
        for t in parse_targets([target(1, bnpc=49), target(2, bnpc=50, zones=(31, 0, 0))])
    }
    coverage = measure(
        entries, targets, {49: {148: 1}, 50: {152: 1}}, {148: 30, 152: 31}
    )
    [shape] = rank_shape(coverage, {}, radius=250.0)
    assert (shape.log, shape.rank, shape.entries, shape.zones) == ("Gladiator", 1, 2, 2)
    # One zone each, so there is nothing to group and no field to share.
    assert (shape.trips, shape.paired_entries) == (2, 0)


def test_two_entries_standing_close_in_one_zone_are_one_field():
    entries = parse_entries(
        [
            note(10001, name="Gladiator 01", targets=((1, 3),)),
            note(10002, name="Gladiator 02", targets=((2, 3),)),
        ]
    )
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49), target(2, bnpc=50)])}
    coverage = measure(entries, targets, {49: {148: 1}, 50: {148: 1}}, {148: 30})
    points = {49: {148: [(0.0, 0.0)]}, 50: {148: [(100.0, 0.0)]}}
    [shape] = rank_shape(coverage, points, radius=250.0)
    assert (shape.zones, shape.trips, shape.paired_entries) == (1, 1, 2)


def test_two_entries_at_opposite_ends_of_a_zone_are_two_trips():
    entries = parse_entries(
        [
            note(10001, name="Gladiator 01", targets=((1, 3),)),
            note(10002, name="Gladiator 02", targets=((2, 3),)),
        ]
    )
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49), target(2, bnpc=50)])}
    coverage = measure(entries, targets, {49: {148: 1}, 50: {148: 1}}, {148: 30})
    points = {49: {148: [(0.0, 0.0)]}, 50: {148: [(900.0, 0.0)]}}
    [shape] = rank_shape(coverage, points, radius=250.0)
    # Still one trip, since one teleport reaches both, but two fields to fly to.
    assert (shape.zones, shape.trips, shape.paired_entries) == (1, 1, 0)


def test_the_level_a_class_entry_is_written_for_is_checked_against_the_ground():
    entries = parse_entries([note(40011, name="Lancer 11", targets=((1, 3),))])
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49)])}
    coverage = measure(entries, targets, {49: {148: 2}}, {148: 30})
    assert level_agreement(coverage, {49: {148: [12, 13]}}) == (1, 1, 2.0)


def test_a_grand_company_entry_has_no_level_to_check():
    entries = parse_entries([note(1000001, targets=((1, 3),))])
    targets = {t.row_id: t for t in parse_targets([target(1, bnpc=49)])}
    coverage = measure(entries, targets, {49: {148: 2}}, {148: 30})
    assert level_agreement(coverage, {49: {148: [40]}}) == (0, 0, 0.0)
