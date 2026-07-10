from __future__ import annotations

import os
from pathlib import Path

import numpy as np
import pytest
import gymnasium as gym
from gymnasium.utils.env_checker import check_env as gym_check_env
from stable_baselines3.common.env_checker import check_env as sb3_check_env

from mosim_rl import ENV_ID, GAMEPAD_ENV_ID
from mosim_rl.env import MoSimEnv


EXECUTABLE = Path(os.environ.get("MOSIM_EXECUTABLE", ""))
HAS_PLAYER = bool(str(EXECUTABLE)) and EXECUTABLE.is_file()
pytestmark = [
    pytest.mark.integration,
    pytest.mark.skipif(
        not HAS_PLAYER, reason="MOSIM_EXECUTABLE is not a built Unity player"
    ),
]


def test_real_worker_contract_and_seeded_reset() -> None:
    env = MoSimEnv(EXECUTABLE, base_seed=123)
    try:
        first, _ = env.reset(seed=456)
        second, _ = env.reset(seed=456)
        # Preserve MoSimulator's normal reset instead of force-teleporting every
        # articulated child. PhysX can settle joint telemetry by ~0.02 while the
        # robot root, score, and match state remain seeded and deterministic.
        np.testing.assert_allclose(first, second, rtol=0, atol=2e-2)
        assert env.capabilities["team_number"] == 118
        assert env.capabilities["fixed_dt"] == pytest.approx(0.0045, abs=1e-6)
        assert env.capabilities["control_dt"] == pytest.approx(0.02, abs=1e-6)
        assert env.capabilities["decision_dt"] == pytest.approx(0.1, abs=1e-6)

        def short_trajectory() -> tuple[np.ndarray, list[dict[str, object]]]:
            observation, info = env.reset(seed=789)
            observations = [observation]
            scores = [info["score"]]
            for _ in range(5):
                observation, _, terminated, truncated, info = env.step(
                    np.zeros(6, dtype=np.float32)
                )
                assert not terminated and not truncated
                observations.append(observation)
                scores.append(info["score"])
            return np.stack(observations), scores

        trajectory_a, scores_a = short_trajectory()
        trajectory_b, scores_b = short_trajectory()
        # PhysX joint/contact convergence is not bitwise stable; policy vectors are
        # bounded to [-1, 1], so 0.2 is the explicit five-decision solver tolerance.
        np.testing.assert_allclose(trajectory_a, trajectory_b, rtol=0, atol=2e-1)
        assert scores_a == scores_b

        before = env._last_raw_state["match"]["sim_time"]
        for _ in range(10):
            env.step(np.zeros(6, dtype=np.float32))
        after = env._last_raw_state["match"]["sim_time"]
        # Native 4.5 ms physics steps alternate in count to approximate each
        # 100 ms decision; carried substep error is bounded over multiple actions.
        assert after - before == pytest.approx(
            1.0, abs=float(env.capabilities["fixed_dt"]) + 1e-5
        )
    finally:
        env.close()


def test_team_118_ground_intake_uses_real_game_piece_path() -> None:
    env = MoSimEnv(EXECUTABLE, base_seed=710, action_mode="gamepad")
    try:
        _, info = env.reset(
            seed=711,
            options={"curriculum_stage": 3, "scenario": "pickup_test"},
        )
        assert info["mechanism"]["has_coral"] is False

        action = np.zeros(env.action_space.shape, dtype=np.float32)
        # LEFT_TRIGGER is the production-compatible ground-intake binding.
        from mosim_rl.gamepad import BUTTON_INDEX

        action[BUTTON_INDEX["LEFT_TRIGGER"]] = 0.9
        for _ in range(100):
            _, _, terminated, truncated, info = env.step(action)
            assert not terminated and not truncated
            if info["mechanism"]["has_coral"]:
                break
        else:
            pytest.fail("Team 118 did not acquire coral through its normal ground intake")

        assert info["mechanism"]["coral_state"] > 0
    finally:
        env.close()


def test_manual_empty_start_removes_preload() -> None:
    env = MoSimEnv(EXECUTABLE, base_seed=715, action_mode="gamepad")
    try:
        _, info = env.reset(seed=716, options={"scenario": "empty_start"})
        assert info["mechanism"]["has_coral"] is False
        assert info["mechanism"]["coral_state"] == 0
    finally:
        env.close()


@pytest.mark.parametrize("source", ["preload", "pickup"])
def test_right_trigger_releases_coral_from_team_118(source: str) -> None:
    from mosim_rl.gamepad import BUTTON_INDEX

    env = MoSimEnv(EXECUTABLE, base_seed=717, action_mode="gamepad")
    try:
        options = (
            {"curriculum_stage": 3, "scenario": "pickup_test"}
            if source == "pickup"
            else {"curriculum_stage": 4, "scenario": "official_match"}
        )
        _, info = env.reset(seed=718, options=options)
        neutral = np.zeros(env.action_space.shape, dtype=np.float32)

        if source == "pickup":
            intake = neutral.copy()
            intake[BUTTON_INDEX["LEFT_TRIGGER"]] = 0.9
            for _ in range(100):
                _, _, _, truncated, info = env.step(intake)
                assert not truncated
                if info["mechanism"]["coral_state"] >= 4:
                    break
            else:
                pytest.fail("pickup coral never reached Team 118's stored state")

        assert info["mechanism"]["has_coral"] is True

        select_l2 = neutral.copy()
        select_l2[BUTTON_INDEX["EAST"]] = 1.0
        env.step(select_l2)
        for _ in range(30):
            env.step(neutral)

        place = neutral.copy()
        place[BUTTON_INDEX["RIGHT_TRIGGER"]] = 0.9
        _, _, _, truncated, info = env.step(place)
        assert not truncated
        assert info["mechanism"]["has_coral"] is False
        assert info["mechanism"]["coral_state"] == 0
    finally:
        env.close()


def test_native_physics_keeps_idle_mechanism_and_cages_stable() -> None:
    env = MoSimEnv(EXECUTABLE, base_seed=720)
    try:
        env.reset(seed=721)
        mechanism_samples: list[list[float]] = []
        cage_speeds: list[float] = []
        for _ in range(100):
            env.step(np.zeros(6, dtype=np.float32))
            mechanism = env._last_raw_state["mechanism"]
            physics = env._last_raw_state["physics"]
            mechanism_samples.append(
                [
                    mechanism["arm_angle"],
                    mechanism["elevator_height"],
                    mechanism["intake_angle"],
                    mechanism["algae_arms_angle"],
                ]
            )
            cage_speeds.append(float(physics["max_cage_angular_speed"]))

        settled = np.asarray(mechanism_samples[-40:], dtype=np.float64)
        # The former forced 20 ms solver step produced about 5.9 degrees of arm
        # chatter. Native physics remains two orders of magnitude below that.
        assert np.ptp(settled[:, 0]) < 0.2
        assert np.max(np.ptp(settled[:, 1:], axis=0)) < 0.2
        assert np.isfinite(cage_speeds).all()
        assert max(cage_speeds[-40:]) < 2.0
    finally:
        env.close()


@pytest.mark.parametrize("environment_id", [ENV_ID, GAMEPAD_ENV_ID])
def test_real_worker_passes_environment_checkers(environment_id: str) -> None:
    env = gym.make(environment_id, executable_path=EXECUTABLE, base_seed=800)
    try:
        gym_check_env(env, skip_render_check=True)
        sb3_check_env(env, warn=True, skip_render_check=True)
    finally:
        env.close()


@pytest.mark.skipif(
    os.environ.get("MOSIM_RUN_SLOW_INTEGRATION") != "1",
    reason="set MOSIM_RUN_SLOW_INTEGRATION=1 for the full 156-second match test",
)
def test_full_match_includes_pauses_and_final_grace() -> None:
    env = MoSimEnv(EXECUTABLE, base_seed=900)
    states: set[str] = set()
    try:
        env.reset(seed=901)
        for _ in range(1_700):
            _, _, terminated, truncated, info = env.step(np.zeros(6, dtype=np.float32))
            states.add(info["game_state"])
            assert not truncated
            if terminated:
                assert info["termination_reason"] == "match_complete"
                assert info["sim_time"] == pytest.approx(156.0, abs=0.15)
                break
        else:
            pytest.fail(
                "match did not terminate after AUTO, TELEOP, and both grace periods"
            )
        assert {"Auto", "Teleop", "Endgame", "End"}.issubset(states)
    finally:
        env.close()
