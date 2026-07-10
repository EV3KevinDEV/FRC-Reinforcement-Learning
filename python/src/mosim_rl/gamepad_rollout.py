from __future__ import annotations

import argparse
from contextlib import nullcontext
from pathlib import Path

import numpy as np

from .cli import development_executable
from .env import MoSimEnv
from .physical_gamepad import PhysicalGamepad, active_button_names

PICKUP_IDLE_STEPS = 30
PICKUP_INTAKE_STEPS = 100


def scripted_gamepad_action(step: int) -> np.ndarray:
    """Repeatable controller sequence that exercises every active V1 binding."""
    from .constants import GAMEPAD_ACTION_DIM
    from .gamepad import BUTTON_INDEX

    action = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
    phase = step % 180
    if phase < 20:
        action[1] = 0.6  # forward
    elif phase < 40:
        action[0] = 0.6  # right strafe
    elif phase < 60:
        action[2] = 0.5  # right-stick yaw
    elif phase in {60, 70, 80, 90}:
        face = {60: "SOUTH", 70: "EAST", 80: "WEST", 90: "NORTH"}[phase]
        action[BUTTON_INDEX[face]] = 1.0
    elif 100 <= phase < 120:
        action[BUTTON_INDEX["LEFT_TRIGGER"]] = 0.9
    elif 130 <= phase < 135:
        action[BUTTON_INDEX["RIGHT_TRIGGER"]] = 0.9
    elif phase == 145:
        action[BUTTON_INDEX["DPAD_RIGHT"]] = 1.0
    elif phase == 160:
        action[BUTTON_INDEX["DPAD_DOWN"]] = 1.0
    return action


def scripted_pickup_action(step: int) -> np.ndarray:
    """Begin empty, then hold the normal controller intake binding."""
    from .constants import GAMEPAD_ACTION_DIM
    from .gamepad import BUTTON_INDEX

    action = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
    if PICKUP_IDLE_STEPS <= step < PICKUP_IDLE_STEPS + PICKUP_INTAKE_STEPS:
        action[BUTTON_INDEX["LEFT_TRIGGER"]] = 0.9
    return action


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Drive the Gymnasium environment with a physical gamepad"
    )
    parser.add_argument("--executable", type=Path, default=development_executable())
    parser.add_argument("--controller", type=int, default=0)
    parser.add_argument("--deadzone", type=float, default=0.12)
    parser.add_argument("--steps", type=int, default=0, help="0 runs until Ctrl-C")
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--curriculum-stage", type=int, choices=range(5), default=4)
    parser.add_argument("--print-every", type=int, default=10)
    parser.add_argument(
        "--scripted-demo",
        action="store_true",
        help="exercise all active gamepad mappings without physical input",
    )
    parser.add_argument(
        "--pickup-test",
        action="store_true",
        help="release the preload into Team 118's real ground intake for an acquisition test",
    )
    parser.add_argument(
        "--with-preload",
        action="store_true",
        help="retain the official coral preload instead of the default empty manual start",
    )
    args = parser.parse_args()
    if not 0 <= args.deadzone < 1:
        parser.error("--deadzone must be in [0, 1)")
    if args.controller < 0 or args.steps < 0 or args.print_every <= 0:
        parser.error("controller/steps must be non-negative and print-every positive")

    env = MoSimEnv(
        args.executable,
        base_seed=args.seed,
        curriculum_stage=args.curriculum_stage,
        automatic_curriculum=False,
        action_mode="gamepad",
        graphical=True,
        realtime=True,
        log_dir="runs/gamepad",
    )
    try:
        source = (
            nullcontext(None)
            if args.scripted_demo
            else PhysicalGamepad(args.controller, args.deadzone)
        )
        with source as gamepad:
            if gamepad is None:
                if args.pickup_test:
                    print("Using scripted Team 118 ground-pickup test, then the mapping demo.")
                else:
                    print("Using scripted 180-step gamepad mapping demo.")
            else:
                print(f"Using controller {args.controller}: {gamepad.name}")
                print(
                    "Ctrl-C exits. A/B/X/Y=L1/L2/L3/L4, LT=intake, RT=place, D-pad down=stow."
                )
            if args.pickup_test:
                reset_options = {"scenario": "pickup_test", "curriculum_stage": 3}
            elif args.with_preload:
                reset_options = None
            else:
                reset_options = {"scenario": "empty_start"}
            _, info = env.reset(seed=args.seed, options=reset_options)
            initial_mechanism = info.get("mechanism", {})
            print(
                "reset "
                f"has_coral={initial_mechanism.get('has_coral', False)} "
                f"coral_state={initial_mechanism.get('coral_state', 0)}"
            )
            step = 0
            pickup_prefix_steps = PICKUP_IDLE_STEPS + PICKUP_INTAKE_STEPS
            default_script_steps = 180 + pickup_prefix_steps if args.pickup_test else 180
            step_limit = args.steps or (default_script_steps if args.scripted_demo else 0)
            while step_limit == 0 or step < step_limit:
                if gamepad is None and args.pickup_test and step < pickup_prefix_steps:
                    action = scripted_pickup_action(step)
                elif gamepad is None:
                    demo_step = step - pickup_prefix_steps if args.pickup_test else step
                    action = scripted_gamepad_action(demo_step)
                else:
                    action = gamepad.read()
                _, reward, terminated, truncated, info = env.step(action)
                if step % args.print_every == 0:
                    semantic = np.asarray(info.get("semantic_action", np.zeros(6)))
                    mechanism = info.get("mechanism", {})
                    print(
                        f"step={step} reward={reward:+.3f} score={info.get('score', {}).get('total_points', 0)} "
                        f"has_coral={mechanism.get('has_coral', False)} coral_state={mechanism.get('coral_state', 0)} "
                        f"sticks={action[:4].round(2).tolist()} buttons={active_button_names(action)} "
                        f"command={semantic.round(2).tolist()}"
                    )
                step += 1
                if terminated or truncated:
                    print(f"episode ended: {info.get('termination_reason')}; resetting")
                    _, info = env.reset(options=reset_options)
    except KeyboardInterrupt:
        print("Gamepad rollout stopped.")
    finally:
        env.close()


if __name__ == "__main__":
    main()
