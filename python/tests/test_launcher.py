from pathlib import Path
from unittest.mock import Mock

from mosim_rl import launcher


def test_windows_worker_uses_windows_process_group(monkeypatch, tmp_path: Path) -> None:
    executable = tmp_path / "MoSimRL.exe"
    executable.touch()
    popen = Mock()
    process = Mock()
    popen.return_value = process
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)
    monkeypatch.setattr(launcher.subprocess, "Popen", popen)

    worker = launcher.UnityWorkerProcess(executable, 0, 9000, tmp_path, 1)
    worker.start()

    kwargs = popen.call_args.kwargs
    assert kwargs["creationflags"] == launcher.WINDOWS_CREATE_NEW_PROCESS_GROUP
    assert "start_new_session" not in kwargs


def test_windows_worker_uses_process_methods_to_stop(monkeypatch, tmp_path: Path) -> None:
    process = Mock()
    process.poll.return_value = None
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)

    worker = launcher.UnityWorkerProcess(tmp_path / "MoSimRL.exe", 0, 9000, tmp_path, 1)
    worker.process = process
    worker.stop()

    process.terminate.assert_called_once_with()
    process.wait.assert_called_once_with(timeout=10.0)


def test_graphical_worker_defaults_to_1280x720_window(monkeypatch, tmp_path: Path) -> None:
    executable = tmp_path / "MoSimRL.exe"
    executable.touch()
    popen = Mock()
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)
    monkeypatch.setattr(launcher.subprocess, "Popen", popen)

    worker = launcher.UnityWorkerProcess(
        executable, 0, 9000, tmp_path, 1, graphical=True
    )
    worker.start()

    command = popen.call_args.args[0]
    fullscreen_index = command.index("-screen-fullscreen")
    assert command[fullscreen_index + 1] == "0"
    assert command[command.index("-screen-width") + 1] == "1280"
    assert command[command.index("-screen-height") + 1] == "720"
    assert "-window-mode" not in command


def test_windows_windowed_fullscreen_uses_borderless_mode(
    monkeypatch, tmp_path: Path
) -> None:
    executable = tmp_path / "MoSimRL.exe"
    executable.touch()
    popen = Mock()
    monkeypatch.setattr(launcher, "_is_windows", lambda: True)
    monkeypatch.setattr(launcher.subprocess, "Popen", popen)

    worker = launcher.UnityWorkerProcess(
        executable,
        0,
        9000,
        tmp_path,
        1,
        graphical=True,
        windowed_fullscreen=True,
    )
    worker.start()

    command = popen.call_args.args[0]
    fullscreen_index = command.index("-screen-fullscreen")
    window_mode_index = command.index("-window-mode")
    assert command[fullscreen_index + 1] == "1"
    assert command[window_mode_index + 1] == "borderless"
    assert "-screen-width" not in command
    assert "-screen-height" not in command
