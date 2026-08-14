import pytest

from autokill_data.spots import cluster


def test_no_points_gives_no_spots():
    assert cluster([], radius=30.0) == []


def test_a_single_point_becomes_a_spot_of_one():
    spots = cluster([(10.0, 20.0)], radius=30.0)
    assert len(spots) == 1
    assert spots[0].count == 1
    assert spots[0].x == pytest.approx(10.0)
    assert spots[0].z == pytest.approx(20.0)


def test_nearby_points_merge_and_the_centre_is_their_average():
    spots = cluster([(0.0, 0.0), (10.0, 0.0), (5.0, 0.0)], radius=30.0)
    assert len(spots) == 1
    assert spots[0].count == 3
    assert spots[0].x == pytest.approx(5.0)
    assert spots[0].z == pytest.approx(0.0)


def test_distant_points_stay_separate():
    spots = cluster([(0.0, 0.0), (500.0, 500.0)], radius=30.0)
    assert len(spots) == 2


def test_a_chain_of_points_links_into_one_spot():
    # Single-link behaviour: each point is within radius of the next, so the
    # whole chain is one farm spot even though the ends are far apart.
    points = [(float(i * 25), 0.0) for i in range(5)]
    spots = cluster(points, radius=30.0)
    assert len(spots) == 1
    assert spots[0].count == 5


def test_spots_are_sorted_by_density_descending():
    points = [(0.0, 0.0), (500.0, 0.0), (505.0, 0.0), (510.0, 0.0)]
    spots = cluster(points, radius=30.0)
    assert [s.count for s in spots] == [3, 1]


def test_the_radius_is_a_distance_not_a_bounding_box():
    # (21, 21) is inside a 30 wide box around the origin but 29.7 away, so it
    # merges; (25, 25) is 35.4 away and must not.
    assert len(cluster([(0.0, 0.0), (21.0, 21.0)], radius=30.0)) == 1
    assert len(cluster([(0.0, 0.0), (25.0, 25.0)], radius=30.0)) == 2


def test_ordering_is_stable_for_equal_counts():
    a = cluster([(500.0, 0.0), (0.0, 0.0)], radius=30.0)
    b = cluster([(0.0, 0.0), (500.0, 0.0)], radius=30.0)
    assert [(s.x, s.z) for s in a] == [(s.x, s.z) for s in b]
