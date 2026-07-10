from __future__ import annotations

import numpy as np
import pytest

from mosim_rl.observation import ObservationEncoder


def test_observation_order_shape_and_bounds(raw_state: dict) -> None:
    previous_action = np.array([-1.0, -0.5, 0.0, 0.5, 1.0, 0.25], dtype=np.float32)
    observation = ObservationEncoder().encode(raw_state, previous_action)

    assert observation.shape == (62,)
    assert observation.dtype == np.float32
    assert np.isfinite(observation).all()
    assert np.all(observation >= -1.0) and np.all(observation <= 1.0)
    np.testing.assert_allclose(observation[0:4], [1 / 9, -2 / 5, 1.0, 0.0], atol=1e-6)
    np.testing.assert_array_equal(observation[20:25], [1, 0, 0, 0, 0])
    np.testing.assert_array_equal(observation[36:40], [0, 0, 1, 0])
    np.testing.assert_array_equal(observation[41:45], [1, 0, 0, 0])
    np.testing.assert_allclose(observation[56:62], previous_action)


def test_observation_rejects_nan(raw_state: dict) -> None:
    raw_state["robot"]["yaw_rate"] = float("nan")
    with pytest.raises(ValueError, match="NaN"):
        ObservationEncoder().encode(raw_state, np.zeros(6, dtype=np.float32))
